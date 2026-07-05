using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace RobustDownloader.Models;

public sealed class AppSettings
{
    public int DefaultThreadCount { get; set; } = 4;
    public double DefaultBlockSizeMb { get; set; } = 16;
    public int MaxConcurrency { get; set; } = 3;
    public int TaskListLimit { get; set; } = 100;
    public bool UpdateFileTimestampByDefault { get; set; } = true;
    public bool SkipCrcByDefault { get; set; }
    public AppLanguageMode LanguageMode { get; set; } = AppLanguageMode.Auto;
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;
    public AppProxyMode ProxyMode { get; set; } = AppProxyMode.System;
    public string ProxyAddress { get; set; } = "";
    public string DefaultHeaderText { get; set; } = "";
    public SaveDirectoryMode SaveDirectoryMode { get; set; } = SaveDirectoryMode.LastUsed;
    public string FixedDownloadDirectory { get; set; } = "";
    public string LastDownloadDirectory { get; set; } = "";
    public ObservableCollection<SiteCredential> SiteCredentials { get; set; } = [];

    [JsonIgnore]
    public string TaskDataFile { get; set; } = "";

    [JsonIgnore]
    public string SettingsDataFile { get; set; } = "";

    public AppSettings Clone()
    {
        return new AppSettings
        {
            DefaultThreadCount = DefaultThreadCount,
            DefaultBlockSizeMb = DefaultBlockSizeMb,
            MaxConcurrency = MaxConcurrency,
            TaskListLimit = TaskListLimit,
            UpdateFileTimestampByDefault = UpdateFileTimestampByDefault,
            SkipCrcByDefault = SkipCrcByDefault,
            LanguageMode = LanguageMode,
            ThemeMode = ThemeMode,
            ProxyMode = ProxyMode,
            ProxyAddress = ProxyAddress,
            DefaultHeaderText = DefaultHeaderText,
            SaveDirectoryMode = SaveDirectoryMode,
            FixedDownloadDirectory = FixedDownloadDirectory,
            LastDownloadDirectory = LastDownloadDirectory,
            TaskDataFile = TaskDataFile,
            SettingsDataFile = SettingsDataFile,
            SiteCredentials = new ObservableCollection<SiteCredential>(
                SiteCredentials.Select(c => new SiteCredential
                {
                    Enabled = c.Enabled,
                    Pattern = c.Pattern,
                    Username = c.Username,
                    Password = c.Password
                }))
        };
    }

    public SiteCredential? FindCredentialFor(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        return SiteCredentials.FirstOrDefault(c =>
            c.Enabled &&
            !string.IsNullOrWhiteSpace(c.Pattern) &&
            !string.IsNullOrWhiteSpace(c.Username) &&
            IsMatch(c.Pattern.Trim(), uri));
    }

    private static bool IsMatch(string pattern, Uri uri)
    {
        if (Uri.TryCreate(pattern, UriKind.Absolute, out var patternUri))
        {
            if (!string.Equals(patternUri.Host, uri.Host, StringComparison.OrdinalIgnoreCase))
                return false;
            var path = patternUri.AbsolutePath.TrimEnd('/');
            return string.IsNullOrEmpty(path) ||
                   path == "/" ||
                   uri.AbsolutePath.StartsWith(path, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = pattern[1..];
            return uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(uri.Host, pattern, StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith("." + pattern, StringComparison.OrdinalIgnoreCase);
    }
}
