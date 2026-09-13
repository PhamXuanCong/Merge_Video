using VideoMergeTool.Core.Features.MergeVideo.Enums;
using VideoMergeTool.Core.Features.MergeVideo.Interfaces;
using VideoMergeTool.Core.Features.MergeVideo.Models;
using VideoMergeTool.Infrastructure.Features.MergeVideo;

namespace VideoMergeTool.Infrastructure.Tests;

public sealed class VideoProcessingServiceTests : IDisposable
{
    private static readonly TimeSpan InputDuration = TimeSpan.FromSeconds(10);

    private static readonly string[] ExpectedSequentialPairing = ["one.mp4", "two.mp4", "one.mp4"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"VideoMergeTool.Coordinator.{Guid.NewGuid():N}");

    [Fact]
    public async Task ASuccessfulMergeCommitsTheOutputAndAppliesTheSourceAction()
    {
        var input = CreateInput("clip.mp4");
        var service = CreateService(out var ffmpeg, out _);

        var summary = await service.ProcessAsync(
            _root,
            new ProcessingOptions { SourceFileAction = SourceFileAction.Delete },
            null,
            CancellationToken.None);

        var task = Assert.Single(summary.Tasks);
        Assert.Equal(VideoTaskStatus.Completed, task.Status);
        Assert.Null(task.ErrorMessage);
        Assert.True(File.Exists(task.OutputFile));
        Assert.False(File.Exists(task.TemporaryOutputFile));
        Assert.False(File.Exists(input));
        Assert.Equal(1, ffmpeg.CallCount);
    }

    [Fact]
    public async Task AFailedMergePreservesTheSourceAndRemovesTheTemporaryFile()
    {
        var input = CreateInput("clip.mp4");
        var service = CreateService(out var ffmpeg, out _);
        ffmpeg.Result = new FFmpegExecutionResult(false, 1, "encoder exploded");

        var summary = await service.ProcessAsync(
            _root,
            new ProcessingOptions { SourceFileAction = SourceFileAction.Delete },
            null,
            CancellationToken.None);

        var task = Assert.Single(summary.Tasks);
        Assert.Equal(VideoTaskStatus.Failed, task.Status);
        Assert.Equal("encoder exploded", task.ErrorMessage);
        Assert.True(File.Exists(input));
        Assert.False(File.Exists(task.OutputFile));
        Assert.False(File.Exists(task.TemporaryOutputFile));
    }

    [Fact]
    public async Task AnOutputThatFailsValidationNeverReachesTheFinalNameAndKeepsTheSource()
    {
        var input = CreateInput("clip.mp4");
        var service = CreateService(out _, out var validator);
        validator.Result = new OutputValidationResult(false, null, "duration drifted");

        var summary = await service.ProcessAsync(
            _root,
            new ProcessingOptions { SourceFileAction = SourceFileAction.Delete },
            null,
            CancellationToken.None);

        var task = Assert.Single(summary.Tasks);
        Assert.Equal(VideoTaskStatus.Failed, task.Status);
        Assert.Equal("duration drifted", task.ErrorMessage);
        Assert.True(File.Exists(input));
        Assert.False(File.Exists(task.OutputFile));
        Assert.False(File.Exists(task.TemporaryOutputFile));
    }

    [Fact]
    public async Task ASourceFileThatCannotBeDeletedStillCountsAsCompletedWithAWarning()
    {
        var input = CreateInput("clip.mp4");
        var service = CreateService(out _, out _);

        // Hold the source open so the delete fails the way a locked file would in the wild.
        using var handle = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.None);

        var summary = await service.ProcessAsync(
            _root,
            new ProcessingOptions { SourceFileAction = SourceFileAction.Delete },
            null,
            CancellationToken.None);

        var task = Assert.Single(summary.Tasks);
        Assert.Equal(VideoTaskStatus.Completed, task.Status);
        Assert.True(File.Exists(task.OutputFile));
        Assert.NotNull(task.ErrorMessage);
        Assert.Contains("could not be deleted", task.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExistingOutputIsSkippedWithoutRunningFFmpegOrTouchingTheSource()
    {
        var input = CreateInput("clip.mp4");
        var outputPath = Path.Combine(_root, "output", "clip.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, "already there");

        var service = CreateService(out var ffmpeg, out _);

        var summary = await service.ProcessAsync(
            _root,
            new ProcessingOptions
            {
                ExistingOutputAction = ExistingOutputAction.Skip,
                SourceFileAction = SourceFileAction.Delete
            },
            null,
            CancellationToken.None);

        var task = Assert.Single(summary.Tasks);
        Assert.Equal(VideoTaskStatus.Skipped, task.Status);
        Assert.Equal(0, ffmpeg.CallCount);
        Assert.True(File.Exists(input));
        Assert.Equal("already there", File.ReadAllText(outputPath));
    }

    [Fact]
    public async Task SequentialPairingCyclesThroughTheCompanionsInOrder()
    {
        CreateInput("a.mp4");
        CreateInput("b.mp4");
        CreateInput("c.mp4");
        var service = CreateService(out _, out _, companions: ["one.mp4", "two.mp4"]);

        var summary = await service.ProcessAsync(
            _root,
            new ProcessingOptions { PairingMode = PairingMode.Sequential, SourceFileAction = SourceFileAction.Keep },
            null,
            CancellationToken.None);

        Assert.Equal(
            ExpectedSequentialPairing,
            summary.Tasks.Select(task => task.CompanionFile).ToArray());
    }

    [Fact]
    public async Task RandomWithoutImmediateRepeatNeverPicksTheSameCompanionTwiceInARow()
    {
        for (var index = 0; index < 40; index++)
        {
            CreateInput($"clip{index}.mp4");
        }

        var service = CreateService(out _, out _, companions: ["one.mp4", "two.mp4", "three.mp4"]);

        var summary = await service.ProcessAsync(
            _root,
            new ProcessingOptions
            {
                PairingMode = PairingMode.RandomWithoutImmediateRepeat,
                SourceFileAction = SourceFileAction.Keep
            },
            null,
            CancellationToken.None);

        var companions = summary.Tasks.Select(task => task.CompanionFile).ToList();
        Assert.Equal(40, companions.Count);
        for (var index = 1; index < companions.Count; index++)
        {
            Assert.NotEqual(companions[index - 1], companions[index]);
        }
    }

    [Fact]
    public async Task MoveToProcessedKeepsTheSourceUnderTheProcessedFolder()
    {
        var input = CreateInput(Path.Combine("nested", "clip.mp4"));
        var service = CreateService(out _, out _);

        var summary = await service.ProcessAsync(
            _root,
            new ProcessingOptions { SourceFileAction = SourceFileAction.MoveToProcessed },
            null,
            CancellationToken.None);

        var task = Assert.Single(summary.Tasks);
        Assert.Equal(VideoTaskStatus.Completed, task.Status);
        Assert.False(File.Exists(input));
        Assert.True(File.Exists(Path.Combine(_root, "processed", "nested", "clip.mp4")));
    }

    private string CreateInput(string relativePath)
    {
        var fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "input");
        return fullPath;
    }

    private static VideoProcessingService CreateService(
        out FakeFFmpegService ffmpeg,
        out FakeOutputValidator validator,
        IReadOnlyList<string>? companions = null)
    {
        ffmpeg = new FakeFFmpegService();
        validator = new FakeOutputValidator();

        return new VideoProcessingService(
            new InputVideoScanner(),
            new FakeCompanionVideoProvider(companions ?? ["companion.mp4"]),
            new FakeFFprobeService(),
            ffmpeg,
            validator,
            new OutputPathService(),
            new Random(1234));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeCompanionVideoProvider(IReadOnlyList<string> companions) : ICompanionVideoProvider
    {
        public Task<IReadOnlyList<string>> GetVideosAsync(CancellationToken cancellationToken) =>
            Task.FromResult(companions);
    }

    private sealed class FakeFFprobeService : IFFprobeService
    {
        public Task<VideoMetadata> GetMetadataAsync(string videoPath, CancellationToken cancellationToken) =>
            Task.FromResult(new VideoMetadata(InputDuration, true, true, 1080, 1920));
    }

    private sealed class FakeFFmpegService : IFFmpegService
    {
        public int CallCount { get; private set; }

        public FFmpegExecutionResult Result { get; set; } = new(true, 0, string.Empty);

        public Task<FFmpegExecutionResult> MergeAsync(
            VideoMergeTask task,
            ProcessingOptions options,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            CallCount++;

            if (Result.Success)
            {
                File.WriteAllText(task.TemporaryOutputFile, "merged");
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class FakeOutputValidator : IOutputValidator
    {
        public OutputValidationResult Result { get; set; } =
            new(true, new VideoMetadata(InputDuration, true, true, 1080, 1920), null);

        public Task<OutputValidationResult> ValidateAsync(
            string outputFile,
            TimeSpan expectedDuration,
            TimeSpan durationTolerance,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result);
    }
}
