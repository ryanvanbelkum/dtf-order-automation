using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

internal class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, int type);

    [STAThread]
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException +=
            (s, e) => DtfOrderAutomation.Services.CrashLogger.LogAndShow(e.ExceptionObject as Exception);

        // No Bootstrap.Initialize() call: this app is self-contained
        // (WindowsAppSDKSelfContained=true, WindowsPackageType=None, OutputType=WinExe),
        // so the Windows App SDK auto-initializes UndockedRegFreeWinRT support at startup.
        // Bootstrap.Initialize() is for framework-dependent deployment, where it looks for
        // an installed/registered Windows App Runtime — on a dev machine with Visual
        // Studio's WinAppSDK tooling that's present, so it quietly succeeds there, but on a
        // clean machine it fails via an uncatchable STATUS_FAIL_FAST_EXCEPTION instead of a
        // normal exception.
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
    }
}
