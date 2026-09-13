using VideoMergeTool.Core.Features.RenameVideo.Enums;

namespace VideoMergeTool.Core.Features.RenameVideo.Models;

public sealed record RenameBatchResult(IReadOnlyList<RenameItemResult> Items, bool WasCancelled)
{
    public int RenamedCount => Items.Count(item => item.Status == RenameItemStatus.Renamed);

    public int FailedCount => Items.Count(item => item.Status == RenameItemStatus.Failed);
}
