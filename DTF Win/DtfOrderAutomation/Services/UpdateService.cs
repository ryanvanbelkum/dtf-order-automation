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
    /// Downloads the MSIX installer from <paramref name="downloadUrl"/> and launches it,
    /// then exits the current process so Windows can replace the package.
    /// </summary>
    public async Task DownloadAndInstallAsync(string downloadUrl, IProgress<int>? progress = null)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "DtfSetup.msix");

        using var resp = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1L;

        await using var stream = await resp.Content.ReadAsStreamAsync();
        await using var file   = File.Create(tempPath);

        var buffer     = new byte[81920];
        long downloaded = 0;
        int  read;

        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read));
            downloaded += read;
            if (total > 0) progress?.Report((int)(downloaded * 100 / total));
        }

        // Close before launching
        await file.FlushAsync();
        file.Close();

        // Open the MSIX — Windows will prompt the user to install/update
        Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });

        // Exit so the package can be replaced
        Application.Current.Exit();
    }

    public void Dispose() => _http.Dispose();
}
