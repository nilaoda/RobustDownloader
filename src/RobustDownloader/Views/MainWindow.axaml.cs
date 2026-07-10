using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is INotifyPropertyChanged npc)
            npc.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.BackgroundStretch) && DataContext is MainWindowViewModel vm)
        {
            if (Enum.TryParse<Avalonia.Media.Stretch>(vm.BackgroundStretch, out var s))
            {
                var border = this.FindControl<Border>("BackgroundBorder");
                if (border?.Background is Avalonia.Media.ImageBrush brush)
                    brush.Stretch = s;
            }
        }
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
        ScrollTaskGridToBottomAsync();
    }

    private async void ScrollTaskGridToBottomAsync()
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var lastTask = vm.VisibleTasks.LastOrDefault();
        if (lastTask == null) return;

        await System.Threading.Tasks.Task.Delay(50);
        TaskGrid.ScrollIntoView(lastTask, null);

        var sv = FindScrollViewer(TaskGrid);
        if (sv != null)
            sv.Offset = new Vector(sv.Offset.X, sv.Extent.Height);
    }

    private static ScrollViewer? FindScrollViewer(Visual visual)
    {
        foreach (var child in visual.GetVisualChildren())
        {
            if (child is ScrollViewer sv) return sv;
            var found = FindScrollViewer(child);
            if (found != null) return found;
        }
        return null;
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

    private void TaskGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.UpdateIsAnyTaskSelected(TaskGrid.SelectedItems.Count > 0);
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
        ShowToast(LocalizationService.Get("Main.SettingsSaved"), ToastKind.Success);
    }

    private void SpeedLimit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.ShowSpeedLimitDialog();
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
            ShowToast(LocalizationService.Get("Main.OpenedFolder"), ToastKind.Success);
        }
        catch (System.Exception ex)
        {
            ShowToast(LocalizationService.Format("Main.OpenFolderFailed", ex.Message), ToastKind.Error);
        }
    }

    private void ReDownload_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var tasks = GetSelectedTasks();
        if (tasks.Count == 0) return;

        try
        {
            (DataContext as MainWindowViewModel)?.ReDownloadTasks(tasks);
            ShowToast(LocalizationService.Get("Main.ReDownloadQueued"), ToastKind.Success);
        }
        catch (System.Exception ex)
        {
            ShowToast(LocalizationService.Format("Main.ReDownloadFailed", ex.Message), ToastKind.Error);
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
        try
        {
            await Clipboard.SetTextAsync(text);
            ShowToast(LocalizationService.Get("Main.Copied"), ToastKind.Success);
        }
        catch (System.Exception ex)
        {
            ShowToast(LocalizationService.Format("Main.CopyFailed", ex.Message), ToastKind.Error);
        }
    }

    private void SetStatus(string text)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.StatusText = text;
    }

    private void ShowToast(string message, ToastKind kind, double delay = 3)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.ShowToast(message, kind, delay);
    }

    private static void OpenContainingFolder(string filePath, string fallbackDirectory)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Directory.Exists(fallbackDirectory)
            ? fallbackDirectory
            : Path.GetDirectoryName(fullPath) ?? fallbackDirectory;

        if (OperatingSystem.IsWindows())
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

        if (OperatingSystem.IsMacOS())
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
