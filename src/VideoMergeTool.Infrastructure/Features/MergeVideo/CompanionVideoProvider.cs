using VideoMergeTool.Core.Features.MergeVideo.Interfaces;
using VideoMergeTool.Core.Features.MergeVideo.Models;
using VideoMergeTool.Core.Models;

namespace VideoMergeTool.Infrastructure.Features.MergeVideo;

public sealed class CompanionVideoProvider : ICompanionVideoProvider
{
    private readonly string _companionVideosDirectory;

    /// <summary>
    /// The bundled companion videos are read-only for the lifetime of the process, so the
    /// listing is enumerated once and reused for every batch.
    /// </summary>
    private IReadOnlyList<string>? _cachedVideos;

    public CompanionVideoProvider(ApplicationPaths paths)
        : this(paths.CompanionVideoDirectory)
    {
    }

    public CompanionVideoProvider(string companionVideosDirectory)
    {
        _companionVideosDirectory = companionVideosDirectory;
    }

    public Task<IReadOnlyList<string>> GetVideosAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cached = _cachedVideos;
        if (cached is not null)
        {
            return Task.FromResult(cached);
        }

        if (!Directory.Exists(_companionVideosDirectory))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var videos = Directory
            .EnumerateFiles(_companionVideosDirectory, "*.mp4", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Only cache a non-empty listing so a directory that appears later is still picked up.
        if (videos.Length > 0)
        {
            _cachedVideos = videos;
        }

        return Task.FromResult<IReadOnlyList<string>>(videos);
    }
}
