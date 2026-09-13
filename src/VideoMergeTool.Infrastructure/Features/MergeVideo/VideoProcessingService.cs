using VideoMergeTool.Core.Features.MergeVideo.Enums;
using VideoMergeTool.Core.Features.MergeVideo.Interfaces;
using VideoMergeTool.Core.Features.MergeVideo.Models;

namespace VideoMergeTool.Infrastructure.Features.MergeVideo;

public sealed class VideoProcessingService : IVideoProcessingCoordinator
{
    private readonly IInputVideoScanner _inputVideoScanner;
    private readonly ICompanionVideoProvider _companionVideoProvider;
    private readonly IFFprobeService _ffprobeService;
    private readonly IFFmpegService _ffmpegService;
    private readonly IOutputValidator _outputValidator;
    private readonly IOutputPathService _outputPathService;
    private readonly Random _random;

    public VideoProcessingService(
        IInputVideoScanner inputVideoScanner,
        ICompanionVideoProvider companionVideoProvider,
        IFFprobeService ffprobeService,
        IFFmpegService ffmpegService,
        IOutputValidator outputValidator,
        IOutputPathService outputPathService)
        : this(
            inputVideoScanner,
            companionVideoProvider,
            ffprobeService,
            ffmpegService,
            outputValidator,
            outputPathService,
            Random.Shared)
    {
    }

    internal VideoProcessingService(
        IInputVideoScanner inputVideoScanner,
        ICompanionVideoProvider companionVideoProvider,
        IFFprobeService ffprobeService,
        IFFmpegService ffmpegService,
        IOutputValidator outputValidator,
        IOutputPathService outputPathService,
        Random random)
    {
        _inputVideoScanner = inputVideoScanner;
        _companionVideoProvider = companionVideoProvider;
        _ffprobeService = ffprobeService;
        _ffmpegService = ffmpegService;
        _outputValidator = outputValidator;
        _outputPathService = outputPathService;
        _random = random;
    }

    public async Task<ProcessingSummary> ProcessAsync(
        string inputFolder,
        ProcessingOptions options,
        IProgress<VideoMergeTask>? progress,
        CancellationToken cancellationToken)
    {
        var inputs = await _inputVideoScanner.ScanAsync(inputFolder, cancellationToken);
        var companions = await _companionVideoProvider.GetVideosAsync(cancellationToken);
        if (companions.Count == 0)
        {
            throw new InvalidOperationException("No companion videos were found in Assets/CompanionVideos.");
        }

        var tasks = new List<VideoMergeTask>(inputs.Count);
        string? previousCompanion = null;
        var companionIndex = 0;

        foreach (var input in inputs)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var companion = SelectCompanion(companions, options.PairingMode, ref companionIndex, previousCompanion);
            previousCompanion = companion;
            var outputPath = GetOutputPath(inputFolder, input, options.ExistingOutputAction);
            var task = new VideoMergeTask
            {
                InputFile = input.FullPath,
                CompanionFile = companion,
                OutputFile = outputPath,
                TemporaryOutputFile = _outputPathService.GetTemporaryOutputFilePath(outputPath)
            };
            tasks.Add(task);

            if (options.ExistingOutputAction == ExistingOutputAction.Skip && File.Exists(outputPath))
            {
                task.Status = VideoTaskStatus.Skipped;
                task.ErrorMessage = "Output file already exists.";
                progress?.Report(task);
                continue;
            }

            try
            {
                await ProcessTaskAsync(task, input, inputFolder, options, progress, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                task.Status = VideoTaskStatus.Cancelled;
                task.ErrorMessage = "Processing was cancelled.";
                DeleteTemporaryOutput(task.TemporaryOutputFile);
                progress?.Report(task);
                break;
            }
            catch (Exception exception)
            {
                task.Status = VideoTaskStatus.Failed;
                task.ErrorMessage = exception.Message;
                DeleteTemporaryOutput(task.TemporaryOutputFile);
                progress?.Report(task);
            }
        }

        return new ProcessingSummary(tasks);
    }

    private async Task ProcessTaskAsync(
        VideoMergeTask task,
        VideoFileInfo input,
        string inputFolder,
        ProcessingOptions options,
        IProgress<VideoMergeTask>? progress,
        CancellationToken cancellationToken)
    {
        task.Status = VideoTaskStatus.ReadingMetadata;
        progress?.Report(task);
        var inputMetadata = await _ffprobeService.GetMetadataAsync(task.InputFile, cancellationToken);
        if (!inputMetadata.HasVideo)
        {
            throw new InvalidOperationException("The input does not contain a video stream.");
        }

        task.InputDuration = inputMetadata.Duration;
        task.Status = VideoTaskStatus.Processing;
        progress?.Report(task);

        Directory.CreateDirectory(Path.GetDirectoryName(task.OutputFile)!);
        DeleteTemporaryOutput(task.TemporaryOutputFile);
        var mergeResult = await _ffmpegService.MergeAsync(
            task,
            options,
            new Progress<double>(value =>
            {
                task.Progress = value;
                progress?.Report(task);
            }),
            cancellationToken);

        if (!mergeResult.Success)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(mergeResult.ErrorOutput)
                ? $"FFmpeg exited with code {mergeResult.ExitCode}."
                : mergeResult.ErrorOutput.Trim());
        }

        task.Status = VideoTaskStatus.Validating;
        progress?.Report(task);
        var validation = await _outputValidator.ValidateAsync(
            task.TemporaryOutputFile,
            inputMetadata.Duration,
            options.DurationTolerance,
            cancellationToken);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.FailureReason ?? "Output validation failed.");
        }

        File.Move(task.TemporaryOutputFile, task.OutputFile, overwrite: true);

        // The output is committed from here on, so a failure to tidy up the source must not be
        // reported as a failed merge: that would hide a good output and invite a pointless rerun.
        task.ErrorMessage = TryApplySourceFileAction(inputFolder, input, options.SourceFileAction);
        task.Progress = 1;
        task.Status = VideoTaskStatus.Completed;
        progress?.Report(task);
    }

    private string GetOutputPath(
        string inputFolder,
        VideoFileInfo input,
        ExistingOutputAction existingOutputAction)
    {
        var outputPath = _outputPathService.GetOutputFilePath(inputFolder, input);
        if (existingOutputAction != ExistingOutputAction.CreateUniqueName)
        {
            return outputPath;
        }

        var directory = Path.GetDirectoryName(outputPath)!;
        var fileName = Path.GetFileNameWithoutExtension(outputPath);
        var extension = Path.GetExtension(outputPath);
        var candidate = outputPath;
        var index = 1;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{fileName} ({index++}){extension}");
        }

        return candidate;
    }

    private string SelectCompanion(
        IReadOnlyList<string> companions,
        PairingMode pairingMode,
        ref int companionIndex,
        string? previousCompanion)
    {
        switch (pairingMode)
        {
            case PairingMode.Sequential:
                var current = companions[companionIndex];
                // Wrap the counter itself so a long batch can never overflow it into a negative index.
                companionIndex = (companionIndex + 1) % companions.Count;
                return current;
            case PairingMode.Random:
                return companions[_random.Next(companions.Count)];
            case PairingMode.RandomWithoutImmediateRepeat:
                return SelectRandomWithoutImmediateRepeat(companions, previousCompanion);
            default:
                throw new ArgumentOutOfRangeException(nameof(pairingMode), pairingMode, null);
        }
    }

    private string SelectRandomWithoutImmediateRepeat(IReadOnlyList<string> companions, string? previousCompanion)
    {
        if (companions.Count == 1 || previousCompanion is null)
        {
            return companions[_random.Next(companions.Count)];
        }

        var previousIndex = IndexOf(companions, previousCompanion);
        if (previousIndex < 0)
        {
            return companions[_random.Next(companions.Count)];
        }

        // Draw from the companions other than the previous one and shift past it. This is the same
        // uniform distribution as re-rolling until the value differs, but it always terminates.
        var index = _random.Next(companions.Count - 1);
        return companions[index >= previousIndex ? index + 1 : index];
    }

    private static int IndexOf(IReadOnlyList<string> companions, string value)
    {
        for (var index = 0; index < companions.Count; index++)
        {
            if (string.Equals(companions[index], value, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    /// <returns>A warning message when the source file could not be handled; otherwise <c>null</c>.</returns>
    private string? TryApplySourceFileAction(string inputFolder, VideoFileInfo input, SourceFileAction action)
    {
        try
        {
            switch (action)
            {
                case SourceFileAction.Keep:
                    return null;
                case SourceFileAction.MoveToProcessed:
                    var processedPath = _outputPathService.GetProcessedFilePath(inputFolder, input);
                    Directory.CreateDirectory(Path.GetDirectoryName(processedPath)!);
                    File.Move(input.FullPath, processedPath, overwrite: true);
                    return null;
                case SourceFileAction.Delete:
                    File.Delete(input.FullPath);
                    return null;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"Output created, but the source file could not be {DescribeAction(action)}: {exception.Message}";
        }
    }

    private static string DescribeAction(SourceFileAction action) => action switch
    {
        SourceFileAction.MoveToProcessed => "moved to the processed folder",
        SourceFileAction.Delete => "deleted",
        _ => "handled"
    };

    private static void DeleteTemporaryOutput(string temporaryOutputFile)
    {
        try
        {
            if (File.Exists(temporaryOutputFile))
            {
                File.Delete(temporaryOutputFile);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: the caller already has the real failure to report.
        }
    }
}
