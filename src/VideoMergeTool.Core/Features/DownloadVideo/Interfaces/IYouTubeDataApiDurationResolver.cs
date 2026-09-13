namespace VideoMergeTool.Core.Features.DownloadVideo.Interfaces;

/// <summary>
/// Resolves public YouTube video durations through the official YouTube Data API.
/// </summary>
public interface IYouTubeDataApiDurationResolver
{
    bool IsConfigured { get; }

    Task<IReadOnlyDictionary<string, double>> ResolveAsync(
        IEnumerable<string> videoIds,
        CancellationToken cancellationToken);
}
