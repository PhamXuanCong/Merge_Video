namespace VideoMergeTool.Core.Models;

public sealed record OutputValidationResult(
    bool IsValid,
    VideoMetadata? Metadata,
    string? FailureReason);
