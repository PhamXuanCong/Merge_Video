namespace VideoMergeTool.Core.Enums;

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
