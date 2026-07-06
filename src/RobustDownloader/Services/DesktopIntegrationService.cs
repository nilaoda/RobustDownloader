using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using RobustDownloader.Models;
using RobustDownloader.ViewModels;
using RobustDownloader.Views;
using ShadUI;

namespace RobustDownloader.Services;

public sealed class DesktopIntegrationService : IDisposable
{
    private static readonly Uri TrayIconUri = new("avares://RobustDownloader/Assets/app-icon.png");

    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly MainWindow _mainWindow;
    private readonly MainWindowViewModel _viewModel;
    private readonly NativeMenuItem _exitMenuItem = new();
    private readonly IActivatableLifetime? _activatableLifetime;
    private TrayIcon? _trayIcon;
    private bool _exitRequested;
    private bool _closeDialogOpen;
    private bool _disposed;

    public DesktopIntegrationService(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow mainWindow,
        MainWindowViewModel viewModel)
    {
        _desktop = desktop;
        _mainWindow = mainWindow;
        _viewModel = viewModel;

        _desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _desktop.Exit += (_, _) => _viewModel.Shutdown();
        _mainWindow.Closing += MainWindow_Closing;
        LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
        _activatableLifetime = Application.Current?.ApplicationLifetime as IActivatableLifetime;
        if (_activatableLifetime != null)
            _activatableLifetime.Activated += ActivatableLifetime_Activated;

        ConfigureTrayIcon();
    }

    public void ShowMainWindow()
    {
        if (_disposed) return;

        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_exitRequested) return;

        e.Cancel = true;

        if (_viewModel.WindowCloseBehavior == WindowCloseBehavior.ExitApplication)
        {
            ExitApplication();
            return;
        }

        if (!_viewModel.ConfirmCloseToTray)
        {
            HideMainWindow();
            return;
        }

        ShowCloseConfirmation();
    }

    private void ShowCloseConfirmation()
    {
        if (_closeDialogOpen) return;

        _closeDialogOpen = true;
        var context = new CloseToTrayDialogViewModel(_viewModel.DialogManager);
        _viewModel.DialogManager
            .CreateDialog(context)
            .WithSuccessCallback(HandleCloseConfirmation)
            .WithCancelCallback(() => _closeDialogOpen = false)
            .WithMinWidth(420)
            .Show();
    }

    private void HandleCloseConfirmation(CloseToTrayDialogViewModel context)
    {
        _closeDialogOpen = false;

        if (context.DoNotAskAgain)
            _viewModel.RememberWindowCloseChoice(context.Choice);

        if (context.Choice == WindowCloseBehavior.ExitApplication)
            ExitApplication();
        else
            HideMainWindow();
    }

    private void HideMainWindow()
    {
        _mainWindow.Hide();
    }

    private void ConfigureTrayIcon()
    {
        _exitMenuItem.Click += (_, _) => ExitApplication();

        var menu = new NativeMenu();
        menu.Items.Add(_exitMenuItem);

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(TrayIconUri)),
            Menu = menu,
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();
        UpdateLocalizedText();
    }

    private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
    {
        UpdateLocalizedText();
    }

    private void ActivatableLifetime_Activated(object? sender, ActivatedEventArgs e)
    {
        if (!_mainWindow.IsVisible)
            ShowMainWindow();
    }

    private void UpdateLocalizedText()
    {
        _exitMenuItem.Header = LocalizationService.Get("Tray.Exit");
        if (_trayIcon != null)
            _trayIcon.ToolTipText = LocalizationService.Get("App.Name");
    }

    private void ExitApplication()
    {
        if (_exitRequested) return;

        _exitRequested = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _viewModel.Shutdown();
        _desktop.Shutdown(0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        if (_activatableLifetime != null)
            _activatableLifetime.Activated -= ActivatableLifetime_Activated;
        _mainWindow.Closing -= MainWindow_Closing;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
