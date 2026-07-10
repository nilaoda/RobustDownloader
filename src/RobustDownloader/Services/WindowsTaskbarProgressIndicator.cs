using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace RobustDownloader.Services;

[SupportedOSPlatform("windows")]
internal sealed class WindowsTaskbarProgressIndicator : IPlatformProgressIndicator
{
    private static readonly Guid TaskbarListClsid = new("56FDF344-FD6D-11d0-958A-006097C9A090");
    private static readonly Guid TaskbarList3Iid = new("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF");

    private readonly Window _window;
    private IntPtr _taskbarList;
    private bool _initialized;
    private bool _coInitialized;
    private bool _disposed;
    private bool _loggedMissingWindowHandle;
    private bool _loggedInitializationFailure;
    private bool _loggedSetFailure;

    public WindowsTaskbarProgressIndicator(Window window)
    {
        _window = window;
    }

    public void SetProgress(double value)
    {
        if (_disposed) return;

        var hwnd = GetWindowHandle();
        if (hwnd == IntPtr.Zero)
        {
            LogMissingWindowHandle();
            return;
        }

        if (!EnsureInitialized()) return;

        var progressResult = CallSetProgressValue(hwnd, (ulong)Math.Round(Math.Clamp(value, 0, 1) * 1000), 1000);
        var stateResult = CallSetProgressState(hwnd, TaskbarProgressState.Normal);
        if (progressResult != 0 || stateResult != 0)
            LogSetFailure(progressResult, stateResult);
    }

    public void Clear()
    {
        if (_disposed) return;
        if (!_initialized) return;
        if (!EnsureInitialized()) return;

        ClearCore();
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_initialized && _taskbarList != IntPtr.Zero)
            ClearCore();

        if (_taskbarList != IntPtr.Zero)
        {
            _ = Marshal.Release(_taskbarList);
            _taskbarList = IntPtr.Zero;
        }

        if (_coInitialized)
        {
            CoUninitialize();
            _coInitialized = false;
        }

        _disposed = true;
    }

    private bool EnsureInitialized()
    {
        if (_disposed) return false;
        if (_initialized) return _taskbarList != IntPtr.Zero;

        if (!EnsureComInitialized())
        {
            LogInitializationFailure("CoInitializeEx failed.");
            return false;
        }

        var createResult = CoCreateInstance(
                in TaskbarListClsid,
                IntPtr.Zero,
                ClsctxInprocServer,
                in TaskbarList3Iid,
                out _taskbarList);
        if (createResult != 0 || _taskbarList == IntPtr.Zero)
        {
            LogInitializationFailure($"CoCreateInstance failed: 0x{createResult:X8}.");
            return false;
        }

        var initResult = CallHrInit();
        if (initResult != 0)
        {
            LogInitializationFailure($"ITaskbarList3.HrInit failed: 0x{initResult:X8}.");
            Marshal.Release(_taskbarList);
            _taskbarList = IntPtr.Zero;
            return false;
        }

        _initialized = true;
        return true;
    }

    private bool EnsureComInitialized()
    {
        if (_coInitialized) return true;

        var hr = CoInitializeEx(IntPtr.Zero, CoInitApartmentThreaded);
        if (hr is 0 or 1)
        {
            _coInitialized = true;
            return true;
        }

        return hr == unchecked((int)0x80010106);
    }

    private IntPtr GetWindowHandle()
    {
        return _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
    }

    private int CallHrInit()
    {
        var method = GetVTableMethod<HrInitDelegate>(3);
        return method(_taskbarList);
    }

    private int CallSetProgressValue(IntPtr hwnd, ulong completed, ulong total)
    {
        var method = GetVTableMethod<SetProgressValueDelegate>(9);
        return method(_taskbarList, hwnd, completed, total);
    }

    private int CallSetProgressState(IntPtr hwnd, TaskbarProgressState state)
    {
        var method = GetVTableMethod<SetProgressStateDelegate>(10);
        return method(_taskbarList, hwnd, state);
    }

    private void ClearCore()
    {
        var hwnd = GetWindowHandle();
        if (hwnd != IntPtr.Zero)
            _ = CallSetProgressState(hwnd, TaskbarProgressState.NoProgress);
    }

    private void LogMissingWindowHandle()
    {
        if (_loggedMissingWindowHandle) return;

        _loggedMissingWindowHandle = true;
        WriteDiagnostic("Windows taskbar progress skipped because the main window handle is not available yet.");
    }

    private void LogInitializationFailure(string message)
    {
        if (_loggedInitializationFailure) return;

        _loggedInitializationFailure = true;
        WriteDiagnostic($"Windows taskbar progress initialization failed. {message}");
    }

    private void LogSetFailure(int progressResult, int stateResult)
    {
        if (_loggedSetFailure) return;

        _loggedSetFailure = true;
        WriteDiagnostic(
            $"Windows taskbar progress update failed. SetProgressValue=0x{progressResult:X8}, SetProgressState=0x{stateResult:X8}.");
    }

    private static void WriteDiagnostic(string message)
    {
        Trace.TraceWarning(message);

        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.AppendAllText(
                Path.Combine(AppPaths.DataDirectory, "platform-progress.log"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never interfere with downloading.
        }
    }

    private TDelegate GetVTableMethod<TDelegate>(int index)
        where TDelegate : Delegate
    {
        var vtable = Marshal.ReadIntPtr(_taskbarList);
        var pointer = Marshal.ReadIntPtr(vtable, index * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(pointer);
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        in Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out IntPtr ppv);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int HrInitDelegate(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetProgressValueDelegate(IntPtr self, IntPtr hwnd, ulong completed, ulong total);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetProgressStateDelegate(IntPtr self, IntPtr hwnd, TaskbarProgressState state);

    private enum TaskbarProgressState
    {
        NoProgress = 0,
        Normal = 2
    }

    private const uint CoInitApartmentThreaded = 0x2;
    private const uint ClsctxInprocServer = 0x1;
}
