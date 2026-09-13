using VideoMergeTool.Core.Features.RenameVideo.Enums;
using VideoMergeTool.Core.Features.RenameVideo.Models;
using VideoMergeTool.Infrastructure.Features.RenameVideo;

namespace VideoMergeTool.Infrastructure.Tests.RenameVideo;

public sealed class RenameLogCsvTests
{
    [Theory]
    [InlineData("Plain name.mp4", "Plain name #trend.mp4")]
    [InlineData("Comma, and \"quote\".mp4", "Comma, and #fyp.mp4")]
    [InlineData("=HYPERLINK(\"x\").mp4", "-dash @at +plus.mp4")]
    [InlineData("'apostrophe.mp4", "Con mèo 🐱 #trend.mp4")]
    public void FormatThenParseRoundTripsTheExactNames(string oldName, string newName)
    {
        var entry = new RenameLogEntry(
            new DateTime(2026, 9, 13, 14, 5, 9),
            "ab12cd34",
            RenameLogAction.Rename,
            oldName,
            newName,
            RenameItemStatus.Renamed,
            "Không tìm thấy ID");

        Assert.True(RenameLogCsv.TryParseLine(RenameLogCsv.FormatLine(entry), out var parsed));
        Assert.Equal(entry, parsed);
    }

    [Fact]
    public void FormatLineDisarmsNamesThatExcelWouldEvaluateAsFormulas()
    {
        var entry = new RenameLogEntry(DateTime.Now, "b", RenameLogAction.Rename, "=1+1.mp4", "x.mp4", RenameItemStatus.Renamed, string.Empty);

        Assert.Contains(",'=1+1.mp4,", RenameLogCsv.FormatLine(entry));
    }

    [Fact]
    public void TryParseLineRejectsTheHeaderRow()
    {
        Assert.False(RenameLogCsv.TryParseLine("Time,Batch,Action,OldName,NewName,Status,Note", out _));
    }
}
