using VideoMergeTool.Core.Enums;

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

    // Record-synthesized equality would compare RecentFolders by reference, so two settings
    // loaded from the same JSON would never be equal. Compare it by sequence instead.
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
        RecentFolders.SequenceEqual(other.RecentFolders);

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
        foreach (var folder in RecentFolders)
        {
            hash.Add(folder);
        }

        return hash.ToHashCode();
    }
}
