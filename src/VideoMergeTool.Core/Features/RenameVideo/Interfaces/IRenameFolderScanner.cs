using VideoMergeTool.Core.Features.RenameVideo.Models;

namespace VideoMergeTool.Core.Features.RenameVideo.Interfaces;

public interface IRenameFolderScanner
{
    /// <summary>Lists the folder's own entries; subfolders are not searched.</summary>
    Task<RenameFolderSnapshot> ScanAsync(string folder, CancellationToken cancellationToken);
}
