using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VideoMergeTool.App.Features.DownloadVideo;
using VideoMergeTool.App.Features.DownloadVideo.Services;
using VideoMergeTool.App.Features.MergeVideo;
using VideoMergeTool.App.Features.MergeVideo.Services;
using VideoMergeTool.App.Features.RenameVideo;
using VideoMergeTool.App.Features.RenameVideo.Services;
using VideoMergeTool.App.Shell;
using VideoMergeTool.App.Theming;
using VideoMergeTool.Core.Features.DownloadVideo.Interfaces;
using VideoMergeTool.Core.Features.MergeVideo.Interfaces;
using VideoMergeTool.Core.Features.RenameVideo.Interfaces;
using VideoMergeTool.Core.Interfaces;
using VideoMergeTool.Core.Models;
using VideoMergeTool.Infrastructure;
using VideoMergeTool.Infrastructure.Features.DownloadVideo;
using VideoMergeTool.Infrastructure.Features.MergeVideo;
using VideoMergeTool.Infrastructure.Features.RenameVideo;

namespace VideoMergeTool.App;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            // appsettings.json sits next to the exe, wherever the app was launched from.
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton(new ApplicationPaths(AppContext.BaseDirectory));
                services.AddSingleton<ApplicationPathValidator>();
                services.AddSingleton<IInputVideoScanner, InputVideoScanner>();
                services.AddSingleton<ICompanionVideoProvider, CompanionVideoProvider>();
                services.AddSingleton<IOutputPathService, OutputPathService>();
                services.AddSingleton<IFFprobeService, FFprobeService>();
                services.AddSingleton<FFmpegCommandBuilder>();
                services.AddSingleton<IFFmpegService, FFmpegService>();
                services.AddSingleton<IOutputValidator, OutputValidationService>();
                services.AddSingleton<IVideoProcessingCoordinator, VideoProcessingService>();
                services.AddSingleton<IUserSettingsService, UserSettingsService>();
                services.AddSingleton<IUserPrompt, MessageBoxUserPrompt>();
                services.AddSingleton<ThemeManager>();
                services.AddSingleton<MergeVideoViewModel>();
                services.AddSingleton<IRenameFolderScanner, RenameFolderScanner>();
                services.AddSingleton<IVideoRenamer, VideoRenamer>();
                services.AddSingleton<IRenamePrompt, MessageBoxRenamePrompt>();
                services.AddSingleton<RenameVideoViewModel>();
                services.AddSingleton<HttpClient>();
                services.AddSingleton<IDownloadLogger, FileDownloadLogger>();
                services.AddSingleton<IDownloadDependencyService, DownloadDependencyService>();
                services.AddSingleton<IArchiveService, ArchiveService>();
                services.AddSingleton<IVideoDurationCache, FileVideoDurationCache>();
                services.AddSingleton<IVideoMetadataLogger, FileVideoMetadataLogger>();
                services.AddSingleton<IYouTubeDataApiDurationResolver>(provider => new YouTubeDataApiDurationResolver(
                    provider.GetRequiredService<HttpClient>(),
                    context.Configuration["YouTubeDataApi:ApiKey"]));
                services.AddSingleton<IChannelAnalyzer, YtDlpChannelAnalyzer>();
                services.AddSingleton<IVideoDownloadService, YtDlpDownloadService>();
                services.AddSingleton<IDownloadDialogService, DownloadDialogService>();
                services.AddSingleton<DownloadVideoViewModel>();
                services.AddSingleton<ShellViewModel>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host.Services.GetRequiredService<ApplicationPathValidator>().Validate();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Video Merge Tool",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        await _host.StartAsync();

        // Resolving the view model applies the saved theme, so do it before the window exists.
        var viewModel = _host.Services.GetRequiredService<ShellViewModel>();
        var window = new ShellWindow(
            _host.Services.GetRequiredService<ThemeManager>(),
            viewModel);

        MainWindow = window;
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
