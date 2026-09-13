using System.Text.Json;
using VideoMergeTool.Core.Features.DownloadVideo.Interfaces;

namespace VideoMergeTool.Infrastructure.Features.DownloadVideo;

/// <summary>
/// Persists video id → duration (seconds) in <c>durations.json</c>, shared by every channel.
/// </summary>
public sealed class FileVideoDurationCache : IVideoDurationCache, IDisposable
{
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly IDownloadLogger _logger;
    private readonly Dictionary<string, double> _durations;

    public FileVideoDurationCache(IDownloadLogger logger)
        : this(logger, cacheDirectory: null)
    {
    }

    internal FileVideoDurationCache(IDownloadLogger logger, string? cacheDirectory)
    {
        _logger = logger;
        var targetDirectory = string.IsNullOrWhiteSpace(cacheDirectory)
            ? DownloaderDataPaths.Cache
            : Path.GetFullPath(cacheDirectory);
        CachePath = Path.Combine(targetDirectory, "durations.json");
        _durations = LoadCache();
    }

    public string CachePath { get; }

    public bool TryGet(string videoId, out double durationSeconds)
    {
        lock (_syncRoot)
        {
            return _durations.TryGetValue(videoId, out durationSeconds);
        }
    }

    public async Task StoreAsync(string videoId, double durationSeconds, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);

        if (!double.IsFinite(durationSeconds) || durationSeconds < 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_durations.TryGetValue(videoId, out var existingDuration) &&
                Math.Abs(existingDuration - durationSeconds) < 0.001)
            {
                return;
            }

            _durations[videoId] = durationSeconds;
        }

        await PersistAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _writeLock.Dispose();

    private Dictionary<string, double> LoadCache()
    {
        if (!File.Exists(CachePath))
        {
            return new Dictionary<string, double>(StringComparer.Ordinal);
        }

        try
        {
            var json = File.ReadAllText(CachePath);
            var cachedDurations = JsonSerializer.Deserialize<Dictionary<string, double>>(json);
            return cachedDurations?
                .Where(static item =>
                    !string.IsNullOrWhiteSpace(item.Key) &&
                    double.IsFinite(item.Value) &&
                    item.Value >= 0)
                .ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, double>(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Warning($"Could not read duration cache: {exception.Message}");
            return new Dictionary<string, double>(StringComparer.Ordinal);
        }
    }

    /// <summary>Writes a temporary file then swaps it in, so a crash never leaves half a JSON file.</summary>
    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporaryPath = $"{CachePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Dictionary<string, double> snapshot;
            lock (_syncRoot)
            {
                snapshot = new Dictionary<string, double>(_durations, StringComparer.Ordinal);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            var json = JsonSerializer.Serialize(snapshot);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, CachePath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not write duration cache: {exception.Message}");
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
            _writeLock.Release();
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
            System.Diagnostics.Debug.WriteLine($"Unable to delete temporary duration cache file: {exception.Message}");
        }
    }
}
