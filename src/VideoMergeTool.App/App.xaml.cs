using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VideoMergeTool.App.Services;
using VideoMergeTool.App.Theming;
using VideoMergeTool.App.ViewModels;
using VideoMergeTool.Core.Interfaces;
using VideoMergeTool.Core.Models;
using VideoMergeTool.Infrastructure;

namespace VideoMergeTool.App;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
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
                services.AddSingleton<MainViewModel>();
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
        var viewModel = _host.Services.GetRequiredService<MainViewModel>();
        var window = new MainWindow(
            _host.Services.GetRequiredService<ThemeManager>(),
            _host.Services.GetRequiredService<IUserPrompt>())
        {
            DataContext = viewModel
        };

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
