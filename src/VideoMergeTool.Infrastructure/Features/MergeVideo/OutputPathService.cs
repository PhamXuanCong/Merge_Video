using VideoMergeTool.Core.Features.MergeVideo.Interfaces;
using VideoMergeTool.Core.Features.MergeVideo.Models;

namespace VideoMergeTool.Infrastructure.Features.MergeVideo;

public sealed class OutputPathService : IOutputPathService
{
    public string GetOutputFilePath(string inputFolder, VideoFileInfo inputVideo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFolder);
        ArgumentNullException.ThrowIfNull(inputVideo);

        return Path.Combine(inputFolder, "output", inputVideo.RelativePath);
    }

    public string GetTemporaryOutputFilePath(string outputFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFilePath);

        var extension = Path.GetExtension(outputFilePath);
        var fileName = Path.GetFileNameWithoutExtension(outputFilePath);
        var directory = Path.GetDirectoryName(outputFilePath);
        return Path.Combine(directory!, $"{fileName}.processing{extension}");
    }

    public string GetProcessedFilePath(string inputFolder, VideoFileInfo inputVideo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFolder);
        ArgumentNullException.ThrowIfNull(inputVideo);

        return Path.Combine(inputFolder, "processed", inputVideo.RelativePath);
    }
}
