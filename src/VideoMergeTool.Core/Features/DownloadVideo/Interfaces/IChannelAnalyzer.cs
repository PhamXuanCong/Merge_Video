using VideoMergeTool.Core.Features.DownloadVideo.Enums;
using VideoMergeTool.Core.Features.DownloadVideo.Models;

namespace VideoMergeTool.Core.Features.DownloadVideo.Interfaces;

/// <summary>
/// Reads public and authorized channel metadata through yt-dlp without downloading media.
/// </summary>
public interface IChannelAnalyzer
{
    /// <exception cref="ChannelAnalysisException">yt-dlp could not list the channel.</exception>
    Task<ChannelAnalysisResult> AnalyzeAsync(
        Uri channelBaseUri,
        IReadOnlyCollection<ContentType> contentTypes,
        int recentVideoLimit,
        bool resolveMissingDurations,
        bool useBrowserCookies,
        string browserName,
        bool useCookieFile,
        string cookieFilePath,
        CancellationToken cancellationToken);
}
