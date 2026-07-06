using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System.IO;
using System.Text.Json;
using RobustDownloader.Models;
using RobustDownloader.Services;
using RobustDownloader.ViewModels;
using RobustDownloader.Views;

namespace RobustDownloader;

public partial class App : Application
{
    private DesktopIntegrationService? _desktopIntegration;

    internal static SingleInstanceService? SingleInstance { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var settings = LoadStartupSettings();
        LocalizationService.Apply(settings.LanguageMode);
        AppThemeService.Apply(settings.ThemeMode);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
            viewModel.DialogManager.Register<CloseToTrayDialog, CloseToTrayDialogViewModel>();
            viewModel.Initialize();

            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
            desktop.MainWindow = mainWindow;

            _desktopIntegration = new DesktopIntegrationService(desktop, mainWindow, viewModel);
            SingleInstance?.Start(_desktopIntegration.ShowMainWindow);
            desktop.Exit += (_, _) => _desktopIntegration?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static AppSettings LoadStartupSettings()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return new AppSettings();

            using var stream = File.OpenRead(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize(stream, AppJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }
}
