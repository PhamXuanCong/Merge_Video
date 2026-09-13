namespace VideoMergeTool.Core.Features.MergeVideo.Enums;

public enum VideoEncoder
{
    /// <summary>Software encode with libx264. Works on every machine, uses CPU only.</summary>
    Cpu,

    /// <summary>Hardware encode with NVIDIA NVENC (h264_nvenc). Needs an NVIDIA GPU and driver.</summary>
    NvidiaGpu
}
