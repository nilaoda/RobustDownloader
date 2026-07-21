using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RobustDownloader.Services;

public static class CommandLineConsole
{
    private const int AttachParentProcess = -1;

    public static void WriteOut(string text)
    {
        EnsureConsole();
        Console.Out.WriteLine(text);
    }

    public static void WriteError(string text)
    {
        EnsureConsole();
        Console.Error.WriteLine(text);
    }

    private static void EnsureConsole()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            AttachConsole(AttachParentProcess);
            ResetConsoleWriters();
        }
        catch
        {
            // If attaching fails, keep the default console streams.
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ResetConsoleWriters()
    {
        try
        {
            var encoding = Console.OutputEncoding;

            var output = Console.OpenStandardOutput();
            if (output != Stream.Null)
                Console.SetOut(new StreamWriter(output, encoding) { AutoFlush = true });

            var error = Console.OpenStandardError();
            if (error != Stream.Null)
                Console.SetError(new StreamWriter(error, encoding) { AutoFlush = true });
        }
        catch
        {
            // Ignore console stream reset failures for GUI launches.
        }
    }

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);
}
