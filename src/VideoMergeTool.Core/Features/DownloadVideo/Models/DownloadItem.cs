using VideoMergeTool.Core.Features.DownloadVideo.Enums;

namespace VideoMergeTool.Core.Features.DownloadVideo.Models;

public sealed class DownloadItem
{
    public bool IsSelected { get; set; }

    public string VideoId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string ChannelName { get; init; } = string.Empty;

    public ContentType ContentType { get; init; }

    public int? DurationSeconds { get; init; }

    public DateTime? UploadDate { get; init; }

    public string? ThumbnailUrl { get; init; }

    public double ProgressPercent { get; set; }

    public string Speed { get; set; } = string.Empty;

    public string Eta { get; set; } = string.Empty;

    public DownloadStatus Status { get; set; }

    public string? OutputFilePath { get; set; }

    public string? ErrorMessage { get; set; }
}
