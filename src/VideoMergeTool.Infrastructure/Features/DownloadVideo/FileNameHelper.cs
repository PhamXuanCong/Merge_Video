using VideoMergeTool.Core.Features.DownloadVideo.Enums;

namespace VideoMergeTool.Infrastructure.Features.DownloadVideo;

public static class FileNameHelper
{
    private const int MaxChannelFolderLength = 80;

    private static readonly HashSet<string> ReservedWindowsNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

    public static string SanitizeDirectoryName(string? value, string fallback = "Unknown Channel")
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = string.Concat(source.Select(character =>
            invalid.Contains(character) || char.IsControl(character) ? '_' : character));
        cleaned = cleaned.Trim().TrimEnd('.', ' ');

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = fallback;
        }

        if (ReservedWindowsNames.Contains(cleaned))
        {
            cleaned = $"_{cleaned}";
        }

        return cleaned.Length <= MaxChannelFolderLength
            ? cleaned
            : cleaned[..MaxChannelFolderLength].TrimEnd('.', ' ');
    }

    public static string GetChannelDirectory(string outputDirectory, string? channelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        return Path.Combine(Path.GetFullPath(outputDirectory), SanitizeDirectoryName(channelName));
    }

    public static string GetContentFolderName(ContentType contentType) =>
        contentType switch
        {
            ContentType.Video => "Videos",
            ContentType.Short => "Shorts",
            ContentType.Stream => "Streams",
            _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, null)
        };

    /// <summary>
    /// yt-dlp output template: <c>YYYY-MM-DD - title.ext</c>. The title is capped at 140 bytes to
    /// stay clear of the Windows path limit, and the video id is intentionally not appended.
    /// </summary>
    public static string BuildOutputTemplate(string channelDirectory, ContentType contentType)
    {
        var contentDirectory = Path.Combine(channelDirectory, GetContentFolderName(contentType));
        return Path.Combine(contentDirectory, "%(upload_date>%Y-%m-%d)s - %(title).140B.%(ext)s");
    }
}
