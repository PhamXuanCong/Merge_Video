using System.Text;
using System.Text.RegularExpressions;

namespace VideoMergeTool.Core.Features.RenameVideo;

/// <summary>
/// Pure naming rules for the bulk renamer: strip the trailing <c>[id]</c> that downloaders such as
/// yt-dlp append, then append the user's hashtags.
/// </summary>
public static partial class VideoNameRules
{
    /// <summary>Extensions treated as videos. Add or remove entries here.</summary>
    public static IReadOnlyList<string> VideoExtensions { get; } =
        [".mp4", ".mov", ".avi", ".mkv", ".webm", ".flv"];

    private const string InvalidFileNameCharacters = "\\/:*?\"<>|";

    [GeneratedRegex(@"\s*\[[^\[\]]*\]$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingIdPattern();

    public static bool IsVideoFile(string fileName) =>
        VideoExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    /// <param name="baseName">File name without its extension.</param>
    public static string RemoveTrailingId(string baseName, out bool idFound)
    {
        var match = TrailingIdPattern().Match(baseName);
        idFound = match.Success;
        return idFound ? baseName[..match.Index] : baseName;
    }

    /// <summary>
    /// Drops characters Windows forbids in file names and collapses the whitespace between tags,
    /// so "  #trend   #fyp " becomes "#trend #fyp".
    /// </summary>
    public static string NormalizeHashtags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                cleaned.Append(' ');
            }
            else if (!InvalidFileNameCharacters.Contains(character))
            {
                cleaned.Append(character);
            }
        }

        return string.Join(' ', cleaned.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Returns the name the video should get, or <paramref name="fileName"/> itself when the rules
    /// change nothing.
    /// </summary>
    /// <param name="normalizedHashtags">Output of <see cref="NormalizeHashtags"/>.</param>
    public static string ComposeName(string fileName, string normalizedHashtags, out bool idFound)
    {
        var extension = Path.GetExtension(fileName);
        var baseName = RemoveTrailingId(Path.GetFileNameWithoutExtension(fileName), out idFound);

        if (normalizedHashtags.Length > 0)
        {
            baseName = baseName.TrimEnd();
            baseName = baseName.Length == 0 ? normalizedHashtags : $"{baseName} {normalizedHashtags}";
        }

        // "[id].mp4" with no hashtag would otherwise be renamed to just ".mp4".
        return baseName.Length == 0 ? fileName : baseName + extension;
    }

    /// <summary>Appends " (1)", " (2)", … before the extension until the name is free.</summary>
    public static string MakeUnique(string fileName, Func<string, bool> isTaken)
    {
        if (!isTaken(fileName))
        {
            return fileName;
        }

        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);

        for (var index = 1; ; index++)
        {
            var candidate = $"{baseName} ({index}){extension}";
            if (!isTaken(candidate))
            {
                return candidate;
            }
        }
    }
}
