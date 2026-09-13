using VideoMergeTool.Core.Features.RenameVideo;
using VideoMergeTool.Core.Features.RenameVideo.Enums;
using VideoMergeTool.Core.Features.RenameVideo.Models;
using VideoMergeTool.Infrastructure.Features.RenameVideo;

namespace VideoMergeTool.Infrastructure.Tests.RenameVideo;

public sealed class VideoRenamerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"VideoMergeTool.Tests.{Guid.NewGuid():N}");
    private readonly VideoRenamer _renamer = new();

    public VideoRenamerTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task RenameAsyncRenamesEveryChangedFileAndLogsEachOne()
    {
        CreateFiles("Con mèo [7123456789012345678].mp4", "No id.mov", "notes.txt");
        var plan = await PlanAsync("#trend");

        var result = await _renamer.RenameAsync(_root, plan, progress: null, CancellationToken.None);

        Assert.False(result.WasCancelled);
        Assert.Equal(2, result.RenamedCount);
        AssertFiles("Con mèo #trend.mp4", "No id #trend.mov", "notes.txt", RenameLogCsv.FileName);

        var log = RenameLogCsv.Read(_root);
        Assert.Equal(2, log.Count);
        Assert.All(log, entry => Assert.Equal(RenameItemStatus.Renamed, entry.Status));
        Assert.Contains(log, entry => entry.OldName == "No id.mov" && entry.Note.Contains("Không tìm thấy ID"));
    }

    [Fact]
    public async Task RenameAsyncSkipsALockedFileAndCarriesOnWithTheRest()
    {
        CreateFiles("a [1].mp4", "b [2].mp4");
        var plan = await PlanAsync("#x");

        RenameBatchResult result;
        using (new FileStream(Path.Combine(_root, "a [1].mp4"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await _renamer.RenameAsync(_root, plan, progress: null, CancellationToken.None);
        }

        var failed = Assert.Single(result.Items, item => item.Status == RenameItemStatus.Failed);
        Assert.Equal("a [1].mp4", failed.OriginalName);
        Assert.Equal("File đang được mở bởi chương trình khác", failed.Note);
        AssertFiles("a [1].mp4", "b #x.mp4", RenameLogCsv.FileName);
        Assert.Contains(RenameLogCsv.Read(_root), entry => entry.Status == RenameItemStatus.Failed);
    }

    [Fact]
    public async Task RenameAsyncNeverOverwritesAFileCreatedAfterThePreview()
    {
        CreateFiles("clip [1].mp4");
        var plan = await PlanAsync("#x");
        CreateFiles("clip #x.mp4");

        var result = await _renamer.RenameAsync(_root, plan, progress: null, CancellationToken.None);

        Assert.Equal("clip #x (1).mp4", Assert.Single(result.Items).NewName);
        AssertFiles("clip #x.mp4", "clip #x (1).mp4", RenameLogCsv.FileName);
    }

    [Fact]
    public async Task RenameAsyncStopsBeforeTheFirstFileWhenAlreadyCancelled()
    {
        CreateFiles("a [1].mp4");
        var plan = await PlanAsync("#x");

        var result = await _renamer.RenameAsync(_root, plan, progress: null, new CancellationToken(canceled: true));

        Assert.True(result.WasCancelled);
        Assert.Empty(result.Items);
        Assert.True(File.Exists(Path.Combine(_root, "a [1].mp4")));
    }

    [Fact]
    public async Task UndoAsyncRestoresTheNewestBatchThenTheOneBeforeIt()
    {
        CreateFiles("a [1].mp4");
        await _renamer.RenameAsync(_root, await PlanAsync("#one"), progress: null, CancellationToken.None);
        await _renamer.RenameAsync(_root, await PlanAsync("#two"), progress: null, CancellationToken.None);
        AssertFiles("a #one #two.mp4", RenameLogCsv.FileName);

        var newest = await _renamer.FindLastUndoableBatchAsync(_root, CancellationToken.None);
        Assert.NotNull(newest);
        var undo = await _renamer.UndoAsync(_root, newest, progress: null, CancellationToken.None);
        Assert.Equal(1, undo.RenamedCount);
        AssertFiles("a #one.mp4", RenameLogCsv.FileName);

        var previous = await _renamer.FindLastUndoableBatchAsync(_root, CancellationToken.None);
        Assert.NotNull(previous);
        Assert.NotEqual(newest.BatchId, previous.BatchId);
        await _renamer.UndoAsync(_root, previous, progress: null, CancellationToken.None);
        AssertFiles("a [1].mp4", RenameLogCsv.FileName);

        Assert.Null(await _renamer.FindLastUndoableBatchAsync(_root, CancellationToken.None));
    }

    [Fact]
    public async Task UndoAsyncLeavesAFileAloneWhenItsOriginalNameIsTakenAndKeepsTheBatchUndoable()
    {
        CreateFiles("a [1].mp4");
        await _renamer.RenameAsync(_root, await PlanAsync("#x"), progress: null, CancellationToken.None);
        CreateFiles("a [1].mp4");

        var batch = await _renamer.FindLastUndoableBatchAsync(_root, CancellationToken.None);
        var result = await _renamer.UndoAsync(_root, batch!, progress: null, CancellationToken.None);

        Assert.Equal(RenameItemStatus.Failed, Assert.Single(result.Items).Status);
        AssertFiles("a [1].mp4", "a #x.mp4", RenameLogCsv.FileName);

        File.Delete(Path.Combine(_root, "a [1].mp4"));
        var retry = await _renamer.FindLastUndoableBatchAsync(_root, CancellationToken.None);
        Assert.Equal(batch!.BatchId, retry?.BatchId);
        await _renamer.UndoAsync(_root, retry!, progress: null, CancellationToken.None);
        AssertFiles("a [1].mp4", RenameLogCsv.FileName);
    }

    [Fact]
    public async Task FindLastUndoableBatchAsyncReturnsNullWithoutALog()
    {
        Assert.Null(await _renamer.FindLastUndoableBatchAsync(_root, CancellationToken.None));
    }

    [Fact]
    public async Task RenameAsyncReportsAnUnwritableLogBeforeRenamingAnything()
    {
        CreateFiles("a [1].mp4");
        var plan = await PlanAsync("#x");

        // A directory where the log file should be makes the log impossible to open.
        Directory.CreateDirectory(Path.Combine(_root, RenameLogCsv.FileName));

        await Assert.ThrowsAnyAsync<Exception>(
            () => _renamer.RenameAsync(_root, plan, progress: null, CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(_root, "a [1].mp4")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void CreateFiles(params string[] names)
    {
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(_root, name), name);
        }
    }

    private async Task<IReadOnlyList<RenamePlanItem>> PlanAsync(string hashtags) =>
        RenamePlanner.Plan(await new RenameFolderScanner().ScanAsync(_root, CancellationToken.None), hashtags);

    private void AssertFiles(params string[] expected)
    {
        var actual = Directory.EnumerateFileSystemEntries(_root).Select(Path.GetFileName).Order(StringComparer.Ordinal);
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }
}
