using System.Diagnostics;

namespace BdvEngine.Gui;

/// <summary>
/// Cross-platform system clipboard via shell-out — pbcopy/pbpaste on macOS,
/// xclip/xsel on Linux, clip/PowerShell on Windows. No NuGet dependency.
/// Calls are synchronous; failures swallow silently and return empty/no-op.
/// </summary>
public static class Clipboard
{
    public static void SetText(string text)
    {
        try
        {
            if (OperatingSystem.IsMacOS())   Run("pbcopy",        "", text);
            else if (OperatingSystem.IsLinux()) Run("xclip", "-selection clipboard", text);
            else if (OperatingSystem.IsWindows()) Run("clip", "", text);
        }
        catch { /* clipboard failures are non-fatal */ }
    }

    public static string GetText()
    {
        try
        {
            if (OperatingSystem.IsMacOS())   return Capture("pbpaste", "");
            if (OperatingSystem.IsLinux())   return Capture("xclip", "-selection clipboard -o");
            if (OperatingSystem.IsWindows()) return Capture("powershell", "-NoProfile -Command Get-Clipboard").TrimEnd('\r', '\n');
        }
        catch { }
        return "";
    }

    private static void Run(string exe, string args, string stdin)
    {
        var psi = new ProcessStartInfo(exe, args) { RedirectStandardInput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        p.StandardInput.Write(stdin);
        p.StandardInput.Close();
        p.WaitForExit(500);
    }

    private static string Capture(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        string s = p.StandardOutput.ReadToEnd();
        p.WaitForExit(500);
        return s;
    }
}
