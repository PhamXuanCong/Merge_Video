using VideoMergeTool.Core.Features.DownloadVideo.Enums;
using VideoMergeTool.Core.Features.DownloadVideo.Models;

namespace VideoMergeTool.Core.Features.DownloadVideo.Interfaces;

/// <summary>
/// Writes sanitized downloader log entries to disk and broadcasts them to the UI.
/// </summary>
public interface IDownloadLogger
{
    event EventHandler<DownloadLogEntry>? EntryLogged;

    void Log(DownloadLogLevel level, string message, string? videoId = null, Exception? exception = null);

    void Info(string message, string? videoId = null);

    void Warning(string message, string? videoId = null);

    void Error(string message, string? videoId = null, Exception? exception = null);

    void Debug(string message, string? videoId = null);
}
