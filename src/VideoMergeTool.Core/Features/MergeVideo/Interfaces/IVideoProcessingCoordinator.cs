using VideoMergeTool.Core.Features.MergeVideo.Models;

namespace VideoMergeTool.Core.Features.MergeVideo.Interfaces;

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
