using VideoMergeTool.Core.Models;

namespace VideoMergeTool.Core.Interfaces;

public interface IInputVideoScanner
{
    Task<IReadOnlyList<VideoFileInfo>> ScanAsync(
        string inputFolder,
        CancellationToken cancellationToken);
}
