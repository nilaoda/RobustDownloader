using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Avalonia;
using RobustDownloader.Models;

namespace RobustDownloader.Services;

public static class LocalizationService
{
    private static AppLanguageMode _mode = AppLanguageMode.Auto;

    public static event EventHandler? LanguageChanged;

    public static AppLanguageMode Mode => _mode;
    public static AppLanguageMode EffectiveMode { get; private set; } = Resolve(AppLanguageMode.Auto);

    public static void ApplyForCommandLine(AppLanguageMode mode)
    {
        _mode = mode;
        EffectiveMode = Resolve(mode);
    }

    public static void Apply(AppLanguageMode mode)
    {
        ApplyForCommandLine(mode);
        var resources = GetResources(EffectiveMode);

        if (Application.Current != null)
        {
            foreach (var (key, value) in resources)
                Application.Current.Resources[key] = value;
        }

        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Get(string key)
    {
        var resources = GetResources(EffectiveMode);
        return resources.TryGetValue(key, out var value) ? value : key;
    }

    public static string Format(string key, params object?[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }

    public static bool IsLocalizedValue(string key, string value)
    {
        return string.Equals(value, key, StringComparison.Ordinal) ||
               string.Equals(GetResourceValue(LocalizationCatalog.ZhHans, key), value, StringComparison.Ordinal) ||
               string.Equals(GetResourceValue(LocalizationCatalog.En, key), value, StringComparison.Ordinal) ||
               string.Equals(GetResourceValue(LocalizationCatalog.ZhHant, key), value, StringComparison.Ordinal);
    }

    public static IEnumerable<string> GetLocalizedValues(string key)
    {
        yield return GetResourceValue(LocalizationCatalog.ZhHans, key);
        yield return GetResourceValue(LocalizationCatalog.En, key);
        yield return GetResourceValue(LocalizationCatalog.ZhHant, key);
    }

    private static string GetResourceValue(IReadOnlyDictionary<string, string> resources, string key)
    {
        return resources.TryGetValue(key, out var value) ? value : key;
    }

    private static AppLanguageMode Resolve(AppLanguageMode mode)
    {
        if (mode != AppLanguageMode.Auto) return mode;

        foreach (var name in GetPreferredLanguageNames())
        {
            var resolved = ResolveLanguageName(name);
            if (resolved.HasValue)
                return resolved.Value;
        }

        return AppLanguageMode.English;
    }

    private static AppLanguageMode? ResolveLanguageName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        name = NormalizeLanguageName(name);
        if (name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("-TW", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("-HK", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("-MO", StringComparison.OrdinalIgnoreCase))
            return AppLanguageMode.TraditionalChinese;

        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return AppLanguageMode.SimplifiedChinese;

        return name.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? AppLanguageMode.English
            : null;
    }

    private static IEnumerable<string> GetPreferredLanguageNames()
    {
        if (OperatingSystem.IsMacOS())
        {
            foreach (var name in ReadMacOSAppleLanguages())
                yield return name;
        }

        yield return CultureInfo.CurrentUICulture.Name;
        yield return CultureInfo.CurrentCulture.Name;

        foreach (var variable in new[] { "LC_ALL", "LC_MESSAGES", "LANG" })
            yield return Environment.GetEnvironmentVariable(variable) ?? "";
    }

    [SupportedOSPlatform("macos")]
    private static IReadOnlyList<string> ReadMacOSAppleLanguages()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/defaults",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            startInfo.ArgumentList.Add("read");
            startInfo.ArgumentList.Add("-g");
            startInfo.ArgumentList.Add("AppleLanguages");

            using var process = Process.Start(startInfo);
            if (process == null || !process.WaitForExit(1000))
                return [];

            var output = process.StandardOutput.ReadToEnd();
            var names = new List<string>();
            foreach (var line in output.Split('\n'))
            {
                var name = line.Trim().Trim(',', '"');
                if (name.Length > 0 && name is not "(" and not ")")
                    names.Add(name);
            }

            return names;
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizeLanguageName(string name)
    {
        var normalized = name.Trim().Trim('"').Replace('_', '-');
        var dotIndex = normalized.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex >= 0)
            normalized = normalized[..dotIndex];

        return normalized;
    }

    private static IReadOnlyDictionary<string, string> GetResources(AppLanguageMode mode)
    {
        return mode switch
        {
            AppLanguageMode.English => LocalizationCatalog.En,
            AppLanguageMode.TraditionalChinese => LocalizationCatalog.ZhHant,
            _ => LocalizationCatalog.ZhHans
        };
    }

}
