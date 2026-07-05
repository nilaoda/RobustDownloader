using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using RobustDownloader.Models;

namespace RobustDownloader.Views;

public sealed class DownloadStatusBrushConverter : IValueConverter
{
    private static readonly IBrush PendingBackground = Brush.Parse("#FFF7D6");
    private static readonly IBrush PendingForeground = Brush.Parse("#7A4B00");
    private static readonly IBrush RunningBackground = Brush.Parse("#DDEBFF");
    private static readonly IBrush RunningForeground = Brush.Parse("#174EA6");
    private static readonly IBrush PausedBackground = Brush.Parse("#EFE7FF");
    private static readonly IBrush PausedForeground = Brush.Parse("#5B2FA6");
    private static readonly IBrush CompletedBackground = Brush.Parse("#DCFCE7");
    private static readonly IBrush CompletedForeground = Brush.Parse("#166534");
    private static readonly IBrush ErrorBackground = Brush.Parse("#FEE2E2");
    private static readonly IBrush ErrorForeground = Brush.Parse("#991B1B");
    private static readonly IBrush StoppedBackground = Brush.Parse("#F1F5F9");
    private static readonly IBrush StoppedForeground = Brush.Parse("#475569");

    public string Variant { get; set; } = "Background";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var foreground = string.Equals(Variant, "Foreground", StringComparison.OrdinalIgnoreCase);
        var status = value is DownloadTaskStatus typedStatus ? typedStatus : DownloadTaskStatus.Stopped;

        return status switch
        {
            DownloadTaskStatus.Pending => foreground ? PendingForeground : PendingBackground,
            DownloadTaskStatus.Running => foreground ? RunningForeground : RunningBackground,
            DownloadTaskStatus.Paused => foreground ? PausedForeground : PausedBackground,
            DownloadTaskStatus.Completed => foreground ? CompletedForeground : CompletedBackground,
            DownloadTaskStatus.Error => foreground ? ErrorForeground : ErrorBackground,
            _ => foreground ? StoppedForeground : StoppedBackground
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
