using VideoMergeTool.Core.Features.DownloadVideo.Interfaces;

namespace VideoMergeTool.Infrastructure.Features.DownloadVideo;

/// <summary>
/// Reads the standard yt-dlp <c>--download-archive</c> file kept in each channel directory.
/// </summary>
public sealed class ArchiveService : IArchiveService
{
    private readonly IDownloadLogger _logger;

    public ArchiveService(IDownloadLogger logger)
    {
        _logger = logger;
    }

    public string GetArchivePath(string outputDirectory, string channelName) =>
        Path.Combine(FileNameHelper.GetChannelDirectory(outputDirectory, channelName), "archive.txt");

    /// <summary>Best effort: an unreadable archive yields no ids instead of failing the analysis.</summary>
    public async Task<IReadOnlySet<string>> ReadVideoIdsAsync(
        string outputDirectory,
        string channelName,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var archivePath = GetArchivePath(outputDirectory, channelName);
            if (!File.Exists(archivePath))
            {
                return ids;
            }

            // yt-dlp may be appending to the archive at the same time.
            await using var stream = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                var parts = line.Split(
                    [' ', '\t'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length > 0)
                {
                    ids.Add(parts[^1]);
                }
            }
        }
        catch (IOException exception)
        {
            _logger.Warning($"Could not read download archive: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Warning($"Download archive is not accessible: {exception.Message}");
        }
        catch (ArgumentException exception)
        {
            _logger.Warning($"Download archive path is invalid: {exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            _logger.Warning($"Download archive path is unsupported: {exception.Message}");
        }

        return ids;
    }
}
