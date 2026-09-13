using VideoMergeTool.Core.Features.MergeVideo.Models;

namespace VideoMergeTool.Core.Features.MergeVideo.Interfaces;

public interface IOutputPathService
{
    string GetOutputFilePath(string inputFolder, VideoFileInfo inputVideo);

    string GetTemporaryOutputFilePath(string outputFilePath);

    string GetProcessedFilePath(string inputFolder, VideoFileInfo inputVideo);
}
