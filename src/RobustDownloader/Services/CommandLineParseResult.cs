namespace RobustDownloader.Services;

public sealed class CommandLineParseResult
{
    public bool IsHelp { get; init; }
    public CommandLineCommand Command { get; init; } = CommandLineCommand.None;
    public string Error { get; init; } = "";
}
