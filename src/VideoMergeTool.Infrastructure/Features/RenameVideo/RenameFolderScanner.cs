using VideoMergeTool.Core.Features.RenameVideo;
using VideoMergeTool.Core.Features.RenameVideo.Interfaces;
using VideoMergeTool.Core.Features.RenameVideo.Models;

namespace VideoMergeTool.Infrastructure.Features.RenameVideo;

public sealed class RenameFolderScanner : IRenameFolderScanner
{
    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = 0
    };

    public Task<RenameFolderSnapshot> ScanAsync(string folder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"Thư mục không tồn tại: {folder}");
        }

        return Task.Run(
            () =>
            {
                var entryNames = new List<string>();
                var videoFileNames = new List<string>();

                foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", EnumerationOptions))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    entryNames.Add(entry.Name);
                    if (entry is FileInfo && VideoNameRules.IsVideoFile(entry.Name))
                    {
                        videoFileNames.Add(entry.Name);
                    }
                }

                videoFileNames.Sort(StringComparer.OrdinalIgnoreCase);
                return new RenameFolderSnapshot(folder, videoFileNames, entryNames);
            },
            cancellationToken);
    }
}
