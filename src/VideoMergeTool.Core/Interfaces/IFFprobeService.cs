using VideoMergeTool.Core.Models;

namespace VideoMergeTool.Core.Interfaces;

public interface IFFprobeService
{
    Task<VideoMetadata> GetMetadataAsync(
        string videoPath,
        CancellationToken cancellationToken);
}
