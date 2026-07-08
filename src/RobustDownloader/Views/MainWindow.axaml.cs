using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using RobustDownloader.Models;
using RobustDownloader.Services;
using RobustDownloader.ViewModels;
using ShadUI;

namespace RobustDownloader.Views;

public partial class MainWindow : ShadUI.Window
{
    private bool _isTaskTreePaneResizing;
    private double _taskTreePaneResizeStartX;
    private double _taskTreePaneResizeStartWidth;

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
        ScrollTaskGridToBottom();
    }

    private void ScrollTaskGridToBottom()
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var lastTask = vm.VisibleTasks.LastOrDefault();
        if (lastTask == null) return;

        TaskGrid.ScrollIntoView(lastTask, null);
        Dispatcher.UIThread.Post(() => TaskGrid.ScrollIntoView(lastTask, null), DispatcherPriority.Loaded);
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

    private void BtnToggleTaskTree_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.ToggleTaskTreePane();
    }

    private void TaskTreeNode_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm &&
            sender is Button { Tag: TaskTreeNode node })
        {
            vm.SelectTaskTreeNode(node);
        }
    }

    private void TaskTreeDisclosure_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm &&
            sender is Control { Tag: TaskTreeNode node })
        {
            vm.ToggleTaskTreeNodeExpansion(node);
            e.Handled = true;
        }
    }

    private void TaskTreeResizeHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || sender is not Control control) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;

        _isTaskTreePaneResizing = true;
        _taskTreePaneResizeStartX = e.GetPosition(this).X;
        _taskTreePaneResizeStartWidth = vm.TaskTreePaneWidth;
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void TaskTreeResizeHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isTaskTreePaneResizing || DataContext is not MainWindowViewModel vm) return;

        var delta = e.GetPosition(this).X - _taskTreePaneResizeStartX;
        vm.TaskTreePaneWidth = _taskTreePaneResizeStartWidth + delta;
        e.Handled = true;
    }

    private void TaskTreeResizeHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isTaskTreePaneResizing) return;

        _isTaskTreePaneResizing = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void TaskTreeResizeHandle_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isTaskTreePaneResizing = false;
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
        await CopyTextAsync(string.Join("\n", GetSelectedTasks().Select(t => t.Url)));
    }

    private async void CopyFileName_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await CopyTextAsync(string.Join("\n", GetSelectedTasks().Select(t => t.FileName)));
    }

    private async void CopySavePath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await CopyTextAsync(string.Join("\n", GetSelectedTasks().Select(t => t.FullSavePath)));
    }

    private void OpenFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var task = GetSelectedTasks().FirstOrDefault();
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

    private void ReDownload_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var tasks = GetSelectedTasks();
        if (tasks.Count == 0) return;

        try
        {
            (DataContext as MainWindowViewModel)?.ReDownloadTasks(tasks);
            SetStatus(LocalizationService.Get("Main.ReDownloadQueued"));
        }
        catch (System.Exception ex)
        {
            SetStatus(LocalizationService.Format("Main.ReDownloadFailed", ex.Message));
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
