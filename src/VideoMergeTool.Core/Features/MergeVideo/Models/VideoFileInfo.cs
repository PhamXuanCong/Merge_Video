namespace VideoMergeTool.Core.Features.MergeVideo.Models;

public sealed record VideoFileInfo(
    string FullPath,
    string FileName,
    string RelativePath);
