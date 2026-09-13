using System.Diagnostics;
using System.Text;

namespace VideoMergeTool.Infrastructure.Features.DownloadVideo;

/// <summary>The shape of <see cref="ProcessHelper.RunAsync"/>, so tests can stand in for yt-dlp.</summary>
internal delegate Task<int> ProcessRunner(
    ProcessStartInfo startInfo,
    Func<string, Task>? standardOutputHandler,
    Func<string, Task>? standardErrorHandler,
    CancellationToken cancellationToken);

public static class ProcessHelper
{
    public static ProcessStartInfo CreateStartInfo(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        return new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
    }

    /// <summary>
    /// Runs a child process without a shell, drains both streams concurrently and kills its whole
    /// tree on cancellation, so an ffmpeg spawned by yt-dlp is never left running.
    /// </summary>
    public static async Task<int> RunAsync(
        ProcessStartInfo startInfo,
        Func<string, Task>? standardOutputHandler,
        Func<string, Task>? standardErrorHandler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Không thể khởi chạy {Path.GetFileName(startInfo.FileName)}.");
        }

        var outputTask = PumpLinesAsync(process.StandardOutput, standardOutputHandler, cancellationToken);
        var errorTask = PumpLinesAsync(process.StandardError, standardErrorHandler, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            await WaitForExitAfterKillAsync(process).ConfigureAwait(false);

            try
            {
                await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The stream pumps observe the same cancellation once the process tree is gone.
            }

            throw;
        }
    }

    public static void AddArguments(ProcessStartInfo startInfo, IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    private static async Task PumpLinesAsync(
        StreamReader reader,
        Func<string, Task>? handler,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (handler is not null)
            {
                await handler(line).ConfigureAwait(false);
            }
        }
    }

    private static void TryKillProcessTree(Process process)
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
            // The process exited between HasExited and Kill.
        }
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Nothing is left to await when startup or teardown races the cancellation.
        }
    }
}
