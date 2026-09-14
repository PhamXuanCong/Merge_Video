using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoMergeTool.App.Shell;
using VideoMergeTool.Core.Features.FolderVideoStats.Interfaces;
using VideoMergeTool.Core.Features.FolderVideoStats.Models;
using VideoMergeTool.Core.Models;

namespace VideoMergeTool.App.Features.FolderVideoStats;

/// <summary>
/// A table of folders the user is tracking: each row shows how many MP4 files are in that folder
/// (its whole subtree) and, expanded, the same count for each of its direct subfolders.
/// </summary>
public sealed partial class FolderVideoStatsViewModel : ObservableObject, IDisposable, IFolderDropTarget
{
    private readonly IFolderVideoCounter _counter;

    public FolderVideoStatsViewModel(IFolderVideoCounter counter)
    {
        _counter = counter;
    }

    [ObservableProperty]
    private ObservableCollection<FolderRowViewModel> _folders = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTypedFolderCommand))]
    private string _newFolderPath = string.Empty;

    [ObservableProperty]
    private string _summaryText = "Thêm một thư mục để xem số lượng video (.mp4) bên trong.";

    public void LoadSettings(UserSettings settings)
    {
        foreach (var entry in settings.FolderStatsFolders)
        {
            AddFolder(entry.FolderPath, entry.Topic, entry.Hashtag);
        }
    }

    public UserSettings ExportSettings(UserSettings settings) =>
        settings with
        {
            FolderStatsFolders = Folders
                .Select(static row => new FolderStatsFolderSettings(row.FolderPath, row.Topic, row.Hashtag))
                .ToList()
        };

    /// <summary>Mirrors the other tabs' drag-and-drop behaviour: dropping a folder adds and scans it.</summary>
    public void OnFolderDropped(string folderPath) => AddFolder(folderPath);

    private bool CanAddTypedFolder() => !string.IsNullOrWhiteSpace(NewFolderPath);

    [RelayCommand(CanExecute = nameof(CanAddTypedFolder))]
    private void AddTypedFolder()
    {
        AddFolder(NewFolderPath);
        NewFolderPath = string.Empty;
    }

    [RelayCommand]
    private void RemoveFolder(FolderRowViewModel row)
    {
        row.PendingScan?.Cancel();
        Folders.Remove(row);
        UpdateSummary();
    }

    [RelayCommand]
    private async Task RefreshFolderAsync(FolderRowViewModel row) => await ScanAsync(row);

    [RelayCommand]
    private async Task RefreshAllAsync() => await Task.WhenAll(Folders.Select(ScanAsync));

    /// <summary>Also used by the "add by path" box and the drag-and-drop handler.</summary>
    public void AddFolder(string folderPath) => AddFolder(folderPath, string.Empty, string.Empty);

    private void AddFolder(string folderPath, string topic, string hashtag)
    {
        var trimmed = folderPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        if (!Directory.Exists(trimmed))
        {
            SummaryText = $"Thư mục không tồn tại: {trimmed}";
            return;
        }

        if (Folders.Any(row => string.Equals(row.FolderPath, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            SummaryText = "Thư mục này đã có trong danh sách.";
            return;
        }

        var row = new FolderRowViewModel(trimmed) { Topic = topic, Hashtag = hashtag };
        Folders.Add(row);
        UpdateSummary();
        _ = ScanAsync(row);
    }

    private async Task ScanAsync(FolderRowViewModel row)
    {
        row.PendingScan?.Cancel();

        var cancellation = new CancellationTokenSource();
        row.PendingScan = cancellation;
        row.IsScanning = true;
        row.StatusText = "Đang quét…";

        try
        {
            var stats = await _counter.CountAsync(row.FolderPath, cancellation.Token);
            row.ApplyResult(stats);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            row.ApplyError($"Không quét được: {exception.Message}");
        }
        finally
        {
            row.IsScanning = false;
            if (ReferenceEquals(row.PendingScan, cancellation))
            {
                row.PendingScan = null;
            }

            cancellation.Dispose();
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        SummaryText = Folders.Count == 0
            ? "Thêm một thư mục để xem số lượng video (.mp4) bên trong."
            : $"{Folders.Count} thư mục  ·  tổng {Folders.Sum(static row => row.VideoCount)} video";
    }

    public void Dispose()
    {
        foreach (var row in Folders)
        {
            row.PendingScan?.Cancel();
        }
    }
}
