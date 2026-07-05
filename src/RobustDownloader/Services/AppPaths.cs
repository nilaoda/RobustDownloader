using System;
using System.IO;

namespace RobustDownloader.Services;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RobustDownloader");

    public static string TasksFile { get; } = Path.Combine(DataDirectory, "tasks.json");
    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");
}
