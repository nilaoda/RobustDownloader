using System;
using System.Runtime.InteropServices;

namespace RobustDownloader.Services;

internal static class Win32Util
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string name,
        IntPtr pbc,
        out IntPtr pidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHOpenFolderAndSelectItems(
        IntPtr pidlFolder,
        uint cItems,
        [MarshalAs(UnmanagedType.LPArray)] IntPtr[]? apidl,
        uint dwFlags);

    public static bool TryOpenFolderAndSelectItem(string path)
    {
        if (SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
            return false;

        try
        {
            return SHOpenFolderAndSelectItems(pidl, 0, null, 0) == 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pidl);
        }
    }
}
