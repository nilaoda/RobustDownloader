using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using RobustDownloader.Models;
using RobustDownloader.Services;

namespace RobustDownloader.Views;

public partial class SettingsWindow : ShadUI.Window
{
    private static readonly int[] ConcurrencyOptions = [1, 2, 3, 5, 8];
    private const double MinimumSpeedLimitMbps = 0.1;
    private const double MaximumSpeedLimitMbps = 100;
    private bool _updatingSpeedLimit;

    public SettingsWindow()
    {
        InitializeComponent();
        CmbConcurrency.ItemsSource = ConcurrencyOptions;
        ChkSpeedLimit.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
                UpdateSpeedLimitControlsState();
        };
        SliderSpeedLimit.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
                SyncSpeedLimitTextFromSlider();
        };
    }

    public SettingsWindow(AppSettings settings) : this()
    {
        DataContext = settings;
        SelectComboBoxItem(CmbLanguage, settings.LanguageMode.ToString());
        SelectComboBoxItem(CmbTheme, settings.ThemeMode.ToString());
        SelectComboBoxItem(CmbProxyMode, settings.ProxyMode.ToString());
        SelectComboBoxItem(CmbCloseBehavior, settings.WindowCloseBehavior.ToString());
        SelectComboBoxItem(CmbSaveDirectoryMode, settings.SaveDirectoryMode.ToString());
        SelectComboBoxItem(CmbBackgroundStretch, settings.BackgroundStretch);
        ChkSpeedLimit.IsChecked = settings.IsSpeedLimitEnabled;
        SliderSpeedLimit.Value = CoerceSpeedLimitMbps(settings.SpeedLimitMbps);
        SyncSpeedLimitTextFromSlider();
        UpdateSpeedLimitControlsState();
        UpdateProxyAddressState();
        UpdateCloseBehaviorState();
        UpdateSaveDirectoryState();
    }

    private async void BrowseDirectory_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = L.Dialog_SelectDefaultSaveDirectory,
                AllowMultiple = false
            });

            var path = FolderPathHelper.GetLocalPath(folders.FirstOrDefault());
            if (!string.IsNullOrWhiteSpace(path))
                TxtFixedDownloadDirectory.Text = path;
        }
        catch (Exception ex)
        {
            TxtValidation.Text = L.Validation_SelectDirectoryFailed(ex.Message);
        }
    }

    private async void BrowseBackgroundImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = L.Dialog_SelectBackgroundImage,
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Images")
                    {
                        Patterns = ["*.jpg", "*.jpeg", "*.png", "*.bmp", "*.webp"]
                    }
                ]
            });

            var file = files.FirstOrDefault();
            var uri = file?.Path;
            if (uri is { IsAbsoluteUri: true, IsFile: true })
                TxtBackgroundImagePath.Text = uri.LocalPath;
        }
        catch (Exception ex)
        {
            TxtValidation.Text = L.Validation_SelectDirectoryFailed(ex.Message);
        }
    }

    private void ClearBackgroundImage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        TxtBackgroundImagePath.Text = "";
    }

    private void AddCredential_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not AppSettings settings) return;

        var item = new SiteCredential { Pattern = "example.com" };
        settings.SiteCredentials.Add(item);
        CredentialGrid.SelectedItem = item;
    }

    private void DeleteCredential_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not AppSettings settings || CredentialGrid.SelectedItem is not SiteCredential selected)
            return;

        settings.SiteCredentials.Remove(selected);
    }

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        TxtValidation.Text = "";
        if (DataContext is not AppSettings settings)
        {
            Close(null);
            return;
        }

        settings.LanguageMode = ReadComboBoxTag(CmbLanguage, AppLanguageMode.Auto);
        settings.ThemeMode = ReadComboBoxTag(CmbTheme, AppThemeMode.System);
        settings.ProxyMode = ReadComboBoxTag(CmbProxyMode, AppProxyMode.System);
        settings.WindowCloseBehavior = ReadComboBoxTag(CmbCloseBehavior, WindowCloseBehavior.MinimizeToTray);
        settings.SaveDirectoryMode = ReadComboBoxTag(CmbSaveDirectoryMode, SaveDirectoryMode.LastUsed);
        settings.FixedDownloadDirectory = FolderPathHelper.Normalize(settings.FixedDownloadDirectory);
        settings.BackgroundStretch = ReadComboBoxStringTag(CmbBackgroundStretch, "UniformToFill");
        settings.IsSpeedLimitEnabled = ChkSpeedLimit.IsChecked == true;
        settings.SpeedLimitMbps = CoerceSpeedLimitMbps(ReadSpeedLimitText());

        if (!Validate(settings, out var message))
        {
            TxtValidation.Text = message;
            return;
        }

        Close(settings);
    }

    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    private void SpeedLimitText_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SliderSpeedLimit.Value = CoerceSpeedLimitMbps(ReadSpeedLimitText());
        SyncSpeedLimitTextFromSlider();
    }

    private static bool Validate(AppSettings settings, out string message)
    {
        if (settings.DefaultThreadCount < 1)
        {
            message = L.Validation_DefaultThreads;
            return false;
        }

        if (settings.DefaultBlockSizeMb <= 0)
        {
            message = L.Validation_DefaultBlock;
            return false;
        }

        if (!ConcurrencyOptions.Contains(settings.MaxConcurrency))
        {
            message = L.Validation_DefaultConcurrency;
            return false;
        }

        if (double.IsNaN(settings.SpeedLimitMbps) || double.IsInfinity(settings.SpeedLimitMbps) || settings.SpeedLimitMbps <= 0)
        {
            message = L.Validation_SpeedLimit;
            return false;
        }

        if (settings.SaveDirectoryMode == SaveDirectoryMode.Fixed &&
            (string.IsNullOrWhiteSpace(settings.FixedDownloadDirectory) || !FolderPathHelper.DirectoryExists(settings.FixedDownloadDirectory)))
        {
            message = L.Validation_DefaultDirectoryMissing;
            return false;
        }

        if (settings.ProxyMode == AppProxyMode.Manual && !IsValidProxyAddress(settings.ProxyAddress))
        {
            message = L.Validation_ProxyAddress;
            return false;
        }

        foreach (var credential in settings.SiteCredentials)
        {
            if (string.IsNullOrWhiteSpace(credential.Pattern) &&
                (!string.IsNullOrWhiteSpace(credential.Username) || !string.IsNullOrWhiteSpace(credential.Password)))
            {
                message = L.Validation_CredentialPattern;
                return false;
            }
        }

        message = "";
        return true;
    }

    private void ProxyMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateProxyAddressState();
    }

    private void SaveDirectoryMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateSaveDirectoryState();
    }

    private void CloseBehavior_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateCloseBehaviorState();
    }

    private void UpdateProxyAddressState()
    {
        TxtProxyAddress.IsEnabled = ReadComboBoxTag(CmbProxyMode, AppProxyMode.System) == AppProxyMode.Manual;
    }

    private void UpdateSaveDirectoryState()
    {
        var isFixed = ReadComboBoxTag(CmbSaveDirectoryMode, SaveDirectoryMode.LastUsed) == SaveDirectoryMode.Fixed;
        TxtFixedDownloadDirectory.IsEnabled = isFixed;
        BtnBrowseFixedDirectory.IsEnabled = isFixed;
    }

    private void UpdateCloseBehaviorState()
    {
        ChkConfirmCloseToTray.IsEnabled =
            ReadComboBoxTag(CmbCloseBehavior, WindowCloseBehavior.MinimizeToTray) == WindowCloseBehavior.MinimizeToTray;
    }

    private static bool IsValidProxyAddress(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri))
            return false;

        return uri.Scheme is "http" or "https" or "socks4" or "socks4a" or "socks5" &&
               !string.IsNullOrWhiteSpace(uri.Host);
    }

    private void UpdateSpeedLimitControlsState()
    {
        var isEnabled = ChkSpeedLimit.IsChecked == true;
        SliderSpeedLimit.IsEnabled = isEnabled;
        TxtSpeedLimit.IsEnabled = isEnabled;
    }

    private void SyncSpeedLimitTextFromSlider()
    {
        if (_updatingSpeedLimit) return;

        _updatingSpeedLimit = true;
        try
        {
            TxtSpeedLimit.Text = CoerceSpeedLimitMbps(SliderSpeedLimit.Value)
                .ToString("0.##", CultureInfo.CurrentCulture);
        }
        finally
        {
            _updatingSpeedLimit = false;
        }
    }

    private double ReadSpeedLimitText()
    {
        if (_updatingSpeedLimit) return SliderSpeedLimit.Value;

        var text = (TxtSpeedLimit.Text ?? "").Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var current) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out current))
            return current;

        return SliderSpeedLimit.Value;
    }

    private static double CoerceSpeedLimitMbps(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 10;
        return Math.Clamp(value, MinimumSpeedLimitMbps, MaximumSpeedLimitMbps);
    }

    private static void SelectComboBoxItem(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem &&
                string.Equals(comboBoxItem.Tag?.ToString(), value, StringComparison.Ordinal))
            {
                comboBox.SelectedItem = comboBoxItem;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static TEnum ReadComboBoxTag<TEnum>(ComboBox comboBox, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (comboBox.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<TEnum>(item.Tag?.ToString(), out var value))
            return value;

        return fallback;
    }

    private static string ReadComboBoxStringTag(ComboBox comboBox, string fallback)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? fallback;

        return fallback;
    }
}
