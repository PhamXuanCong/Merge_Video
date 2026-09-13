using VideoMergeTool.Core.Features.RenameVideo.Models;

namespace VideoMergeTool.Core.Features.RenameVideo.Interfaces;

public interface IVideoRenamer
{
    /// <summary>
    /// Renames every item that has a change and logs each one. A file that cannot be renamed is
    /// reported as failed and the batch carries on; cancellation stops before the next file and
    /// returns what was done so far.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The folder's rename log cannot be written.</exception>
    /// <exception cref="IOException">The folder's rename log cannot be written.</exception>
    Task<RenameBatchResult> RenameAsync(
        string folder,
        IReadOnlyList<RenamePlanItem> plan,
        IProgress<RenameItemResult>? progress,
        CancellationToken cancellationToken);

    /// <summary>The newest rename batch in the folder's log that has not been undone yet.</summary>
    Task<RenameLogBatch?> FindLastUndoableBatchAsync(string folder, CancellationToken cancellationToken);

    /// <summary>Restores the original names of <paramref name="batch"/>, newest rename first.</summary>
    /// <exception cref="UnauthorizedAccessException">The folder's rename log cannot be written.</exception>
    /// <exception cref="IOException">The folder's rename log cannot be written.</exception>
    Task<RenameBatchResult> UndoAsync(
        string folder,
        RenameLogBatch batch,
        IProgress<RenameItemResult>? progress,
        CancellationToken cancellationToken);
}
