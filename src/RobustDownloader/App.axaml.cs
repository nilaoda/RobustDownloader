using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
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
    internal static CommandLineCommand InitialCommand { get; set; } = CommandLineCommand.None;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        TextBoxContextMenuService.Install();
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
            SingleInstance?.Start(command => ExecuteCommand(command, viewModel, _desktopIntegration, isInitialCommand: false));
            if (InitialCommand.Kind != CommandLineCommandKind.None)
            {
                var command = InitialCommand;
                Dispatcher.UIThread.Post(() => ExecuteCommand(command, viewModel, _desktopIntegration, isInitialCommand: true));
            }
            desktop.Exit += (_, _) => _desktopIntegration?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ExecuteCommand(
        CommandLineCommand command,
        MainWindowViewModel viewModel,
        DesktopIntegrationService desktopIntegration,
        bool isInitialCommand)
    {
        try
        {
            switch (command.Kind)
            {
                case CommandLineCommandKind.Show:
                    desktopIntegration.ShowMainWindow();
                    return;
                case CommandLineCommandKind.AddTasks:
                    if (command.AddTasks != null)
                        viewModel.AddCommandLineTasks(command.AddTasks);
                    break;
                case CommandLineCommandKind.StartAll:
                    viewModel.StartAll();
                    break;
                case CommandLineCommandKind.StopAll:
                    viewModel.StopAll();
                    break;
                case CommandLineCommandKind.None:
                default:
                    return;
            }
        }
        catch (Exception ex)
        {
            viewModel.StatusText = ex.Message;
        }
        finally
        {
            if (command.ActivateWindow)
            {
                desktopIntegration.ShowMainWindow();
            }
            else if (isInitialCommand && command.Silent)
            {
                DispatcherTimer.RunOnce(
                    desktopIntegration.HideMainWindow,
                    System.TimeSpan.FromMilliseconds(250),
                    DispatcherPriority.Background);
            }
        }
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
