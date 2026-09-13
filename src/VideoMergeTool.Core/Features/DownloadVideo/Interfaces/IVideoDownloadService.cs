using VideoMergeTool.Core.Features.DownloadVideo.Models;

namespace VideoMergeTool.Core.Features.DownloadVideo.Interfaces;

/// <summary>
/// Downloads one item at a time and reports machine-readable yt-dlp progress.
/// </summary>
public interface IVideoDownloadService
{
    /// <summary>
    /// Creates the output directory if needed and proves it is writable by writing a probe file, so a
    /// permission problem surfaces before the queue starts rather than halfway through it.
    /// </summary>
    /// <returns>The full path of the directory.</returns>
    /// <exception cref="IOException">The directory cannot be created or written.</exception>
    /// <exception cref="UnauthorizedAccessException">The directory cannot be created or written.</exception>
    /// <exception cref="ArgumentException">The path is invalid.</exception>
    /// <exception cref="NotSupportedException">The path format is not supported.</exception>
    Task<string> PrepareOutputDirectoryAsync(string outputDirectory, CancellationToken cancellationToken);

    Task<DownloadResult> DownloadAsync(
        DownloadItem item,
        DownloadOptions options,
        IProgress<DownloadProgressMessage> progress,
        CancellationToken cancellationToken);
}
