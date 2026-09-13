using VideoMergeTool.Core.Features.DownloadVideo.Interfaces;
using VideoMergeTool.Core.Features.DownloadVideo.Models;
using VideoMergeTool.Core.Models;

namespace VideoMergeTool.Infrastructure.Features.DownloadVideo;

/// <summary>
/// Verifies that yt-dlp, FFmpeg and FFprobe exist in the Tools folder and actually run. It never
/// downloads or updates them.
/// </summary>
public sealed class DownloadDependencyService : IDownloadDependencyService
{
    private readonly ApplicationPaths _paths;
    private readonly IDownloadLogger _logger;

    public DownloadDependencyService(ApplicationPaths paths, IDownloadLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public string ToolsDirectory => _paths.ToolsDirectory;

    public string YtDlpPath => _paths.YtDlpPath;

    public async Task<DependencyCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var dependencies = new List<DependencyInfo>
        {
            await CheckExecutableAsync("yt-dlp", _paths.YtDlpPath, ["--version"], cancellationToken).ConfigureAwait(false),
            await CheckExecutableAsync("ffmpeg", _paths.FFmpegPath, ["-version"], cancellationToken).ConfigureAwait(false),
            await CheckExecutableAsync("ffprobe", _paths.FFprobePath, ["-version"], cancellationToken).ConfigureAwait(false)
        };

        return new DependencyCheckResult(dependencies);
    }

    private async Task<DependencyInfo> CheckExecutableAsync(
        string name,
        string path,
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new DependencyInfo(name, path, IsAvailable: false, Version: null, Error: "Không tìm thấy tệp.");
        }

        var firstOutputLine = string.Empty;
        var firstErrorLine = string.Empty;

        try
        {
            var startInfo = ProcessHelper.CreateStartInfo(path);
            ProcessHelper.AddArguments(startInfo, arguments);

            var exitCode = await ProcessHelper.RunAsync(
                startInfo,
                line =>
                {
                    if (string.IsNullOrWhiteSpace(firstOutputLine))
                    {
                        firstOutputLine = line.Trim();
                    }

                    return Task.CompletedTask;
                },
                line =>
                {
                    if (string.IsNullOrWhiteSpace(firstErrorLine))
                    {
                        firstErrorLine = line.Trim();
                    }

                    return Task.CompletedTask;
                },
                cancellationToken).ConfigureAwait(false);

            var version = !string.IsNullOrWhiteSpace(firstOutputLine) ? firstOutputLine : firstErrorLine;
            return new DependencyInfo(
                name,
                path,
                exitCode == 0,
                version,
                exitCode == 0 ? null : $"Exit code {exitCode}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error($"Dependency check failed for {name}.", exception: exception);
            return new DependencyInfo(name, path, false, null, exception.Message);
        }
    }
}
