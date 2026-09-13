namespace VideoMergeTool.Core.Features.DownloadVideo.Interfaces;

/// <summary>
/// Reads per-channel yt-dlp archive identifiers so the UI can mark prior downloads.
/// </summary>
public interface IArchiveService
{
    string GetArchivePath(string outputDirectory, string channelName);

    Task<IReadOnlySet<string>> ReadVideoIdsAsync(
        string outputDirectory,
        string channelName,
        CancellationToken cancellationToken);
}
