using System.Diagnostics;

namespace ServiceMap.App.Services;

/// <summary>Small OS-shell conveniences for the GUI.</summary>
public static class ShellHelper
{
    /// <summary>
    /// Controlled by Settings ("Open folder after export"). Static so every
    /// view model shares the current value without plumbing.
    /// </summary>
    public static bool OpenAfterExport { get; set; } = true;

    /// <summary>
    /// After a successful export, open an Explorer window with the file
    /// selected. Best-effort and Windows-only; never throws.
    /// </summary>
    public static void RevealAfterExport(string path)
    {
        if (!OpenAfterExport) return;
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{Path.GetFullPath(path)}\"",
                UseShellExecute = true
            });
        }
        catch { /* revealing the file is a nicety, never an error */ }
    }
}
