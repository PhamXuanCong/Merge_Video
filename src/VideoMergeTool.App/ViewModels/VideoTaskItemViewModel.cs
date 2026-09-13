using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoMergeTool.Core.Enums;
using VideoMergeTool.Core.Models;

namespace VideoMergeTool.App.ViewModels;

public sealed partial class VideoTaskItemViewModel : ObservableObject
{
    public VideoTaskItemViewModel(string inputFile)
    {
        _inputFile = inputFile;
        FileName = Path.GetFileName(inputFile);
    }

    [ObservableProperty]
    private string _inputFile;

    /// <summary>The file name alone; the grid shows this and keeps the full path in a tooltip.</summary>
    public string FileName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFinished))]
    [NotifyPropertyChangedFor(nameof(EffectiveProgress))]
    private VideoTaskStatus _status = VideoTaskStatus.Pending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveProgress))]
    private double _progress;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsFinished => Status is
        VideoTaskStatus.Completed or
        VideoTaskStatus.Skipped or
        VideoTaskStatus.Failed or
        VideoTaskStatus.Cancelled;

    /// <summary>
    /// Progress for the overall bar. A skipped or failed file is done with, so it counts as
    /// finished rather than pinning the batch total below 100%.
    /// </summary>
    public double EffectiveProgress => IsFinished ? 1 : Progress;

    public void Update(VideoMergeTask task)
    {
        Status = task.Status;
        Progress = task.Progress;
        ErrorMessage = task.ErrorMessage;
    }

    public void Reset()
    {
        Status = VideoTaskStatus.Pending;
        Progress = 0;
        ErrorMessage = null;
    }
}
