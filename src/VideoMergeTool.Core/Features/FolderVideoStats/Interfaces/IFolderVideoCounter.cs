using VideoMergeTool.Core.Features.FolderVideoStats.Models;

namespace VideoMergeTool.Core.Features.FolderVideoStats.Interfaces;

public interface IFolderVideoCounter
{
    /// <summary>Counts MP4 files in <paramref name="folderPath"/> and in each of its direct subfolders.</summary>
    Task<FolderVideoCounts> CountAsync(string folderPath, CancellationToken cancellationToken);
}
