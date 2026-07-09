using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
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
    private readonly NativeMenuItem _showMenuItem = new();
    private readonly NativeMenuItem _hideMenuItem = new();
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

        _closeDialogOpen = false;
        _viewModel.DialogManager.Dispose();

        MacOSDockIconService.ShowDockIcon();
        _activatableLifetime?.TryLeaveBackground();

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
            .WithMinWidth(520)
            .Show();
    }

    private void HandleCloseConfirmation(CloseToTrayDialogViewModel context)
    {
        _closeDialogOpen = false;

        if (context.DoNotAskAgain)
            _viewModel.RememberWindowCloseChoice(context.Choice);

        if (context.Choice == WindowCloseBehavior.ExitApplication)
        {
            ExitApplication();
        }
        else
        {
            HideMainWindowAfterDialogDismissal();
        }
    }

    private void HideMainWindowAfterDialogDismissal()
    {
        DispatcherTimer.RunOnce(HideMainWindow, TimeSpan.FromMilliseconds(120), DispatcherPriority.Background);
    }

    public void HideMainWindow()
    {
        if (_disposed) return;

        _mainWindow.Hide();
        MacOSDockIconService.HideDockIcon();
    }

    private void ConfigureTrayIcon()
    {
        _showMenuItem.Click += (_, _) => ShowMainWindow();
        _hideMenuItem.Click += (_, _) => HideMainWindow();
        _exitMenuItem.Click += (_, _) => ExitApplication();

        var menu = new NativeMenu();
        menu.Items.Add(_showMenuItem);
        menu.Items.Add(_hideMenuItem);
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
        _showMenuItem.Header = LocalizationService.Get("Tray.Show");
        _hideMenuItem.Header = LocalizationService.Get("Tray.Hide");
        _exitMenuItem.Header = LocalizationService.Get("Tray.Exit");
        if (_trayIcon != null)
            _trayIcon.ToolTipText = LocalizationService.Get("App.Name");
    }

    private void ExitApplication()
    {
        if (_exitRequested) return;

        _exitRequested = true;
        MacOSDockIconService.ShowDockIcon();
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
