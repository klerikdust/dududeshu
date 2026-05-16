using System;
using System.Runtime.InteropServices;

namespace SamplePlugin.Services;

internal static class Win32Clipboard
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalFree(IntPtr hMem);

    public static bool SetText(string text)
    {
        if (text == null) return false;

        // Retry a few times in case another process has the clipboard open.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (TryOnce(text)) return true;
            System.Threading.Thread.Sleep(20);
        }
        return false;
    }

    private static bool TryOnce(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;

        var hGlobal = IntPtr.Zero;
        var allocated = false;
        try
        {
            EmptyClipboard();

            var bytes = (text.Length + 1) * 2;
            hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (hGlobal == IntPtr.Zero) return false;
            allocated = true;

            var locked = GlobalLock(hGlobal);
            if (locked == IntPtr.Zero) return false;
            try
            {
                Marshal.Copy(new char[] { }, 0, locked, 0); // no-op, keeps analyzer happy
                var asBytes = System.Text.Encoding.Unicode.GetBytes(text + '\0');
                Marshal.Copy(asBytes, 0, locked, asBytes.Length);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                return false;

            // Ownership transferred to the clipboard, don't free.
            allocated = false;
            return true;
        }
        finally
        {
            if (allocated && hGlobal != IntPtr.Zero) GlobalFree(hGlobal);
            CloseClipboard();
        }
    }
}
