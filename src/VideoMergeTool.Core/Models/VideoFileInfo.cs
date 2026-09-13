namespace VideoMergeTool.Core.Models;

public sealed record VideoFileInfo(
    string FullPath,
    string FileName,
    string RelativePath);
