namespace VideoMergeTool.Core.Features.DownloadVideo;

public static class RecentVideoFilterHelper
{
    /// <summary>
    /// Keeps the <paramref name="maximumCount"/> newest items (dated items first, newest first,
    /// original order as the tie-break). Zero or less keeps every item in its original order.
    /// </summary>
    public static IReadOnlyList<T> TakeMostRecent<T>(
        IEnumerable<T> items,
        int maximumCount,
        Func<T, DateTime?> uploadDateSelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(uploadDateSelector);

        var indexedItems = items
            .Select((item, index) => new IndexedItem<T>(
                item,
                index,
                uploadDateSelector(item)))
            .ToArray();

        if (maximumCount <= 0)
        {
            return indexedItems.Select(static item => item.Value).ToArray();
        }

        return indexedItems
            .OrderByDescending(static item => item.UploadDate.HasValue)
            .ThenByDescending(static item => item.UploadDate)
            .ThenBy(static item => item.Index)
            .Take(maximumCount)
            .Select(static item => item.Value)
            .ToArray();
    }

    private sealed record IndexedItem<T>(T Value, int Index, DateTime? UploadDate);
}
