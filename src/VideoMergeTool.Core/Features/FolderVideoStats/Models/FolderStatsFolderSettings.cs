namespace VideoMergeTool.Core.Features.FolderVideoStats.Models;

/// <summary>
/// One folder tracked by the folder-video-count table, plus the channel metadata the user typed
/// in for it (each folder is one YouTube channel).
/// </summary>
public sealed record FolderStatsFolderSettings(
    string FolderPath,
    string Topic,
    string Hashtag);
