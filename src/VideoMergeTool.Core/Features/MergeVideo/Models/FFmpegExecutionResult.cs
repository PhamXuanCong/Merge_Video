namespace VideoMergeTool.Core.Features.MergeVideo.Models;

public sealed record FFmpegExecutionResult(
    bool Success,
    int ExitCode,
    string ErrorOutput);
