using System.Globalization;
using System.Text.RegularExpressions;
using VideoMergeTool.Core.Features.DownloadVideo.Enums;
using VideoMergeTool.Core.Features.DownloadVideo.Models;

namespace VideoMergeTool.Infrastructure.Features.DownloadVideo;

public static partial class YtDlpOutputParser
{
    /// <summary>
    /// Parses the fixed <c>START|…</c>, <c>PROGRESS|…</c> and <c>COMPLETE|…</c> templates the
    /// download service asks yt-dlp to print. Splitting with a part limit keeps any pipe that
    /// appears inside a title or path.
    /// </summary>
    public static bool TryParse(string? rawLine, out DownloadProgressMessage message)
    {
        message = new DownloadProgressMessage(ProgressMessageType.Debug, string.Empty, string.Empty);
        if (string.IsNullOrWhiteSpace(rawLine))
        {
            return false;
        }

        var line = AnsiEscapeRegex().Replace(rawLine.Trim(), string.Empty);

        if (line.StartsWith("START|", StringComparison.Ordinal))
        {
            var parts = line.Split('|', 3);
            if (parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                message = new DownloadProgressMessage(ProgressMessageType.Start, parts[1], parts[2]);
                return true;
            }
        }

        if (line.StartsWith("PROGRESS|", StringComparison.Ordinal))
        {
            var parts = line.Split('|', 5);
            if (parts.Length == 5 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                var percentText = parts[2].Trim().TrimEnd('%').Trim();
                _ = double.TryParse(percentText, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent);

                message = new DownloadProgressMessage(
                    ProgressMessageType.Progress,
                    parts[1],
                    line,
                    Math.Clamp(percent, 0, 100),
                    parts[3].Trim(),
                    parts[4].Trim());
                return true;
            }
        }

        if (line.StartsWith("COMPLETE|", StringComparison.Ordinal))
        {
            var parts = line.Split('|', 3);
            if (parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                message = new DownloadProgressMessage(
                    ProgressMessageType.Complete,
                    parts[1],
                    parts[2],
                    100,
                    OutputFilePath: parts[2]);
                return true;
            }
        }

        if (TryExtractDiagnostic(line, "WARNING:", out var warning))
        {
            message = new DownloadProgressMessage(ProgressMessageType.Warning, string.Empty, warning);
            return true;
        }

        if (TryExtractDiagnostic(line, "ERROR:", out var error))
        {
            message = new DownloadProgressMessage(ProgressMessageType.Error, string.Empty, error);
            return true;
        }

        return false;
    }

    /// <summary>Accepts the marker with or without a leading <c>[extractor] </c> label.</summary>
    private static bool TryExtractDiagnostic(string line, string marker, out string diagnostic)
    {
        var candidate = line;
        if (candidate.StartsWith('['))
        {
            var prefixEnd = candidate.IndexOf("] ", StringComparison.Ordinal);
            if (prefixEnd >= 0)
            {
                candidate = candidate[(prefixEnd + 2)..];
            }
        }

        if (candidate.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            diagnostic = candidate[marker.Length..].Trim();
            return true;
        }

        diagnostic = string.Empty;
        return false;
    }

    [GeneratedRegex(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])")]
    private static partial Regex AnsiEscapeRegex();
}
