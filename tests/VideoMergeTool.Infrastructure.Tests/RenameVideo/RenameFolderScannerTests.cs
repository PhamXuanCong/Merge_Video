using VideoMergeTool.Infrastructure.Features.RenameVideo;

namespace VideoMergeTool.Infrastructure.Tests.RenameVideo;

public sealed class RenameFolderScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"VideoMergeTool.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task ScanAsyncListsOnlyVideosDirectlyInsideTheFolder()
    {
        Directory.CreateDirectory(Path.Combine(_root, "nested"));
        File.WriteAllText(Path.Combine(_root, "b [2].MOV"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "a [1].mp4"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "notes.txt"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "nested", "child [3].mp4"), string.Empty);

        var snapshot = await new RenameFolderScanner().ScanAsync(_root, CancellationToken.None);

        string[] expected = ["a [1].mp4", "b [2].MOV"];
        Assert.Equal(expected, snapshot.VideoFileNames);
        Assert.Contains("notes.txt", snapshot.EntryNames);
        Assert.Contains("nested", snapshot.EntryNames);
    }

    [Fact]
    public async Task ScanAsyncReportsAMissingFolder()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => new RenameFolderScanner().ScanAsync(Path.Combine(_root, "missing"), CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
