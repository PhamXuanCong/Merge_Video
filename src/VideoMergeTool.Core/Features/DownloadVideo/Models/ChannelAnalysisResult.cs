namespace VideoMergeTool.Core.Features.DownloadVideo.Models;

public sealed record ChannelAnalysisResult(string ChannelName, IReadOnlyList<DownloadItem> Items);
