using VideoMergeTool.Core.Features.MergeVideo.Models;
using VideoMergeTool.Infrastructure.Features.MergeVideo;

namespace VideoMergeTool.Infrastructure.Tests;

public sealed class OutputPathServiceTests
{
    [Fact]
    public void GetPathsPreservesTheInputRelativeStructure()
    {
        var service = new OutputPathService();
        var input = new VideoFileInfo(
            Path.Combine("C:", "input", "nested", "clip.mp4"),
            "clip.mp4",
            Path.Combine("nested", "clip.mp4"));

        var output = service.GetOutputFilePath(Path.Combine("C:", "input"), input);
        var temporary = service.GetTemporaryOutputFilePath(output);
        var processed = service.GetProcessedFilePath(Path.Combine("C:", "input"), input);

        Assert.Equal(Path.Combine("C:", "input", "output", "nested", "clip.mp4"), output);
        Assert.Equal(Path.Combine("C:", "input", "output", "nested", "clip.processing.mp4"), temporary);
        Assert.Equal(Path.Combine("C:", "input", "processed", "nested", "clip.mp4"), processed);
    }
}
