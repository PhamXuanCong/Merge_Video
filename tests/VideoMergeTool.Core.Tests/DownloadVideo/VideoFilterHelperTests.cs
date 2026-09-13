using VideoMergeTool.Core.Features.DownloadVideo;
using VideoMergeTool.Core.Features.DownloadVideo.Enums;

namespace VideoMergeTool.Core.Tests.DownloadVideo;

public sealed class VideoFilterHelperTests
{
    [Theory]
    [InlineData(59, true, DurationFilterComparison.ShorterThan, 1, DurationFilterUnit.Minutes, true)]
    [InlineData(60, true, DurationFilterComparison.ShorterThan, 1, DurationFilterUnit.Minutes, false)]
    [InlineData(3601, true, DurationFilterComparison.LongerThan, 1, DurationFilterUnit.Hours, true)]
    [InlineData(90, true, DurationFilterComparison.LongerThan, 90, DurationFilterUnit.Seconds, false)]
    [InlineData(null, true, DurationFilterComparison.ShorterThan, 1, DurationFilterUnit.Minutes, false)]
    [InlineData(null, false, DurationFilterComparison.ShorterThan, 1, DurationFilterUnit.Minutes, true)]
    public void DurationFilterComparesStrictlyAndHidesUnknownDurationsOnlyWhenEnabled(
        int? durationSeconds,
        bool isEnabled,
        DurationFilterComparison comparison,
        int limit,
        DurationFilterUnit unit,
        bool expected)
    {
        Assert.Equal(expected, DurationFilterHelper.Matches(durationSeconds, isEnabled, comparison, limit, unit));
    }

    [Fact]
    public void TakeMostRecentReturnsNewestUploadsFirstAndPutsUndatedLast()
    {
        var items = Items();

        var recent = RecentVideoFilterHelper.TakeMostRecent(items, 3, static item => item.UploadDate);

        string[] expected = ["newest", "newer", "old"];
        Assert.Equal(expected, recent.Select(static item => item.Id));
    }

    [Fact]
    public void TakeMostRecentWithZeroKeepsEveryItemInItsOriginalOrder()
    {
        var items = Items();

        Assert.Equal(items, RecentVideoFilterHelper.TakeMostRecent(items, 0, static item => item.UploadDate));
    }

    private static Video[] Items() =>
    [
        new("old", new DateTime(2024, 1, 1)),
        new("newest", new DateTime(2026, 1, 1)),
        new("unknown-date", null),
        new("newer", new DateTime(2025, 1, 1))
    ];

    private sealed record Video(string Id, DateTime? UploadDate);
}
