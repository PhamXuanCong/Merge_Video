using VideoMergeTool.Core.Features.MergeVideo.Enums;
using VideoMergeTool.Core.Features.MergeVideo.Models;
using VideoMergeTool.Infrastructure.Features.MergeVideo;

namespace VideoMergeTool.Infrastructure.Tests;

public sealed class FFmpegCommandBuilderTests
{
    [Fact]
    public void BuildArgumentsCreatesAHorizontalMergeWithLoopedCompanionAndInputAudio()
    {
        var task = new VideoMergeTask
        {
            InputFile = "input.mp4",
            CompanionFile = "companion.mp4",
            OutputFile = "output.mp4",
            TemporaryOutputFile = "output.processing.mp4",
            InputDuration = TimeSpan.FromSeconds(12.5)
        };
        var options = new ProcessingOptions
        {
            CpuThreadLimit = 3
        };

        var arguments = new FFmpegCommandBuilder().BuildArguments(task, options);
        var argumentList = arguments.ToList();

        Assert.Equal("-stream_loop", arguments[9]);
        Assert.Equal("-1", arguments[10]);
        Assert.Contains(arguments, argument => argument.Contains("[left][right]hstack=inputs=2[v]", StringComparison.Ordinal));
        Assert.Equal("libx264", arguments[argumentList.IndexOf("-c:v") + 1]);
        Assert.Equal("0:a?", arguments[argumentList.IndexOf("-map") + 3]);
        Assert.Equal("3", arguments[argumentList.IndexOf("-filter_threads") + 1]);
        Assert.Equal("3", arguments[argumentList.IndexOf("-filter_complex_threads") + 1]);
        Assert.Equal(3, arguments.Count(argument => argument == "-threads"));
        Assert.Contains("-preset", arguments);
        Assert.Contains("pipe:1", arguments);
        Assert.Contains("-nostats", arguments);
        Assert.Equal("output.processing.mp4", arguments[^1]);
    }

    [Fact]
    public void BuildArgumentsCreatesAVerticalMergeWithVstackFilter()
    {
        var task = new VideoMergeTask
        {
            InputFile = "input.mp4",
            CompanionFile = "companion.mp4",
            OutputFile = "output.mp4",
            TemporaryOutputFile = "output.processing.mp4",
            InputDuration = TimeSpan.FromSeconds(10)
        };
        var options = new ProcessingOptions
        {
            MergeLayout = MergeLayout.Vertical,
            CpuThreadLimit = 2
        };

        var arguments = new FFmpegCommandBuilder().BuildArguments(task, options);

        Assert.Contains(arguments, argument => argument.Contains("scale=1080:960", StringComparison.Ordinal));
        Assert.Contains(arguments, argument => argument.Contains("[top][bottom]vstack=inputs=2[v]", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildArgumentsAddsTheSelectedX264Preset()
    {
        var task = new VideoMergeTask
        {
            InputFile = "input.mp4",
            CompanionFile = "companion.mp4",
            OutputFile = "output.mp4",
            TemporaryOutputFile = "output.processing.mp4",
            InputDuration = TimeSpan.FromSeconds(12.5)
        };
        var options = new ProcessingOptions
        {
            X264Preset = X264Preset.Faster,
            CpuThreadLimit = 2
        };

        var arguments = new FFmpegCommandBuilder().BuildArguments(task, options).ToList();

        Assert.Equal("libx264", arguments[arguments.IndexOf("-c:v") + 1]);
        Assert.Equal("faster", arguments[arguments.IndexOf("-preset") + 1]);
    }

    [Fact]
    public void BuildArgumentsUsesNvencWhenTheGpuEncoderIsSelected()
    {
        var task = new VideoMergeTask
        {
            InputFile = "input.mp4",
            CompanionFile = "companion.mp4",
            OutputFile = "output.mp4",
            TemporaryOutputFile = "output.processing.mp4",
            InputDuration = TimeSpan.FromSeconds(12.5)
        };
        var options = new ProcessingOptions
        {
            VideoEncoder = VideoEncoder.NvidiaGpu,
            X264Preset = X264Preset.Fast,
            CpuThreadLimit = 2
        };

        var arguments = new FFmpegCommandBuilder().BuildArguments(task, options).ToList();

        Assert.Equal("h264_nvenc", arguments[arguments.IndexOf("-c:v") + 1]);
        Assert.Equal("p4", arguments[arguments.IndexOf("-preset") + 1]);
        Assert.Equal("yuv420p", arguments[arguments.IndexOf("-pix_fmt") + 1]);
    }

    [Fact]
    public void BuildArgumentsForcesAWidelyPlayablePixelFormat()
    {
        var task = new VideoMergeTask
        {
            InputFile = "input.mp4",
            CompanionFile = "companion.mp4",
            OutputFile = "output.mp4",
            TemporaryOutputFile = "output.processing.mp4",
            InputDuration = TimeSpan.FromSeconds(12.5)
        };

        var arguments = new FFmpegCommandBuilder().BuildArguments(task, new ProcessingOptions()).ToList();

        Assert.Equal("yuv420p", arguments[arguments.IndexOf("-pix_fmt") + 1]);
    }

    [Fact]
    public void BuildArgumentsRejectsAnOutputSizeThatLibx264CannotEncode()
    {
        var task = new VideoMergeTask
        {
            InputFile = "input.mp4",
            CompanionFile = "companion.mp4",
            OutputFile = "output.mp4",
            TemporaryOutputFile = "output.processing.mp4",
            InputDuration = TimeSpan.FromSeconds(12.5)
        };

        // 1079 / 2 = 539, an odd region width that yuv420p cannot represent.
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FFmpegCommandBuilder().BuildArguments(task, new ProcessingOptions { OutputWidth = 1079 }));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void BuildArgumentsRejectsAnInvalidCpuThreadLimit()
    {
        var task = new VideoMergeTask
        {
            InputFile = "input.mp4",
            CompanionFile = "companion.mp4",
            OutputFile = "output.mp4",
            TemporaryOutputFile = "output.processing.mp4",
            InputDuration = TimeSpan.FromSeconds(12.5)
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FFmpegCommandBuilder().BuildArguments(task, new ProcessingOptions { CpuThreadLimit = 0 }));

        Assert.Equal("options", exception.ParamName);
    }
}
