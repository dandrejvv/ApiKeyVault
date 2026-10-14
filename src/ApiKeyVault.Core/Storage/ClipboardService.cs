using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ApiKeyVault.Core.Storage;

public interface IClipboardService
{
    void SetText(string text);
    string? GetText();
    void Clear();
    bool MatchesCurrent(byte[] expectedHash);
}

public static class ClipboardServiceFactory
{
    public static IClipboardService CreateDefault()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsClipboardService();
        }
        return new CommandLineClipboardService();
    }
}

public sealed class ClipboardUnavailableException : VaultException
{
    public ClipboardUnavailableException(string message) : base(message) { }
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

/// <summary>
/// Clipboard access on Linux and macOS via the platform's command-line tools:
/// wl-copy/wl-paste (Wayland), xclip or xsel (X11), pbcopy/pbpaste (macOS).
/// Secrets are always passed over stdin, never as arguments.
/// </summary>
public sealed class CommandLineClipboardService : IClipboardService
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(5);

    private readonly Lazy<ClipboardTool?> _tool = new(DetectTool);

    public void SetText(string text) => Run(RequireTool().Copy, input: text);

    public string? GetText()
    {
        var (exitCode, output) = Run(RequireTool().Paste, input: null, captureOutput: true);
        // wl-paste exits non-zero when the clipboard is empty.
        return exitCode == 0 && output.Length > 0 ? output : null;
    }

    public void Clear() => Run(RequireTool().Clear, input: RequireTool().ClearViaEmptyInput ? string.Empty : null);

    public bool MatchesCurrent(byte[] expectedHash)
    {
        string? current = GetText();
        if (current == null) return false;
        byte[] currentHash = SHA256.HashData(Encoding.UTF8.GetBytes(current));
        return CryptographicOperations.FixedTimeEquals(currentHash, expectedHash);
    }

    private ClipboardTool RequireTool() =>
        _tool.Value ?? throw new ClipboardUnavailableException(
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "Clipboard is unavailable: pbcopy/pbpaste were not found."
                : "Clipboard is unavailable: install wl-clipboard (Wayland) or xclip/xsel (X11).");

    private static ClipboardTool? DetectTool()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return HasExecutable("pbcopy") && HasExecutable("pbpaste")
                ? new ClipboardTool(["pbcopy"], ["pbpaste"], ["pbcopy"], ClearViaEmptyInput: true)
                : null;
        }

        bool wayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        bool x11 = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

        if (wayland && HasExecutable("wl-copy") && HasExecutable("wl-paste"))
        {
            // --sensitive hints clipboard history managers (e.g. cliphist) not to record the secret.
            string[] copy = WlCopySupportsSensitive() ? ["wl-copy", "--sensitive"] : ["wl-copy"];
            return new ClipboardTool(copy, ["wl-paste", "--no-newline"], ["wl-copy", "--clear"], ClearViaEmptyInput: false);
        }

        if (x11 && HasExecutable("xclip"))
        {
            return new ClipboardTool(
                ["xclip", "-selection", "clipboard", "-in"],
                ["xclip", "-selection", "clipboard", "-out"],
                ["xclip", "-selection", "clipboard", "-in"],
                ClearViaEmptyInput: true);
        }

        if (x11 && HasExecutable("xsel"))
        {
            return new ClipboardTool(
                ["xsel", "--clipboard", "--input"],
                ["xsel", "--clipboard", "--output"],
                ["xsel", "--clipboard", "--clear"],
                ClearViaEmptyInput: false);
        }

        return null;
    }

    private static bool WlCopySupportsSensitive()
    {
        try
        {
            var (_, output) = Run(["wl-copy", "--help"], input: null, captureOutput: true);
            return output.Contains("--sensitive", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasExecutable(string name)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return false;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, name)));
    }

    private static (int ExitCode, string Output) Run(string[] command, string? input, bool captureOutput = false)
    {
        var startInfo = new ProcessStartInfo(command[0])
        {
            UseShellExecute = false,
            RedirectStandardInput = input != null,
            // Copy tools (wl-copy, xclip) fork a background process that keeps serving the selection.
            // Redirecting their stdout/stderr would leave our pipes held open by that child, so only
            // capture output for tools that exit straight away.
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
        };
        foreach (string arg in command.Skip(1))
        {
            startInfo.ArgumentList.Add(arg);
        }
        if (captureOutput)
        {
            startInfo.StandardOutputEncoding = new UTF8Encoding(false);
        }

        using var process = Process.Start(startInfo)
            ?? throw new ClipboardUnavailableException($"Failed to start '{command[0]}'.");

        if (input != null)
        {
            using (var stdin = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)))
            {
                stdin.Write(input);
            }
        }

        string output = string.Empty;
        if (captureOutput)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!Task.WaitAll([stdoutTask, stderrTask], ToolTimeout))
            {
                TryKill(process);
                throw new ClipboardUnavailableException($"'{command[0]}' timed out.");
            }
            output = stdoutTask.Result;
        }

        if (!process.WaitForExit(ToolTimeout))
        {
            TryKill(process);
            throw new ClipboardUnavailableException($"'{command[0]}' timed out.");
        }

        if (!captureOutput && process.ExitCode != 0)
        {
            throw new ClipboardUnavailableException($"'{command[0]}' failed with exit code {process.ExitCode}.");
        }

        return (process.ExitCode, output);
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }

    private sealed record ClipboardTool(string[] Copy, string[] Paste, string[] Clear, bool ClearViaEmptyInput);
}
