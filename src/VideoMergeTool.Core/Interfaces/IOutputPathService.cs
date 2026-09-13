using VideoMergeTool.Core.Models;

namespace VideoMergeTool.Core.Interfaces;

public interface IOutputPathService
{
    string GetOutputFilePath(string inputFolder, VideoFileInfo inputVideo);

    string GetTemporaryOutputFilePath(string outputFilePath);

    string GetProcessedFilePath(string inputFolder, VideoFileInfo inputVideo);
}
