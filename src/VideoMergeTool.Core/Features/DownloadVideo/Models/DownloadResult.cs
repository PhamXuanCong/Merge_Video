using VideoMergeTool.Core.Features.DownloadVideo.Enums;

namespace VideoMergeTool.Core.Features.DownloadVideo.Models;

public sealed record DownloadResult(
    DownloadStatus Status,
    string? OutputFilePath = null,
    string? ErrorMessage = null);
