using VideoMergeTool.Core.Enums;

namespace VideoMergeTool.Core.Models;

public sealed record ProcessingOptions
{
    public PairingMode PairingMode { get; init; } =
        PairingMode.RandomWithoutImmediateRepeat;

    public MergeLayout MergeLayout { get; init; } =
        MergeLayout.Horizontal;

    public X264Preset X264Preset { get; init; } =
        X264Preset.VeryFast;

    public VideoEncoder VideoEncoder { get; init; } =
        VideoEncoder.Cpu;

    public int CpuThreadLimit { get; init; } =4;

    public SourceFileAction SourceFileAction { get; init; } =
        SourceFileAction.Delete;

    public ExistingOutputAction ExistingOutputAction { get; init; } =
        ExistingOutputAction.Skip;

    public int OutputWidth { get; init; } = 1080;

    public int OutputHeight { get; init; } = 1920;

    public int LeftRegionWidth => OutputWidth / 2;

    public TimeSpan DurationTolerance { get; init; } =
        TimeSpan.FromSeconds(1);
}
