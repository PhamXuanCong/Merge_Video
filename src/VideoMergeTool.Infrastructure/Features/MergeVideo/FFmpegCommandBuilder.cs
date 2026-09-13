using System.Globalization;
using VideoMergeTool.Core.Features.MergeVideo.Enums;
using VideoMergeTool.Core.Features.MergeVideo.Models;

namespace VideoMergeTool.Infrastructure.Features.MergeVideo;

public sealed class FFmpegCommandBuilder
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Kept as an instance member so the builder stays an injectable seam.")]
    public IReadOnlyList<string> BuildArguments(VideoMergeTask task, ProcessingOptions options)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(options);

        if (task.InputDuration is null)
        {
            throw new InvalidOperationException("Input duration must be read before creating the FFmpeg command.");
        }

        if (options.CpuThreadLimit < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.CpuThreadLimit,
                "The CPU thread limit must be at least one.");
        }

        EnsureEncodableDimensions(options);

        var threadLimit = options.CpuThreadLimit.ToString(CultureInfo.InvariantCulture);
        var filter = options.MergeLayout switch
        {
            MergeLayout.Horizontal => FormattableString.Invariant(
                $"[0:v]scale={options.LeftRegionWidth}:{options.OutputHeight}:force_original_aspect_ratio=increase,crop={options.LeftRegionWidth}:{options.OutputHeight},setsar=1[left];[1:v]scale={options.LeftRegionWidth}:{options.OutputHeight}:force_original_aspect_ratio=increase,crop={options.LeftRegionWidth}:{options.OutputHeight},setsar=1[right];[left][right]hstack=inputs=2[v]"),
            MergeLayout.Vertical => FormattableString.Invariant(
                $"[0:v]scale={options.OutputWidth}:{options.OutputHeight / 2}:force_original_aspect_ratio=increase,crop={options.OutputWidth}:{options.OutputHeight / 2},setsar=1[top];[1:v]scale={options.OutputWidth}:{options.OutputHeight / 2}:force_original_aspect_ratio=increase,crop={options.OutputWidth}:{options.OutputHeight / 2},setsar=1[bottom];[top][bottom]vstack=inputs=2[v]"),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.MergeLayout, null)
        };

        var arguments = new List<string>
        {
            "-y",
            "-filter_threads", threadLimit,
            "-filter_complex_threads", threadLimit,
            "-threads", threadLimit,
            "-i", task.InputFile,
            "-stream_loop", "-1",
            "-threads", threadLimit,
            "-i", task.CompanionFile,
            "-filter_complex", filter,
            "-map", "[v]",
            "-map", "0:a?"
        };

        arguments.AddRange(GetVideoCodecArguments(options, threadLimit));

        arguments.AddRange(
        [
            // Without this the pixel format follows the source, and a 10-bit or 4:2:2 input
            // produces an H.264 file that most players and upload pipelines reject.
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-t", task.InputDuration.Value.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture),
            "-progress", "pipe:1",
            "-nostats",
            task.TemporaryOutputFile
        ]);

        return arguments;
    }

    /// <summary>
    /// h264_nvenc needs an NVIDIA GPU and driver; if the machine has neither, FFmpeg exits with a
    /// non-zero code and its stderr tail (e.g. "no NVENC capable devices found") surfaces as usual
    /// through the task's Details column, so no separate hardware detection is needed here.
    /// </summary>
    private static IEnumerable<string> GetVideoCodecArguments(ProcessingOptions options, string threadLimit) =>
        options.VideoEncoder switch
        {
            VideoEncoder.Cpu =>
            [
                "-c:v", "libx264",
                "-threads", threadLimit,
                "-preset", GetX264Preset(options.X264Preset)
            ],
            VideoEncoder.NvidiaGpu =>
            [
                "-c:v", "h264_nvenc",
                "-preset", GetNvencPreset(options.X264Preset)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.VideoEncoder, null)
        };

    /// <summary>
    /// libx264 with yuv420p needs even width and height on every stacked region, so catch a bad
    /// size here with a clear message instead of letting FFmpeg fail mid-batch.
    /// </summary>
    private static void EnsureEncodableDimensions(ProcessingOptions options)
    {
        var regionWidth = options.MergeLayout == MergeLayout.Horizontal
            ? options.LeftRegionWidth
            : options.OutputWidth;
        var regionHeight = options.MergeLayout == MergeLayout.Horizontal
            ? options.OutputHeight
            : options.OutputHeight / 2;

        if (regionWidth < 2 || regionHeight < 2 || regionWidth % 2 != 0 || regionHeight % 2 != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                FormattableString.Invariant($"{options.OutputWidth}x{options.OutputHeight}"),
                FormattableString.Invariant(
                    $"The {options.MergeLayout} layout needs each region to be an even size of at least 2x2, but it resolves to {regionWidth}x{regionHeight}."));
        }
    }

    private static string GetX264Preset(X264Preset preset) => preset switch
    {
        X264Preset.Medium => "medium",
        X264Preset.Fast => "fast",
        X264Preset.Faster => "faster",
        X264Preset.VeryFast => "veryfast",
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
    };

    /// <summary>
    /// Reuses the same "compression speed" choice the UI already offers for libx264, mapped onto
    /// NVENC's own p1 (fastest/lowest quality) .. p7 (slowest/best quality) preset scale, so
    /// switching to the GPU encoder does not require a second preset picker.
    /// </summary>
    private static string GetNvencPreset(X264Preset preset) => preset switch
    {
        X264Preset.Medium => "p6",
        X264Preset.Fast => "p4",
        X264Preset.Faster => "p2",
        X264Preset.VeryFast => "p1",
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
    };
}
