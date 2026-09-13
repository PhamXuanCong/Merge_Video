using VideoMergeTool.Core.Features.RenameVideo.Enums;

namespace VideoMergeTool.Core.Features.RenameVideo.Models;

/// <summary>One row of the rename log kept inside the renamed folder.</summary>
public sealed record RenameLogEntry(
    DateTime Time,
    string BatchId,
    RenameLogAction Action,
    string OldName,
    string NewName,
    RenameItemStatus Status,
    string Note);
