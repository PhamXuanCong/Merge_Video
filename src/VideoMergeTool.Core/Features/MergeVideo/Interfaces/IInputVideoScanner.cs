using VideoMergeTool.Core.Features.MergeVideo.Models;

namespace VideoMergeTool.Core.Features.MergeVideo.Interfaces;

public interface IInputVideoScanner
{
    Task<IReadOnlyList<VideoFileInfo>> ScanAsync(
        string inputFolder,
        CancellationToken cancellationToken);
}
