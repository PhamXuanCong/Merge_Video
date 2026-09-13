using VideoMergeTool.Core.Features.DownloadVideo.Models;

namespace VideoMergeTool.Core.Features.DownloadVideo.Interfaces;

/// <summary>
/// Writes a structured, non-sensitive snapshot of analyzed video metadata.
/// </summary>
public interface IVideoMetadataLogger
{
    Task WriteAsync(
        Uri channelUri,
        string channelName,
        IReadOnlyList<DownloadItem> items,
        CancellationToken cancellationToken);
}
