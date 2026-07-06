using System;
using System.Runtime.InteropServices;

namespace RobustDownloader.Services;

internal static class MacOSDockIconService
{
    private const long NSApplicationActivationPolicyRegular = 0;
    private const long NSApplicationActivationPolicyAccessory = 1;

    public static void ShowDockIcon()
    {
        if (!OperatingSystem.IsMacOS()) return;

        SetActivationPolicy(NSApplicationActivationPolicyRegular);
        ActivateIgnoringOtherApps();
    }

    public static void HideDockIcon()
    {
        if (!OperatingSystem.IsMacOS()) return;

        SetActivationPolicy(NSApplicationActivationPolicyAccessory);
    }

    private static void SetActivationPolicy(long policy)
    {
        var app = GetSharedApplication();
        if (app == IntPtr.Zero) return;

        _ = objc_msgSend_bool_nint(app, sel_registerName("setActivationPolicy:"), (nint)policy);
    }

    private static void ActivateIgnoringOtherApps()
    {
        var app = GetSharedApplication();
        if (app == IntPtr.Zero) return;

        objc_msgSend_void_bool(app, sel_registerName("activateIgnoringOtherApps:"), true);
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
    private static extern bool objc_msgSend_bool_nint(IntPtr receiver, IntPtr selector, nint value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_bool(IntPtr receiver, IntPtr selector, bool value);
}
