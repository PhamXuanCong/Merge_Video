using VideoMergeTool.Core.Features.RenameVideo;
using VideoMergeTool.Core.Features.RenameVideo.Enums;
using VideoMergeTool.Core.Features.RenameVideo.Interfaces;
using VideoMergeTool.Core.Features.RenameVideo.Models;

namespace VideoMergeTool.Infrastructure.Features.RenameVideo;

public sealed class VideoRenamer : IVideoRenamer
{
    private const int ErrorFileExists = 80;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int ErrorAlreadyExists = 183;

    public Task<RenameBatchResult> RenameAsync(
        string folder,
        IReadOnlyList<RenamePlanItem> plan,
        IProgress<RenameItemResult>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(plan);

        var changes = plan.Where(item => item.HasChange).ToList();

        return Task.Run(() => RunBatch(
            folder,
            Guid.NewGuid().ToString("N")[..8],
            RenameLogAction.Rename,
            changes,
            item => RenameOne(folder, item),
            progress,
            cancellationToken));
    }

    public Task<RenameLogBatch?> FindLastUndoableBatchAsync(string folder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        return Task.Run(
            () =>
            {
                EnsureFolderExists(folder);
                return FindLastUndoableBatch(RenameLogCsv.Read(folder));
            },
            cancellationToken);
    }

    public Task<RenameBatchResult> UndoAsync(
        string folder,
        RenameLogBatch batch,
        IProgress<RenameItemResult>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(batch);

        var entries = batch.RenamedEntries.Reverse().ToList();

        return Task.Run(() => RunBatch(
            folder,
            batch.BatchId,
            RenameLogAction.Undo,
            entries,
            entry => UndoOne(folder, entry),
            progress,
            cancellationToken));
    }

    /// <summary>
    /// A batch stays undoable while any of its renames has no matching successful undo row, so an
    /// undo that was cancelled or hit a locked file can simply be run again.
    /// </summary>
    internal static RenameLogBatch? FindLastUndoableBatch(IReadOnlyList<RenameLogEntry> entries)
    {
        var restored = entries
            .Where(entry => entry.Action == RenameLogAction.Undo && entry.Status == RenameItemStatus.Renamed)
            .Select(entry => (entry.BatchId, RenamedName: entry.OldName, OriginalName: entry.NewName))
            .ToHashSet();

        var pending = new Dictionary<string, List<RenameLogEntry>>(StringComparer.Ordinal);
        string? newestBatchId = null;

        foreach (var entry in entries)
        {
            if (entry.Action != RenameLogAction.Rename ||
                entry.Status != RenameItemStatus.Renamed ||
                restored.Contains((entry.BatchId, entry.NewName, entry.OldName)))
            {
                continue;
            }

            if (!pending.TryGetValue(entry.BatchId, out var batchEntries))
            {
                pending[entry.BatchId] = batchEntries = [];
            }

            batchEntries.Add(entry);
            newestBatchId = entry.BatchId;
        }

        return newestBatchId is null
            ? null
            : new RenameLogBatch(newestBatchId, pending[newestBatchId][0].Time, pending[newestBatchId]);
    }

    private static RenameBatchResult RunBatch<T>(
        string folder,
        string batchId,
        RenameLogAction action,
        IReadOnlyList<T> items,
        Func<T, RenameItemResult> renameOne,
        IProgress<RenameItemResult>? progress,
        CancellationToken cancellationToken)
    {
        EnsureFolderExists(folder);

        if (items.Count == 0)
        {
            return new RenameBatchResult([], WasCancelled: false);
        }

        // Opening the log first doubles as the write-permission check: if it fails, nothing has
        // been renamed yet, and no rename ever happens without a log row to undo it from.
        using var log = OpenLog(folder);
        var results = new List<RenameItemResult>(items.Count);

        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new RenameBatchResult(results, WasCancelled: true);
            }

            var result = renameOne(item);
            results.Add(result);
            progress?.Report(result);

            log.WriteLine(RenameLogCsv.FormatLine(new RenameLogEntry(
                DateTime.Now,
                batchId,
                action,
                result.OriginalName,
                result.NewName,
                result.Status,
                result.Note)));
        }

        return new RenameBatchResult(results, WasCancelled: false);
    }

    private static RenameItemResult RenameOne(string folder, RenamePlanItem item)
    {
        // The folder may have changed since the preview; re-check so nothing is ever overwritten.
        var target = IsNameTaken(folder, item.NewName)
            ? VideoNameRules.MakeUnique(item.DesiredName, name => IsNameTaken(folder, name))
            : item.NewName;

        var error = TryMove(folder, item.OriginalName, target);

        return error is null
            ? new RenameItemResult(item.OriginalName, target, RenameItemStatus.Renamed, (item with { NewName = target }).Note)
            : new RenameItemResult(item.OriginalName, target, RenameItemStatus.Failed, error);
    }

    private static RenameItemResult UndoOne(string folder, RenameLogEntry entry)
    {
        var error = IsNameTaken(folder, entry.OldName)
            ? "Tên gốc đang được một file khác dùng"
            : TryMove(folder, entry.NewName, entry.OldName);

        return error is null
            ? new RenameItemResult(entry.NewName, entry.OldName, RenameItemStatus.Renamed, "Đã khôi phục tên gốc")
            : new RenameItemResult(entry.NewName, entry.OldName, RenameItemStatus.Failed, error);
    }

    /// <returns><c>null</c> on success, otherwise a message for the user.</returns>
    private static string? TryMove(string folder, string fromName, string toName)
    {
        var source = Path.Combine(folder, fromName);
        if (!File.Exists(source))
        {
            return "Không còn tìm thấy file này trong thư mục";
        }

        try
        {
            File.Move(source, Path.Combine(folder, toName), overwrite: false);
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return "Không có quyền đổi tên file này";
        }
        catch (IOException exception)
        {
            return (exception.HResult & 0xFFFF) switch
            {
                ErrorSharingViolation or ErrorLockViolation => "File đang được mở bởi chương trình khác",
                ErrorFileExists or ErrorAlreadyExists => "Tên mới vừa bị một file khác dùng",
                _ => exception.Message
            };
        }
    }

    private static bool IsNameTaken(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        return File.Exists(path) || Directory.Exists(path);
    }

    private static void EnsureFolderExists(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"Thư mục không tồn tại: {folder}");
        }
    }

    private static StreamWriter OpenLog(string folder)
    {
        try
        {
            return RenameLogCsv.OpenForAppend(folder);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new UnauthorizedAccessException(
                $"Không có quyền ghi vào thư mục:{Environment.NewLine}{folder}{Environment.NewLine}{Environment.NewLine}" +
                "Chưa có file nào bị đổi tên.",
                exception);
        }
        catch (IOException exception)
        {
            throw new IOException(
                $"Không ghi được file log {RenameLogCsv.FileName} trong thư mục:{Environment.NewLine}{folder}{Environment.NewLine}{Environment.NewLine}" +
                $"Nếu file log đang mở trong Excel, hãy đóng lại rồi thử lại. Chưa có file nào bị đổi tên.{Environment.NewLine}{Environment.NewLine}" +
                exception.Message,
                exception);
        }
    }
}
