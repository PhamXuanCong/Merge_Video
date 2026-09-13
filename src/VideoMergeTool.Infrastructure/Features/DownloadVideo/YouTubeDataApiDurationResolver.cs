using System.Text.Json;
using System.Text.Json.Serialization;
using VideoMergeTool.Core.Features.DownloadVideo.Interfaces;

namespace VideoMergeTool.Infrastructure.Features.DownloadVideo;

/// <summary>
/// Reads public video durations from YouTube Data API v3 (<c>videos.list?part=contentDetails</c>)
/// without browser cookies.
/// </summary>
public sealed class YouTubeDataApiDurationResolver : IYouTubeDataApiDurationResolver
{
    public const string ApiKeyEnvironmentVariable = "YOUTUBE_DATA_API_KEY";

    private const int MaximumVideoIdsPerRequest = 50;

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    /// <param name="configuredApiKey">
    /// Key from appsettings.json; the <see cref="ApiKeyEnvironmentVariable"/> environment variable wins over it.
    /// </param>
    public YouTubeDataApiDurationResolver(HttpClient httpClient, string? configuredApiKey)
        : this(FirstNonEmpty(Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable), configuredApiKey), httpClient)
    {
    }

    /// <summary>Uses exactly <paramref name="apiKey"/>, ignoring the environment.</summary>
    internal YouTubeDataApiDurationResolver(string? apiKey, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _apiKey = apiKey?.Trim() ?? string.Empty;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    /// <exception cref="YouTubeDataApiException">The API answered with an error status.</exception>
    public async Task<IReadOnlyDictionary<string, double>> ResolveAsync(
        IEnumerable<string> videoIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(videoIds);

        if (!IsConfigured)
        {
            return new Dictionary<string, double>(StringComparer.Ordinal);
        }

        var uniqueIds = videoIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var durations = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var idBatch in uniqueIds.Chunk(MaximumVideoIdsPerRequest))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var response = await _httpClient.GetAsync(
                CreateRequestUri(idBatch),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                // The message is logged, so it must never contain the request URI and its key.
                throw new YouTubeDataApiException(
                    $"YouTube Data API returned {(int)response.StatusCode} ({response.ReasonPhrase}). " +
                    ExtractErrorMessage(error));
            }

            await using var responseStream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync<VideoListResponse>(
                responseStream,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var video in payload?.Items ?? [])
            {
                if (!string.IsNullOrWhiteSpace(video.Id) &&
                    YouTubeApiDurationParser.TryParse(video.ContentDetails?.Duration, out var duration))
                {
                    durations[video.Id] = duration;
                }
            }
        }

        return durations;
    }

    private Uri CreateRequestUri(IEnumerable<string> videoIds)
    {
        var ids = string.Join(",", videoIds.Select(Uri.EscapeDataString));
        return new Uri(
            "https://www.googleapis.com/youtube/v3/videos" +
            $"?part=contentDetails&id={ids}&key={Uri.EscapeDataString(_apiKey)}");
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? string.Empty;

    private static string ExtractErrorMessage(string value)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ApiErrorResponse>(value);
            return error?.Error?.Message?.Trim() ?? "No additional error details were returned.";
        }
        catch (JsonException)
        {
            return "No additional error details were returned.";
        }
    }

    private sealed class VideoListResponse
    {
        [JsonPropertyName("items")]
        public List<VideoDto>? Items { get; init; }
    }

    private sealed class VideoDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("contentDetails")]
        public ContentDetailsDto? ContentDetails { get; init; }
    }

    private sealed class ContentDetailsDto
    {
        [JsonPropertyName("duration")]
        public string? Duration { get; init; }
    }

    private sealed class ApiErrorResponse
    {
        [JsonPropertyName("error")]
        public ApiErrorDto? Error { get; init; }
    }

    private sealed class ApiErrorDto
    {
        [JsonPropertyName("message")]
        public string? Message { get; init; }
    }
}

public sealed class YouTubeDataApiException : Exception
{
    public YouTubeDataApiException(string message)
        : base(message)
    {
    }
}
