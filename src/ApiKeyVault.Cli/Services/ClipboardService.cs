using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ApiKeyVault.Cli.Services;

public interface IClipboardService
{
    void SetText(string text);
    string? GetText();
    void Clear();
    bool MatchesCurrent(byte[] expectedHash);
}

public sealed class WindowsClipboardService : IClipboardService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    public void SetText(string text)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        if (!OpenClipboard(IntPtr.Zero)) return;

        try
        {
            EmptyClipboard();

            // 1. Set Unicode text
            byte[] textBytes = Encoding.Unicode.GetBytes(text + "\0");
            IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)textBytes.Length);
            if (hGlobal != IntPtr.Zero)
            {
                IntPtr target = GlobalLock(hGlobal);
                if (target != IntPtr.Zero)
                {
                    Marshal.Copy(textBytes, 0, target, textBytes.Length);
                    GlobalUnlock(hGlobal);
                    SetClipboardData(CF_UNICODETEXT, hGlobal);
                }
            }

            // 2. Prevent clipboard history and cloud sync (Win+V protection)
            uint cfExcludeMonitor = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
            if (cfExcludeMonitor != 0)
            {
                IntPtr hFlag1 = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)4);
                if (hFlag1 != IntPtr.Zero)
                {
                    SetClipboardData(cfExcludeMonitor, hFlag1);
                }
            }

            uint cfCanIncludeInHistory = RegisterClipboardFormat("CanIncludeInClipboardHistory");
            if (cfCanIncludeInHistory != 0)
            {
                IntPtr hFlag2 = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)4);
                if (hFlag2 != IntPtr.Zero)
                {
                    // Value 0 = do not include
                    IntPtr target = GlobalLock(hFlag2);
                    if (target != IntPtr.Zero)
                    {
                        Marshal.WriteInt32(target, 0);
                        GlobalUnlock(hFlag2);
                    }
                    SetClipboardData(cfCanIncludeInHistory, hFlag2);
                }
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public string? GetText()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        if (!OpenClipboard(IntPtr.Zero)) return null;

        try
        {
            IntPtr handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero) return null;

            IntPtr pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) return null;

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public void Clear()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        if (OpenClipboard(IntPtr.Zero))
        {
            try
            {
                EmptyClipboard();
            }
            finally
            {
                CloseClipboard();
            }
        }
    }

    public bool MatchesCurrent(byte[] expectedHash)
    {
        string? current = GetText();
        if (current == null) return false;
        byte[] currentHash = SHA256.HashData(Encoding.UTF8.GetBytes(current));
        return CryptographicOperations.FixedTimeEquals(currentHash, expectedHash);
    }
}
