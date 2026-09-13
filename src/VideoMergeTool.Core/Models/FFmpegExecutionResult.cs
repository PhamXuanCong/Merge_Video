namespace VideoMergeTool.Core.Models;

public sealed record FFmpegExecutionResult(
    bool Success,
    int ExitCode,
    string ErrorOutput);
