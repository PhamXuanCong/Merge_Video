namespace VideoMergeTool.Core.Models;

public sealed class ApplicationPaths
{
    public ApplicationPaths(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);

        ApplicationDirectory = Path.GetFullPath(applicationDirectory);
        ToolsDirectory = Path.Combine(ApplicationDirectory, "Tools");
        FFmpegPath = Path.Combine(ToolsDirectory, "ffmpeg.exe");
        FFprobePath = Path.Combine(ToolsDirectory, "ffprobe.exe");
        YtDlpPath = Path.Combine(ToolsDirectory, "yt-dlp.exe");
        CompanionVideoDirectory = Path.Combine(ApplicationDirectory, "Assets", "CompanionVideos");
        LicenseDirectory = Path.Combine(ApplicationDirectory, "Licenses");
        ReadmePath = Path.Combine(ApplicationDirectory, "README.txt");
    }

    public string ApplicationDirectory { get; }

    public string ToolsDirectory { get; }

    public string FFmpegPath { get; }

    public string FFprobePath { get; }

    /// <summary>Only the YouTube downloader needs it, so it is checked there rather than at startup.</summary>
    public string YtDlpPath { get; }

    public string CompanionVideoDirectory { get; }

    public string LicenseDirectory { get; }

    public string ReadmePath { get; }
}
