using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace DtfOrderAutomation.Services;

public record VersionInfo(string Version, string DownloadUrl, string ReleaseNotes);

public class UpdateService : IDisposable
{
    // version.json lives at the repo root; hosted on GitHub main branch
    private const string VersionUrl =
        "https://raw.githubusercontent.com/ryanvanbelkum/dtf-order-automation/main/version.json";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Returns a <see cref="VersionInfo"/> if a newer version is available, otherwise null.
    /// </summary>
    public async Task<VersionInfo?> CheckForUpdateAsync(string currentVersion)
    {
        try
        {
            var json    = await _http.GetStringAsync(VersionUrl);
            using var doc = JsonDocument.Parse(json);
            var root    = doc.RootElement;

            var latest  = root.GetProperty("version").GetString()!.Trim();
            var url     = root.GetProperty("download_url").GetString()!.Trim();
            var notes   = root.TryGetProperty("release_notes", out var rn) ? rn.GetString() ?? "" : "";

            return new Version(latest) > new Version(currentVersion)
                ? new VersionInfo(latest, url, notes)
                : null;
        }
        catch
        {
            // Non-fatal: silently ignore network errors on update check
            return null;
        }
    }

    /// <summary>
    /// Downloads the installer from <paramref name="downloadUrl"/>, runs it silently to
    /// update the app in place, then relaunches the app and exits the current process.
    ///
    /// The customer never reinstalls manually: the existing install is overwritten and
    /// the app reopens. User data (settings, mappings, history) lives under
    /// %LocalAppData%\DtfOrderAutomation and is untouched by the installer.
    /// </summary>
    public async Task DownloadAndInstallAsync(string downloadUrl, IProgress<int>? progress = null)
    {
        // Preserve the artifact's real extension so it's executed correctly.
        var ext = Path.GetExtension(new Uri(downloadUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".exe";
        var tempPath = Path.Combine(Path.GetTempPath(), $"DtfSetup{ext}");

        using (var resp = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength ?? -1L;

            await using var stream = await resp.Content.ReadAsStreamAsync();
            await using var file   = File.Create(tempPath);

            var buffer      = new byte[81920];
            long downloaded = 0;
            int  read;

            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (total > 0) progress?.Report((int)(downloaded * 100 / total));
            }

            await file.FlushAsync();
        }

        var appExe = Environment.ProcessPath
                     ?? Process.GetCurrentProcess().MainModule?.FileName;

        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) && appExe is not null)
        {
            // Run the installer silently, then relaunch the (overwritten) app. This runs
            // via a small helper script rather than a single chained `cmd /c "a && b"`
            // string: with multiple nested quoted paths, cmd's quote handling for that
            // pattern is unreliable and can silently do nothing. A short delay is also
            // needed before the install step — this process may not have fully released
            // its file locks on the exe/DLLs by the time Application.Exit() returns,
            // which would otherwise make the installer fail to overwrite them.
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DtfOrderAutomation");
            Directory.CreateDirectory(dataDir);
            var logPath = Path.Combine(dataDir, "update.log");
            var batPath = Path.Combine(Path.GetTempPath(), "dtf_update.bat");

            var script =
                "@echo off\r\n" +
                // timeout.exe requires an interactive console and fails instantly
                // ("Input redirection is not supported") when launched with no window,
                // which is exactly how this script is started — collapsing the intended
                // delay to zero and bringing back the file-lock race. ping against
                // localhost is the standard no-console-needed way to sleep in a batch
                // file; 3 pings ≈ 2 seconds.
                "ping -n 3 127.0.0.1 >nul\r\n" +
                $"\"{tempPath}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART\r\n" +
                "if %ERRORLEVEL% EQU 0 (\r\n" +
                $"  start \"\" \"{appExe}\"\r\n" +
                ") else (\r\n" +
                $"  echo %date% %time% - update install failed, exit code %ERRORLEVEL% >> \"{logPath}\"\r\n" +
                ")\r\n" +
                "del \"%~f0\"\r\n";
            File.WriteAllText(batPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName        = "cmd.exe",
                ArgumentList    = { "/c", batPath },
                UseShellExecute = false,
                CreateNoWindow  = true,
            });
        }
        else
        {
            // Fallback for non-exe artifacts: just open it and let Windows/the user drive.
            Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
        }

        // Exit so the installer can overwrite the running files.
        Application.Current.Exit();
    }

    public void Dispose() => _http.Dispose();
}
