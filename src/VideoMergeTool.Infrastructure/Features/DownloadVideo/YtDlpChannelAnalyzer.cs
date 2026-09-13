using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoMergeTool.Core.Features.DownloadVideo;
using VideoMergeTool.Core.Features.DownloadVideo.Enums;
using VideoMergeTool.Core.Features.DownloadVideo.Interfaces;
using VideoMergeTool.Core.Features.DownloadVideo.Models;

namespace VideoMergeTool.Infrastructure.Features.DownloadVideo;

/// <summary>
/// Lists the selected channel tabs through yt-dlp <c>--flat-playlist</c> JSON, without downloading.
/// </summary>
public sealed class YtDlpChannelAnalyzer : IChannelAnalyzer
{
    private const int MinimumLookupDelayMilliseconds = 1000;
    private const int MaximumLookupDelayMilliseconds = 2000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDownloadDependencyService _dependencyService;
    private readonly IDownloadLogger _logger;
    private readonly IVideoDurationCache _durationCache;
    private readonly IVideoMetadataLogger _metadataLogger;
    private readonly IYouTubeDataApiDurationResolver _youTubeDataApiDurationResolver;
    private readonly ProcessRunner _runProcessAsync;
    private readonly Func<int, CancellationToken, Task> _delayAsync;

    public YtDlpChannelAnalyzer(
        IDownloadDependencyService dependencyService,
        IDownloadLogger logger,
        IVideoDurationCache durationCache,
        IVideoMetadataLogger metadataLogger,
        IYouTubeDataApiDurationResolver youTubeDataApiDurationResolver)
        : this(
            dependencyService,
            logger,
            durationCache,
            metadataLogger,
            youTubeDataApiDurationResolver,
            ProcessHelper.RunAsync,
            static (milliseconds, cancellationToken) => Task.Delay(milliseconds, cancellationToken))
    {
    }

    internal YtDlpChannelAnalyzer(
        IDownloadDependencyService dependencyService,
        IDownloadLogger logger,
        IVideoDurationCache durationCache,
        IVideoMetadataLogger metadataLogger,
        IYouTubeDataApiDurationResolver youTubeDataApiDurationResolver,
        ProcessRunner runProcessAsync,
        Func<int, CancellationToken, Task> delayAsync)
    {
        _dependencyService = dependencyService;
        _logger = logger;
        _durationCache = durationCache;
        _metadataLogger = metadataLogger;
        _youTubeDataApiDurationResolver = youTubeDataApiDurationResolver;
        _runProcessAsync = runProcessAsync;
        _delayAsync = delayAsync;
    }

    public async Task<ChannelAnalysisResult> AnalyzeAsync(
        Uri channelBaseUri,
        IReadOnlyCollection<ContentType> contentTypes,
        int recentVideoLimit,
        bool resolveMissingDurations,
        bool useBrowserCookies,
        string browserName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channelBaseUri);
        ArgumentNullException.ThrowIfNull(contentTypes);

        if (contentTypes.Count == 0)
        {
            throw new ArgumentException("At least one content type is required.", nameof(contentTypes));
        }

        var normalizedVideoLimit = Math.Max(0, recentVideoLimit);
        var candidatesById = new Dictionary<string, AnalysisCandidate>(StringComparer.Ordinal);
        var channelName = string.Empty;

        foreach (var contentType in contentTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tabUri = YouTubeUrlHelper.CreateTabUri(channelBaseUri, GetTabName(contentType));
            _logger.Info($"Analyzing {contentType} tab: {tabUri}");

            var playlist = await AnalyzeTabAsync(
                tabUri,
                normalizedVideoLimit,
                useBrowserCookies,
                browserName,
                cancellationToken).ConfigureAwait(false);

            channelName = FirstNonEmpty(
                channelName,
                playlist.Channel,
                playlist.Uploader,
                CleanPlaylistTitle(playlist.Title));

            if (playlist.Entries is null)
            {
                continue;
            }

            foreach (var entry in playlist.Entries)
            {
                // A video listed on several tabs (e.g. videos and streams) is kept once.
                if (entry is null || string.IsNullOrWhiteSpace(entry.Id) || candidatesById.ContainsKey(entry.Id))
                {
                    continue;
                }

                // Only finished streams (replays) can be downloaded.
                if (contentType == ContentType.Stream && entry.LiveStatus is "is_live" or "is_upcoming")
                {
                    _logger.Debug($"Skipping non-replay stream {entry.Id} with status {entry.LiveStatus}.", entry.Id);
                    continue;
                }

                candidatesById.Add(entry.Id, new AnalysisCandidate(entry, contentType));
            }
        }

        // The limit applies across every analyzed tab together, not per tab.
        var selectedCandidates = RecentVideoFilterHelper.TakeMostRecent(
            candidatesById.Values,
            normalizedVideoLimit,
            static candidate => ParseUploadDate(candidate.Entry.UploadDate));

        if (normalizedVideoLimit > 0)
        {
            _logger.Info(
                $"Selected {selectedCandidates.Count} most recent unique videos " +
                $"using the requested limit of {normalizedVideoLimit}.");
        }

        await PopulateMissingDurationsFromYouTubeDataApiAsync(
            selectedCandidates.Select(static candidate => candidate.Entry),
            cancellationToken).ConfigureAwait(false);

        if (resolveMissingDurations)
        {
            await PopulateMissingDurationsAsync(
                selectedCandidates.Select(static candidate => candidate.Entry),
                useBrowserCookies,
                browserName,
                cancellationToken).ConfigureAwait(false);
        }

        var resolvedChannelName = string.IsNullOrWhiteSpace(channelName) ? "Unknown Channel" : channelName;
        var items = selectedCandidates
            .Select(candidate => CreateDownloadItem(candidate.Entry, channelName, candidate.ContentType))
            .ToArray();

        await _metadataLogger.WriteAsync(channelBaseUri, resolvedChannelName, items, cancellationToken)
            .ConfigureAwait(false);

        return new ChannelAnalysisResult(resolvedChannelName, items);
    }

    private async Task<PlaylistDto> AnalyzeTabAsync(
        Uri tabUri,
        int maximumEntries,
        bool useBrowserCookies,
        string browserName,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();
        var startInfo = ProcessHelper.CreateStartInfo(_dependencyService.YtDlpPath);
        var arguments = new List<string>
        {
            "--flat-playlist",
            "--dump-single-json",
            "--ignore-errors",
            "--encoding",
            "utf-8"
        };

        if (maximumEntries > 0)
        {
            arguments.Add("--playlist-end");
            arguments.Add(maximumEntries.ToString(CultureInfo.InvariantCulture));
        }

        if (useBrowserCookies)
        {
            arguments.Add("--cookies-from-browser");
            arguments.Add(NormalizeBrowser(browserName));
        }

        arguments.Add(tabUri.AbsoluteUri);
        ProcessHelper.AddArguments(startInfo, arguments);

        int exitCode;
        try
        {
            exitCode = await _runProcessAsync(
                startInfo,
                line =>
                {
                    output.AppendLine(line);
                    return Task.CompletedTask;
                },
                line =>
                {
                    error.AppendLine(line);
                    if (line.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Warning(line["WARNING:".Length..].Trim());
                    }
                    else
                    {
                        _logger.Debug(line);
                    }

                    return Task.CompletedTask;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new ChannelAnalysisException(
                "Không thể khởi chạy yt-dlp. Hãy kiểm tra dependency và quyền truy cập.",
                exception);
        }

        if (output.Length == 0)
        {
            throw new ChannelAnalysisException(CreateFriendlyAnalysisError(error.ToString(), exitCode));
        }

        try
        {
            var playlist = JsonSerializer.Deserialize<PlaylistDto>(output.ToString(), JsonOptions)
                           ?? throw new ChannelAnalysisException("yt-dlp không trả về metadata kênh.");

            // --ignore-errors lets yt-dlp fail on some entries yet still list the rest.
            if (exitCode != 0)
            {
                _logger.Warning($"yt-dlp analysis completed with exit code {exitCode}; available entries were retained.");
            }

            return playlist;
        }
        catch (JsonException exception)
        {
            _logger.Error("Invalid metadata JSON returned by yt-dlp.", exception: exception);
            throw new ChannelAnalysisException("Metadata kênh không hợp lệ. Hãy cập nhật yt-dlp rồi thử lại.", exception);
        }
    }

    /// <summary>
    /// The browser-cookie fallback: one yt-dlp call per video, in sequence, with a random 1–2 s
    /// pause between calls so YouTube is less likely to rate-limit or block the account.
    /// </summary>
    private async Task PopulateMissingDurationsAsync(
        IEnumerable<PlaylistEntryDto> entries,
        bool useBrowserCookies,
        string browserName,
        CancellationToken cancellationToken)
    {
        var entriesMissingDuration = entries
            .Where(static entry =>
                entry.Duration is null &&
                !string.IsNullOrWhiteSpace(entry.Id) &&
                GetInitialStatus(entry) == DownloadStatus.Ready)
            .GroupBy(static entry => entry.Id!, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();

        if (entriesMissingDuration.Length == 0)
        {
            return;
        }

        var cacheHitCount = 0;
        foreach (var entry in entriesMissingDuration)
        {
            if (_durationCache.TryGet(entry.Id!, out var cachedDuration))
            {
                entry.Duration = cachedDuration;
                cacheHitCount++;
                _logger.Info($"Duration cache hit: {FormatDuration(cachedDuration)}.", entry.Id);
            }
        }

        var entriesToResolve = entriesMissingDuration.Where(static entry => entry.Duration is null).ToArray();

        if (cacheHitCount > 0)
        {
            _logger.Info($"Loaded duration metadata for {cacheHitCount} videos from cache.");
        }

        if (entriesToResolve.Length == 0)
        {
            return;
        }

        // Without cookies every per-video lookup would fail or crawl, so none is attempted.
        if (!useBrowserCookies)
        {
            _logger.Warning(
                $"Duration metadata is missing for {entriesToResolve.Length} videos, but browser cookies are disabled. " +
                "Enable browser cookies and analyze again to resolve them.");
            return;
        }

        _logger.Info(
            $"Resolving missing duration metadata sequentially for {entriesToResolve.Length} videos " +
            $"using {NormalizeBrowser(browserName)} browser cookies.");

        var resolvedCount = cacheHitCount;
        for (var index = 0; index < entriesToResolve.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (index > 0)
            {
                var delayMilliseconds = Random.Shared.Next(
                    MinimumLookupDelayMilliseconds,
                    MaximumLookupDelayMilliseconds + 1);
                await _delayAsync(delayMilliseconds, cancellationToken).ConfigureAwait(false);
            }

            var entry = entriesToResolve[index];
            _logger.Info(
                $"Resolving duration ({index + 1}/{entriesToResolve.Length}) using " +
                $"{NormalizeBrowser(browserName)} browser cookies.",
                entry.Id);
            var lookup = await ResolveDurationAsync(entry, browserName, cancellationToken).ConfigureAwait(false);

            if (lookup.Duration is not null)
            {
                entry.Duration = lookup.Duration.Value;
                resolvedCount++;
                await _durationCache.StoreAsync(entry.Id!, lookup.Duration.Value, cancellationToken).ConfigureAwait(false);
                _logger.Info($"Duration resolved successfully: {FormatDuration(lookup.Duration.Value)}.", entry.Id);
            }
            else if (lookup.AuthenticationRequired)
            {
                // One bot check must not stop the batch; the next video may still succeed.
                _logger.Warning(
                    $"Skipping duration for video {entry.Id} because YouTube requested bot verification; " +
                    "continuing with the next video.",
                    entry.Id);
            }
            else
            {
                _logger.Warning("Duration could not be resolved; continuing with the next video.", entry.Id);
            }
        }

        var unresolvedCount = entriesMissingDuration.Length - resolvedCount;
        if (unresolvedCount > 0)
        {
            _logger.Warning(
                $"Duration metadata remains unavailable for {unresolvedCount} videos; " +
                "they will be hidden while duration filtering is enabled.");
        }
    }

    /// <summary>
    /// Tried first whenever an API key is configured, whatever the filter and cookie settings.
    /// An API, network or JSON failure only logs a warning.
    /// </summary>
    private async Task PopulateMissingDurationsFromYouTubeDataApiAsync(
        IEnumerable<PlaylistEntryDto> entries,
        CancellationToken cancellationToken)
    {
        if (!_youTubeDataApiDurationResolver.IsConfigured)
        {
            _logger.Debug(
                "YouTube Data API key is not configured; duration metadata will use the yt-dlp fallback only when enabled.");
            return;
        }

        var entriesMissingDuration = entries
            .Where(static entry =>
                entry.Duration is null &&
                !string.IsNullOrWhiteSpace(entry.Id) &&
                GetInitialStatus(entry) == DownloadStatus.Ready)
            .ToArray();
        if (entriesMissingDuration.Length == 0)
        {
            return;
        }

        var cacheHitCount = 0;
        foreach (var entry in entriesMissingDuration)
        {
            if (_durationCache.TryGet(entry.Id!, out var cachedDuration))
            {
                entry.Duration = cachedDuration;
                cacheHitCount++;
            }
        }

        if (cacheHitCount > 0)
        {
            _logger.Info($"Loaded duration metadata for {cacheHitCount} videos from cache.");
        }

        var entriesToResolve = entriesMissingDuration.Where(static entry => entry.Duration is null).ToArray();
        if (entriesToResolve.Length == 0)
        {
            return;
        }

        try
        {
            var durations = await _youTubeDataApiDurationResolver.ResolveAsync(
                entriesToResolve.Select(static entry => entry.Id!),
                cancellationToken).ConfigureAwait(false);
            var resolvedCount = 0;
            foreach (var entry in entriesToResolve)
            {
                if (!durations.TryGetValue(entry.Id!, out var duration))
                {
                    continue;
                }

                entry.Duration = duration;
                resolvedCount++;
                await _durationCache.StoreAsync(entry.Id!, duration, cancellationToken).ConfigureAwait(false);
            }

            _logger.Info(
                $"Resolved duration metadata for {resolvedCount}/{entriesToResolve.Length} videos through YouTube Data API.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (YouTubeDataApiException exception)
        {
            _logger.Warning($"YouTube Data API duration lookup failed: {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            _logger.Warning($"Could not connect to YouTube Data API: {exception.Message}");
        }
        catch (JsonException exception)
        {
            _logger.Warning($"YouTube Data API returned invalid metadata JSON: {exception.Message}");
        }
    }

    private async Task<DurationLookupResult> ResolveDurationAsync(
        PlaylistEntryDto entry,
        string browserName,
        CancellationToken cancellationToken)
    {
        double? resolvedDuration = null;
        var authenticationRequired = false;
        var startInfo = ProcessHelper.CreateStartInfo(_dependencyService.YtDlpPath);
        ProcessHelper.AddArguments(
            startInfo,
            [
                "--skip-download",
                "--no-playlist",
                "--ignore-errors",
                "--no-progress",
                "--no-warnings",
                "--encoding",
                "utf-8",
                "--print",
                YtDlpDurationParser.OutputTemplate,
                "--cookies-from-browser",
                NormalizeBrowser(browserName),
                $"https://www.youtube.com/watch?v={Uri.EscapeDataString(entry.Id!)}"
            ]);

        try
        {
            var exitCode = await _runProcessAsync(
                startInfo,
                line =>
                {
                    if (YtDlpDurationParser.TryParse(line, out var videoId, out var duration) &&
                        string.Equals(videoId, entry.Id, StringComparison.Ordinal))
                    {
                        resolvedDuration = duration;
                    }

                    return Task.CompletedTask;
                },
                line =>
                {
                    if (RequiresAuthentication(line))
                    {
                        authenticationRequired = true;
                    }

                    _logger.Debug(line);
                    return Task.CompletedTask;
                },
                cancellationToken).ConfigureAwait(false);

            if (exitCode != 0)
            {
                _logger.Warning(
                    $"yt-dlp duration lookup completed with exit code {exitCode}; available durations were retained.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Warning($"Could not resolve missing duration metadata: {exception.Message}");
        }

        return new DurationLookupResult(resolvedDuration, authenticationRequired);
    }

    private static bool RequiresAuthentication(string message) =>
        message.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("login required", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("cookies are required", StringComparison.OrdinalIgnoreCase);

    private static string FormatDuration(double durationSeconds)
    {
        var roundedSeconds = Math.Max(0, (int)Math.Round(durationSeconds));
        var duration = TimeSpan.FromSeconds(roundedSeconds);
        var seconds = durationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        return duration.TotalHours >= 1
            ? $"{duration:hh\\:mm\\:ss} ({seconds} seconds)"
            : $"{duration:mm\\:ss} ({seconds} seconds)";
    }

    private static DownloadItem CreateDownloadItem(PlaylistEntryDto entry, string channelName, ContentType contentType)
    {
        var status = GetInitialStatus(entry);
        var thumbnail = entry.Thumbnail ??
                        entry.Thumbnails?.LastOrDefault(static item => !string.IsNullOrWhiteSpace(item.Url))?.Url;

        return new DownloadItem
        {
            IsSelected = status == DownloadStatus.Ready,
            VideoId = entry.Id!,
            Title = string.IsNullOrWhiteSpace(entry.Title) ? $"Video {entry.Id}" : entry.Title,
            Url = $"https://www.youtube.com/watch?v={Uri.EscapeDataString(entry.Id!)}",
            ChannelName = channelName,
            ContentType = contentType,
            DurationSeconds = entry.Duration is null ? null : (int?)Math.Max(0, Math.Round(entry.Duration.Value)),
            UploadDate = ParseUploadDate(entry.UploadDate),
            ThumbnailUrl = thumbnail,
            Status = status
        };
    }

    private static DownloadStatus GetInitialStatus(PlaylistEntryDto entry)
    {
        var availability = entry.Availability?.ToLowerInvariant() ?? string.Empty;
        var title = entry.Title ?? string.Empty;

        if (availability.Contains("private", StringComparison.Ordinal) ||
            title.Contains("[Private video]", StringComparison.OrdinalIgnoreCase))
        {
            return DownloadStatus.Private;
        }

        if (availability.Contains("subscriber", StringComparison.Ordinal) ||
            availability.Contains("premium", StringComparison.Ordinal) ||
            availability.Contains("needs_auth", StringComparison.Ordinal))
        {
            return DownloadStatus.RequiresLogin;
        }

        if (availability.Contains("unavailable", StringComparison.Ordinal) ||
            title.Contains("[Deleted video]", StringComparison.OrdinalIgnoreCase))
        {
            return DownloadStatus.Unavailable;
        }

        return DownloadStatus.Ready;
    }

    private static DateTime? ParseUploadDate(string? value) =>
        DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static string CreateFriendlyAnalysisError(string error, int exitCode)
    {
        if (error.Contains("This channel does not exist", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("channel is not available", StringComparison.OrdinalIgnoreCase))
        {
            return "Kênh không tồn tại hoặc không khả dụng.";
        }

        if (error.Contains("Sign in", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("cookies", StringComparison.OrdinalIgnoreCase))
        {
            return "Kênh yêu cầu đăng nhập. Chỉ bật cookie trình duyệt nếu bạn có quyền truy cập.";
        }

        if (error.Contains("Unable to download", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("network", StringComparison.OrdinalIgnoreCase))
        {
            return "Không thể kết nối YouTube. Hãy kiểm tra mạng rồi thử lại.";
        }

        return $"Không thể phân tích kênh (yt-dlp exit code {exitCode}).";
    }

    private static string GetTabName(ContentType contentType) =>
        contentType switch
        {
            ContentType.Video => "videos",
            ContentType.Short => "shorts",
            ContentType.Stream => "streams",
            _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, null)
        };

    internal static string NormalizeBrowser(string browserName)
    {
        var value = browserName.Trim().ToLowerInvariant();
        return value is "chrome" or "edge" or "firefox" ? value : "chrome";
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? string.Empty;

    private static string CleanPlaylistTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        foreach (var suffix in PlaylistTitleSuffixes)
        {
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return title[..^suffix.Length];
            }
        }

        return title;
    }

    private static readonly string[] PlaylistTitleSuffixes = [" - Videos", " - Shorts", " - Live"];

    private sealed class PlaylistDto
    {
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("channel")]
        public string? Channel { get; init; }

        [JsonPropertyName("uploader")]
        public string? Uploader { get; init; }

        [JsonPropertyName("entries")]
        public List<PlaylistEntryDto?>? Entries { get; init; }
    }

    private sealed class PlaylistEntryDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("duration")]
        public double? Duration { get; set; }

        [JsonPropertyName("upload_date")]
        public string? UploadDate { get; init; }

        [JsonPropertyName("thumbnail")]
        public string? Thumbnail { get; init; }

        [JsonPropertyName("thumbnails")]
        public List<ThumbnailDto>? Thumbnails { get; init; }

        [JsonPropertyName("availability")]
        public string? Availability { get; init; }

        [JsonPropertyName("live_status")]
        public string? LiveStatus { get; init; }
    }

    private sealed class ThumbnailDto
    {
        [JsonPropertyName("url")]
        public string? Url { get; init; }
    }

    private sealed record AnalysisCandidate(PlaylistEntryDto Entry, ContentType ContentType);

    private sealed record DurationLookupResult(double? Duration, bool AuthenticationRequired);
}
