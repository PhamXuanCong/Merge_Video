using VideoMergeTool.Infrastructure.Features.MergeVideo;

namespace VideoMergeTool.Infrastructure.Tests;

public sealed class FFprobeServiceTests
{
    [Fact]
    public void ParseMetadataReadsDurationStreamsAndDimensions()
    {
        const string json = """
            {"format":{"duration":"15.25"},"streams":[{"codec_type":"video","width":1080,"height":1920},{"codec_type":"audio"}]}
            """;

        var metadata = FFprobeService.ParseMetadata(json);

        Assert.True(metadata.HasVideo);
        Assert.True(metadata.HasAudio);
        Assert.Equal(TimeSpan.FromSeconds(15.25), metadata.Duration);
        Assert.Equal(1080, metadata.Width);
        Assert.Equal(1920, metadata.Height);
    }
}
