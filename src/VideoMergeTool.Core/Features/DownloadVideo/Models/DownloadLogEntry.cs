using VideoMergeTool.Core.Features.DownloadVideo.Enums;

namespace VideoMergeTool.Core.Features.DownloadVideo.Models;

public sealed record DownloadLogEntry(
    DateTime Timestamp,
    DownloadLogLevel Level,
    string Message,
    string? VideoId = null)
{
    public override string ToString()
    {
        var videoPart = string.IsNullOrWhiteSpace(VideoId) ? string.Empty : $" [{VideoId}]";
        return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level}]{videoPart} {Message}";
    }
}
