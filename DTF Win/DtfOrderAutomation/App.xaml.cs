using System;
using System.Threading;
using System.Threading.Tasks;
using DtfOrderAutomation.Models;
using DtfOrderAutomation.Services;
using Microsoft.UI.Xaml;

namespace DtfOrderAutomation;

public partial class App : Application
{
    // ── Singletons ─────────────────────────────────────────────────────────
    public static ConfigService    ConfigService    { get; } = new();
    public static LogService       LogService       { get; } = new();
    public static MappingService   MappingService   { get; } = new();
    public static ShopifyService   ShopifyService   { get; private set; } = null!;
    public static AutomationService AutomationService { get; private set; } = null!;
    public static SchedulerService SchedulerService { get; } = new();
    public static UpdateService    UpdateService    { get; } = new();

    public static AppConfig Config    { get; private set; } = null!;
    public static AppState  State     { get; }              = new();
    public static MainWindow Window   { get; private set; } = null!;
    public static IntPtr    WindowHandle { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Load persisted data
        Config = ConfigService.Load();
        State.Log.AddRange(LogService.Load());

        // Wire up services
        ShopifyService    = new ShopifyService(ConfigService);
        AutomationService = new AutomationService(ShopifyService, MappingService);

        // Wire scheduler → run engine
        SchedulerService.RunRequested += (_, _) => _ = RunAutomationAsync();
        SchedulerService.SetSchedule(Config.ScheduleEnabled, Config.IntervalHours);

        // Create main window (hidden until ready)
        Window = new MainWindow();
        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(Window);
        Window.Activate();

        // Check for updates in the background
        _ = CheckForUpdateAsync();
    }

    // ── Automation run ─────────────────────────────────────────────────────

    public static async Task RunAutomationAsync()
    {
        if (State.IsRunning) return;

        var cts = new CancellationTokenSource();
        State.BeginRun(cts);

        RunResult result;
        try
        {
            result = await AutomationService.RunAsync(
                Config,
                line => State.EmitLog(line),
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            result = new RunResult
            {
                Timestamp = DateTime.Now.ToString("O"),
                Status    = "stopped",
                SkippedDetails = new() { new() { OrderId = "—", Reason = "Stopped by user" } },
            };
            State.EmitLog("\n⚠ Stopped by user.");
        }
        catch (Exception ex)
        {
            result = new RunResult
            {
                Timestamp = DateTime.Now.ToString("O"),
                Status    = "error",
                SkippedDetails = new() { new() { OrderId = "—", Reason = $"Unexpected error: {ex.Message}" } },
            };
            State.EmitLog($"\n✗ Unexpected error: {ex.Message}");
        }

        // Persist
        Config.LastRun = result.Timestamp;
        ConfigService.Save(Config);
        LogService.Append(State.Log, result);

        // Reset scheduler countdown
        SchedulerService.ResetAfterRun();

        State.EndRun(result);
    }

    // ── Auto-updater ───────────────────────────────────────────────────────

    private static async Task CheckForUpdateAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5)); // let the UI settle first
        var info = await UpdateService.CheckForUpdateAsync(AppVersion.Current);
        if (info is null) return;

        // Marshal to UI thread
        Window.DispatcherQueue.TryEnqueue(() => Window.ShowUpdatePrompt(info));
    }
}
