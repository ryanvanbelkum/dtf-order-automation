using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

internal class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, int type);

    [STAThread]
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException +=
            (s, e) => DtfOrderAutomation.Services.CrashLogger.LogAndShow(e.ExceptionObject as Exception);

        // Bootstrap the Windows App SDK runtime for unpackaged apps (version 1.6.x)
        try
        {
            Bootstrap.Initialize(0x00010008); // minimum Windows App SDK 1.8
        }
        catch (Exception ex)
        {
            MessageBox(IntPtr.Zero,
                $"Could not initialize Windows App SDK 1.8.\n\n{ex.Message}\n\n" +
                "Make sure the Windows App SDK runtime is installed.",
                "DTF Order Automation — Startup Error", 0x10);
            return;
        }

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(p =>
            {
                var ctx = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                System.Threading.SynchronizationContext.SetSynchronizationContext(ctx);
                _ = new DtfOrderAutomation.App();
            });
        }
        catch (Exception ex)
        {
            MessageBox(IntPtr.Zero, $"Fatal error:\n{ex}", "DTF Order Automation", 0x10);
        }
        finally
        {
            Bootstrap.Shutdown();
        }
    }
}
