namespace VideoMergeTool.Core.Features.FolderVideoStats.Models;

/// <summary>
/// Video counts for one folder that was added to the table: <see cref="VideoCount"/> covers the
/// whole subtree, and <see cref="Subfolders"/> gives the same total for each direct subfolder.
/// </summary>
public sealed record FolderVideoCounts(
    string FolderPath,
    int VideoCount,
    IReadOnlyList<SubfolderVideoCount> Subfolders);

/// <summary>One direct subfolder of a tracked folder, with its own (recursive) video count.</summary>
public sealed record SubfolderVideoCount(
    string FolderPath,
    string FolderName,
    int VideoCount);
