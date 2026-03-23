using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamServerTool.Core.Services;

/// <summary>
/// Downloads the official Vintage Story dedicated server from the Anego Studios CDN.
/// No SteamCMD required — uses the public <c>http://api.vintagestory.at/stable.json</c> API
/// to discover the latest stable version and its download URL.
/// </summary>
public class VintageStoryService
{
    private const string StableApiUrl = "http://api.vintagestory.at/stable.json";

    // Shared client avoids socket exhaustion.
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        DefaultRequestHeaders = { { "User-Agent", "SteamServerTool/1.0" } }
    };

    /// <summary>
    /// Downloads and extracts the latest stable Vintage Story server into
    /// <paramref name="installDir"/>.
    /// </summary>
    public async Task<bool> DownloadServerAsync(string installDir, IProgress<string>? progress = null)
    {
        try
        {
            Directory.CreateDirectory(installDir);

            // ── 1. Fetch stable.json to get the latest version & download URL ──
            progress?.Report("Fetching Vintage Story version info from api.vintagestory.at …");
            var apiJson = await Http.GetStringAsync(StableApiUrl);

            // The JSON is an object keyed by version string; take the first (newest) entry.
            using var doc      = JsonDocument.Parse(apiJson);
            var root           = doc.RootElement;
            var firstProp      = root.EnumerateObject().FirstOrDefault();
            var version        = firstProp.Name;

            if (string.IsNullOrEmpty(version))
                throw new InvalidDataException("No versions found in stable.json.");

            progress?.Report($"Latest stable version: {version}");

            // ── 2. Resolve download URL ───────────────────────────────────────
            bool isWindows  = OperatingSystem.IsWindows();
            string downloadUrl;

            if (isWindows)
            {
                // Try windowsserver.urls.cdn first; fall back to constructed CDN URL.
                downloadUrl = TryGetString(firstProp.Value, "windowsserver", "urls", "cdn")
                              ?? $"https://cdn.vintagestory.at/gamefiles/stable/vs_server_win-x64_{version}.zip";
            }
            else
            {
                downloadUrl = TryGetString(firstProp.Value, "linuxserver", "urls", "cdn")
                              ?? $"https://cdn.vintagestory.at/gamefiles/stable/vs_server_linux-x64_{version}.tar.gz";
            }

            progress?.Report($"Download URL: {downloadUrl}");

            // ── 3. Download archive to a temp file ───────────────────────────
            var archiveName = isWindows ? $"vs_server_{version}.zip" : $"vs_server_{version}.tar.gz";
            var archivePath = Path.Combine(Path.GetTempPath(), archiveName);

            progress?.Report($"Downloading Vintage Story server …");
            await using (var stream = await Http.GetStreamAsync(downloadUrl))
            await using (var file   = File.Create(archivePath))
                await stream.CopyToAsync(file);

            progress?.Report("Download complete. Extracting …");

            // ── 4. Extract ────────────────────────────────────────────────────
            if (isWindows)
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, installDir, overwriteFiles: true);
            }
            else
            {
                var tar = new ProcessStartInfo("tar", $"-xzf \"{archivePath}\" -C \"{installDir}\"")
                {
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };
                using var proc = Process.Start(tar)!;
                await proc.WaitForExitAsync();
                if (proc.ExitCode != 0)
                    throw new InvalidOperationException($"tar exited with code {proc.ExitCode}.");
            }

            // ── 5. Clean up archive ───────────────────────────────────────────
            try { File.Delete(archivePath); } catch { /* best effort */ }

            progress?.Report($"Vintage Story server {version} deployed to: {installDir}");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Vintage Story server download failed: {ex}");
            progress?.Report($"[ERROR] Vintage Story download failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Navigates a chain of JSON object keys and returns the string value, or null.</summary>
    private static string? TryGetString(JsonElement element, params string[] keys)
    {
        var cur = element;
        foreach (var key in keys)
        {
            if (cur.ValueKind != JsonValueKind.Object) return null;
            if (!cur.TryGetProperty(key, out cur)) return null;
        }
        return cur.ValueKind == JsonValueKind.String ? cur.GetString() : null;
    }
}
