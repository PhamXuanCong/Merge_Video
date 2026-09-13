using VideoMergeTool.Core.Features.MergeVideo.Interfaces;
using VideoMergeTool.Core.Features.MergeVideo.Models;

namespace VideoMergeTool.Infrastructure.Features.MergeVideo;

public sealed class InputVideoScanner : IInputVideoScanner
{
    private const string ProcessingSuffix = ".processing.mp4";

    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Matches the previous behaviour (case-insensitive, hidden files included) but skips
    /// entries the process cannot read instead of failing the whole scan.
    /// </summary>
    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        MatchCasing = MatchCasing.CaseInsensitive,
        AttributesToSkip = 0
    };

    public Task<IReadOnlyList<VideoFileInfo>> ScanAsync(
        string inputFolder,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFolder);

        if (!Directory.Exists(inputFolder))
        {
            throw new DirectoryNotFoundException($"The input folder does not exist: {inputFolder}");
        }

        // Walking a large tree is blocking I/O; keep it off the caller's thread so the UI stays responsive.
        return Task.Run<IReadOnlyList<VideoFileInfo>>(
            () =>
            {
                var videos = new List<VideoFileInfo>();
                ScanDirectory(inputFolder, inputFolder, videos, cancellationToken);
                return videos;
            },
            cancellationToken);
    }

    private static void ScanDirectory(
        string rootFolder,
        string currentFolder,
        ICollection<VideoFileInfo> videos,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var directory in ListOrEmpty(currentFolder, "*", Directory.EnumerateDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsExcludedDirectory(directory))
            {
                ScanDirectory(rootFolder, directory, videos, cancellationToken);
            }
        }

        foreach (var file in ListOrEmpty(currentFolder, "*.mp4", Directory.EnumerateFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(file);
            if (fileName.EndsWith(ProcessingSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            videos.Add(new VideoFileInfo(
                file,
                fileName,
                Path.GetRelativePath(rootFolder, file)));
        }
    }

    /// <summary>
    /// A folder that disappears or denies access mid-scan must not abort the whole scan.
    /// </summary>
    private static List<string> ListOrEmpty(
        string folder,
        string pattern,
        Func<string, string, EnumerationOptions, IEnumerable<string>> enumerate)
    {
        try
        {
            return enumerate(folder, pattern, EnumerationOptions).ToList();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return [];
        }
    }

    private static bool IsExcludedDirectory(string directoryPath)
    {
        var name = Path.GetFileName(directoryPath);
        return PathComparer.Equals(name, "output") || PathComparer.Equals(name, "processed");
    }
}
