using VideoMergeTool.Core.Enums;
using VideoMergeTool.Core.Features.DownloadVideo.Models;
using VideoMergeTool.Core.Features.FolderVideoStats.Models;
using VideoMergeTool.Core.Features.MergeVideo.Enums;

namespace VideoMergeTool.Core.Models;

public sealed record UserSettings
{
    public string InputFolder { get; init; } = string.Empty;

    public MergeLayout MergeLayout { get; init; } =
        MergeLayout.Horizontal;

    public X264Preset X264Preset { get; init; } =
        X264Preset.VeryFast;

    public VideoEncoder VideoEncoder { get; init; } =
        VideoEncoder.Cpu;

    public int CpuThreadLimit { get; init; } =
        Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

    public PairingMode PairingMode { get; init; } =
        PairingMode.RandomWithoutImmediateRepeat;

    public SourceFileAction SourceFileAction { get; init; } =
        SourceFileAction.Delete;

    public ExistingOutputAction ExistingOutputAction { get; init; } =
        ExistingOutputAction.Skip;

    public ThemePreference ThemePreference { get; init; } =
        ThemePreference.System;

    /// <summary>Most-recently-used input folders, newest first.</summary>
    public IReadOnlyList<string> RecentFolders { get; init; } = [];

    /// <summary>Folder last used by the bulk video renamer.</summary>
    public string RenameFolder { get; init; } = string.Empty;

    /// <summary>Hashtags last appended by the bulk video renamer, as typed.</summary>
    public string RenameHashtags { get; init; } = string.Empty;

    /// <summary>One entry per YouTube downloader tab; empty until the downloader has been saved once.</summary>
    public IReadOnlyList<DownloadTabSettings> DownloadTabs { get; init; } = [];

    public int DownloadSelectedTabIndex { get; init; }

    /// <summary>Shared by every downloader tab, not saved per tab: only one cookie file is ever active at a time.</summary>
    public bool DownloadUseCookieFile { get; init; }

    public string DownloadCookieFilePath { get; init; } = string.Empty;

    /// <summary>Folders tracked by the folder video-count table, in the order they were added.</summary>
    public IReadOnlyList<FolderStatsFolderSettings> FolderStatsFolders { get; init; } = [];

    // Record-synthesized equality would compare the lists by reference, so two settings
    // loaded from the same JSON would never be equal. Compare them by sequence instead.
    public bool Equals(UserSettings? other) =>
        other is not null &&
        InputFolder == other.InputFolder &&
        MergeLayout == other.MergeLayout &&
        X264Preset == other.X264Preset &&
        VideoEncoder == other.VideoEncoder &&
        CpuThreadLimit == other.CpuThreadLimit &&
        PairingMode == other.PairingMode &&
        SourceFileAction == other.SourceFileAction &&
        ExistingOutputAction == other.ExistingOutputAction &&
        ThemePreference == other.ThemePreference &&
        RenameFolder == other.RenameFolder &&
        RenameHashtags == other.RenameHashtags &&
        DownloadSelectedTabIndex == other.DownloadSelectedTabIndex &&
        DownloadUseCookieFile == other.DownloadUseCookieFile &&
        DownloadCookieFilePath == other.DownloadCookieFilePath &&
        RecentFolders.SequenceEqual(other.RecentFolders) &&
        DownloadTabs.SequenceEqual(other.DownloadTabs) &&
        FolderStatsFolders.SequenceEqual(other.FolderStatsFolders);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(InputFolder);
        hash.Add(MergeLayout);
        hash.Add(X264Preset);
        hash.Add(VideoEncoder);
        hash.Add(CpuThreadLimit);
        hash.Add(PairingMode);
        hash.Add(SourceFileAction);
        hash.Add(ExistingOutputAction);
        hash.Add(ThemePreference);
        hash.Add(RenameFolder);
        hash.Add(RenameHashtags);
        hash.Add(DownloadSelectedTabIndex);
        hash.Add(DownloadUseCookieFile);
        hash.Add(DownloadCookieFilePath);
        foreach (var folder in RecentFolders)
        {
            hash.Add(folder);
        }

        foreach (var tab in DownloadTabs)
        {
            hash.Add(tab);
        }

        foreach (var folder in FolderStatsFolders)
        {
            hash.Add(folder);
        }

        return hash.ToHashCode();
    }
}
