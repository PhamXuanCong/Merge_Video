using VideoMergeTool.Core.Features.DownloadVideo.Enums;
using VideoMergeTool.Infrastructure.Features.DownloadVideo;

namespace VideoMergeTool.Infrastructure.Tests.DownloadVideo;

public sealed class YtDlpParserTests
{
    [Fact]
    public void TryParseReadsProgressLines()
    {
        Assert.True(YtDlpOutputParser.TryParse("PROGRESS|abc123| 32.4%|4.5MiB/s|00:52", out var progress));

        Assert.Equal(ProgressMessageType.Progress, progress.Type);
        Assert.Equal("abc123", progress.VideoId);
        Assert.Equal(32.4, progress.Percent!.Value, precision: 3);
        Assert.Equal("4.5MiB/s", progress.Speed);
        Assert.Equal("00:52", progress.Eta);
    }

    [Fact]
    public void TryParseKeepsPipesInsideTitlesAndPaths()
    {
        Assert.True(YtDlpOutputParser.TryParse("START|abc123|A title | with pipes", out var start));
        Assert.Equal(ProgressMessageType.Start, start.Type);
        Assert.Equal("A title | with pipes", start.Message);

        Assert.True(YtDlpOutputParser.TryParse(@"COMPLETE|abc123|D:\Videos\A | title.mp4", out var complete));
        Assert.Equal(ProgressMessageType.Complete, complete.Type);
        Assert.Equal(@"D:\Videos\A | title.mp4", complete.OutputFilePath);
    }

    [Fact]
    public void TryParseStripsAnsiColoursAndExtractorLabels()
    {
        Assert.True(YtDlpOutputParser.TryParse("\u001b[0;31mERROR:\u001b[0m [youtube] abc: Private video", out var error));
        Assert.Equal(ProgressMessageType.Error, error.Type);
        Assert.Equal("[youtube] abc: Private video", error.Message);

        Assert.True(YtDlpOutputParser.TryParse("[download] WARNING: slow connection", out var warning));
        Assert.Equal(ProgressMessageType.Warning, warning.Type);
        Assert.Equal("slow connection", warning.Message);
    }

    [Theory]
    [InlineData("arbitrary output")]
    [InlineData("PROGRESS|abc123|50%")]
    [InlineData("")]
    public void TryParseLeavesOtherLinesUnrecognised(string line)
    {
        Assert.False(YtDlpOutputParser.TryParse(line, out _));
    }

    [Fact]
    public void DurationParserReadsTheDurationTemplate()
    {
        Assert.True(YtDlpDurationParser.TryParse("DURATION|abc123|48.5", out var videoId, out var duration));
        Assert.Equal("abc123", videoId);
        Assert.Equal(48.5, duration, precision: 3);
    }

    [Theory]
    [InlineData("DURATION|abc123|NA")]
    [InlineData("DURATION|abc123|-1")]
    [InlineData("DURATION||12")]
    [InlineData("abc123|12")]
    public void DurationParserRejectsUnknownOrInvalidDurations(string line)
    {
        Assert.False(YtDlpDurationParser.TryParse(line, out _, out _));
    }

    [Theory]
    [InlineData("PT1H2M3S", 3723)]
    [InlineData("PT48S", 48)]
    [InlineData("P0D", 0)]
    public void YouTubeApiDurationParserReadsIso8601(string value, double expectedSeconds)
    {
        Assert.True(YouTubeApiDurationParser.TryParse(value, out var duration));
        Assert.Equal(expectedSeconds, duration);
    }

    [Theory]
    [InlineData("not-a-duration")]
    [InlineData("")]
    [InlineData(null)]
    public void YouTubeApiDurationParserRejectsInvalidValues(string? value)
    {
        Assert.False(YouTubeApiDurationParser.TryParse(value, out _));
    }

    [Fact]
    public void FileNameHelperProtectsReservedAndInvalidNames()
    {
        Assert.Equal("_CON", FileNameHelper.SanitizeDirectoryName("CON"));
        Assert.DoesNotContain(':', FileNameHelper.SanitizeDirectoryName("A:B/C"));
        Assert.Equal("Unknown Channel", FileNameHelper.SanitizeDirectoryName("  ...  "));
        Assert.Equal(80, FileNameHelper.SanitizeDirectoryName(new string('x', 200)).Length);
        Assert.Equal("Streams", FileNameHelper.GetContentFolderName(ContentType.Stream));
    }

    [Fact]
    public void FileNameHelperOutputTemplateDoesNotAppendTheVideoId()
    {
        var template = FileNameHelper.BuildOutputTemplate(@"D:\Downloads\Channel", ContentType.Video);

        Assert.Equal(@"D:\Downloads\Channel\Videos", Path.GetDirectoryName(template));
        Assert.Equal("%(upload_date>%Y-%m-%d)s - %(title).140B.%(ext)s", Path.GetFileName(template));
    }
}
