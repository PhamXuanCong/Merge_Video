using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoMergeTool.App.Features.DownloadVideo;

/// <summary>
/// The cookie file is chosen once and shared by every downloader tab, unlike the rest of a tab's
/// options (channel, output folder, filters...) which are each tab's own.
/// </summary>
public sealed class SharedCookieFileSettings : ObservableObject
{
    private bool _useCookieFile;
    private string _cookieFilePath = string.Empty;

    public bool UseCookieFile
    {
        get => _useCookieFile;
        set => SetProperty(ref _useCookieFile, value);
    }

    public string CookieFilePath
    {
        get => _cookieFilePath;
        set => SetProperty(ref _cookieFilePath, value ?? string.Empty);
    }
}
