namespace VideoMergeTool.Core.Features.DownloadVideo.Interfaces;

/// <summary>
/// Stores resolved YouTube video durations so later analyses can avoid another detail request.
/// </summary>
public interface IVideoDurationCache
{
    bool TryGet(string videoId, out double durationSeconds);

    Task StoreAsync(
        string videoId,
        double durationSeconds,
        CancellationToken cancellationToken);
}
