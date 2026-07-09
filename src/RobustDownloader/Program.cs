using Avalonia;
using System;
using System.IO;
using System.Text.Json;
using RobustDownloader.Models;
using RobustDownloader.Services;

namespace RobustDownloader;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        LocalizationService.ApplyForCommandLine(LoadCommandLineLanguageMode());

        var parseResult = CommandLineParser.Parse(args);
        if (parseResult.IsHelp)
        {
            CommandLineConsole.WriteOut(CommandLineParser.HelpText);
            return;
        }

        if (!string.IsNullOrWhiteSpace(parseResult.Error))
        {
            CommandLineConsole.WriteError(parseResult.Error);
            CommandLineConsole.WriteError(LocalizationService.Get("Cli.Error.HelpHint"));
            Environment.ExitCode = 2;
            return;
        }

        using var singleInstance = SingleInstanceService.TryAcquire(parseResult.Command, out var commandSent);
        if (singleInstance == null)
        {
            if (!commandSent)
            {
                CommandLineConsole.WriteError(LocalizationService.Get("Cli.Error.SendFailed"));
                Environment.ExitCode = 1;
            }
            return;
        }

        App.SingleInstance = singleInstance;
        App.InitialCommand = parseResult.Command;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static AppLanguageMode LoadCommandLineLanguageMode()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return AppLanguageMode.Auto;

            using var stream = File.OpenRead(AppPaths.SettingsFile);
            var settings = JsonSerializer.Deserialize(stream, AppJsonContext.Default.AppSettings);
            return settings?.LanguageMode ?? AppLanguageMode.Auto;
        }
        catch
        {
            return AppLanguageMode.Auto;
        }
    }
}
