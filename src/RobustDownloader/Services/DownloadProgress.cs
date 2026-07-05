namespace RobustDownloader.Services;

public sealed class DownloadProgress
{
    public double ProgressPercent { get; init; }
    public long BytesWritten { get; init; }
    public long TotalBytes { get; init; }
    public long NetworkBytes { get; init; }
    public long SpeedBytesPerSecond { get; init; }
    public string Eta { get; init; } = "--:--:--";
    public string Message { get; init; } = "";
    public string Diagnostic { get; init; } = "";
    public string Mode { get; init; } = "";
    public bool IsWarning { get; init; }
}
