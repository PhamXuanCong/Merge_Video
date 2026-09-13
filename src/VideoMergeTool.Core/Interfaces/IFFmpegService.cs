using VideoMergeTool.Core.Models;

namespace VideoMergeTool.Core.Interfaces;

public interface IFFmpegService
{
    Task<FFmpegExecutionResult> MergeAsync(
        VideoMergeTask task,
        ProcessingOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
