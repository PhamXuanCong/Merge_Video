using VideoMergeTool.Core.Features.MergeVideo.Models;

namespace VideoMergeTool.Core.Features.MergeVideo.Interfaces;

public interface IFFprobeService
{
    Task<VideoMetadata> GetMetadataAsync(
        string videoPath,
        CancellationToken cancellationToken);
}
