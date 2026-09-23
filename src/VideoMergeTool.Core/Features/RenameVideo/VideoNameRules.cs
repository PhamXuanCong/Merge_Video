using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VideoMergeTool.Core.Features.RenameVideo;

/// <summary>
/// Pure naming rules for the bulk renamer: strip the trailing <c>[id]</c> that downloaders such as
/// yt-dlp append, optionally drop a number of leading characters, then append the user's hashtags.
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
    /// Removes the first <paramref name="count"/> characters, then any spaces left at the front.
    /// Characters are counted as the user sees them, so a letter with combining accents counts once.
    /// </summary>
    public static string RemoveLeadingCharacters(string baseName, int count)
    {
        if (count <= 0 || baseName.Length == 0)
        {
            return baseName;
        }

        var index = 0;
        for (var removed = 0; removed < count && index < baseName.Length; removed++)
        {
            index += StringInfo.GetNextTextElementLength(baseName, index);
        }

        return baseName[index..].TrimStart();
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
    /// <param name="removeLeadingCount">Characters to drop from the start of the name; the extension is never touched.</param>
    public static string ComposeName(string fileName, string normalizedHashtags, out bool idFound, int removeLeadingCount = 0)
    {
        var extension = Path.GetExtension(fileName);
        var baseName = RemoveTrailingId(Path.GetFileNameWithoutExtension(fileName), out idFound);
        baseName = RemoveLeadingCharacters(baseName, removeLeadingCount);

        if (normalizedHashtags.Length > 0)
        {
            baseName = baseName.TrimEnd();
            baseName = baseName.Length == 0 ? normalizedHashtags : $"{baseName} {normalizedHashtags}";
        }

        // "[id].mp4" with no hashtag, or a name shorter than the characters removed, would otherwise
        // be renamed to just ".mp4".
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
