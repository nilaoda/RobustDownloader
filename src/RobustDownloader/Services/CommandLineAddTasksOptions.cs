namespace RobustDownloader.Services;

public sealed class CommandLineAddTasksOptions
{
    public string[] Urls { get; init; } = [];
    public string SaveDirectory { get; init; } = "";
    public string SingleFileName { get; init; } = "";
    public int? ThreadCount { get; init; }
    public double? BlockSizeMb { get; init; }
    public string HeaderText { get; init; } = "";
    public bool StartImmediately { get; init; }
}
