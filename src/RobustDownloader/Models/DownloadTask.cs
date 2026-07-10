using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using RobustDownloader.Services;

namespace RobustDownloader.Models;

public sealed class DownloadTask : INotifyPropertyChanged
{
    private const string FormattedLogSeparator = "|";
    private const string CompletedAverageSpeedKey = LocKeys.Download_CompletedAverageSpeed;

    private static readonly string[] LocalizableLogKeys =
    [
        LocKeys.TaskLog_Added,
        LocKeys.TaskLog_Queued,
        LocKeys.TaskLog_Started,
        LocKeys.TaskLog_Stopping,
        LocKeys.TaskLog_Removed,
        LocKeys.Download_SkippedExisting,
        LocKeys.Download_CrcDone,
        LocKeys.Download_RangeFallback,
        LocKeys.Download_CanceledWithResume,
        LocKeys.Download_Canceled,
        LocKeys.Download_SingleThreadCompleted,
        CompletedAverageSpeedKey
    ];

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
    public long ProgressBytesWritten { get; set; }

    [JsonIgnore]
    public long ProgressTotalBytes { get; set; }

    [JsonIgnore]
    public string UpdateFileTimestampText => UpdateFileTimestamp
        ? L.Task_FileTime_Server
        : L.Task_FileTime_Local;

    [JsonIgnore]
    public string StatusLabel => Status switch
    {
        DownloadTaskStatus.Pending => L.Task_Status_Pending,
        DownloadTaskStatus.Running => L.Task_Status_Running,
        DownloadTaskStatus.Paused => L.Task_Status_Paused,
        DownloadTaskStatus.Completed => L.Task_Status_Completed,
        DownloadTaskStatus.Error => L.Task_Status_Error,
        DownloadTaskStatus.Stopped => L.Task_Status_Stopped,
        _ => Status.ToString()
    };

    public void RefreshLocalizedProperties()
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(UpdateFileTimestampText));
        OnPropertyChanged(nameof(EtaText));
        OnPropertyChanged(nameof(LogText));
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
        set => SetField(ref _eta, NormalizeEta(value));
    }

    [JsonIgnore]
    public string EtaText => Eta switch
    {
        LocKeys.Eta_Completed => L.Eta_Completed,
        LocKeys.Eta_Exists => L.Eta_Exists,
        _ => Eta
    };

    public DownloadTaskStatus Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string Log
    {
        get => _log;
        set => SetField(ref _log, NormalizeLog(value));
    }

    [JsonIgnore]
    public string LogText => LocalizeLog(Log);

    public string Diagnostic
    {
        get => _diagnostic;
        set => SetField(ref _diagnostic, value);
    }

    [JsonIgnore]
    public string DiagnosticText => Diagnostic switch
    {
        "" or "-" => "-",
        "Running" => L.Diagnostic_None,
        "Initializing" => L.Diagnostic_Initializing,
        "No Range support" => L.Diagnostic_NoRangeSupport,
        "Disk I/O Bottleneck" => L.Diagnostic_DiskBottleneck,
        "DEADLOCK: Buffer Full & Missing Next Block" => L.Diagnostic_BufferDeadlock,
        "Network Hang / Fragmenting" => L.Diagnostic_NetworkHang,
        "Idle / Queue Empty" => L.Diagnostic_Idle,
        "Completed" => L.Diagnostic_Completed,
        "Error" => L.Diagnostic_Error,
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
        if (propertyName is nameof(Eta))
            OnPropertyChanged(nameof(EtaText));
        if (propertyName is nameof(Log))
            OnPropertyChanged(nameof(LogText));
        if (propertyName is nameof(Status))
            OnPropertyChanged(nameof(StatusLabel));
        if (propertyName is nameof(Diagnostic))
            OnPropertyChanged(nameof(DiagnosticText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public static string FormatLogValue(string key, params string[] args)
    {
        return args.Length == 0 ? key : $"{key}{FormattedLogSeparator}{string.Join(FormattedLogSeparator, args)}";
    }

    private static string NormalizeEta(string value)
    {
        if (LocalizationService.IsLocalizedValue(LocKeys.Eta_Completed, value))
            return LocKeys.Eta_Completed;
        if (LocalizationService.IsLocalizedValue(LocKeys.Eta_Exists, value))
            return LocKeys.Eta_Exists;
        return value;
    }

    private static string NormalizeLog(string value)
    {
        foreach (var key in LocalizableLogKeys)
        {
            if (LocalizationService.IsLocalizedValue(key, value))
                return key;
        }

        return TryNormalizeFormattedLog(value) ?? value;
    }

    private static string? TryNormalizeFormattedLog(string value)
    {
        foreach (var format in LocalizationService.GetLocalizedValues(CompletedAverageSpeedKey))
        {
            var parts = format.Split("{0}", StringSplitOptions.None);
            if (parts.Length != 2 ||
                !value.StartsWith(parts[0], StringComparison.Ordinal) ||
                !value.EndsWith(parts[1], StringComparison.Ordinal))
                continue;

            var argStart = parts[0].Length;
            var argLength = value.Length - argStart - parts[1].Length;
            if (argLength < 0) continue;
            return FormatLogValue(CompletedAverageSpeedKey, value.Substring(argStart, argLength));
        }

        return null;
    }

    private static string LocalizeLog(string value)
    {
        if (TryDecodeFormattedLog(value, out var key, out var args))
            return LocalizationService.Format(key, args);

        foreach (var logKey in LocalizableLogKeys)
        {
            if (value == logKey)
                return LocalizationService.Get(logKey);
        }

        return value;
    }

    private static bool TryDecodeFormattedLog(string value, out string key, out object[] args)
    {
        var parts = value.Split(FormattedLogSeparator);
        if (parts.Length > 1 && parts[0] == CompletedAverageSpeedKey)
        {
            key = parts[0];
            args = parts[1..];
            return true;
        }

        key = "";
        args = [];
        return false;
    }
}
