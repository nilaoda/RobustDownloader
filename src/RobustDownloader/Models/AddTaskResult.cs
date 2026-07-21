namespace RobustDownloader.Models;

public sealed class AddTaskResult
{
    public string[] Urls { get; init; } = [];
    public string[] FileNames { get; init; } = [];
    public string SaveDirectory { get; init; } = "";
    public string SingleFileName { get; init; } = "";
    public int ThreadCount { get; init; } = 4;
    public double BlockSize { get; init; } = 16;
    public bool CrcOnly { get; init; }
    public bool SkipCrc { get; init; }
    public bool UpdateFileTimestamp { get; init; } = true;
    public string HeaderText { get; init; } = "";
    public bool StartImmediately { get; init; }
}
