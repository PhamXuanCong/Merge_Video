namespace VideoMergeTool.Core.Features.RenameVideo.Models;

/// <param name="OriginalName">Current file name, extension included.</param>
/// <param name="DesiredName">Name the rules produce, before any (1), (2) suffix for a clash.</param>
/// <param name="NewName">Name the file will get: <paramref name="DesiredName"/> made unique.</param>
/// <param name="IdFound">Whether a trailing <c>[id]</c> was found and removed.</param>
public sealed record RenamePlanItem(string OriginalName, string DesiredName, string NewName, bool IdFound)
{
    public bool HasChange => !string.Equals(OriginalName, NewName, StringComparison.Ordinal);

    public bool SuffixAdded => !string.Equals(DesiredName, NewName, StringComparison.Ordinal);

    /// <summary>Short remark shown in the preview and written to the rename log.</summary>
    public string Note
    {
        get
        {
            var parts = new List<string>(3);
            if (!IdFound)
            {
                parts.Add("Không tìm thấy ID");
            }

            if (SuffixAdded)
            {
                parts.Add("Trùng tên, đã thêm số thứ tự");
            }

            if (!HasChange)
            {
                parts.Add("Giữ nguyên tên");
            }

            return string.Join(" · ", parts);
        }
    }
}
