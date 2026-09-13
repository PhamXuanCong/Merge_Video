namespace VideoMergeTool.Core.Features.RenameVideo.Enums;

public enum RenameItemStatus
{
    /// <summary>Previewed and waiting for the user to run the batch.</summary>
    Pending,

    /// <summary>The rules produce the same name, so the file is left alone.</summary>
    Unchanged,

    Renamed,

    Failed
}
