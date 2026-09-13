using System.Text.Json;
using System.Text.Json.Serialization;
using VideoMergeTool.Core.Features.DownloadVideo.Enums;
using VideoMergeTool.Core.Features.DownloadVideo.Interfaces;
using VideoMergeTool.Core.Features.DownloadVideo.Models;

namespace VideoMergeTool.Infrastructure.Features.DownloadVideo;

/// <summary>
/// Writes one new JSON snapshot per completed channel analysis, for manual lookup and debugging.
/// </summary>
public sealed class FileVideoMetadataLogger : IVideoMetadataLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IDownloadLogger _logger;

    public FileVideoMetadataLogger(IDownloadLogger logger)
        : this(logger, logDirectory: null)
    {
    }

    internal FileVideoMetadataLogger(IDownloadLogger logger, string? logDirectory)
    {
        _logger = logger;
        LogDirectory = string.IsNullOrWhiteSpace(logDirectory)
            ? DownloaderDataPaths.Metadata
            : Path.GetFullPath(logDirectory);
    }

    public string LogDirectory { get; }

    public async Task WriteAsync(
        Uri channelUri,
        string channelName,
        IReadOnlyList<DownloadItem> items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channelUri);
        ArgumentNullException.ThrowIfNull(items);

        var snapshot = new MetadataSnapshot(
            SchemaVersion: 1,
            AnalyzedAtUtc: DateTimeOffset.UtcNow,
            ChannelName: channelName,
            ChannelUrl: channelUri.AbsoluteUri,
            VideoCount: items.Count,
            Videos: items.Select(static item => new VideoMetadata(
                item.VideoId,
                item.Title,
                item.Url,
                item.ChannelName,
                item.ContentType,
                item.DurationSeconds,
                item.UploadDate,
                item.ThumbnailUrl,
                item.Status)).ToArray());

        var fileName = $"metadata-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json";
        var targetPath = Path.Combine(LogDirectory, fileName);
        var temporaryPath = $"{targetPath}.tmp";

        try
        {
            Directory.CreateDirectory(LogDirectory);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, targetPath);
            _logger.Info($"Video metadata JSON written: {targetPath}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Warning($"Could not write video metadata JSON: {exception.Message}");
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to delete temporary metadata log: {exception.Message}");
        }
    }

    private sealed record MetadataSnapshot(
        int SchemaVersion,
        DateTimeOffset AnalyzedAtUtc,
        string ChannelName,
        string ChannelUrl,
        int VideoCount,
        IReadOnlyList<VideoMetadata> Videos);

    private sealed record VideoMetadata(
        string VideoId,
        string Title,
        string Url,
        string ChannelName,
        ContentType ContentType,
        int? DurationSeconds,
        DateTime? UploadDate,
        string? ThumbnailUrl,
        DownloadStatus Status);
}
