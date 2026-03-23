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

            progress?.Report($"Downloading SteamCMD from {url} …");

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "SteamServerTool/1.0");
                var bytes = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(archivePath, bytes);
            }

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

            progress?.Report($"SteamCMD installed to: {installDir}");
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report($"[ERROR] SteamCMD download failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> InstallOrUpdateServer(ServerConfig config, IProgress<string>? progress = null)
    {
        if (config.AppId <= 0)
        {
            progress?.Report("Error: Invalid AppID.");
            return false;
        }

        Directory.CreateDirectory(config.Dir);

        var args = BuildInstallArgs(config);
        return await RunSteamCmd(args, progress);
    }

    public async Task<bool> UpdateMod(ServerConfig config, long modId, IProgress<string>? progress = null)
    {
        if (config.AppId <= 0)
        {
            progress?.Report("Error: Invalid AppID.");
            return false;
        }

        var args = $"+login anonymous +workshop_download_item {config.AppId} {modId} +quit";
        return await RunSteamCmd(args, progress);
    }

    private string BuildInstallArgs(ServerConfig config)
    {
        return $"+login anonymous +force_install_dir \"{config.Dir}\" +app_update {config.AppId} validate +quit";
    }

    private async Task<bool> RunSteamCmd(string args, IProgress<string>? progress)
    {
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
            if (e.Data != null) progress?.Report(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) progress?.Report($"[ERR] {e.Data}");
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            progress?.Report($"Error running steamcmd: {ex.Message}");
            return false;
        }
    }
}
