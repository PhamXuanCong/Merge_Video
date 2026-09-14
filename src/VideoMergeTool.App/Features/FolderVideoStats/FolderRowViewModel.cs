using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoMergeTool.Core.Features.FolderVideoStats.Models;

namespace VideoMergeTool.App.Features.FolderVideoStats;

/// <summary>One row in the folder table: the folder itself plus, once scanned, its direct subfolders.</summary>
public sealed partial class FolderRowViewModel : ObservableObject
{
    public FolderRowViewModel(string folderPath)
    {
        FolderPath = folderPath;
        var name = Path.GetFileName(folderPath);
        FolderName = string.IsNullOrEmpty(name) ? folderPath : name;
    }

    public string FolderPath { get; }

    /// <summary>Just the last path segment, e.g. "channel-a" rather than the full path — a bare drive keeps the full path since it has no name segment.</summary>
    public string FolderName { get; }

    /// <summary>What kind of channel this folder is, as typed by the user (each folder is one YouTube channel).</summary>
    [ObservableProperty]
    private string _topic = string.Empty;

    /// <summary>The hashtag that goes with this channel, as typed by the user.</summary>
    [ObservableProperty]
    private string _hashtag = string.Empty;

    [ObservableProperty]
    private int _videoCount;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isScanning;

    /// <summary>Empty once a scan has completed successfully; otherwise the message to show instead of counts.</summary>
    [ObservableProperty]
    private string _statusText = "Đang quét…";

    [ObservableProperty]
    private ObservableCollection<SubfolderVideoCount> _subfolders = [];

    /// <summary>Tracks the in-flight scan so a second scan (refresh, or a fast re-add) cancels the first.</summary>
    internal CancellationTokenSource? PendingScan { get; set; }

    public void ApplyResult(FolderVideoCounts stats)
    {
        VideoCount = stats.VideoCount;
        Subfolders = new ObservableCollection<SubfolderVideoCount>(stats.Subfolders);
        StatusText = string.Empty;
    }

    public void ApplyError(string message)
    {
        StatusText = message;
        Subfolders = [];
    }
}
