using VideoMergeTool.Infrastructure.Features.MergeVideo;

namespace VideoMergeTool.Infrastructure.Tests;

public sealed class CompanionVideoProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"VideoMergeTool.Companions.{Guid.NewGuid():N}");

    [Fact]
    public async Task GetVideosAsyncReturnsMp4FilesInAStableOrder()
    {
        Directory.CreateDirectory(Path.Combine(_root, "nested"));
        File.WriteAllText(Path.Combine(_root, "b.mp4"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "a.mp4"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "nested", "c.MP4"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "notes.txt"), string.Empty);

        var videos = await new CompanionVideoProvider(_root).GetVideosAsync(CancellationToken.None);

        Assert.Equal(3, videos.Count);
        Assert.Equal(videos.OrderBy(path => path, StringComparer.OrdinalIgnoreCase), videos);
    }

    [Fact]
    public async Task GetVideosAsyncReusesTheListingInsteadOfRescanningTheAssetsFolder()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "a.mp4"), string.Empty);
        var provider = new CompanionVideoProvider(_root);

        var first = await provider.GetVideosAsync(CancellationToken.None);

        // The bundled assets are read-only at runtime, so a later change must not be picked up.
        File.WriteAllText(Path.Combine(_root, "b.mp4"), string.Empty);
        var second = await provider.GetVideosAsync(CancellationToken.None);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetVideosAsyncReturnsEmptyWhenTheFolderIsMissing()
    {
        var videos = await new CompanionVideoProvider(Path.Combine(_root, "missing"))
            .GetVideosAsync(CancellationToken.None);

        Assert.Empty(videos);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
