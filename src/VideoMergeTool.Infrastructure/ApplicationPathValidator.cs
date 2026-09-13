using VideoMergeTool.Core.Models;

namespace VideoMergeTool.Infrastructure;

public sealed class ApplicationPathValidator
{
    private readonly ApplicationPaths _paths;

    public ApplicationPathValidator(ApplicationPaths paths)
    {
        _paths = paths;
    }

    public void Validate()
    {
        EnsureFileExists(_paths.FFmpegPath, "Không tìm thấy ffmpeg.exe.");
        EnsureFileExists(_paths.FFprobePath, "Không tìm thấy ffprobe.exe.");

        if (!Directory.Exists(_paths.CompanionVideoDirectory))
        {
            throw new InvalidOperationException(
                $"Không tìm thấy thư mục video đi kèm: {_paths.CompanionVideoDirectory}");
        }

        try
        {
            var containsCompanionVideo = Directory
                .EnumerateFiles(_paths.CompanionVideoDirectory, "*", SearchOption.TopDirectoryOnly)
                .Any(file => string.Equals(
                    Path.GetExtension(file),
                    ".mp4",
                    StringComparison.OrdinalIgnoreCase));

            if (!containsCompanionVideo)
            {
                throw new InvalidOperationException(
                    $"Không tìm thấy video MP4 đi kèm trong: {_paths.CompanionVideoDirectory}");
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException(
                $"Không thể đọc thư mục video đi kèm: {_paths.CompanionVideoDirectory}",
                exception);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                $"Không thể đọc thư mục video đi kèm: {_paths.CompanionVideoDirectory}",
                exception);
        }
    }

    private static void EnsureFileExists(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"{description} Đường dẫn: {path}");
        }
    }
}
