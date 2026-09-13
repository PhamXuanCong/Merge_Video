using VideoMergeTool.Core.Models;
using VideoMergeTool.Infrastructure;

namespace VideoMergeTool.Infrastructure.Tests;

public sealed class ApplicationPathValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"VideoMergeTool.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void ApplicationPathsCreatesExpectedPathsFromSuppliedRoot()
    {
        var paths = new ApplicationPaths(_root);

        Assert.Equal(Path.GetFullPath(_root), paths.ApplicationDirectory);
        Assert.Equal(Path.Combine(_root, "Tools", "ffmpeg.exe"), paths.FFmpegPath);
        Assert.Equal(Path.Combine(_root, "Tools", "ffprobe.exe"), paths.FFprobePath);
        Assert.Equal(Path.Combine(_root, "Assets", "CompanionVideos"), paths.CompanionVideoDirectory);
        Assert.Equal(Path.Combine(_root, "Licenses"), paths.LicenseDirectory);
        Assert.Equal(Path.Combine(_root, "README.txt"), paths.ReadmePath);
    }

    [Fact]
    public void ValidateReportsMissingFfmpeg()
    {
        var paths = CreateValidPaths();
        File.Delete(paths.FFmpegPath);

        var exception = Assert.Throws<InvalidOperationException>(() => new ApplicationPathValidator(paths).Validate());

        Assert.Contains(paths.FFmpegPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateReportsMissingFfprobe()
    {
        var paths = CreateValidPaths();
        File.Delete(paths.FFprobePath);

        var exception = Assert.Throws<InvalidOperationException>(() => new ApplicationPathValidator(paths).Validate());

        Assert.Contains(paths.FFprobePath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateReportsMissingCompanionDirectory()
    {
        var paths = CreateValidPaths();
        Directory.Delete(paths.CompanionVideoDirectory, recursive: true);

        var exception = Assert.Throws<InvalidOperationException>(() => new ApplicationPathValidator(paths).Validate());

        Assert.Contains(paths.CompanionVideoDirectory, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateReportsEmptyCompanionDirectory()
    {
        var paths = CreateValidPaths();

        var exception = Assert.Throws<InvalidOperationException>(() => new ApplicationPathValidator(paths).Validate());

        Assert.Contains(paths.CompanionVideoDirectory, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateAcceptsUppercaseMp4Extension()
    {
        var paths = CreateValidPaths();
        File.WriteAllText(Path.Combine(paths.CompanionVideoDirectory, "companion.MP4"), string.Empty);

        new ApplicationPathValidator(paths).Validate();
    }

    [Fact]
    public void ApplicationPathsDoesNotDependOnCurrentDirectory()
    {
        var originalCurrentDirectory = Environment.CurrentDirectory;
        var otherDirectory = Path.Combine(Path.GetTempPath(), $"VideoMergeTool.Tests.Current.{Guid.NewGuid():N}");
        Directory.CreateDirectory(otherDirectory);

        try
        {
            Environment.CurrentDirectory = otherDirectory;

            var paths = new ApplicationPaths(_root);

            Assert.Equal(Path.GetFullPath(_root), paths.ApplicationDirectory);
            Assert.DoesNotContain(otherDirectory, paths.FFmpegPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            Directory.Delete(otherDirectory, recursive: true);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ApplicationPaths CreateValidPaths()
    {
        var paths = new ApplicationPaths(_root);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.FFmpegPath)!);
        Directory.CreateDirectory(paths.CompanionVideoDirectory);
        File.WriteAllText(paths.FFmpegPath, string.Empty);
        File.WriteAllText(paths.FFprobePath, string.Empty);
        return paths;
    }
}
