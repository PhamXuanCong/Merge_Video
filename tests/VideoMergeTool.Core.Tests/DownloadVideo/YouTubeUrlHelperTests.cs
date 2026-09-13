using VideoMergeTool.Core.Features.DownloadVideo;

namespace VideoMergeTool.Core.Tests.DownloadVideo;

public sealed class YouTubeUrlHelperTests
{
    [Theory]
    [InlineData("https://www.youtube.com/@ChannelHandle", "https://www.youtube.com/@ChannelHandle")]
    [InlineData("https://www.youtube.com/channel/UCxxxxxxxx", "https://www.youtube.com/channel/UCxxxxxxxx")]
    [InlineData(" https://youtube.com/@ChannelHandle/videos?view=0#top ", "https://www.youtube.com/@ChannelHandle")]
    [InlineData("http://m.youtube.com/c/SomeName/shorts", "https://www.youtube.com/c/SomeName")]
    public void TryNormalizeChannelUrlReturnsTheBareChannelUrl(string input, string expected)
    {
        Assert.True(YouTubeUrlHelper.TryNormalizeChannelUrl(input, out var normalized, out var error), error);
        Assert.Equal(expected, normalized!.AbsoluteUri.TrimEnd('/'));
        Assert.EndsWith("/shorts", YouTubeUrlHelper.CreateTabUri(normalized, "shorts").AbsoluteUri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.com/@Channel", "URL không thuộc miền youtube.com.")]
    [InlineData("https://www.youtube.com/watch?v=abc123", "Đây là URL video, không phải URL kênh YouTube.")]
    [InlineData("not a URL", "URL phải là địa chỉ HTTP hoặc HTTPS hợp lệ.")]
    [InlineData("", "Vui lòng nhập URL kênh YouTube.")]
    [InlineData("https://www.youtube.com/@Channel/community", "URL chứa đường dẫn không phải trang kênh YouTube.")]
    public void TryNormalizeChannelUrlRejectsUrlsThatAreNotChannels(string input, string expectedError)
    {
        Assert.False(YouTubeUrlHelper.TryNormalizeChannelUrl(input, out var normalized, out var error));
        Assert.Null(normalized);
        Assert.Equal(expectedError, error);
    }

    [Fact]
    public void CreateTabUriRejectsUnknownTabs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => YouTubeUrlHelper.CreateTabUri(new Uri("https://www.youtube.com/@Channel"), "community"));
    }
}
