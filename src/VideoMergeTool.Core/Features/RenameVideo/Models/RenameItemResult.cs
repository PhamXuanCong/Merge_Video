using VideoMergeTool.Core.Features.RenameVideo.Enums;

namespace VideoMergeTool.Core.Features.RenameVideo.Models;

/// <param name="OriginalName">Name before this operation.</param>
/// <param name="NewName">Name the file actually got, or the name that was attempted if it failed.</param>
public sealed record RenameItemResult(string OriginalName, string NewName, RenameItemStatus Status, string Note);
