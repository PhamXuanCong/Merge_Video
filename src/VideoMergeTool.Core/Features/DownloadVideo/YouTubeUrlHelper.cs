namespace VideoMergeTool.Core.Features.DownloadVideo;

public static class YouTubeUrlHelper
{
    private static readonly HashSet<string> SupportedTabs =
        new(StringComparer.OrdinalIgnoreCase) { "videos", "shorts", "streams" };

    /// <summary>
    /// Validates a YouTube channel URL and removes query, fragment and an existing channel tab.
    /// </summary>
    public static bool TryNormalizeChannelUrl(
        string? input,
        out Uri? normalizedUri,
        out string errorMessage)
    {
        normalizedUri = null;
        errorMessage = string.Empty;
        var value = input?.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            errorMessage = "Vui lòng nhập URL kênh YouTube.";
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var sourceUri) ||
            (sourceUri.Scheme != Uri.UriSchemeHttps && sourceUri.Scheme != Uri.UriSchemeHttp))
        {
            errorMessage = "URL phải là địa chỉ HTTP hoặc HTTPS hợp lệ.";
            return false;
        }

        if (!IsYouTubeHost(sourceUri.Host))
        {
            errorMessage = "URL không thuộc miền youtube.com.";
            return false;
        }

        var segments = sourceUri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        if (!TryGetBaseSegments(segments, out var baseSegments, out errorMessage))
        {
            return false;
        }

        var builder = new UriBuilder(Uri.UriSchemeHttps, "www.youtube.com")
        {
            Port = -1,
            Query = string.Empty,
            Fragment = string.Empty,
            Path = "/" + string.Join("/", baseSegments.Select(EscapeChannelPathSegment))
        };

        normalizedUri = builder.Uri;
        return true;
    }

    /// <summary>
    /// Creates a safe absolute URL for the requested channel tab.
    /// </summary>
    public static Uri CreateTabUri(Uri normalizedChannelUri, string tab)
    {
        ArgumentNullException.ThrowIfNull(normalizedChannelUri);

        if (!SupportedTabs.Contains(tab))
        {
            throw new ArgumentOutOfRangeException(nameof(tab), tab, "Unsupported YouTube channel tab.");
        }

        var builder = new UriBuilder(normalizedChannelUri)
        {
            Query = string.Empty,
            Fragment = string.Empty,
            Path = $"{normalizedChannelUri.AbsolutePath.TrimEnd('/')}/{tab.ToLowerInvariant()}"
        };

        return builder.Uri;
    }

    private static bool IsYouTubeHost(string host) =>
        host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);

    private static string EscapeChannelPathSegment(string segment) =>
        segment.StartsWith('@')
            ? $"@{Uri.EscapeDataString(segment[1..])}"
            : Uri.EscapeDataString(segment);

    private static bool TryGetBaseSegments(
        IReadOnlyList<string> segments,
        out string[] baseSegments,
        out string errorMessage)
    {
        baseSegments = [];
        errorMessage = string.Empty;

        if (segments.Count == 0)
        {
            errorMessage = "URL chưa chỉ định kênh YouTube.";
            return false;
        }

        var first = segments[0];
        var expectedBaseLength = first.StartsWith('@') ? 1 :
            first.Equals("channel", StringComparison.OrdinalIgnoreCase) ||
            first.Equals("c", StringComparison.OrdinalIgnoreCase) ||
            first.Equals("user", StringComparison.OrdinalIgnoreCase) ? 2 : 0;

        if (expectedBaseLength == 0)
        {
            errorMessage = first.Equals("watch", StringComparison.OrdinalIgnoreCase) ||
                           first.Equals("shorts", StringComparison.OrdinalIgnoreCase) ||
                           first.Equals("live", StringComparison.OrdinalIgnoreCase)
                ? "Đây là URL video, không phải URL kênh YouTube."
                : "Định dạng URL kênh YouTube không được hỗ trợ.";
            return false;
        }

        if (segments.Count < expectedBaseLength ||
            segments.Take(expectedBaseLength).Any(string.IsNullOrWhiteSpace))
        {
            errorMessage = "URL kênh YouTube thiếu handle hoặc mã kênh.";
            return false;
        }

        if (expectedBaseLength == 1 && first.Length == 1)
        {
            errorMessage = "Handle kênh YouTube không hợp lệ.";
            return false;
        }

        var remaining = segments.Skip(expectedBaseLength).ToArray();
        if (remaining.Length > 1 || (remaining.Length == 1 && !SupportedTabs.Contains(remaining[0])))
        {
            errorMessage = "URL chứa đường dẫn không phải trang kênh YouTube.";
            return false;
        }

        baseSegments = segments.Take(expectedBaseLength).ToArray();
        return true;
    }
}
