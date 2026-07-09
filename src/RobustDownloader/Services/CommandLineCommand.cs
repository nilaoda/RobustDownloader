namespace RobustDownloader.Services;

public enum CommandLineCommandKind
{
    None,
    Show,
    AddTasks,
    StartAll,
    StopAll
}

public sealed class CommandLineCommand
{
    public CommandLineCommandKind Kind { get; init; }
    public bool Silent { get; init; }
    public bool ActivateWindow { get; init; }
    public CommandLineAddTasksOptions? AddTasks { get; init; }

    public static CommandLineCommand None { get; } = new();

    public static CommandLineCommand Show() => new()
    {
        Kind = CommandLineCommandKind.Show,
        ActivateWindow = true
    };
}
