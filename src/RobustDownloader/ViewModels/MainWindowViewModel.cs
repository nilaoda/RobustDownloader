using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using RobustDownloader.Models;
using RobustDownloader.Services;

namespace RobustDownloader.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningTasks = new();
    private readonly CancellationTokenSource _globalCts = new();
    private readonly string _dataDirectory;
    private readonly string _dataFile;
    private readonly string _settingsFile;
    private readonly ConcurrentDictionary<string, long> _taskSpeeds = new();
    private AppSettings _settings = new();
    private int _taskListLimit = 100;
    private bool _queueStarted;
    private bool _suppressTaskCollectionSideEffects;
    private bool _suppressSettingsSideEffects;

    [ObservableProperty] private string _defaultThreads = "4";
    [ObservableProperty] private string _defaultBlockSize = "16";
    [ObservableProperty] private int _maxConcurrency = 3;
    [ObservableProperty] private string _statusText = LocalizationService.Get("Queue.Ready");
    [ObservableProperty] private string _globalSpeedText = "0 B/s";
    [ObservableProperty] private string _tabInfo = "";
    [ObservableProperty] private DownloadTask? _selectedTask;
    [ObservableProperty] private bool _isDetailPaneOpen;
    [ObservableProperty] private TaskListScopeOption? _selectedTaskListScope;

    public ObservableCollection<DownloadTask> Tasks { get; } = [];
    public ObservableCollection<DownloadTask> VisibleTasks { get; } = [];
    public ObservableCollection<TaskListScopeOption> TaskListScopes { get; } = [];
    public int[] ConcurrencyOptions { get; } = [1, 2, 3, 5, 8];

    public MainWindowViewModel()
    {
        _dataDirectory = AppPaths.DataDirectory;
        _dataFile = AppPaths.TasksFile;
        _settingsFile = AppPaths.SettingsFile;

        Tasks.CollectionChanged += (_, _) =>
        {
            if (_suppressTaskCollectionSideEffects) return;

            RefreshVisibleTasks();
            SaveTasks();
        };
        LocalizationService.LanguageChanged += (_, _) => RefreshLocalizedText();
        RefreshTaskListScopeOptions(_taskListLimit);
        RefreshVisibleTasks();
    }

    public string DataFile => _dataFile;
    public string SettingsFile => _settingsFile;
    public bool HasVisibleTasks => VisibleTasks.Count > 0;
    public bool HasNoVisibleTasks => !HasVisibleTasks;
    public bool HasSelectedTask => SelectedTask != null;
    public bool HasNoSelectedTask => SelectedTask == null;
    public string WindowTitle => $"{LocalizationService.Get("App.Name")} v{AppVersion}";
    public string GlobalSpeedDisplay => $"{LocalizationService.Get("Main.GlobalSpeed")} {GlobalSpeedText}";
    public string DetailPaneButtonText => LocalizationService.Get(IsDetailPaneOpen ? "Main.HideDetails" : "Main.ShowDetails");

    partial void OnSelectedTaskChanged(DownloadTask? value)
    {
        OnPropertyChanged(nameof(HasSelectedTask));
        OnPropertyChanged(nameof(HasNoSelectedTask));
    }

    partial void OnGlobalSpeedTextChanged(string value)
    {
        OnPropertyChanged(nameof(GlobalSpeedDisplay));
    }

    partial void OnIsDetailPaneOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(DetailPaneButtonText));
    }

    partial void OnSelectedTaskListScopeChanged(TaskListScopeOption? value)
    {
        if (value == null || _taskListLimit == value.Limit) return;

        _taskListLimit = value.Limit;
        _settings.TaskListLimit = value.Limit;
        RefreshVisibleTasks();

        if (!_suppressSettingsSideEffects)
            SaveSettings();
    }

    partial void OnMaxConcurrencyChanged(int value)
    {
        var coerced = CoerceConcurrency(value);
        if (value != coerced) MaxConcurrency = coerced;
    }

    public void Initialize()
    {
        LoadSettings();
        LoadTasks();
        if (!_queueStarted)
        {
            _queueStarted = true;
            _ = Task.Run(QueueLoop);
        }
    }

    public void AddTasks(AddTaskResult result)
    {
        foreach (var url in result.Urls)
        {
            var fileName = result.Urls.Length == 1 && !string.IsNullOrWhiteSpace(result.SingleFileName)
                ? result.SingleFileName.Trim()
                : TaskFileNameHelper.GetFileName(url);

            var task = new DownloadTask
            {
                Url = url,
                SaveDirectory = result.SaveDirectory,
                FileName = fileName,
                ThreadCount = result.ThreadCount,
                BlockSize = result.BlockSize,
                CrcOnly = result.CrcOnly,
                SkipCrc = result.SkipCrc,
                UpdateFileTimestamp = result.UpdateFileTimestamp,
                HeaderText = result.HeaderText,
                Status = DownloadTaskStatus.Stopped,
                Log = LocalizationService.Get("TaskLog.Added")
            };

            Tasks.Add(task);
        }
        _settings.LastDownloadDirectory = result.SaveDirectory;
        SaveSettings();
        SaveTasks();
    }

    public void StartTasks(IEnumerable<DownloadTask> tasks)
    {
        foreach (var task in tasks)
        {
            if (task.Status is DownloadTaskStatus.Stopped or DownloadTaskStatus.Error or DownloadTaskStatus.Paused)
            {
                task.Status = DownloadTaskStatus.Pending;
                task.Log = LocalizationService.Get("TaskLog.Queued");
                task.Speed = "-";
                task.Eta = "-";
            }
        }
        SaveTasks();
    }

    public void StopTasks(IEnumerable<DownloadTask> tasks)
    {
        foreach (var task in tasks)
            StopTask(task);
        SaveTasks();
    }

    public void DeleteTasks(IEnumerable<DownloadTask> tasks)
    {
        foreach (var task in tasks.ToList())
        {
            StopTask(task);
            Tasks.Remove(task);
        }
        SaveTasks();
    }

    public void StartAll()
    {
        StartTasks(Tasks.Where(t => t.Status is DownloadTaskStatus.Stopped or DownloadTaskStatus.Error or DownloadTaskStatus.Paused));
    }

    public void StopAll()
    {
        StopTasks(Tasks);
    }

    public void ToggleDetailPane()
    {
        IsDetailPaneOpen = !IsDetailPaneOpen;
    }

    public string GetDefaultDirectory()
    {
        if (_settings.SaveDirectoryMode == SaveDirectoryMode.Fixed)
        {
            if (!string.IsNullOrWhiteSpace(_settings.FixedDownloadDirectory) && Directory.Exists(_settings.FixedDownloadDirectory))
                return _settings.FixedDownloadDirectory;

            return GetDownloadsDirectory();
        }

        if (!string.IsNullOrWhiteSpace(_settings.LastDownloadDirectory) && Directory.Exists(_settings.LastDownloadDirectory))
            return _settings.LastDownloadDirectory;

        var lastTaskDirectory = Tasks
            .Reverse()
            .FirstOrDefault(task => !string.IsNullOrWhiteSpace(task.SaveDirectory) && Directory.Exists(task.SaveDirectory))
            ?.SaveDirectory;

        return lastTaskDirectory ?? GetDownloadsDirectory();
    }

    public AddTaskResult BuildDefaultAddTask()
    {
        return new AddTaskResult
        {
            SaveDirectory = GetDefaultDirectory(),
            ThreadCount = int.TryParse(DefaultThreads, out var threads) ? threads : 4,
            BlockSize = double.TryParse(DefaultBlockSize, out var block) ? block : 16,
            UpdateFileTimestamp = _settings.UpdateFileTimestampByDefault,
            SkipCrc = _settings.SkipCrcByDefault,
            HeaderText = _settings.DefaultHeaderText
        };
    }

    public AppSettings BuildSettingsSnapshot()
    {
        SyncSettingsFromTopBar();
        var snapshot = _settings.Clone();
        if (string.IsNullOrWhiteSpace(snapshot.FixedDownloadDirectory) || !Directory.Exists(snapshot.FixedDownloadDirectory))
            snapshot.FixedDownloadDirectory = GetDownloadsDirectory();
        snapshot.TaskDataFile = _dataFile;
        snapshot.SettingsDataFile = _settingsFile;
        return snapshot;
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.Clone();
        LocalizationService.Apply(_settings.LanguageMode);
        AppThemeService.Apply(_settings.ThemeMode);
        DefaultThreads = _settings.DefaultThreadCount.ToString();
        DefaultBlockSize = _settings.DefaultBlockSizeMb.ToString("0.##");
        MaxConcurrency = CoerceConcurrency(_settings.MaxConcurrency);
        if (string.IsNullOrWhiteSpace(_settings.FixedDownloadDirectory) || !Directory.Exists(_settings.FixedDownloadDirectory))
            _settings.FixedDownloadDirectory = GetDownloadsDirectory();
        _taskListLimit = CoerceTaskListLimit(_settings.TaskListLimit);
        RefreshTaskListScopeOptions(_taskListLimit);
        RefreshVisibleTasks();
        SaveSettings();
    }

    public void Shutdown()
    {
        _globalCts.Cancel();
        foreach (var kvp in _runningTasks)
            kvp.Value.Cancel();
        SaveTasks();
        SaveSettings();
    }

    private async Task QueueLoop()
    {
        while (!_globalCts.IsCancellationRequested)
        {
            DownloadTask? taskToStart = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var running = _runningTasks.Count;
                var pending = Tasks.Count(t => t.Status == DownloadTaskStatus.Pending);
                UpdateStatusText(running, pending);

                if (running < MaxConcurrency)
                    taskToStart = Tasks.FirstOrDefault(t => t.Status == DownloadTaskStatus.Pending);
            });

            if (taskToStart != null)
            {
                _ = Task.Run(() => RunTaskAsync(taskToStart));
                await Task.Delay(100, _globalCts.Token).ContinueWith(_ => { }, CancellationToken.None);
            }
            else
            {
                await Task.Delay(500, _globalCts.Token).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    private async Task RunTaskAsync(DownloadTask task)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token);
        if (!_runningTasks.TryAdd(task.Id, cts))
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            task.Status = DownloadTaskStatus.Running;
            task.Log = LocalizationService.Get("TaskLog.Started");
            task.Mode = "-";
        });

        var service = new RobustDownloaderService();
        var progress = new Progress<DownloadProgress>(p =>
        {
            Dispatcher.UIThread.Post(() => ApplyProgress(task, p));
        });

        try
        {
            var credential = ShouldAutoApplyCredential(task)
                ? _settings.FindCredentialFor(task.Url)
                : null;

            var result = await service.RunAsync(new DownloadRequest
            {
                Url = task.Url,
                SavePath = task.FullSavePath,
                ThreadCount = task.ThreadCount,
                BlockSizeMb = task.BlockSize,
                CrcOnly = task.CrcOnly,
                SkipCrc = task.SkipCrc,
                UpdateFileTimestamp = task.UpdateFileTimestamp,
                HeaderText = task.HeaderText,
                BasicAuthUsername = credential?.Username ?? "",
                BasicAuthPassword = credential?.Password ?? "",
                UseSystemProxy = _settings.ProxyMode == AppProxyMode.System,
                ProxyAddress = _settings.ProxyMode == AppProxyMode.Manual ? _settings.ProxyAddress : ""
            }, progress, cts.Token);

            await Dispatcher.UIThread.InvokeAsync(() => ApplyResult(task, result));
        }
        finally
        {
            _runningTasks.TryRemove(task.Id, out _);
            _taskSpeeds.TryRemove(task.Id, out _);
            await Dispatcher.UIThread.InvokeAsync(UpdateGlobalSpeed);
            cts.Dispose();
            await Dispatcher.UIThread.InvokeAsync(SaveTasks);
        }
    }

    private void ApplyProgress(DownloadTask task, DownloadProgress progress)
    {
        if (progress.TotalBytes > 0)
        {
            task.TotalSizeStr = FormatSize(progress.TotalBytes);
            task.FileSize = $"{FormatSize(progress.BytesWritten)} / {task.TotalSizeStr}";
            task.Progress = progress.ProgressPercent;
        }
        else if (progress.BytesWritten > 0)
        {
            task.FileSize = FormatSize(progress.BytesWritten);
        }

        if (progress.SpeedBytesPerSecond > 0)
            task.Speed = $"{FormatSize(progress.SpeedBytesPerSecond)}/s";
        _taskSpeeds[task.Id] = progress.SpeedBytesPerSecond;
        UpdateGlobalSpeed();
        task.Eta = progress.Eta;
        if (!string.IsNullOrWhiteSpace(progress.Message))
            task.Log = progress.Message;
        task.Diagnostic = progress.Diagnostic;
        task.Mode = progress.Mode;
    }

    private static void ApplyResult(DownloadTask task, DownloadResult result)
    {
        switch (result.Kind)
        {
            case DownloadResultKind.Completed:
                task.Status = DownloadTaskStatus.Completed;
                task.Progress = 100;
                task.Speed = "-";
                task.Eta = LocalizationService.Get("Eta.Completed");
                if (task.TotalSizeStr != "?")
                    task.FileSize = $"{task.TotalSizeStr} / {task.TotalSizeStr}";
                break;
            case DownloadResultKind.Skipped:
                task.Status = DownloadTaskStatus.Completed;
                task.Progress = 100;
                task.Speed = "-";
                task.Eta = LocalizationService.Get("Eta.Exists");
                break;
            case DownloadResultKind.CrcOnlyCompleted:
                task.Status = DownloadTaskStatus.Completed;
                task.Speed = "-";
                task.Eta = "CRC";
                break;
            case DownloadResultKind.Canceled:
                task.Status = DownloadTaskStatus.Stopped;
                task.Speed = "-";
                task.Eta = "-";
                break;
            case DownloadResultKind.Failed:
                task.Status = DownloadTaskStatus.Error;
                task.Speed = "-";
                break;
        }

        task.Log = result.Message;
    }

    private void StopTask(DownloadTask task)
    {
        if (task.Status == DownloadTaskStatus.Running)
        {
            if (_runningTasks.TryGetValue(task.Id, out var cts))
            {
                cts.Cancel();
                task.Log = LocalizationService.Get("TaskLog.Stopping");
            }
        }
        else if (task.Status == DownloadTaskStatus.Pending)
        {
            task.Status = DownloadTaskStatus.Stopped;
            task.Log = LocalizationService.Get("TaskLog.Removed");
        }
    }

    private void LoadTasks()
    {
        if (!File.Exists(_dataFile)) return;

        try
        {
            using var stream = File.OpenRead(_dataFile);
            var loadedTasks = JsonSerializer.Deserialize(stream, AppJsonContext.Default.ObservableCollectionDownloadTask);
            if (loadedTasks == null) return;

            _suppressTaskCollectionSideEffects = true;
            try
            {
                foreach (var task in loadedTasks)
                {
                    if (task.Status == DownloadTaskStatus.Running)
                        task.Status = DownloadTaskStatus.Stopped;

                    if (task.Status != DownloadTaskStatus.Completed && task.Status != DownloadTaskStatus.Error)
                    {
                        task.Speed = "-";
                        task.Eta = "-";
                    }

                    Tasks.Add(task);
                }
            }
            finally
            {
                _suppressTaskCollectionSideEffects = false;
            }

            RefreshVisibleTasks();
            UpdateStatusText(_runningTasks.Count, Tasks.Count(t => t.Status == DownloadTaskStatus.Pending));
        }
        catch
        {
            _suppressTaskCollectionSideEffects = false;
            StatusText = LocalizationService.Get("Error.TasksJsonLoadFailed");
        }
    }

    private void SaveTasks()
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            using var stream = File.Create(_dataFile);
            JsonSerializer.Serialize(stream, Tasks, AppJsonContext.Default.ObservableCollectionDownloadTask);
        }
        catch
        {
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFile))
            {
                using var stream = File.OpenRead(_settingsFile);
                _settings = JsonSerializer.Deserialize(stream, AppJsonContext.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch
        {
            _settings = new AppSettings();
            StatusText = LocalizationService.Get("Error.SettingsJsonLoadFailed");
        }

        LocalizationService.Apply(_settings.LanguageMode);
        AppThemeService.Apply(_settings.ThemeMode);
        if (string.IsNullOrWhiteSpace(_settings.FixedDownloadDirectory) || !Directory.Exists(_settings.FixedDownloadDirectory))
            _settings.FixedDownloadDirectory = GetDownloadsDirectory();
        _taskListLimit = CoerceTaskListLimit(_settings.TaskListLimit);
        _settings.TaskListLimit = _taskListLimit;
        RefreshTaskListScopeOptions(_taskListLimit);
        ApplySettingsToTopBar();
    }

    private void SaveSettings()
    {
        try
        {
            SyncSettingsFromTopBar();
            if (string.IsNullOrWhiteSpace(_settings.FixedDownloadDirectory) || !Directory.Exists(_settings.FixedDownloadDirectory))
                _settings.FixedDownloadDirectory = GetDownloadsDirectory();
            Directory.CreateDirectory(_dataDirectory);
            using var stream = File.Create(_settingsFile);
            JsonSerializer.Serialize(stream, _settings, AppJsonContext.Default.AppSettings);
        }
        catch
        {
        }
    }

    private void ApplySettingsToTopBar()
    {
        DefaultThreads = Math.Max(1, _settings.DefaultThreadCount).ToString();
        DefaultBlockSize = Math.Max(0.03125, _settings.DefaultBlockSizeMb).ToString("0.##");
        MaxConcurrency = CoerceConcurrency(_settings.MaxConcurrency);
    }

    private void SyncSettingsFromTopBar()
    {
        if (int.TryParse(DefaultThreads, out var threads))
            _settings.DefaultThreadCount = Math.Clamp(threads, 1, 256);
        if (double.TryParse(DefaultBlockSize, out var block))
            _settings.DefaultBlockSizeMb = Math.Max(0.03125, block);
        _settings.MaxConcurrency = CoerceConcurrency(MaxConcurrency);
        _settings.TaskListLimit = _taskListLimit;
    }

    private void UpdateGlobalSpeed()
    {
        var totalSpeed = _taskSpeeds.Values.Sum();
        GlobalSpeedText = $"{FormatSize(totalSpeed)}/s";
        UpdateStatusText(_runningTasks.Count, Tasks.Count(t => t.Status == DownloadTaskStatus.Pending));
    }

    private void UpdateStatusText(int running, int pending)
    {
        if (Tasks.Count == 0)
        {
            StatusText = LocalizationService.Get("Queue.Empty");
            return;
        }

        StatusText = running > 0
            ? LocalizationService.Format("Queue.Running", running, MaxConcurrency, pending, Tasks.Count)
            : LocalizationService.Format("Queue.Idle", pending, Tasks.Count);
    }

    private static bool ShouldAutoApplyCredential(DownloadTask task)
    {
        if (task.HeaderText.Contains("Authorization:", StringComparison.OrdinalIgnoreCase))
            return false;
        return !Uri.TryCreate(task.Url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo);
    }

    private static int CoerceConcurrency(int value)
    {
        int[] options = [1, 2, 3, 5, 8];
        return options.Contains(value) ? value : 3;
    }

    private static string GetDownloadsDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
            return AppContext.BaseDirectory;

        var downloads = Path.Combine(userProfile, "Downloads");
        return Directory.Exists(downloads) ? downloads : userProfile;
    }

    private static int CoerceTaskListLimit(int value)
    {
        int[] options = [0, 50, 100, 200];
        return options.Contains(value) ? value : 100;
    }

    private void RefreshTaskListScopeOptions(int selectedLimit)
    {
        selectedLimit = CoerceTaskListLimit(selectedLimit);
        _suppressSettingsSideEffects = true;
        try
        {
            TaskListScopes.Clear();
            TaskListScopes.Add(new TaskListScopeOption(50, LocalizationService.Get("Main.Recent50")));
            TaskListScopes.Add(new TaskListScopeOption(100, LocalizationService.Get("Main.Recent100")));
            TaskListScopes.Add(new TaskListScopeOption(200, LocalizationService.Get("Main.Recent200")));
            TaskListScopes.Add(new TaskListScopeOption(0, LocalizationService.Get("Main.AllTasks")));
            SelectedTaskListScope = TaskListScopes.First(option => option.Limit == selectedLimit);
        }
        finally
        {
            _suppressSettingsSideEffects = false;
        }
    }

    private void RefreshVisibleTasks()
    {
        VisibleTasks.Clear();
        IEnumerable<DownloadTask> source = _taskListLimit == 0
            ? Tasks
            : Tasks.Skip(Math.Max(0, Tasks.Count - _taskListLimit));
        foreach (var task in source)
            VisibleTasks.Add(task);

        var visible = VisibleTasks.Count;
        TabInfo = _taskListLimit == 0
            ? LocalizationService.Format("Queue.AllInfo", Tasks.Count)
            : LocalizationService.Format("Queue.RecentInfo", visible, Tasks.Count);
        OnPropertyChanged(nameof(HasVisibleTasks));
        OnPropertyChanged(nameof(HasNoVisibleTasks));
    }

    private void RefreshLocalizedText()
    {
        foreach (var task in Tasks)
            task.RefreshLocalizedProperties();

        RefreshTaskListScopeOptions(_taskListLimit);
        RefreshVisibleTasks();
        UpdateStatusText(_runningTasks.Count, Tasks.Count(t => t.Status == DownloadTaskStatus.Pending));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(GlobalSpeedDisplay));
        OnPropertyChanged(nameof(DetailPaneButtonText));
    }

    private static string AppVersion
    {
        get
        {
            var version = typeof(MainWindowViewModel).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            return string.IsNullOrWhiteSpace(version) ? "0.0.1" : version.Split('+')[0];
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.00} {sizes[order]}";
    }

    public sealed class TaskListScopeOption(int limit, string label)
    {
        public int Limit { get; } = limit;
        public string Label { get; } = label;

        public override string ToString() => Label;
    }
}
