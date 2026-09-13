using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMergeTool.App.Features.RenameVideo.Services;
using VideoMergeTool.App.Shell;
using VideoMergeTool.Core.Features.RenameVideo;
using VideoMergeTool.Core.Features.RenameVideo.Enums;
using VideoMergeTool.Core.Features.RenameVideo.Interfaces;
using VideoMergeTool.Core.Features.RenameVideo.Models;
using VideoMergeTool.Core.Models;

namespace VideoMergeTool.App.Features.RenameVideo;

public sealed partial class RenameVideoViewModel : ObservableObject, IDisposable, ICloseGuard, IFolderDropTarget
{
    private readonly IRenameFolderScanner _scanner;
    private readonly IVideoRenamer _renamer;
    private readonly IRenamePrompt _prompt;

    private readonly Dictionary<string, RenameItemViewModel> _itemLookup = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The folder as last scanned. Cleared once names on disk change, forcing a fresh scan.</summary>
    private RenameFolderSnapshot? _snapshot;

    private IReadOnlyList<RenamePlanItem> _plan = [];
    private CancellationTokenSource? _cancellation;
    private int _batchTotal;
    private int _batchProcessed;
    private bool _isDisposed;

    public RenameVideoViewModel(IRenameFolderScanner scanner, IVideoRenamer renamer, IRenamePrompt prompt)
    {
        _scanner = scanner;
        _renamer = renamer;
        _prompt = prompt;
    }

    public void LoadSettings(UserSettings settings)
    {
        Folder = settings.RenameFolder;
        HashtagText = settings.RenameHashtags;
    }

    public UserSettings ExportSettings(UserSettings settings) =>
        settings with { RenameFolder = Folder, RenameHashtags = HashtagText };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(UndoCommand))]
    private string _folder = string.Empty;

    [ObservableProperty]
    private string _hashtagText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private ObservableCollection<RenameItemViewModel> _items = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(RenameCommand), nameof(CancelCommand), nameof(UndoCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _summaryText = "Chọn thư mục chứa video để xem trước tên mới.";

    [ObservableProperty]
    private string _countsText = string.Empty;

    public bool IsIdle => !IsBusy;

    public string SupportedExtensionsText { get; } = string.Join(", ", VideoNameRules.VideoExtensions);

    partial void OnFolderChanged(string value)
    {
        if (!IsBusy && (_snapshot is not null || Items.Count > 0))
        {
            ClearList("Thư mục đã thay đổi — bấm \"Xem trước\" để quét lại.");
        }
    }

    partial void OnHashtagTextChanged(string value)
    {
        if (!IsBusy && _snapshot is not null)
        {
            RebuildPreview();
        }
    }

    private bool CanScan() => !IsBusy && !string.IsNullOrWhiteSpace(Folder);

    private bool CanRename() => !IsBusy && _snapshot is not null && _plan.Any(item => item.HasChange);

    private bool CanCancel() => IsBusy || Items.Count > 0;

    private bool CanUndo() => !IsBusy && !string.IsNullOrWhiteSpace(Folder);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsBusy = true;
        SummaryText = "Đang quét thư mục…";
        _cancellation = new CancellationTokenSource();

        try
        {
            _snapshot = await _scanner.ScanAsync(Folder.Trim(), _cancellation.Token);
            RebuildPreview();
        }
        catch (OperationCanceledException)
        {
            ClearList("Đã huỷ quét thư mục.");
        }
        catch (Exception exception)
        {
            ClearList($"Không đọc được thư mục: {exception.Message}");
        }
        finally
        {
            DisposeCancellation();
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRename))]
    private async Task RenameAsync()
    {
        var folder = _snapshot!.Folder;
        var plan = _plan;

        IsBusy = true;
        try
        {
            await RunBatchAsync(
                plan.Count(item => item.HasChange),
                "Đang đổi tên…",
                "Đổi tên",
                (progress, token) => _renamer.RenameAsync(folder, plan, progress, token));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        var folder = Folder.Trim();

        IsBusy = true;
        try
        {
            RenameLogBatch? batch;
            try
            {
                batch = await _renamer.FindLastUndoableBatchAsync(folder, CancellationToken.None);
            }
            catch (Exception exception)
            {
                SummaryText = $"Không đọc được lịch sử đổi tên: {exception.Message}";
                return;
            }

            if (batch is null)
            {
                SummaryText = "Thư mục này chưa có lần đổi tên nào để hoàn tác.";
                return;
            }

            if (!_prompt.ConfirmUndo(batch.RenamedEntries.Count, batch.StartedAt, folder))
            {
                SummaryText = "Đã huỷ hoàn tác — không có file nào bị đổi tên.";
                return;
            }

            ReplaceItems(batch.RenamedEntries
                .Reverse()
                .Select(entry => new RenameItemViewModel(entry.NewName, entry.OldName, RenameItemStatus.Pending, "Sẽ khôi phục tên gốc")));

            await RunBatchAsync(
                batch.RenamedEntries.Count,
                "Đang hoàn tác…",
                "Hoàn tác",
                (progress, token) => _renamer.UndoAsync(folder, batch, progress, token));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>While busy this stops the scan or batch; otherwise it discards the list without renaming anything.</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (IsBusy)
        {
            SummaryText = "Đang dừng…";
            TryCancel(_cancellation);
            return;
        }

        ClearList(_snapshot is not null
            ? "Đã huỷ — không có file nào bị đổi tên."
            : "Đã xoá danh sách.");
    }

    public Task<bool> ConfirmCloseAsync() => Task.FromResult(true);

    /// <summary>Renames stop between files, so waiting lets the file in progress finish and get logged.</summary>
    public async Task CancelAndWaitAsync(TimeSpan timeout)
    {
        TryCancel(_cancellation);

        var running = new[] { ScanCommand.ExecutionTask, RenameCommand.ExecutionTask, UndoCommand.ExecutionTask }
            .OfType<Task>()
            .Where(task => !task.IsCompleted)
            .ToList();

        if (running.Count == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(running).WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void OnFolderDropped(string folderPath)
    {
        if (IsBusy)
        {
            return;
        }

        Folder = folderPath;

        if (ScanCommand.CanExecute(null))
        {
            ScanCommand.Execute(null);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _cancellation?.Dispose();
    }

    private async Task RunBatchAsync(
        int total,
        string runningText,
        string actionName,
        Func<IProgress<RenameItemResult>, CancellationToken, Task<RenameBatchResult>> run)
    {
        // Names on disk are about to change, so this preview can never be run a second time.
        _snapshot = null;
        _plan = [];
        RenameCommand.NotifyCanExecuteChanged();

        _batchTotal = total;
        _batchProcessed = 0;
        Progress = 0;
        SummaryText = runningText;
        CountsText = string.Empty;
        _cancellation = new CancellationTokenSource();

        try
        {
            var result = await run(new Progress<RenameItemResult>(OnItemProgress), _cancellation.Token);

            foreach (var item in result.Items)
            {
                ApplyResult(item);
            }

            Progress = total == 0 ? 1 : (double)result.Items.Count / total;

            var notRun = total - result.Items.Count;
            SummaryText = result.WasCancelled
                ? $"{actionName} đã dừng giữa chừng. Lịch sử được ghi vào rename-log.csv trong thư mục."
                : $"{actionName} xong. Lịch sử được ghi vào rename-log.csv trong thư mục.";
            CountsText = $"{result.RenamedCount} thành công  ·  {result.FailedCount} lỗi  ·  {notRun} chưa xử lý";
        }
        catch (Exception exception)
        {
            SummaryText = $"{actionName} thất bại — bấm \"Xem trước\" để quét lại thư mục.";
            _prompt.ShowError(exception.Message);
        }
        finally
        {
            DisposeCancellation();
        }
    }

    private void OnItemProgress(RenameItemResult result)
    {
        ApplyResult(result);
        _batchProcessed++;
        Progress = _batchTotal == 0 ? 1 : Math.Clamp((double)_batchProcessed / _batchTotal, 0, 1);
        CountsText = $"{_batchProcessed}/{_batchTotal}";
    }

    private void ApplyResult(RenameItemResult result)
    {
        if (_itemLookup.TryGetValue(result.OriginalName, out var item))
        {
            item.Apply(result);
        }
    }

    private void RebuildPreview()
    {
        if (_snapshot is null)
        {
            return;
        }

        _plan = RenamePlanner.Plan(_snapshot, HashtagText);
        ReplaceItems(_plan.Select(item => new RenameItemViewModel(item)));
        Progress = 0;

        var changeCount = _plan.Count(item => item.HasChange);
        var missingIdCount = _plan.Count(item => !item.IdFound);

        SummaryText = _plan.Count == 0
            ? $"Không có video nào ({SupportedExtensionsText}) nằm trực tiếp trong thư mục này."
            : changeCount == 0
                ? "Không có file nào cần đổi tên. Hãy nhập hashtag hoặc chọn thư mục khác."
                : $"Xem trước: {changeCount} video sẽ được đổi tên. Kiểm tra danh sách rồi bấm \"Đổi tên hàng loạt\".";
        CountsText = _plan.Count == 0
            ? string.Empty
            : $"{_plan.Count} video  ·  {changeCount} sẽ đổi  ·  {missingIdCount} không có ID";

        RenameCommand.NotifyCanExecuteChanged();
    }

    private void ClearList(string summary)
    {
        _snapshot = null;
        _plan = [];
        ReplaceItems([]);
        Progress = 0;
        CountsText = string.Empty;
        SummaryText = summary;
        RenameCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Replaced wholesale so a large folder raises one collection notification, not one per file.</summary>
    private void ReplaceItems(IEnumerable<RenameItemViewModel> items)
    {
        var collection = new ObservableCollection<RenameItemViewModel>(items);
        _itemLookup.Clear();
        foreach (var item in collection)
        {
            _itemLookup[item.OriginalName] = item;
        }

        Items = collection;
    }

    private void DisposeCancellation()
    {
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private static void TryCancel(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation finished while the click was being handled.
        }
    }
}
