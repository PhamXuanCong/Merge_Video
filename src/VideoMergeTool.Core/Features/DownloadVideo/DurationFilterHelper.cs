using VideoMergeTool.Core.Features.DownloadVideo.Enums;

namespace VideoMergeTool.Core.Features.DownloadVideo;

public static class DurationFilterHelper
{
    /// <summary>
    /// A disabled filter matches everything; an enabled one hides videos whose duration is unknown.
    /// </summary>
    public static bool Matches(
        int? durationSeconds,
        bool isEnabled,
        DurationFilterComparison comparison,
        int limitValue,
        DurationFilterUnit unit)
    {
        if (!isEnabled)
        {
            return true;
        }

        if (durationSeconds is null)
        {
            return false;
        }

        var safeLimitValue = Math.Max(1, limitValue);
        var multiplier = unit switch
        {
            DurationFilterUnit.Seconds => 1L,
            DurationFilterUnit.Minutes => 60L,
            DurationFilterUnit.Hours => 3600L,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
        var limitSeconds = safeLimitValue * multiplier;

        return comparison switch
        {
            DurationFilterComparison.ShorterThan => durationSeconds.Value < limitSeconds,
            DurationFilterComparison.LongerThan => durationSeconds.Value > limitSeconds,
            _ => throw new ArgumentOutOfRangeException(nameof(comparison))
        };
    }
}
