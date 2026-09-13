namespace VideoMergeTool.Core.Features.MergeVideo.Enums;

public enum VideoTaskStatus
{
    Pending,
    ReadingMetadata,
    Processing,
    Validating,
    Completed,
    Skipped,
    Failed,
    Cancelled
}
