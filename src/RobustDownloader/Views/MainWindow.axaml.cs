using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using RobustDownloader.Models;
using RobustDownloader.Services;
using RobustDownloader.ViewModels;
using ShadUI;

namespace RobustDownloader.Views;

public partial class MainWindow : ShadUI.Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosed(System.EventArgs e)
    {
        base.OnClosed(e);
        this.FindControl<DialogHost>("PART_DialogHost")?.Dispose();
    }

    private async void BtnAdd_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var dialog = new AddTaskWindow(vm.BuildDefaultAddTask());
        var result = await dialog.ShowDialog<AddTaskResult?>(this);
        if (result == null) return;
        vm.AddTasks(result);
        if (vm.VisibleTasks.Count > 0)
            TaskGrid.ScrollIntoView(vm.VisibleTasks.LastOrDefault(), null);
    }

    private void BtnStart_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.StartTasks(GetSelectedTasks());
    }

    private void BtnStop_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.StopTasks(GetSelectedTasks());
    }

    private void BtnDelete_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.DeleteTasks(GetSelectedTasks());
    }

    private void BtnStartAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.StartAll();
    }

    private void BtnStopAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.StopAll();
    }

    private void BtnToggleDetails_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.ToggleDetailPane();
    }

    private async void BtnSettings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var dialog = new SettingsWindow(vm.BuildSettingsSnapshot());
        var result = await dialog.ShowDialog<AppSettings?>(this);
        if (result == null) return;

        vm.ApplySettings(result);
        SetStatus(LocalizationService.Get("Main.SettingsSaved"));
    }

    private List<DownloadTask> GetSelectedTasks()
    {
        return TaskGrid.SelectedItems.Cast<DownloadTask>().ToList();
    }

    private async void CopyUrl_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await CopyTextAsync(GetContextTask()?.Url);
    }

    private async void CopyFileName_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await CopyTextAsync(GetContextTask()?.FileName);
    }

    private async void CopySavePath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await CopyTextAsync(GetContextTask()?.FullSavePath);
    }

    private void OpenFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var task = GetContextTask();
        if (task == null) return;

        try
        {
            OpenContainingFolder(task.FullSavePath, task.SaveDirectory);
            SetStatus(LocalizationService.Get("Main.OpenedFolder"));
        }
        catch (System.Exception ex)
        {
            SetStatus(LocalizationService.Format("Main.OpenFolderFailed", ex.Message));
        }
    }

    private DownloadTask? GetContextTask()
    {
        return TaskGrid.SelectedItem as DownloadTask
            ?? (DataContext as MainWindowViewModel)?.SelectedTask;
    }

    private async System.Threading.Tasks.Task CopyTextAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || Clipboard == null) return;
        await Clipboard.SetTextAsync(text);
        SetStatus(LocalizationService.Get("Main.Copied"));
    }

    private void SetStatus(string text)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.StatusText = text;
    }

    private static void OpenContainingFolder(string filePath, string fallbackDirectory)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Directory.Exists(fallbackDirectory)
            ? fallbackDirectory
            : Path.GetDirectoryName(fullPath) ?? fallbackDirectory;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var target = File.Exists(fullPath) ? fullPath : directory;
            if (Win32Util.TryOpenFolderAndSelectItem(target))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = false,
                ArgumentList = { directory }
            });
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "open",
                UseShellExecute = false
            };

            if (File.Exists(fullPath))
            {
                startInfo.ArgumentList.Add("-R");
                startInfo.ArgumentList.Add(fullPath);
            }
            else
            {
                startInfo.ArgumentList.Add(directory);
            }

            Process.Start(startInfo);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "xdg-open",
            UseShellExecute = false,
            ArgumentList = { directory }
        });
    }
}
