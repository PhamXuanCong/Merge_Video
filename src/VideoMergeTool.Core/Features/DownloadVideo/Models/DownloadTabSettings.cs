using VideoMergeTool.Core.Features.DownloadVideo.Enums;

namespace VideoMergeTool.Core.Features.DownloadVideo.Models;

/// <summary>
/// The saved options of one downloader tab. The analyzed video list is deliberately not part of it.
/// </summary>
public sealed record DownloadTabSettings
{
    public string Title { get; init; } = "Chủ đề";

    public string ChannelUrl { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "YouTube Downloads");

    public DownloadQuality Quality { get; init; } = DownloadQuality.P1080;

    public bool DownloadVideos { get; init; } = true;

    public bool DownloadShorts { get; init; }

    public bool DownloadStreams { get; init; }

    public bool DownloadThumbnail { get; init; }

    public bool DownloadSubtitles { get; init; }

    public bool SkipPreviouslyDownloaded { get; init; } = true;

    public bool UseBrowserCookies { get; init; }

    public string SelectedBrowser { get; init; } = "chrome";

    public bool IsDurationFilterEnabled { get; init; }

    public DurationFilterComparison DurationFilterComparison { get; init; } =
        DurationFilterComparison.ShorterThan;

    public int DurationLimitValue { get; init; } = 1;

    public DurationFilterUnit DurationFilterUnit { get; init; } =
        DurationFilterUnit.Minutes;

    public int RecentVideoLimit { get; init; }
}
