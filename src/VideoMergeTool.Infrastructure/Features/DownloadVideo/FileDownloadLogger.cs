using System.Text;
using VideoMergeTool.Core.Features.DownloadVideo.Enums;
using VideoMergeTool.Core.Features.DownloadVideo.Interfaces;
using VideoMergeTool.Core.Features.DownloadVideo.Models;

namespace VideoMergeTool.Infrastructure.Features.DownloadVideo;

/// <summary>
/// Writes timestamped downloader events to one log file per run and publishes them to the UI.
/// The file is only created on the first entry, so runs that never open the downloader leave none.
/// </summary>
public sealed class FileDownloadLogger : IDownloadLogger, IDisposable
{
    private readonly object _syncRoot = new();
    private StreamWriter? _writer;
    private bool _writerFailed;
    private bool _isDisposed;

    public event EventHandler<DownloadLogEntry>? EntryLogged;

    public void Info(string message, string? videoId = null) =>
        Log(DownloadLogLevel.Info, message, videoId);

    public void Warning(string message, string? videoId = null) =>
        Log(DownloadLogLevel.Warning, message, videoId);

    public void Error(string message, string? videoId = null, Exception? exception = null) =>
        Log(DownloadLogLevel.Error, message, videoId, exception);

    public void Debug(string message, string? videoId = null) =>
        Log(DownloadLogLevel.Debug, message, videoId);

    public void Log(DownloadLogLevel level, string message, string? videoId = null, Exception? exception = null)
    {
        var safeMessage = NormalizeLine(message);
        if (exception is not null)
        {
            safeMessage = $"{safeMessage} ({exception.GetType().Name}: {NormalizeLine(exception.Message)})";
        }

        var entry = new DownloadLogEntry(DateTime.Now, level, safeMessage, videoId);

        lock (_syncRoot)
        {
            if (!_isDisposed && GetWriter() is { } writer)
            {
                try
                {
                    writer.WriteLine(entry.ToString());
                }
                catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine($"Unable to write downloader log: {writeException.Message}");
                }
            }
        }

        EntryLogged?.Invoke(this, entry);
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _writer?.Dispose();
        }
    }

    private StreamWriter? GetWriter()
    {
        if (_writer is not null || _writerFailed)
        {
            return _writer;
        }

        try
        {
            var path = Path.Combine(
                CreateLogDirectory(),
                $"app-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");

            // AutoFlush keeps the log complete even if the app is killed.
            _writer = new StreamWriter(
                new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _writerFailed = true;
            System.Diagnostics.Debug.WriteLine($"Unable to create downloader log: {exception.Message}");
        }

        return _writer;
    }

    private static string NormalizeLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string CreateLogDirectory()
    {
        try
        {
            Directory.CreateDirectory(DownloaderDataPaths.Logs);
            return DownloaderDataPaths.Logs;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to create preferred log directory: {exception.Message}");
            var fallbackDirectory = Path.Combine(Path.GetTempPath(), "VideoMergeTool", "YouTubeDownloader", "Logs");
            Directory.CreateDirectory(fallbackDirectory);
            return fallbackDirectory;
        }
    }
}
