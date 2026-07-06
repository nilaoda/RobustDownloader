namespace RobustDownloader.Services;

public enum DownloadResultKind
{
    Completed,
    Skipped,
    CrcOnlyCompleted,
    Canceled,
    Failed
}

public sealed class DownloadResult
{
    public DownloadResultKind Kind { get; init; }
    public string Message { get; init; } = "";
    public string MessageKey { get; init; } = "";
    public string[] MessageArgs { get; init; } = [];
}
