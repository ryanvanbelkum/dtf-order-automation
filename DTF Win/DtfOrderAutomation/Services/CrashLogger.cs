using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DtfOrderAutomation.Services;

internal static class CrashLogger
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, int type);

    public static void LogAndShow(Exception? ex)
    {
        if (ex is null) return;

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DtfOrderAutomation");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"{DateTime.Now:O}\n{ex}\n\n");
        }
        catch
        {
            // Best-effort logging only — don't let a logging failure mask the original crash.
        }

        MessageBox(IntPtr.Zero, ex.ToString(), "DTF Order Automation — Unexpected Error", 0x10);
    }
}
