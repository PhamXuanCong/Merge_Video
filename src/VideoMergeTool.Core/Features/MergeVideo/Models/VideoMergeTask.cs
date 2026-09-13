using VideoMergeTool.Core.Features.MergeVideo.Enums;

namespace VideoMergeTool.Core.Features.MergeVideo.Models;

public sealed class VideoMergeTask
{
    public required string InputFile { get; init; }

    public required string CompanionFile { get; init; }

    public required string OutputFile { get; init; }

    public required string TemporaryOutputFile { get; init; }

    public TimeSpan? InputDuration { get; set; }

    public VideoTaskStatus Status { get; set; } =
        VideoTaskStatus.Pending;

    public double Progress { get; set; }

    public string? ErrorMessage { get; set; }
}
