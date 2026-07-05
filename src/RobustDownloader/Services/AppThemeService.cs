using Avalonia;
using Avalonia.Styling;
using RobustDownloader.Models;

namespace RobustDownloader.Services;

public static class AppThemeService
{
    public static void Apply(AppThemeMode mode)
    {
        if (Application.Current == null) return;

        Application.Current.RequestedThemeVariant = mode switch
        {
            AppThemeMode.Light => ThemeVariant.Light,
            AppThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
