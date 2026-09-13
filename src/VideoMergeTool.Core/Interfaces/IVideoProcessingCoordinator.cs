using VideoMergeTool.Core.Models;

namespace VideoMergeTool.Core.Interfaces;

/// <summary>
/// Coordinates scanning, pairing, merging, validation, and source-file actions.
/// </summary>
public interface IVideoProcessingCoordinator
{
    Task<ProcessingSummary> ProcessAsync(
        string inputFolder,
        ProcessingOptions options,
        IProgress<VideoMergeTask>? progress,
        CancellationToken cancellationToken);
}
