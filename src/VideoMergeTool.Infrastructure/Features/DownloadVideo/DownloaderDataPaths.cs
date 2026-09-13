namespace VideoMergeTool.Infrastructure.Features.DownloadVideo;

/// <summary>Where the YouTube downloader keeps its logs and caches, under the user's local app data.</summary>
internal static class DownloaderDataPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VideoMergeTool",
        "YouTubeDownloader");

    public static string Logs { get; } = Path.Combine(Root, "Logs");

    public static string Metadata { get; } = Path.Combine(Logs, "Metadata");

    public static string Cache { get; } = Path.Combine(Root, "Cache");
}
