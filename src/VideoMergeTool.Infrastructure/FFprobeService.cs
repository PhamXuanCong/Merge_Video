using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using VideoMergeTool.Core.Interfaces;
using VideoMergeTool.Core.Models;

namespace VideoMergeTool.Infrastructure;

public sealed class FFprobeService : IFFprobeService
{
    private readonly string _ffprobePath;

    public FFprobeService(ApplicationPaths paths)
        : this(paths.FFprobePath)
    {
    }

    public FFprobeService(string ffprobePath)
    {
        _ffprobePath = ffprobePath;
    }

    public async Task<VideoMetadata> GetMetadataAsync(
        string videoPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(_ffprobePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("format=duration:stream=codec_type,width,height");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add(videoPath);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        try
        {
            using var registration = cancellationToken.Register(() => KillProcess(process));
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            var output = await standardOutput;
            var error = await standardError;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"FFprobe failed for '{videoPath}': {error.Trim()}");
            }

            return ParseMetadata(output);
        }
        catch
        {
            // Covers cancellation and any parse or I/O failure: never leave an ffprobe process behind.
            KillProcess(process);
            throw;
        }
    }

    public static VideoMetadata ParseMetadata(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var hasVideo = false;
        var hasAudio = false;
        int? width = null;
        int? height = null;

        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streams.EnumerateArray())
            {
                if (!stream.TryGetProperty("codec_type", out var codecType))
                {
                    continue;
                }

                switch (codecType.GetString())
                {
                    case "video" when !hasVideo:
                        hasVideo = true;
                        width = ReadInt32(stream, "width");
                        height = ReadInt32(stream, "height");
                        break;
                    case "audio":
                        hasAudio = true;
                        break;
                }

                if (hasVideo && hasAudio)
                {
                    break;
                }
            }
        }

        var duration = TimeSpan.Zero;
        if (root.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var durationProperty) &&
            double.TryParse(
                durationProperty.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var seconds))
        {
            duration = TimeSpan.FromSeconds(seconds);
        }

        return new VideoMetadata(duration, hasVideo, hasAudio, width, height);
    }

    private static int? ReadInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and Kill.
        }
    }
}
