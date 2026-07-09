using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RobustDownloader.Services;

public static class CommandLineParser
{
    public static string HelpText => LocalizationService.Get("Cli.Help");

    public static CommandLineParseResult Parse(string[] args)
    {
        if (args.Length == 0)
            return new CommandLineParseResult();

        if (args.Any(IsHelpOption))
            return new CommandLineParseResult { IsHelp = true };

        var urls = new List<string>();
        var headers = new List<string>();
        var saveDirectory = "";
        var singleFileName = "";
        int? threadCount = null;
        double? blockSizeMb = null;
        var start = false;
        var queue = false;
        var silent = false;
        var show = false;
        CommandLineCommandKind? commandKind = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--add":
                    if (!SetCommandKind(CommandLineCommandKind.AddTasks, ref commandKind, out var addError))
                        return Error(addError);

                    while (index + 1 < args.Length && !IsOption(args[index + 1]))
                        urls.Add(args[++index]);
                    break;

                case "--show":
                    show = true;
                    break;

                case "--start-all":
                    if (!SetCommandKind(CommandLineCommandKind.StartAll, ref commandKind, out var startAllError))
                        return Error(startAllError);
                    break;

                case "--stop-all":
                    if (!SetCommandKind(CommandLineCommandKind.StopAll, ref commandKind, out var stopAllError))
                        return Error(stopAllError);
                    break;

                case "--dir":
                    if (!TryReadValue(args, ref index, arg, out saveDirectory, out var dirError))
                        return Error(dirError);
                    break;

                case "--name":
                    if (!TryReadValue(args, ref index, arg, out singleFileName, out var nameError))
                        return Error(nameError);
                    break;

                case "--threads":
                    if (!TryReadValue(args, ref index, arg, out var threadsValue, out var threadsError))
                        return Error(threadsError);
                    if (!int.TryParse(threadsValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedThreads) ||
                        parsedThreads < 1 ||
                        parsedThreads > 256)
                        return Error(LocalizationService.Get("Cli.Error.Threads"));
                    threadCount = parsedThreads;
                    break;

                case "--block-size":
                    if (!TryReadValue(args, ref index, arg, out var blockValue, out var blockError))
                        return Error(blockError);
                    if (!double.TryParse(blockValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedBlock) ||
                        parsedBlock <= 0)
                        return Error(LocalizationService.Get("Cli.Error.BlockSize"));
                    blockSizeMb = parsedBlock;
                    break;

                case "--header":
                    if (!TryReadValue(args, ref index, arg, out var header, out var headerError))
                        return Error(headerError);
                    if (!header.Contains(':', StringComparison.Ordinal))
                        return Error(LocalizationService.Get("Cli.Error.Header"));
                    headers.Add(header);
                    break;

                case "--start":
                    start = true;
                    break;

                case "--queue":
                    queue = true;
                    break;

                case "--silent":
                    silent = true;
                    break;

                default:
                    return IsOption(arg)
                        ? Error(LocalizationService.Format("Cli.Error.UnknownOption", arg))
                        : Error(LocalizationService.Format("Cli.Error.UnexpectedArgument", arg));
            }
        }

        if (start && queue)
            return Error(LocalizationService.Get("Cli.Error.StartQueueConflict"));

        var kind = commandKind ?? (show ? CommandLineCommandKind.Show : CommandLineCommandKind.None);
        if (kind == CommandLineCommandKind.None)
            return silent ? Error(LocalizationService.Get("Cli.Error.SilentRequiresCommand")) : new CommandLineParseResult();

        if (kind != CommandLineCommandKind.AddTasks && HasAddOnlyOptions(saveDirectory, singleFileName, threadCount, blockSizeMb, headers, start, queue))
            return Error(LocalizationService.Get("Cli.Error.AddOptionsRequireAdd"));

        if (kind == CommandLineCommandKind.AddTasks)
        {
            if (urls.Count == 0)
                return Error(LocalizationService.Get("Cli.Error.AddRequiresUrl"));

            foreach (var url in urls)
            {
                if (!IsHttpUrl(url))
                    return Error(LocalizationService.Format("Cli.Error.InvalidHttpUrl", url));
            }

            if (!string.IsNullOrWhiteSpace(singleFileName) && urls.Count != 1)
                return Error(LocalizationService.Get("Cli.Error.NameSingleUrl"));

            if (!string.IsNullOrWhiteSpace(saveDirectory) && !Directory.Exists(saveDirectory))
                return Error(LocalizationService.Format("Cli.Error.SaveDirMissing", saveDirectory));
        }

        return new CommandLineParseResult
        {
            Command = new CommandLineCommand
            {
                Kind = kind,
                Silent = silent,
                ActivateWindow = show || !silent,
                AddTasks = kind == CommandLineCommandKind.AddTasks
                    ? new CommandLineAddTasksOptions
                    {
                        Urls = urls.ToArray(),
                        SaveDirectory = saveDirectory.Trim(),
                        SingleFileName = singleFileName.Trim(),
                        ThreadCount = threadCount,
                        BlockSizeMb = blockSizeMb,
                        HeaderText = string.Join(Environment.NewLine, headers),
                        StartImmediately = start
                    }
                    : null
            }
        };
    }

    private static bool IsHelpOption(string arg)
    {
        return string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(arg, "/?", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOption(string value)
    {
        return value.StartsWith("--", StringComparison.Ordinal) ||
               string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "/?", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SetCommandKind(CommandLineCommandKind value, ref CommandLineCommandKind? commandKind, out string error)
    {
        if (commandKind is { } existing && existing != value)
        {
            error = LocalizationService.Format("Cli.Error.OnlyOneCommand", existing, value);
            return false;
        }

        commandKind = value;
        error = "";
        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, string option, out string value, out string error)
    {
        if (index + 1 >= args.Length || IsOption(args[index + 1]))
        {
            value = "";
            error = LocalizationService.Format("Cli.Error.OptionRequiresValue", option);
            return false;
        }

        value = args[++index];
        error = "";
        return true;
    }

    private static bool HasAddOnlyOptions(
        string saveDirectory,
        string singleFileName,
        int? threadCount,
        double? blockSizeMb,
        List<string> headers,
        bool start,
        bool queue)
    {
        return !string.IsNullOrWhiteSpace(saveDirectory) ||
               !string.IsNullOrWhiteSpace(singleFileName) ||
               threadCount.HasValue ||
               blockSizeMb.HasValue ||
               headers.Count > 0 ||
               start ||
               queue;
    }

    private static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static CommandLineParseResult Error(string message)
    {
        return new CommandLineParseResult { Error = message };
    }
}
