namespace VideoMergeTool.Core.Features.MergeVideo.Models;

public sealed record VideoMetadata(
    TimeSpan Duration,
    bool HasVideo,
    bool HasAudio,
    int? Width,
    int? Height);
