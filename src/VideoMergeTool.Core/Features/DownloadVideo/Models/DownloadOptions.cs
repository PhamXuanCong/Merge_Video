using VideoMergeTool.Core.Features.DownloadVideo.Enums;

namespace VideoMergeTool.Core.Features.DownloadVideo.Models;

public sealed class DownloadOptions
{
    public string OutputDirectory { get; init; } = string.Empty;

    public string ChannelName { get; init; } = string.Empty;

    public DownloadQuality Quality { get; init; } = DownloadQuality.P1080;

    public bool DownloadThumbnail { get; init; }

    public bool DownloadSubtitles { get; init; }

    public bool SkipPreviouslyDownloaded { get; init; } = true;

    public bool UseBrowserCookies { get; init; }

    public string BrowserName { get; init; } = "chrome";
}
