using System.Net;
using System.Text.Json;
using VideoMergeTool.Core.Features.DownloadVideo.Enums;
using VideoMergeTool.Core.Features.DownloadVideo.Models;
using VideoMergeTool.Core.Models;
using VideoMergeTool.Infrastructure.Features.DownloadVideo;

namespace VideoMergeTool.Infrastructure.Tests.DownloadVideo;

public sealed class DownloadStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"VideoMergeTool.Tests.{Guid.NewGuid():N}");
    private readonly MemoryDownloadLogger _logger = new();

    public DownloadStorageTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task DurationCachePersistsAcrossInstances()
    {
        using var cache = new FileVideoDurationCache(_logger, _root);
        await cache.StoreAsync("cached-video", 64.5, CancellationToken.None);

        using var reloaded = new FileVideoDurationCache(_logger, _root);

        Assert.True(reloaded.TryGet("cached-video", out var duration));
        Assert.Equal(64.5, duration, precision: 3);
        Assert.True(File.Exists(cache.CachePath));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void DurationCacheStartsEmptyWhenTheFileIsCorrupt()
    {
        File.WriteAllText(Path.Combine(_root, "durations.json"), "{ not json");

        using var cache = new FileVideoDurationCache(_logger, _root);

        Assert.False(cache.TryGet("anything", out _));
        Assert.Contains(_logger.Entries, static entry => entry.Level == DownloadLogLevel.Warning);
    }

    [Fact]
    public async Task MetadataLoggerWritesOneJsonSnapshotPerAnalysis()
    {
        var metadataLogger = new FileVideoMetadataLogger(_logger, _root);

        await metadataLogger.WriteAsync(
            new Uri("https://www.youtube.com/@MetadataChannel"),
            "Metadata Channel",
            [
                new DownloadItem
                {
                    VideoId = "metadata-video",
                    Title = "Metadata video",
                    Url = "https://www.youtube.com/watch?v=metadata-video",
                    ChannelName = "Metadata Channel",
                    ContentType = ContentType.Short,
                    DurationSeconds = 48,
                    UploadDate = new DateTime(2026, 8, 1),
                    Status = DownloadStatus.Ready
                }
            ],
            CancellationToken.None);

        var metadataPath = Assert.Single(Directory.GetFiles(_root, "metadata-*.json"));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, root.GetProperty("videoCount").GetInt32());
        var video = root.GetProperty("videos")[0];
        Assert.Equal("metadata-video", video.GetProperty("videoId").GetString());
        Assert.Equal(48, video.GetProperty("durationSeconds").GetInt32());
        Assert.Equal("Short", video.GetProperty("contentType").GetString());
        Assert.Contains(_logger.Entries, static entry => entry.Message.Contains("Video metadata JSON written", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ArchiveServiceReadsTheLastTokenOfEachLine()
    {
        var archive = new ArchiveService(_logger);
        var archivePath = archive.GetArchivePath(_root, "My:Channel");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await File.WriteAllLinesAsync(archivePath, ["youtube abc123", "youtube\tdef456", "", "  "]);

        var ids = await archive.ReadVideoIdsAsync(_root, "My:Channel", CancellationToken.None);

        Assert.Equal(Path.Combine(_root, "My_Channel", "archive.txt"), archivePath);
        Assert.Equal(2, ids.Count);
        Assert.Contains("abc123", ids);
        Assert.Contains("def456", ids);
    }

    [Fact]
    public async Task DependencyServiceReportsEveryMissingTool()
    {
        var service = new DownloadDependencyService(new ApplicationPaths(_root), _logger);

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        string[] expected = ["yt-dlp", "ffmpeg", "ffprobe"];
        Assert.Equal(expected, result.Missing.Select(static dependency => dependency.Name));
        Assert.Equal(Path.Combine(_root, "Tools", "yt-dlp.exe"), service.YtDlpPath);
    }

    [Fact]
    public async Task YouTubeDataApiResolverRequestsContentDetailsAndParsesDurations()
    {
        Uri? requestUri = null;
        using var client = new HttpClient(new DelegateHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    { "items": [
                        { "id": "video-1", "contentDetails": { "duration": "PT48S" } },
                        { "id": "video-2", "contentDetails": { "duration": "PT1M2S" } }
                    ] }
                    """)
            };
        }));
        var resolver = new YouTubeDataApiDurationResolver("test-key", client);

        var durations = await resolver.ResolveAsync(["video-1", "video-2", "video-1"], CancellationToken.None);

        Assert.Equal(48, durations["video-1"]);
        Assert.Equal(62, durations["video-2"]);
        var requestText = requestUri!.AbsoluteUri;
        Assert.Contains("part=contentDetails", requestText, StringComparison.Ordinal);
        Assert.Contains("id=video-1,video-2&", requestText, StringComparison.Ordinal);
        Assert.Contains("key=test-key", requestText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task YouTubeDataApiResolverReportsApiErrorsWithoutTheKey()
    {
        using var client = new HttpClient(new DelegateHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{ "error": { "message": "quota exceeded" } }""")
            }));
        var resolver = new YouTubeDataApiDurationResolver("secret-key", client);

        var exception = await Assert.ThrowsAsync<YouTubeDataApiException>(
            () => resolver.ResolveAsync(["video-1"], CancellationToken.None));

        Assert.Contains("quota exceeded", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task YouTubeDataApiResolverWithoutAKeyMakesNoRequest()
    {
        using var client = new HttpClient(new DelegateHttpMessageHandler(_ => throw new InvalidOperationException("No request expected.")));
        var resolver = new YouTubeDataApiDurationResolver("  ", client);

        Assert.False(resolver.IsConfigured);
        Assert.Empty(await resolver.ResolveAsync(["video-1"], CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
