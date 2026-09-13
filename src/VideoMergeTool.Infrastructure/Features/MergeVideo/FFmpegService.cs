using System.Diagnostics;
using System.Globalization;
using VideoMergeTool.Core.Features.MergeVideo.Interfaces;
using VideoMergeTool.Core.Features.MergeVideo.Models;
using VideoMergeTool.Core.Models;

namespace VideoMergeTool.Infrastructure.Features.MergeVideo;

public sealed class FFmpegService : IFFmpegService
{
    private const string ProgressKey = "out_time_us=";

    /// <summary>
    /// FFmpeg can emit thousands of diagnostic lines; only the tail is useful as an error message
    /// and keeping it bounded stops a failing file from allocating megabytes of text.
    /// </summary>
    private const int MaxCapturedErrorLines = 20;

    /// <summary>
    /// Smallest progress change worth pushing to the UI. FFmpeg reports far more often than a
    /// progress bar can usefully redraw.
    /// </summary>
    private const double MinimumProgressDelta = 0.005;

    private readonly string _ffmpegPath;
    private readonly FFmpegCommandBuilder _commandBuilder;

    public FFmpegService(ApplicationPaths paths, FFmpegCommandBuilder commandBuilder)
        : this(paths.FFmpegPath, commandBuilder)
    {
    }

    public FFmpegService(string ffmpegPath, FFmpegCommandBuilder commandBuilder)
    {
        _ffmpegPath = ffmpegPath;
        _commandBuilder = commandBuilder;
    }

    public async Task<FFmpegExecutionResult> MergeAsync(
        VideoMergeTask task,
        ProcessingOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(_ffmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in _commandBuilder.BuildArguments(task, options))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        Task? progressTask = null;
        Task<string>? errorTask = null;

        try
        {
            process.Start();
            using var registration = cancellationToken.Register(() => KillProcess(process));
            progressTask = ReadProgressAsync(process.StandardOutput, task.InputDuration, progress, cancellationToken);
            errorTask = ReadErrorTailAsync(process.StandardError, cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            await progressTask;
            var errorOutput = await errorTask;

            if (process.ExitCode != 0)
            {
                DeleteTemporaryOutput(task.TemporaryOutputFile);
                return new FFmpegExecutionResult(false, process.ExitCode, errorOutput);
            }

            progress?.Report(1);
            return new FFmpegExecutionResult(true, process.ExitCode, errorOutput);
        }
        catch
        {
            KillProcess(process);
            await ObserveAsync(progressTask, errorTask);
            DeleteTemporaryOutput(task.TemporaryOutputFile);
            throw;
        }
    }

    private static async Task ReadProgressAsync(
        StreamReader reader,
        TimeSpan? inputDuration,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (progress is null || inputDuration is not { } duration || duration <= TimeSpan.Zero)
        {
            // Still drain stdout so FFmpeg never blocks on a full pipe.
            await reader.ReadToEndAsync(cancellationToken);
            return;
        }

        var totalMicroseconds = duration.TotalMilliseconds * 1_000d;
        var lastReported = -1d;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith(ProgressKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (!long.TryParse(line.AsSpan(ProgressKey.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
            {
                continue;
            }

            var value = Math.Clamp(microseconds / totalMicroseconds, 0, 1);
            if (value - lastReported < MinimumProgressDelta)
            {
                continue;
            }

            lastReported = value;
            progress.Report(value);
        }
    }

    private static async Task<string> ReadErrorTailAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var lines = new Queue<string>(MaxCapturedErrorLines);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (lines.Count == MaxCapturedErrorLines)
            {
                lines.Dequeue();
            }

            lines.Enqueue(line);
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Awaits the stream readers on the failure path so their exceptions are never left unobserved.
    /// </summary>
    private static async Task ObserveAsync(params Task?[] tasks)
    {
        foreach (var task in tasks)
        {
            if (task is null)
            {
                continue;
            }

            try
            {
                await task;
            }
            catch
            {
                // The original failure is the one worth surfacing.
            }
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and Kill.
        }
    }

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
            // Cleaning up is best effort; the real failure is reported to the caller.
        }
    }
}
