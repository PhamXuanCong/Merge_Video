using System.Globalization;

namespace VideoMergeTool.Infrastructure.Features.DownloadVideo;

public static class YtDlpDurationParser
{
    private const string Prefix = "DURATION|";

    public const string OutputTemplate = "DURATION|%(id)s|%(duration)s";

    /// <summary>Parses one line printed with <see cref="OutputTemplate"/>; "NA" and negatives are rejected.</summary>
    public static bool TryParse(string line, out string videoId, out double durationSeconds)
    {
        videoId = string.Empty;
        durationSeconds = 0;

        if (!line.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var durationSeparator = line.IndexOf('|', Prefix.Length);
        if (durationSeparator < 0)
        {
            return false;
        }

        var parsedVideoId = line[Prefix.Length..durationSeparator].Trim();
        var durationText = line[(durationSeparator + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(parsedVideoId) ||
            !double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDuration) ||
            !double.IsFinite(parsedDuration) ||
            parsedDuration < 0)
        {
            return false;
        }

        videoId = parsedVideoId;
        durationSeconds = parsedDuration;
        return true;
    }
}
