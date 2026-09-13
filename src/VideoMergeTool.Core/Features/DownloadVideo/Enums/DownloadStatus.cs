namespace VideoMergeTool.Core.Features.DownloadVideo.Enums;

public enum DownloadStatus
{
    Pending,
    Analyzing,
    Ready,
    Downloading,
    Processing,
    Completed,
    AlreadyDownloaded,
    RequiresLogin,
    Private,
    Unavailable,
    Skipped,
    Failed,
    Cancelled
}
