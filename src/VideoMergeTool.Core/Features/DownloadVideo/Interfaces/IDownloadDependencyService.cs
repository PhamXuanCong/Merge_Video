using VideoMergeTool.Core.Features.DownloadVideo.Models;

namespace VideoMergeTool.Core.Features.DownloadVideo.Interfaces;

/// <summary>
/// Locates and verifies the bundled yt-dlp and FFmpeg executables the downloader needs.
/// </summary>
public interface IDownloadDependencyService
{
    string ToolsDirectory { get; }

    string YtDlpPath { get; }

    Task<DependencyCheckResult> CheckAsync(CancellationToken cancellationToken);
}
