using System.Diagnostics;
using VideoMergeTool.Core.Features.DownloadVideo.Enums;
using VideoMergeTool.Core.Features.DownloadVideo.Interfaces;
using VideoMergeTool.Core.Features.DownloadVideo.Models;

namespace VideoMergeTool.Infrastructure.Features.DownloadVideo;

/// <summary>
/// Runs one yt-dlp download at a time and translates its output into progress messages.
/// </summary>
public sealed class YtDlpDownloadService : IVideoDownloadService, IDisposable
{
    private readonly IDownloadDependencyService _dependencyService;
    private readonly IArchiveService _archiveService;
    private readonly IDownloadLogger _logger;
    private readonly ProcessRunner _runProcessAsync;

    /// <summary>
    /// App-wide, not per tab: two tabs downloading at once would compete for bandwidth and could
    /// both write the same archive file.
    /// </summary>
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    public YtDlpDownloadService(
        IDownloadDependencyService dependencyService,
        IArchiveService archiveService,
        IDownloadLogger logger)
        : this(dependencyService, archiveService, logger, ProcessHelper.RunAsync)
    {
    }

    internal YtDlpDownloadService(
        IDownloadDependencyService dependencyService,
        IArchiveService archiveService,
        IDownloadLogger logger,
        ProcessRunner runProcessAsync)
    {
        _dependencyService = dependencyService;
        _archiveService = archiveService;
        _logger = logger;
        _runProcessAsync = runProcessAsync;
    }

    public async Task<DownloadResult> DownloadAsync(
        DownloadItem item,
        DownloadOptions options,
        IProgress<DownloadProgressMessage> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);

        await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await DownloadCoreAsync(item, options, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    public async Task<string> PrepareOutputDirectoryAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var fullPath = Path.GetFullPath(outputDirectory.Trim());
        Directory.CreateDirectory(fullPath);

        var probePath = Path.Combine(fullPath, $".write-test-{Guid.NewGuid():N}.tmp");
        await using (var stream = new FileStream(
                         probePath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 1,
                         FileOptions.Asynchronous | FileOptions.DeleteOnClose))
        {
            await stream.WriteAsync(new byte[] { 0 }, cancellationToken).ConfigureAwait(false);
        }

        return fullPath;
    }

    public void Dispose() => _downloadGate.Dispose();

    private async Task<DownloadResult> DownloadCoreAsync(
        DownloadItem item,
        DownloadOptions options,
        IProgress<DownloadProgressMessage> progress,
        CancellationToken cancellationToken)
    {
        var channelDirectory = FileNameHelper.GetChannelDirectory(options.OutputDirectory, options.ChannelName);
        Directory.CreateDirectory(Path.Combine(channelDirectory, FileNameHelper.GetContentFolderName(item.ContentType)));

        var startInfo = BuildStartInfo(item, options, channelDirectory);
        var syncRoot = new object();
        var completedPath = string.Empty;
        var lastError = string.Empty;
        var archiveHit = false;

        Task HandleLineAsync(string line, bool isStandardError)
        {
            if (YtDlpOutputParser.TryParse(line, out var parsed))
            {
                var message = string.IsNullOrWhiteSpace(parsed.VideoId)
                    ? parsed with { VideoId = item.VideoId }
                    : parsed;
                progress.Report(message);

                if (message.Type == ProgressMessageType.Complete)
                {
                    lock (syncRoot)
                    {
                        completedPath = message.OutputFilePath ?? message.Message;
                    }
                }
                else if (message.Type == ProgressMessageType.Error)
                {
                    lock (syncRoot)
                    {
                        lastError = message.Message;
                    }

                    _logger.Error(message.Message, item.VideoId);
                }
                else if (message.Type == ProgressMessageType.Warning)
                {
                    _logger.Warning(message.Message, item.VideoId);
                }

                return Task.CompletedTask;
            }

            if (line.Contains("has already been recorded in the archive", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("has already been downloaded", StringComparison.OrdinalIgnoreCase))
            {
                lock (syncRoot)
                {
                    archiveHit = true;
                }
            }

            // FFmpeg merging or converting after the download itself has finished.
            var isPostProcessing =
                line.StartsWith("[Merger]", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("[ExtractAudio]", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("[VideoConvertor]", StringComparison.OrdinalIgnoreCase);
            progress.Report(new DownloadProgressMessage(
                isPostProcessing ? ProgressMessageType.Processing : ProgressMessageType.Debug,
                item.VideoId,
                line));

            if (isStandardError &&
                (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("Sign in", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("unavailable", StringComparison.OrdinalIgnoreCase)))
            {
                lock (syncRoot)
                {
                    lastError = line;
                }
            }

            _logger.Debug(line, item.VideoId);
            return Task.CompletedTask;
        }

        int exitCode;
        try
        {
            exitCode = await _runProcessAsync(
                startInfo,
                line => HandleLineAsync(line, isStandardError: false),
                line => HandleLineAsync(line, isStandardError: true),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Download cancelled by user.", item.VideoId);
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error("Could not start or communicate with yt-dlp.", item.VideoId, exception);
            return new DownloadResult(
                DownloadStatus.Failed,
                ErrorMessage: "Không thể chạy yt-dlp. Hãy kiểm tra dependency và quyền thư mục.");
        }

        lock (syncRoot)
        {
            if (archiveHit)
            {
                return new DownloadResult(DownloadStatus.AlreadyDownloaded);
            }

            if (!string.IsNullOrWhiteSpace(completedPath))
            {
                return new DownloadResult(DownloadStatus.Completed, completedPath);
            }

            var friendly = MapDownloadError(lastError, exitCode, out var status);
            return new DownloadResult(status, ErrorMessage: friendly);
        }
    }

    private ProcessStartInfo BuildStartInfo(DownloadItem item, DownloadOptions options, string channelDirectory)
    {
        var startInfo = ProcessHelper.CreateStartInfo(_dependencyService.YtDlpPath);
        var arguments = new List<string>
        {
            // --continue resumes the .part file a cancelled download leaves behind.
            "--continue",
            "--ignore-errors",
            "--no-overwrites",
            "--newline",
            "--windows-filenames",
            "--no-simulate",
            "--no-playlist",
            "--encoding",
            "utf-8",
            "--ffmpeg-location",
            _dependencyService.ToolsDirectory,
            "--progress-template",
            "download:PROGRESS|%(info.id)s|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s",
            "--print",
            "before_dl:START|%(id)s|%(title)s",
            "--print",
            "after_move:COMPLETE|%(id)s|%(filepath)s",
            "--output",
            FileNameHelper.BuildOutputTemplate(channelDirectory, item.ContentType)
        };

        AddQualityArguments(arguments, options.Quality);

        if (options.Quality != DownloadQuality.AudioMp3)
        {
            arguments.Add("--merge-output-format");
            arguments.Add("mp4");
        }

        if (options.SkipPreviouslyDownloaded)
        {
            arguments.Add("--download-archive");
            arguments.Add(_archiveService.GetArchivePath(options.OutputDirectory, options.ChannelName));
        }

        if (options.DownloadThumbnail)
        {
            arguments.Add("--write-thumbnail");
        }

        if (options.DownloadSubtitles)
        {
            arguments.Add("--write-subs");
            arguments.Add("--write-auto-subs");
            arguments.Add("--sub-langs");
            arguments.Add("vi.*,en.*");
        }

        if (options.UseBrowserCookies)
        {
            arguments.Add("--cookies-from-browser");
            arguments.Add(YtDlpChannelAnalyzer.NormalizeBrowser(options.BrowserName));
        }

        arguments.Add(item.Url);
        ProcessHelper.AddArguments(startInfo, arguments);
        return startInfo;
    }

    private static void AddQualityArguments(List<string> arguments, DownloadQuality quality)
    {
        if (quality == DownloadQuality.AudioMp3)
        {
            arguments.AddRange(["--extract-audio", "--audio-format", "mp3", "--audio-quality", "0"]);
            return;
        }

        arguments.Add("-f");
        arguments.Add(quality switch
        {
            DownloadQuality.Best => "bv*+ba/b",
            DownloadQuality.P2160 => "bv*[height<=2160]+ba/b[height<=2160]",
            DownloadQuality.P1440 => "bv*[height<=1440]+ba/b[height<=1440]",
            DownloadQuality.P1080 => "bv*[height<=1080]+ba/b[height<=1080]",
            DownloadQuality.P720 => "bv*[height<=720]+ba/b[height<=720]",
            DownloadQuality.P480 => "bv*[height<=480]+ba/b[height<=480]",
            _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, null)
        });
    }

    internal static string MapDownloadError(string error, int exitCode, out DownloadStatus status)
    {
        if (error.Contains("private video", StringComparison.OrdinalIgnoreCase))
        {
            status = DownloadStatus.Private;
            return "Video riêng tư hoặc tài khoản hiện tại không có quyền truy cập.";
        }

        if (error.Contains("members-only", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Join this channel", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("age-restricted", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Sign in", StringComparison.OrdinalIgnoreCase))
        {
            status = DownloadStatus.RequiresLogin;
            return "Video yêu cầu đăng nhập hoặc quyền truy cập phù hợp.";
        }

        if (error.Contains("not available in your country", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("removed", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("deleted", StringComparison.OrdinalIgnoreCase))
        {
            status = DownloadStatus.Unavailable;
            return "Video không khả dụng, đã bị xóa hoặc bị giới hạn khu vực.";
        }

        if (error.Contains("Unable to download", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("network", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            status = DownloadStatus.Failed;
            return "Lỗi mạng khi tải video. Có thể tiếp tục lại ở lần sau.";
        }

        status = DownloadStatus.Failed;
        return !string.IsNullOrWhiteSpace(error)
            ? error
            : $"yt-dlp kết thúc mà không tạo tệp (exit code {exitCode}).";
    }
}
