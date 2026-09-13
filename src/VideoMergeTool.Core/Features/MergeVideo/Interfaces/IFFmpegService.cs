using VideoMergeTool.Core.Features.MergeVideo.Models;

namespace VideoMergeTool.Core.Features.MergeVideo.Interfaces;

public interface IFFmpegService
{
    Task<FFmpegExecutionResult> MergeAsync(
        VideoMergeTask task,
        ProcessingOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
