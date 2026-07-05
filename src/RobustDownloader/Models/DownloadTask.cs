using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using RobustDownloader.Services;

namespace RobustDownloader.Models;

public sealed class DownloadTask : INotifyPropertyChanged
{
    private string _fileName = "";
    private string _fileSize = "-";
    private double _progress;
    private string _speed = "-";
    private string _eta = "-";
    private DownloadTaskStatus _status = DownloadTaskStatus.Pending;
    private string _log = "";
    private string _diagnostic = "";
    private string _mode = "-";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Url { get; set; } = "";
    public string SaveDirectory { get; set; } = "";
    public string TotalSizeStr { get; set; } = "?";
    public int ThreadCount { get; set; } = 4;
    public double BlockSize { get; set; } = 16;
    public bool CrcOnly { get; set; }
    public bool SkipCrc { get; set; }
    public bool UpdateFileTimestamp { get; set; } = true;
    public string HeaderText { get; set; } = "";

    [JsonIgnore]
    public string UpdateFileTimestampText => UpdateFileTimestamp
        ? LocalizationService.Get("Task.FileTime.Server")
        : LocalizationService.Get("Task.FileTime.Local");

    [JsonIgnore]
    public string StatusLabel => Status switch
    {
        DownloadTaskStatus.Pending => LocalizationService.Get("Task.Status.Pending"),
        DownloadTaskStatus.Running => LocalizationService.Get("Task.Status.Running"),
        DownloadTaskStatus.Paused => LocalizationService.Get("Task.Status.Paused"),
        DownloadTaskStatus.Completed => LocalizationService.Get("Task.Status.Completed"),
        DownloadTaskStatus.Error => LocalizationService.Get("Task.Status.Error"),
        DownloadTaskStatus.Stopped => LocalizationService.Get("Task.Status.Stopped"),
        _ => Status.ToString()
    };

    public void RefreshLocalizedProperties()
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(UpdateFileTimestampText));
        OnPropertyChanged(nameof(DiagnosticText));
    }

    public string FileName
    {
        get => _fileName;
        set => SetField(ref _fileName, value);
    }

    [JsonIgnore]
    public string FullSavePath => Path.Combine(SaveDirectory, FileName);

    public string FileSize
    {
        get => _fileSize;
        set => SetField(ref _fileSize, value);
    }

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, Math.Clamp(value, 0, 100));
    }

    public string Speed
    {
        get => _speed;
        set => SetField(ref _speed, value);
    }

    public string Eta
    {
        get => _eta;
        set => SetField(ref _eta, value);
    }

    public DownloadTaskStatus Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string Log
    {
        get => _log;
        set => SetField(ref _log, value);
    }

    public string Diagnostic
    {
        get => _diagnostic;
        set => SetField(ref _diagnostic, value);
    }

    [JsonIgnore]
    public string DiagnosticText => Diagnostic switch
    {
        "" or "-" => "-",
        "Running" => LocalizationService.Get("Diagnostic.None"),
        "Initializing" => LocalizationService.Get("Diagnostic.Initializing"),
        "No Range support" => LocalizationService.Get("Diagnostic.NoRangeSupport"),
        "Disk I/O Bottleneck" => LocalizationService.Get("Diagnostic.DiskBottleneck"),
        "DEADLOCK: Buffer Full & Missing Next Block" => LocalizationService.Get("Diagnostic.BufferDeadlock"),
        "Network Hang / Fragmenting" => LocalizationService.Get("Diagnostic.NetworkHang"),
        "Idle / Queue Empty" => LocalizationService.Get("Diagnostic.Idle"),
        "Completed" => LocalizationService.Get("Diagnostic.Completed"),
        "Error" => LocalizationService.Get("Diagnostic.Error"),
        _ => Diagnostic
    };

    public string Mode
    {
        get => _mode;
        set => SetField(ref _mode, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName is nameof(FileName) or nameof(SaveDirectory))
            OnPropertyChanged(nameof(FullSavePath));
        if (propertyName is nameof(Status))
            OnPropertyChanged(nameof(StatusLabel));
        if (propertyName is nameof(Diagnostic))
            OnPropertyChanged(nameof(DiagnosticText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
