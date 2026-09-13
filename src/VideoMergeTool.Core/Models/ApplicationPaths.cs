namespace VideoMergeTool.Core.Models;

public sealed class ApplicationPaths
{
    public ApplicationPaths(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);

        ApplicationDirectory = Path.GetFullPath(applicationDirectory);
        FFmpegPath = Path.Combine(ApplicationDirectory, "Tools", "ffmpeg.exe");
        FFprobePath = Path.Combine(ApplicationDirectory, "Tools", "ffprobe.exe");
        CompanionVideoDirectory = Path.Combine(ApplicationDirectory, "Assets", "CompanionVideos");
        LicenseDirectory = Path.Combine(ApplicationDirectory, "Licenses");
        ReadmePath = Path.Combine(ApplicationDirectory, "README.txt");
    }

    public string ApplicationDirectory { get; }

    public string FFmpegPath { get; }

    public string FFprobePath { get; }

    public string CompanionVideoDirectory { get; }

    public string LicenseDirectory { get; }

    public string ReadmePath { get; }
}
