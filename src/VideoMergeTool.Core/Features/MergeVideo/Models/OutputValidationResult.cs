namespace VideoMergeTool.Core.Features.MergeVideo.Models;

public sealed record OutputValidationResult(
    bool IsValid,
    VideoMetadata? Metadata,
    string? FailureReason);
