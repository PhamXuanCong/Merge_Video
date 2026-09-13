using VideoMergeTool.Core.Enums;
using VideoMergeTool.Core.Features.MergeVideo.Enums;
using VideoMergeTool.Core.Models;
using VideoMergeTool.Infrastructure;

namespace VideoMergeTool.Infrastructure.Tests;

public sealed class UserSettingsServiceTests : IDisposable
{
    private readonly string _settingsFilePath;

    public UserSettingsServiceTests()
    {
        _settingsFilePath = Path.Combine(
            Path.GetTempPath(),
            $"VideoMergeToolTests_{Guid.NewGuid():N}",
            "settings.json");
    }

    [Fact]
    public void LoadReturnsDefaultsWhenNoFileExists()
    {
        var service = new UserSettingsService(_settingsFilePath);

        var settings = service.Load();

        Assert.Equal(new UserSettings(), settings);
    }

    [Fact]
    public void SaveThenLoadRoundTripsTheChosenValues()
    {
        var service = new UserSettingsService(_settingsFilePath);
        var settings = new UserSettings
        {
            InputFolder = Path.Combine("C:", "videos", "input"),
            MergeLayout = MergeLayout.Vertical,
            X264Preset = X264Preset.Fast,
            CpuThreadLimit = 4,
            PairingMode = PairingMode.Sequential,
            VideoEncoder = VideoEncoder.NvidiaGpu,
            SourceFileAction = SourceFileAction.Keep,
            ExistingOutputAction = ExistingOutputAction.CreateUniqueName,
            ThemePreference = ThemePreference.Dark,
            RenameFolder = Path.Combine("D:", "downloads"),
            RenameHashtags = "#trend #fyp"
        };

        service.Save(settings);
        var loaded = service.Load();

        Assert.Equal(settings, loaded);
    }

    [Fact]
    public void SaveThenLoadRoundTripsRecentFolders()
    {
        var service = new UserSettingsService(_settingsFilePath);
        var settings = new UserSettings
        {
            RecentFolders = [Path.Combine("C:", "videos", "a"), Path.Combine("D:", "videos", "b")]
        };

        service.Save(settings);
        var loaded = service.Load();

        Assert.Equal(settings, loaded);
        Assert.Equal(settings.RecentFolders, loaded.RecentFolders);
    }

    [Fact]
    public void LoadStillReadsTheNumericEnumFormatWrittenByEarlierVersions()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
        File.WriteAllText(
            _settingsFilePath,
            """{"InputFolder":"C:\\videos","MergeLayout":1,"X264Preset":1,"CpuThreadLimit":4}""");
        var service = new UserSettingsService(_settingsFilePath);

        var settings = service.Load();

        Assert.Equal(MergeLayout.Vertical, settings.MergeLayout);
        Assert.Equal(X264Preset.Fast, settings.X264Preset);
        Assert.Equal(4, settings.CpuThreadLimit);
    }

    [Fact]
    public void SaveDoesNotThrowWhenTheSettingsLocationIsUnusable()
    {
        // A file standing where the settings directory should be makes every write fail.
        var blocker = Path.Combine(Path.GetTempPath(), $"VideoMergeToolTests_{Guid.NewGuid():N}");
        File.WriteAllText(blocker, string.Empty);

        try
        {
            var service = new UserSettingsService(Path.Combine(blocker, "settings.json"));

            service.Save(new UserSettings());

            Assert.Equal(new UserSettings(), service.Load());
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public void LoadReturnsDefaultsWhenFileContentIsCorrupt()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
        File.WriteAllText(_settingsFilePath, "{ not valid json");
        var service = new UserSettingsService(_settingsFilePath);

        var settings = service.Load();

        Assert.Equal(new UserSettings(), settings);
    }

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
