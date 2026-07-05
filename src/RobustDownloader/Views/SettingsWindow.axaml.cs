using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using RobustDownloader.Models;
using RobustDownloader.Services;

namespace RobustDownloader.Views;

public partial class SettingsWindow : ShadUI.Window
{
    private static readonly int[] ConcurrencyOptions = [1, 2, 3, 5, 8];

    public SettingsWindow()
    {
        InitializeComponent();
        CmbConcurrency.ItemsSource = ConcurrencyOptions;
    }

    public SettingsWindow(AppSettings settings) : this()
    {
        DataContext = settings;
        SelectComboBoxItem(CmbLanguage, settings.LanguageMode.ToString());
        SelectComboBoxItem(CmbTheme, settings.ThemeMode.ToString());
        SelectComboBoxItem(CmbProxyMode, settings.ProxyMode.ToString());
        UpdateProxyAddressState();
    }

    private async void BrowseDirectory_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.Get("Dialog.SelectDefaultSaveDirectory"),
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(path))
            TxtLastDownloadDirectory.Text = path;
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

    private static bool Validate(AppSettings settings, out string message)
    {
        if (settings.DefaultThreadCount < 1)
        {
            message = LocalizationService.Get("Validation.DefaultThreads");
            return false;
        }

        if (settings.DefaultBlockSizeMb <= 0)
        {
            message = LocalizationService.Get("Validation.DefaultBlock");
            return false;
        }

        if (!ConcurrencyOptions.Contains(settings.MaxConcurrency))
        {
            message = LocalizationService.Get("Validation.DefaultConcurrency");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(settings.LastDownloadDirectory) &&
            !Directory.Exists(settings.LastDownloadDirectory))
        {
            message = LocalizationService.Get("Validation.DefaultDirectoryMissing");
            return false;
        }

        if (settings.ProxyMode == AppProxyMode.Manual && !IsValidProxyAddress(settings.ProxyAddress))
        {
            message = LocalizationService.Get("Validation.ProxyAddress");
            return false;
        }

        foreach (var credential in settings.SiteCredentials)
        {
            if (string.IsNullOrWhiteSpace(credential.Pattern) &&
                (!string.IsNullOrWhiteSpace(credential.Username) || !string.IsNullOrWhiteSpace(credential.Password)))
            {
                message = LocalizationService.Get("Validation.CredentialPattern");
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

    private void UpdateProxyAddressState()
    {
        TxtProxyAddress.IsEnabled = ReadComboBoxTag(CmbProxyMode, AppProxyMode.System) == AppProxyMode.Manual;
    }

    private static bool IsValidProxyAddress(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri))
            return false;

        return uri.Scheme is "http" or "https" or "socks4" or "socks4a" or "socks5" &&
               !string.IsNullOrWhiteSpace(uri.Host);
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
}
