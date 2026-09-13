namespace VideoMergeTool.Core.Features.MergeVideo.Models;

public sealed record ProcessingSummary(IReadOnlyList<VideoMergeTask> Tasks)
{
    public int CompletedCount => Tasks.Count(task => task.Status == Enums.VideoTaskStatus.Completed);

    public int FailedCount => Tasks.Count(task => task.Status == Enums.VideoTaskStatus.Failed);

    public int SkippedCount => Tasks.Count(task => task.Status == Enums.VideoTaskStatus.Skipped);

    public int CancelledCount => Tasks.Count(task => task.Status == Enums.VideoTaskStatus.Cancelled);
}
