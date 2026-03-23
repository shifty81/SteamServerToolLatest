using System.Diagnostics;
using SteamServerTool.Core.Models;

namespace SteamServerTool.Core.Services;

public class SteamCmdService
{
    public string SteamCmdPath { get; set; } = "steamcmd";

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
