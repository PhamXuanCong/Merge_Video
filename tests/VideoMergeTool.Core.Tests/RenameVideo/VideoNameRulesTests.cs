using VideoMergeTool.Core.Features.RenameVideo;

namespace VideoMergeTool.Core.Tests.RenameVideo;

public sealed class VideoNameRulesTests
{
    [Theory]
    [InlineData("Con mèo dễ thương [7123456789012345678].mp4", "#trend", "Con mèo dễ thương #trend.mp4")]
    [InlineData("Funny dog compilation [7098765432109876543].mov", "#trend", "Funny dog compilation #trend.mov")]
    [InlineData("Clip [abc_DEF-123].MKV", "#a #b", "Clip #a #b.MKV")]
    [InlineData("Con mèo dễ thương [7123456789012345678].mp4", "", "Con mèo dễ thương.mp4")]
    public void ComposeNameRemovesTheTrailingIdAndAppendsHashtags(string fileName, string hashtags, string expected)
    {
        var result = VideoNameRules.ComposeName(fileName, VideoNameRules.NormalizeHashtags(hashtags), out var idFound);

        Assert.Equal(expected, result);
        Assert.True(idFound);
    }

    [Fact]
    public void ComposeNameKeepsTheNameWhenThereIsNoIdAndOnlyAppendsHashtags()
    {
        var result = VideoNameRules.ComposeName("Không có id.mp4", "#trend", out var idFound);

        Assert.Equal("Không có id #trend.mp4", result);
        Assert.False(idFound);
    }

    [Theory]
    [InlineData("[intro] My clip.mp4")]
    [InlineData("My [part 1] clip.mp4")]
    public void ComposeNameOnlyRemovesBracketsAtTheVeryEnd(string fileName)
    {
        var result = VideoNameRules.ComposeName(fileName, string.Empty, out var idFound);

        Assert.Equal(fileName, result);
        Assert.False(idFound);
    }

    [Fact]
    public void ComposeNameRemovesOnlyTheLastBracketGroup()
    {
        var result = VideoNameRules.ComposeName("Clip [HD] [123].mp4", string.Empty, out _);

        Assert.Equal("Clip [HD].mp4", result);
    }

    [Fact]
    public void ComposeNameUsesJustTheHashtagsWhenTheNameWasOnlyAnId()
    {
        Assert.Equal("#trend.mp4", VideoNameRules.ComposeName("[123].mp4", "#trend", out _));
    }

    [Fact]
    public void ComposeNameNeverProducesAnEmptyName()
    {
        Assert.Equal("[123].mp4", VideoNameRules.ComposeName("[123].mp4", string.Empty, out _));
    }

    [Fact]
    public void ComposeNameSeparatesHashtagsWithExactlyOneSpace()
    {
        Assert.Equal("Clip #trend.mp4", VideoNameRules.ComposeName("Clip   .mp4", "#trend", out _));
    }

    [Theory]
    [InlineData("abc_Con mèo [123].mp4", 4, "", "Con mèo.mp4")]
    [InlineData("01 - Clip [123].mp4", 4, "#trend", "Clip #trend.mp4")]
    [InlineData("Clip.mp4", 0, "", "Clip.mp4")]
    [InlineData("Clip.MKV", 2, "", "ip.MKV")]
    public void ComposeNameRemovesTheRequestedLeadingCharacters(string fileName, int count, string hashtags, string expected)
    {
        var result = VideoNameRules.ComposeName(fileName, VideoNameRules.NormalizeHashtags(hashtags), out _, count);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ComposeNameKeepsTheOriginalWhenEveryCharacterWouldBeRemoved()
    {
        Assert.Equal("Clip [1].mp4", VideoNameRules.ComposeName("Clip [1].mp4", string.Empty, out _, 10));
        Assert.Equal("#trend.mp4", VideoNameRules.ComposeName("Clip [1].mp4", "#trend", out _, 10));
    }

    [Fact]
    public void RemoveLeadingCharactersCountsACombiningAccentAsPartOfItsLetter()
    {
        // "e" followed by a combining circumflex and acute, as some tools save Vietnamese names.
        Assert.Equal("m", VideoNameRules.RemoveLeadingCharacters("e\u0302\u0301m", 1));
        Assert.Equal("\U0001F431", VideoNameRules.RemoveLeadingCharacters("\U0001F436\U0001F431", 1));
    }

    [Theory]
    [InlineData("#trending #fyp", "#trending #fyp")]
    [InlineData("  #trending    #fyp  ", "#trending #fyp")]
    [InlineData("#a\t#b\r\n#c", "#a #b #c")]
    [InlineData("#tr:e*n?d \"#f<y>p|\" #a/b\\c", "#trend #fyp #abc")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void NormalizeHashtagsStripsInvalidCharactersAndCollapsesSpaces(string? input, string expected)
    {
        Assert.Equal(expected, VideoNameRules.NormalizeHashtags(input));
    }

    [Theory]
    [InlineData("a.mp4", true)]
    [InlineData("a.MOV", true)]
    [InlineData("a.avi", true)]
    [InlineData("a.mkv", true)]
    [InlineData("a.webm", true)]
    [InlineData("a.flv", true)]
    [InlineData("a.mp3", false)]
    [InlineData("rename-log.csv", false)]
    [InlineData("mp4", false)]
    public void IsVideoFileMatchesTheConfiguredExtensions(string fileName, bool expected)
    {
        Assert.Equal(expected, VideoNameRules.IsVideoFile(fileName));
    }

    [Fact]
    public void MakeUniqueAddsTheFirstFreeNumberBeforeTheExtension()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Clip #trend.mp4", "clip #trend (1).mp4" };

        Assert.Equal("Clip #trend (2).mp4", VideoNameRules.MakeUnique("Clip #trend.mp4", taken.Contains));
        Assert.Equal("Other.mp4", VideoNameRules.MakeUnique("Other.mp4", taken.Contains));
    }
}
