namespace RobustDownloader.Services;

public sealed class DownloadRequest
{
    public string Url { get; init; } = "";
    public string SavePath { get; init; } = "";
    public int ThreadCount { get; init; } = 8;
    public double BlockSizeMb { get; init; } = 16;
    public bool CrcOnly { get; init; }
    public bool SkipCrc { get; init; }
    public bool UpdateFileTimestamp { get; init; } = true;
    public string HeaderText { get; init; } = "";
    public string BasicAuthUsername { get; init; } = "";
    public string BasicAuthPassword { get; init; } = "";
    public bool UseSystemProxy { get; init; } = true;
    public string ProxyAddress { get; init; } = "";
}
