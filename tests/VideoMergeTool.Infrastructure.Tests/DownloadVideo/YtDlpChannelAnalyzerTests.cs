using VideoMergeTool.Core.Features.DownloadVideo;
using VideoMergeTool.Core.Features.DownloadVideo.Enums;
using VideoMergeTool.Core.Features.DownloadVideo.Models;
using VideoMergeTool.Infrastructure.Features.DownloadVideo;

namespace VideoMergeTool.Infrastructure.Tests.DownloadVideo;

public sealed class YtDlpChannelAnalyzerTests
{
    private const string DefaultChannelJson = """
        {
          "channel": "Test Channel",
          "entries": [
            { "id": "missing-duration", "title": "Missing duration", "duration": null },
            { "id": "known-duration", "title": "Known duration", "duration": 75 }
          ]
        }
        """;

    private readonly MemoryDownloadLogger _logger = new();
    private readonly List<int> _delays = [];

    [Fact]
    public async Task AnalyzeAsyncAppliesYouTubeDataApiDurationsBeforeCreatingItems()
    {
        var analyzer = CreateAnalyzer(
            new ScriptedProcess(FakeYtDlp),
            apiDurations: new Dictionary<string, double> { ["missing-duration"] = 48 });

        var result = await AnalyzeAsync(analyzer, "@ChannelHandle", resolveMissingDurations: false, useBrowserCookies: false);

        Assert.Equal(48, result.Items.Single(static item => item.VideoId == "missing-duration").DurationSeconds);
        Assert.Contains(_logger.Entries, static entry => entry.Message.Contains("through YouTube Data API", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeAsyncResolvesMissingDurationsThroughTheBrowserCookieFallback()
    {
        var process = new ScriptedProcess(FakeYtDlp);
        var analyzer = CreateAnalyzer(process);

        var result = await AnalyzeAsync(analyzer, "@ChannelHandle", resolveMissingDurations: true, useBrowserCookies: true);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Test Channel", result.ChannelName);
        Assert.Equal(48, result.Items.Single(static item => item.VideoId == "missing-duration").DurationSeconds);
        Assert.Equal(75, result.Items.Single(static item => item.VideoId == "known-duration").DurationSeconds);
        Assert.Contains(_logger.Entries, static entry =>
            entry.VideoId == "missing-duration" && entry.Message.Contains("Duration resolved successfully", StringComparison.Ordinal));

        // Only the video without a duration needed a lookup, and it used the chosen browser's cookies.
        var lookup = Assert.Single(process.Invocations, static arguments => arguments.Contains("--print"));
        Assert.Contains("https://www.youtube.com/watch?v=missing-duration", lookup);
        Assert.Equal("chrome", lookup[lookup.ToList().IndexOf("--cookies-from-browser") + 1]);
    }

    [Fact]
    public async Task AnalyzeAsyncSkipsDurationLookupsWhenDurationFilteringIsOff()
    {
        var process = new ScriptedProcess(FakeYtDlp);
        var analyzer = CreateAnalyzer(process);

        var result = await AnalyzeAsync(analyzer, "@ChannelHandle", resolveMissingDurations: false, useBrowserCookies: false);

        Assert.Equal(2, result.Items.Count);
        Assert.Null(result.Items.Single(static item => item.VideoId == "missing-duration").DurationSeconds);
        Assert.DoesNotContain(process.Invocations, static arguments => arguments.Contains("--print"));
    }

    [Fact]
    public async Task AnalyzeAsyncContinuesAfterBotVerificationAndPausesBetweenLookups()
    {
        var analyzer = CreateAnalyzer(new ScriptedProcess(FakeYtDlp));

        var result = await AnalyzeAsync(analyzer, "@AuthenticationRequired", resolveMissingDurations: true, useBrowserCookies: true);

        Assert.Equal(2, result.Items.Count);
        Assert.Null(result.Items.Single(static item => item.VideoId == "auth-required-1").DurationSeconds);
        Assert.Equal(33, result.Items.Single(static item => item.VideoId == "auth-required-2").DurationSeconds);
        Assert.Contains(_logger.Entries, static entry =>
            entry.Message.Contains("continuing with the next video", StringComparison.Ordinal));
        var delay = Assert.Single(_delays);
        Assert.InRange(delay, 1000, 2000);
    }

    [Fact]
    public async Task AnalyzeAsyncUsesCachedDurationsWithoutCallingYtDlpAgain()
    {
        var process = new ScriptedProcess(FakeYtDlp);
        var analyzer = CreateAnalyzer(process, cache: new MemoryDurationCache(new() { ["missing-duration"] = 42 }));

        var result = await AnalyzeAsync(analyzer, "@ChannelHandle", resolveMissingDurations: true, useBrowserCookies: true);

        Assert.Equal(42, result.Items.Single(static item => item.VideoId == "missing-duration").DurationSeconds);
        Assert.Contains(_logger.Entries, static entry => entry.Message.Contains("from cache", StringComparison.Ordinal));
        Assert.Contains(_logger.Entries, static entry =>
            entry.VideoId == "missing-duration" && entry.Message.Contains("Duration cache hit", StringComparison.Ordinal));
        Assert.DoesNotContain(process.Invocations, static arguments => arguments.Contains("--print"));
    }

    [Fact]
    public async Task AnalyzeAsyncPassesThePlaylistLimitAndKeepsTheNewestVideos()
    {
        var analyzer = CreateAnalyzer(new ScriptedProcess(FakeYtDlp));

        var result = await analyzer.AnalyzeAsync(
            new Uri("https://www.youtube.com/@LimitedChannel"),
            [ContentType.Short],
            recentVideoLimit: 2,
            resolveMissingDurations: false,
            useBrowserCookies: false,
            browserName: "chrome",
            CancellationToken.None);

        string[] expected = ["limited-3", "limited-2"];
        Assert.Equal(expected, result.Items.Select(static item => item.VideoId));
        Assert.Contains(_logger.Entries, static entry => entry.Message.Contains("requested limit of 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeAsyncMergesTabsAndSkipsStreamsThatHaveNotEnded()
    {
        var process = new ScriptedProcess(arguments => arguments[^1].EndsWith("/streams", StringComparison.Ordinal)
            ? (0, """
                {"entries": [
                  { "id": "shared", "title": "Also a stream" },
                  { "id": "live-now", "title": "Live", "live_status": "is_live" },
                  { "id": "replay", "title": "Replay", "live_status": "was_live", "availability": "subscriber_only" }
                ]}
                """, string.Empty)
            : (0, """{"uploader": "Uploader Name", "entries": [{ "id": "shared", "title": "Video" }]}""", string.Empty));
        var analyzer = CreateAnalyzer(process);

        var result = await analyzer.AnalyzeAsync(
            new Uri("https://www.youtube.com/@Channel"),
            [ContentType.Video, ContentType.Stream],
            recentVideoLimit: 0,
            resolveMissingDurations: false,
            useBrowserCookies: false,
            browserName: "chrome",
            CancellationToken.None);

        Assert.Equal("Uploader Name", result.ChannelName);
        Assert.Collection(
            result.Items,
            item =>
            {
                Assert.Equal("shared", item.VideoId);
                Assert.Equal(ContentType.Video, item.ContentType);
                Assert.True(item.IsSelected);
            },
            item =>
            {
                Assert.Equal("replay", item.VideoId);
                Assert.Equal(DownloadStatus.RequiresLogin, item.Status);
                Assert.False(item.IsSelected);
            });
    }

    [Theory]
    [InlineData("ERROR: [youtube:tab] This channel does not exist.", "Kênh không tồn tại hoặc không khả dụng.")]
    [InlineData("ERROR: Unable to download API page: network is unreachable", "Không thể kết nối YouTube. Hãy kiểm tra mạng rồi thử lại.")]
    [InlineData("", "Không thể phân tích kênh (yt-dlp exit code 1).")]
    public async Task AnalyzeAsyncTurnsAnEmptyYtDlpResultIntoAFriendlyError(string standardError, string expectedMessage)
    {
        var analyzer = CreateAnalyzer(new ScriptedProcess(_ => (1, string.Empty, standardError)));

        var exception = await Assert.ThrowsAsync<ChannelAnalysisException>(
            () => AnalyzeAsync(analyzer, "@Missing", resolveMissingDurations: false, useBrowserCookies: false));

        Assert.Equal(expectedMessage, exception.Message);
    }

    private YtDlpChannelAnalyzer CreateAnalyzer(
        ScriptedProcess process,
        MemoryDurationCache? cache = null,
        IReadOnlyDictionary<string, double>? apiDurations = null) =>
        new(
            new FakeDependencyService(),
            _logger,
            cache ?? new MemoryDurationCache(),
            new MemoryVideoMetadataLogger(),
            new MemoryYouTubeDataApiDurationResolver(apiDurations),
            process.RunAsync,
            (milliseconds, _) =>
            {
                _delays.Add(milliseconds);
                return Task.CompletedTask;
            });

    private static Task<ChannelAnalysisResult> AnalyzeAsync(
        YtDlpChannelAnalyzer analyzer,
        string handle,
        bool resolveMissingDurations,
        bool useBrowserCookies) =>
        analyzer.AnalyzeAsync(
            new Uri($"https://www.youtube.com/{handle}"),
            [ContentType.Short],
            recentVideoLimit: 0,
            resolveMissingDurations,
            useBrowserCookies,
            browserName: "Chrome",
            CancellationToken.None);

    /// <summary>The same canned channels the original LogicChecks fake yt-dlp served.</summary>
    private static (int, string, string) FakeYtDlp(IReadOnlyList<string> arguments)
    {
        if (arguments.Contains("--flat-playlist"))
        {
            if (arguments.Any(static argument => argument.Contains("@LimitedChannel", StringComparison.Ordinal)))
            {
                var limitIndex = arguments.ToList().IndexOf("--playlist-end");
                if (limitIndex < 0 || arguments[limitIndex + 1] != "2")
                {
                    return (1, string.Empty, "ERROR: expected --playlist-end 2");
                }

                return (0, """
                    {
                      "channel": "Limited Test Channel",
                      "entries": [
                        { "id": "limited-1", "title": "Old", "upload_date": "20240101" },
                        { "id": "limited-3", "title": "Newest", "upload_date": "20260101" },
                        { "id": "limited-2", "title": "Newer", "upload_date": "20250101" }
                      ]
                    }
                    """, string.Empty);
            }

            if (arguments.Any(static argument => argument.Contains("@AuthenticationRequired", StringComparison.Ordinal)))
            {
                return (0, """
                    {
                      "channel": "Authentication Test Channel",
                      "entries": [
                        { "id": "auth-required-1", "title": "First" },
                        { "id": "auth-required-2", "title": "Second" }
                      ]
                    }
                    """, string.Empty);
            }

            return (0, DefaultChannelJson, string.Empty);
        }

        if (arguments.Contains("--print"))
        {
            if (arguments.Any(static argument => argument.Contains("v=auth-required-1", StringComparison.Ordinal)))
            {
                return (1, string.Empty, "ERROR: [youtube] Sign in to confirm you're not a bot. Use --cookies-from-browser.");
            }

            if (arguments.Any(static argument => argument.Contains("v=auth-required-2", StringComparison.Ordinal)))
            {
                return (0, "DURATION|auth-required-2|33", string.Empty);
            }

            if (arguments.Any(static argument => argument.Contains("v=missing-duration", StringComparison.Ordinal)))
            {
                return (0, "DURATION|missing-duration|48", string.Empty);
            }
        }

        return (0, string.Empty, string.Empty);
    }
}
