using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using RobustDownloader.Models;
using RobustDownloader.Services;

namespace RobustDownloader.Views;

public partial class AddTaskWindow : ShadUI.Window
{
    public AddTaskWindow()
    {
        InitializeComponent();
    }

    public AddTaskWindow(AddTaskResult defaults) : this()
    {
        TxtSaveDir.Text = defaults.SaveDirectory;
        TxtThreads.Text = defaults.ThreadCount.ToString();
        TxtBlock.Text = defaults.BlockSize.ToString("0.##");
        TxtHeaders.Text = defaults.HeaderText;
        ChkCrcOnly.IsChecked = defaults.CrcOnly;
        ChkSkipCrc.IsChecked = defaults.SkipCrc;
        ChkUpdateFileTimestamp.IsChecked = defaults.UpdateFileTimestamp;
    }

    private void TxtUrls_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var lines = GetUrls();
        if (lines.Length == 1)
        {
            TxtFileName.IsEnabled = true;
            TxtFileName.Text = TaskFileNameHelper.GetFileName(lines[0]);
        }
        else if (lines.Length > 1)
        {
            TxtFileName.Text = LocalizationService.Get("Add.BatchFileName");
            TxtFileName.IsEnabled = false;
        }
        else
        {
            TxtFileName.Text = "";
            TxtFileName.IsEnabled = true;
        }
    }

    private async void BtnBrowse_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.Get("Dialog.SelectSaveDirectory"),
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(path))
            TxtSaveDir.Text = path;
    }

    private void BtnAdd_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        TxtValidation.Text = "";
        var urls = GetUrls();
        if (urls.Length == 0)
        {
            TxtValidation.Text = LocalizationService.Get("Validation.EnterUrl");
            return;
        }

        var saveDir = TxtSaveDir.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(saveDir))
        {
            TxtValidation.Text = LocalizationService.Get("Validation.SelectDirectory");
            return;
        }

        if (!Directory.Exists(saveDir))
        {
            TxtValidation.Text = LocalizationService.Get("Validation.DirectoryMissing");
            return;
        }

        if (!int.TryParse(TxtThreads.Text, out var threads) || threads < 1)
        {
            TxtValidation.Text = LocalizationService.Get("Validation.Threads");
            return;
        }

        if (!double.TryParse(TxtBlock.Text, out var block) || block <= 0)
        {
            TxtValidation.Text = LocalizationService.Get("Validation.BlockSize");
            return;
        }

        if (ChkCrcOnly.IsChecked == true && ChkSkipCrc.IsChecked == true)
        {
            TxtValidation.Text = LocalizationService.Get("Validation.CrcConflict");
            return;
        }

        Close(new AddTaskResult
        {
            Urls = urls,
            SaveDirectory = saveDir,
            SingleFileName = TxtFileName.IsEnabled ? TxtFileName.Text?.Trim() ?? "" : "",
            ThreadCount = threads,
            BlockSize = block,
            CrcOnly = ChkCrcOnly.IsChecked == true,
            SkipCrc = ChkSkipCrc.IsChecked == true,
            UpdateFileTimestamp = ChkUpdateFileTimestamp.IsChecked == true,
            HeaderText = TxtHeaders.Text ?? ""
        });
    }

    private void BtnCancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    private string[] GetUrls()
    {
        return (TxtUrls.Text ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(u => u.Trim())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .ToArray();
    }
}
