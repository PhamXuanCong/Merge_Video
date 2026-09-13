using System.Xml;

namespace VideoMergeTool.Infrastructure.Features.DownloadVideo;

public static class YouTubeApiDurationParser
{
    /// <summary>
    /// Parses the ISO 8601 duration returned in <c>video.contentDetails.duration</c>, e.g. <c>PT1H2M3S</c>.
    /// </summary>
    public static bool TryParse(string? value, out double durationSeconds)
    {
        durationSeconds = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var duration = XmlConvert.ToTimeSpan(value);
            if (duration < TimeSpan.Zero || !double.IsFinite(duration.TotalSeconds))
            {
                return false;
            }

            durationSeconds = duration.TotalSeconds;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
