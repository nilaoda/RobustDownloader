using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Platform;
using SkiaSharp;

namespace RobustDownloader.Services;

[SupportedOSPlatform("macos")]
internal sealed class MacOSDockProgressIndicator : IPlatformProgressIndicator
{
    private const int IconSize = 512;
    private const int BarHeight = 34;
    private const int BarMargin = 48;
    private static readonly Uri IconUri = new("avares://RobustDownloader/Assets/app-icon.png");

    private readonly string _iconPath = Path.Combine(Path.GetTempPath(), "RobustDownloader", "dock-progress.png");
    private SKBitmap? _baseIcon;
    private double? _lastValue;
    private bool _disposed;

    public void SetProgress(double value)
    {
        if (_disposed) return;

        value = Math.Clamp(value, 0, 1);
        if (_lastValue.HasValue && Math.Abs(_lastValue.Value - value) < 0.005)
            return;

        _lastValue = value;
        RenderIcon(value);
        SetApplicationIcon(_iconPath);
    }

    public void Clear()
    {
        if (_disposed) return;

        _lastValue = null;
        SetApplicationIcon(null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        _disposed = true;
        _baseIcon?.Dispose();
    }

    private void RenderIcon(double value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_iconPath)!);

        _baseIcon ??= LoadBaseIcon();
        using var surface = SKSurface.Create(new SKImageInfo(IconSize, IconSize, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (_baseIcon != null)
        {
            var rect = new SKRect(0, 0, IconSize, IconSize);
            canvas.DrawBitmap(_baseIcon, rect);
        }

        var barRect = new SKRoundRect(
            new SKRect(BarMargin, IconSize - BarMargin - BarHeight, IconSize - BarMargin, IconSize - BarMargin),
            BarHeight / 2f,
            BarHeight / 2f);
        using var trackPaint = new SKPaint { Color = new SKColor(0, 0, 0, 150), IsAntialias = true };
        canvas.DrawRoundRect(barRect, trackPaint);

        if (value > 0)
        {
            var fillWidth = Math.Max(BarHeight, (IconSize - BarMargin * 2) * (float)value);
            var fillRect = new SKRoundRect(
                new SKRect(BarMargin, IconSize - BarMargin - BarHeight, BarMargin + fillWidth, IconSize - BarMargin),
                BarHeight / 2f,
                BarHeight / 2f);
            using var fillPaint = new SKPaint { Color = new SKColor(255, 255, 255, 245), IsAntialias = true };
            canvas.DrawRoundRect(fillRect, fillPaint);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        using var stream = File.Create(_iconPath);
        data.SaveTo(stream);
    }

    private static SKBitmap? LoadBaseIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(IconUri);
            return SKBitmap.Decode(stream);
        }
        catch
        {
            return null;
        }
    }

    private static void SetApplicationIcon(string? path)
    {
        var app = GetSharedApplication();
        if (app == IntPtr.Zero) return;

        var image = IntPtr.Zero;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var nsString = objc_msgSend_intptr_string(
                objc_getClass("NSString"),
                sel_registerName("stringWithUTF8String:"),
                path);
            if (nsString != IntPtr.Zero)
            {
                image = objc_msgSend_intptr(objc_getClass("NSImage"), sel_registerName("alloc"));
                image = objc_msgSend_intptr_intptr(image, sel_registerName("initWithContentsOfFile:"), nsString);
            }
        }

        objc_msgSend_void_intptr(app, sel_registerName("setApplicationIconImage:"), image);

        if (image != IntPtr.Zero)
            objc_msgSend_void(image, sel_registerName("release"));
    }

    private static IntPtr GetSharedApplication()
    {
        var nsApplication = objc_getClass("NSApplication");
        return nsApplication == IntPtr.Zero
            ? IntPtr.Zero
            : objc_msgSend_intptr(nsApplication, sel_registerName("sharedApplication"));
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_intptr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_intptr_intptr(IntPtr receiver, IntPtr selector, IntPtr value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_intptr_string(IntPtr receiver, IntPtr selector, string value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_intptr(IntPtr receiver, IntPtr selector, IntPtr value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector);
}
