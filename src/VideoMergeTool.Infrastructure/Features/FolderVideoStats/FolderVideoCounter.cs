using VideoMergeTool.Core.Features.FolderVideoStats.Interfaces;
using VideoMergeTool.Core.Features.FolderVideoStats.Models;

namespace VideoMergeTool.Infrastructure.Features.FolderVideoStats;

public sealed class FolderVideoCounter : IFolderVideoCounter
{
    private const string VideoSearchPattern = "*.mp4";
    private const string AnyDirectoryPattern = "*";

    private static readonly EnumerationOptions RecursiveFileOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        AttributesToSkip = 0
    };

    private static readonly EnumerationOptions TopLevelDirectoryOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        MatchCasing = MatchCasing.CaseInsensitive,
        AttributesToSkip = 0
    };

    public Task<FolderVideoCounts> CountAsync(string folderPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"The folder does not exist: {folderPath}");
        }

        // Walking a tree (possibly more than once, for the subfolder breakdown) is blocking I/O.
        return Task.Run(
            () =>
            {
                var total = CountVideos(folderPath, cancellationToken);

                var subfolders = ListOrEmpty(folderPath, AnyDirectoryPattern, TopLevelDirectoryOptions, Directory.EnumerateDirectories)
                    .Select(subfolder => new SubfolderVideoCount(
                        subfolder,
                        Path.GetFileName(subfolder),
                        CountVideos(subfolder, cancellationToken)))
                    .OrderBy(static subfolder => subfolder.FolderName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new FolderVideoCounts(folderPath, total, subfolders);
            },
            cancellationToken);
    }

    private static int CountVideos(string folder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ListOrEmpty(folder, VideoSearchPattern, RecursiveFileOptions, Directory.EnumerateFiles).Count;
    }

    /// <summary>A subfolder that disappears or denies access mid-scan must not abort the whole scan.</summary>
    private static List<string> ListOrEmpty(
        string folder,
        string pattern,
        EnumerationOptions options,
        Func<string, string, EnumerationOptions, IEnumerable<string>> enumerate)
    {
        try
        {
            return enumerate(folder, pattern, options).ToList();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return [];
        }
    }
}
