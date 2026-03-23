using System.Diagnostics;
using System.Net.Http;
using SteamServerTool.Core.Models;

namespace SteamServerTool.Core.Services;

public class SteamCmdService
{
    // Official Valve SteamCMD download URLs
    private const string WindowsDownloadUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
    private const string LinuxDownloadUrl   = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";

    public string SteamCmdPath { get; set; } = "steamcmd";

    /// <summary>
    /// Returns true when the configured SteamCMD executable can be found on disk or in PATH.
    /// </summary>
    public bool IsSteamCmdInstalled()
    {
        // Check absolute/relative path first
        if (File.Exists(SteamCmdPath)) return true;

        // Check PATH
        var envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var ext     = OperatingSystem.IsWindows() ? ".exe" : "";
        var exeName = Path.GetFileName(SteamCmdPath) is { Length: > 0 } n ? n : "steamcmd";
        if (!exeName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            exeName += ext;

        return envPath
            .Split(Path.PathSeparator)
            .Any(dir => File.Exists(Path.Combine(dir, exeName)));
    }

    /// <summary>
    /// Downloads and extracts SteamCMD into <paramref name="installDir"/>.
    /// Sets <see cref="SteamCmdPath"/> to the newly installed executable on success.
    /// </summary>
    public async Task<bool> DownloadSteamCmdAsync(string installDir, IProgress<string>? progress = null)
    {
        try
        {
            Directory.CreateDirectory(installDir);

            bool isWindows = OperatingSystem.IsWindows();
            var url        = isWindows ? WindowsDownloadUrl : LinuxDownloadUrl;
            var archiveName = isWindows ? "steamcmd.zip" : "steamcmd_linux.tar.gz";
            var archivePath = Path.Combine(installDir, archiveName);

            AppLogger.Info($"Downloading SteamCMD from {url} to '{installDir}'.");
            progress?.Report($"Downloading SteamCMD from {url} …");

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "SteamServerTool/1.0");
                var bytes = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(archivePath, bytes);
            }

            AppLogger.Info("SteamCMD archive downloaded; extracting …");
            progress?.Report("Extracting SteamCMD (existing files will be overwritten) …");

            if (isWindows)
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, installDir, overwriteFiles: true);
                SteamCmdPath = Path.Combine(installDir, "steamcmd.exe");
            }
            else
            {
                // On Linux use tar
                var tar = new ProcessStartInfo("tar", $"-xzf \"{archivePath}\" -C \"{installDir}\"")
                {
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };
                using var proc = Process.Start(tar)!;
                await proc.WaitForExitAsync();
                SteamCmdPath = Path.Combine(installDir, "steamcmd.sh");
            }

            // Clean up archive
            try { File.Delete(archivePath); } catch { /* best effort */ }

            AppLogger.Info($"SteamCMD installed to '{installDir}'; executable: '{SteamCmdPath}'.");
            progress?.Report($"SteamCMD installed to: {installDir}");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SteamCMD download failed: {ex}");
            progress?.Report($"[ERROR] SteamCMD download failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> InstallOrUpdateServer(ServerConfig config, IProgress<string>? progress = null)
    {
        if (config.AppId <= 0)
        {
            AppLogger.Error($"InstallOrUpdateServer: invalid AppID ({config.AppId}) for '{config.Name}'.");
            progress?.Report("Error: Invalid AppID.");
            return false;
        }

        AppLogger.Info($"Starting SteamCMD install/update for '{config.Name}' (AppID {config.AppId}).");
        Directory.CreateDirectory(config.Dir);

        var args = BuildInstallArgs(config);
        return await RunSteamCmd(args, progress);
    }

    public async Task<bool> UpdateMod(ServerConfig config, long modId, IProgress<string>? progress = null)
    {
        if (config.AppId <= 0)
        {
            AppLogger.Error($"UpdateMod: invalid AppID ({config.AppId}) for '{config.Name}'.");
            progress?.Report("Error: Invalid AppID.");
            return false;
        }

        AppLogger.Info($"Downloading Workshop mod {modId} for AppID {config.AppId}.");
        var args = $"+login anonymous +workshop_download_item {config.AppId} {modId} +quit";
        return await RunSteamCmd(args, progress);
    }

    private string BuildInstallArgs(ServerConfig config)
    {
        return $"+login anonymous +force_install_dir \"{config.Dir}\" +app_update {config.AppId} validate +quit";
    }

    private async Task<bool> RunSteamCmd(string args, IProgress<string>? progress)
    {
        AppLogger.Info($"Running SteamCMD: {SteamCmdPath} {args}");
        var psi = new ProcessStartInfo
        {
            FileName = SteamCmdPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                AppLogger.Info($"[SteamCMD] {e.Data}");
                progress?.Report(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                AppLogger.Warn($"[SteamCMD ERR] {e.Data}");
                progress?.Report($"[ERR] {e.Data}");
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            var success = process.ExitCode == 0;
            AppLogger.Info($"SteamCMD finished with exit code {process.ExitCode} ({(success ? "OK" : "FAILED")}).");
            return success;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to run SteamCMD: {ex}");
            progress?.Report($"Error running steamcmd: {ex.Message}");
            return false;
        }
    }
}
