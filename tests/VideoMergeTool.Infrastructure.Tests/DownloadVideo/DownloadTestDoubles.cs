using System.Diagnostics;
using VideoMergeTool.Core.Features.DownloadVideo.Enums;
using VideoMergeTool.Core.Features.DownloadVideo.Interfaces;
using VideoMergeTool.Core.Features.DownloadVideo.Models;

namespace VideoMergeTool.Infrastructure.Tests.DownloadVideo;

internal sealed class MemoryDownloadLogger : IDownloadLogger
{
    public event EventHandler<DownloadLogEntry>? EntryLogged;

    public List<DownloadLogEntry> Entries { get; } = [];

    public void Log(DownloadLogLevel level, string message, string? videoId = null, Exception? exception = null)
    {
        var entry = new DownloadLogEntry(DateTime.Now, level, message, videoId);
        lock (Entries)
        {
            Entries.Add(entry);
        }

        EntryLogged?.Invoke(this, entry);
    }

    public void Info(string message, string? videoId = null) => Log(DownloadLogLevel.Info, message, videoId);

    public void Warning(string message, string? videoId = null) => Log(DownloadLogLevel.Warning, message, videoId);

    public void Error(string message, string? videoId = null, Exception? exception = null) =>
        Log(DownloadLogLevel.Error, message, videoId, exception);

    public void Debug(string message, string? videoId = null) => Log(DownloadLogLevel.Debug, message, videoId);
}

internal sealed class FakeDependencyService(string toolsDirectory = "") : IDownloadDependencyService
{
    public string ToolsDirectory { get; } = toolsDirectory;

    public string YtDlpPath => "yt-dlp.exe";

    public Task<DependencyCheckResult> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new DependencyCheckResult([]));
}

internal sealed class MemoryDurationCache(Dictionary<string, double>? durations = null) : IVideoDurationCache
{
    private readonly Dictionary<string, double> _durations = durations ?? new Dictionary<string, double>(StringComparer.Ordinal);

    public bool TryGet(string videoId, out double durationSeconds) => _durations.TryGetValue(videoId, out durationSeconds);

    public Task StoreAsync(string videoId, double durationSeconds, CancellationToken cancellationToken)
    {
        _durations[videoId] = durationSeconds;
        return Task.CompletedTask;
    }
}

internal sealed class MemoryVideoMetadataLogger : IVideoMetadataLogger
{
    public Task WriteAsync(Uri channelUri, string channelName, IReadOnlyList<DownloadItem> items, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

internal sealed class MemoryYouTubeDataApiDurationResolver(IReadOnlyDictionary<string, double>? durations) : IYouTubeDataApiDurationResolver
{
    public bool IsConfigured => durations is not null;

    public Task<IReadOnlyDictionary<string, double>> ResolveAsync(IEnumerable<string> videoIds, CancellationToken cancellationToken) =>
        Task.FromResult(durations ?? new Dictionary<string, double>());
}

internal sealed class DelegateHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(handler(request));
}

/// <summary>Invokes the callback inline, unlike <see cref="Progress{T}"/>, so assertions never race it.</summary>
internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

/// <summary>Plays back canned yt-dlp output for a given command line instead of starting a process.</summary>
internal sealed class ScriptedProcess
{
    private readonly Func<IReadOnlyList<string>, (int ExitCode, string StandardOutput, string StandardError)> _script;

    public ScriptedProcess(Func<IReadOnlyList<string>, (int ExitCode, string StandardOutput, string StandardError)> script)
    {
        _script = script;
    }

    public List<IReadOnlyList<string>> Invocations { get; } = [];

    public async Task<int> RunAsync(
        ProcessStartInfo startInfo,
        Func<string, Task>? standardOutputHandler,
        Func<string, Task>? standardErrorHandler,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var arguments = startInfo.ArgumentList.ToList();
        Invocations.Add(arguments);
        var (exitCode, standardOutput, standardError) = _script(arguments);

        await PumpAsync(standardOutput, standardOutputHandler);
        await PumpAsync(standardError, standardErrorHandler);
        return exitCode;
    }

    private static async Task PumpAsync(string text, Func<string, Task>? handler)
    {
        if (handler is null || text.Length == 0)
        {
            return;
        }

        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            await handler(line);
        }
    }
}
