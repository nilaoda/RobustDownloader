using System;
using Avalonia.Controls;

namespace RobustDownloader.Services;

public sealed class PlatformProgressIndicatorService : IDisposable
{
    private readonly IPlatformProgressIndicator _indicator;

    public PlatformProgressIndicatorService(Window window)
    {
        _indicator = OperatingSystem.IsWindows()
            ? new WindowsTaskbarProgressIndicator(window)
            : OperatingSystem.IsMacOS()
                ? new MacOSDockProgressIndicator()
                : new NoopPlatformProgressIndicator();
    }

    public void SetProgress(PlatformProgressSnapshot snapshot)
    {
        if (snapshot.IsVisible)
            _indicator.SetProgress(Math.Clamp(snapshot.Value, 0, 1));
        else
            _indicator.Clear();
    }

    public void Clear()
    {
        _indicator.Clear();
    }

    public void Dispose()
    {
        _indicator.Dispose();
    }
}

internal interface IPlatformProgressIndicator : IDisposable
{
    void SetProgress(double value);
    void Clear();
}

internal sealed class NoopPlatformProgressIndicator : IPlatformProgressIndicator
{
    public void SetProgress(double value)
    {
    }

    public void Clear()
    {
    }

    public void Dispose()
    {
    }
}
