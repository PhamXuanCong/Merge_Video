using VideoMergeTool.Core.Features.DownloadVideo.Enums;

namespace VideoMergeTool.Core.Features.DownloadVideo.Models;

public sealed record DownloadProgressMessage(
    ProgressMessageType Type,
    string VideoId,
    string Message,
    double? Percent = null,
    string Speed = "",
    string Eta = "",
    string? OutputFilePath = null);
