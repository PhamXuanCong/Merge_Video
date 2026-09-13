using VideoMergeTool.Infrastructure.Features.MergeVideo;

namespace VideoMergeTool.Infrastructure.Tests;

public sealed class InputVideoScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"VideoMergeTool.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task ScanAsyncReturnsOnlyEligibleMp4FilesWithRelativePaths()
    {
        Directory.CreateDirectory(Path.Combine(_root, "nested"));
        Directory.CreateDirectory(Path.Combine(_root, "output"));
        Directory.CreateDirectory(Path.Combine(_root, "processed"));
        File.WriteAllText(Path.Combine(_root, "root.mp4"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "nested", "child.MP4"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "nested", "partial.processing.mp4"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "output", "ignored.mp4"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "processed", "ignored.mp4"), string.Empty);

        var result = await new InputVideoScanner().ScanAsync(_root, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, video => video.RelativePath == "root.mp4");
        Assert.Contains(result, video => video.RelativePath == Path.Combine("nested", "child.MP4"));
    }

    [Fact]
    public async Task ScanAsyncReportsAMissingInputFolderInsteadOfReturningNothing()
    {
        var missing = Path.Combine(_root, "does-not-exist");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => new InputVideoScanner().ScanAsync(missing, CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
