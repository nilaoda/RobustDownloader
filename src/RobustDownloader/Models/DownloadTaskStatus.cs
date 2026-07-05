namespace RobustDownloader.Models;

public enum DownloadTaskStatus
{
    Pending,
    Running,
    Paused,
    Completed,
    Error,
    Stopped
}
