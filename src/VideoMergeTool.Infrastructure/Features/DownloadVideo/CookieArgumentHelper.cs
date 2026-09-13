namespace VideoMergeTool.Infrastructure.Features.DownloadVideo;

/// <summary>
/// Builds the yt-dlp cookie arguments. yt-dlp rejects a command line that combines
/// <c>--cookies</c> and <c>--cookies-from-browser</c>, so the cookie file always wins when both are set.
/// </summary>
internal static class CookieArgumentHelper
{
    public static void Add(
        List<string> arguments,
        bool useBrowserCookies,
        string browserName,
        bool useCookieFile,
        string cookieFilePath)
    {
        if (useCookieFile && !string.IsNullOrWhiteSpace(cookieFilePath))
        {
            arguments.Add("--cookies");
            arguments.Add(cookieFilePath);
            return;
        }

        if (useBrowserCookies)
        {
            arguments.Add("--cookies-from-browser");
            arguments.Add(YtDlpChannelAnalyzer.NormalizeBrowser(browserName));
        }
    }
}
