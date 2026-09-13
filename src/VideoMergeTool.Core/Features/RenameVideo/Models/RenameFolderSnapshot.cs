namespace VideoMergeTool.Core.Features.RenameVideo.Models;

/// <param name="VideoFileNames">Video files directly inside the folder, sorted by name.</param>
/// <param name="EntryNames">Every file and subfolder name in the folder, used to avoid name clashes.</param>
public sealed record RenameFolderSnapshot(
    string Folder,
    IReadOnlyList<string> VideoFileNames,
    IReadOnlyList<string> EntryNames);
