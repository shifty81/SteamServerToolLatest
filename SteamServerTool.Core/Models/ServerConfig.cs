using System.Text.Json.Serialization;

namespace SteamServerTool.Core.Models;

public class ServerConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("appId")]
    public int AppId { get; set; }

    [JsonPropertyName("dir")]
    public string Dir { get; set; } = "";

    [JsonPropertyName("executable")]
    public string Executable { get; set; } = "";

    [JsonPropertyName("launchArgs")]
    public string LaunchArgs { get; set; } = "";

    [JsonPropertyName("rcon")]
    public RconConfig Rcon { get; set; } = new();

    [JsonPropertyName("mods")]
    public List<long> Mods { get; set; } = new();

    [JsonPropertyName("disabledMods")]
    public List<long> DisabledMods { get; set; } = new();

    [JsonPropertyName("backupFolder")]
    public string BackupFolder { get; set; } = "";

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("discordWebhookUrl")]
    public string DiscordWebhookUrl { get; set; } = "";

    [JsonPropertyName("autoUpdate")]
    public bool AutoUpdate { get; set; } = false;

    [JsonPropertyName("autoStartOnLaunch")]
    public bool AutoStartOnLaunch { get; set; } = false;

    [JsonPropertyName("favorite")]
    public bool Favorite { get; set; } = false;

    [JsonPropertyName("keepBackups")]
    public int KeepBackups { get; set; } = 10;

    [JsonPropertyName("backupIntervalMinutes")]
    public int BackupIntervalMinutes { get; set; } = 0;

    [JsonPropertyName("restartIntervalHours")]
    public int RestartIntervalHours { get; set; } = 0;

    [JsonPropertyName("scheduledRconCommands")]
    public List<ScheduledRconCommand> ScheduledRconCommands { get; set; } = new();

    [JsonPropertyName("maxPlayers")]
    public int MaxPlayers { get; set; } = 0;

    [JsonPropertyName("restartWarningMinutes")]
    public int RestartWarningMinutes { get; set; } = 0;

    [JsonPropertyName("restartWarningMessage")]
    public string RestartWarningMessage { get; set; } = "";

    [JsonPropertyName("cpuAlertThreshold")]
    public double CpuAlertThreshold { get; set; } = 90.0;

    [JsonPropertyName("memAlertThresholdMB")]
    public long MemAlertThresholdMB { get; set; } = 0;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("group")]
    public string Group { get; set; } = "";

    [JsonPropertyName("startupPriority")]
    public int StartupPriority { get; set; } = 0;

    [JsonPropertyName("backupBeforeRestart")]
    public bool BackupBeforeRestart { get; set; } = false;

    [JsonPropertyName("gracefulShutdownSeconds")]
    public int GracefulShutdownSeconds { get; set; } = 15;

    [JsonPropertyName("environmentVariables")]
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    [JsonPropertyName("autoUpdateCheckIntervalMinutes")]
    public int AutoUpdateCheckIntervalMinutes { get; set; } = 60;

    [JsonPropertyName("queryPort")]
    public int QueryPort { get; set; } = 0;

    [JsonPropertyName("totalUptimeSeconds")]
    public long TotalUptimeSeconds { get; set; } = 0;

    [JsonPropertyName("totalCrashes")]
    public int TotalCrashes { get; set; } = 0;

    [JsonPropertyName("lastCrashTime")]
    public string LastCrashTime { get; set; } = "";

    [JsonPropertyName("isRemote")]
    public bool IsRemote { get; set; } = false;

    /// <summary>
    /// Identifies the install/download mechanism for this server.
    /// Set automatically when a template is applied.
    /// Well-known values: "" or "steamcmd" (default), "minecraft", "vintagestory".
    /// </summary>
    [JsonPropertyName("serverType")]
    public string ServerType { get; set; } = "";

    public (bool IsValid, string Error) Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return (false, "Name is required.");

        var rconPort = Rcon?.Port ?? 0;

        if (IsRemote)
        {
            // Remote servers only need a valid RCON port to connect.
            if (rconPort is < 1 or > 65535) return (false, "RCON port must be between 1 and 65535.");
            return (true, "");
        }

        // Local servers
        if (AppId < 0) return (false, "AppID must not be negative.");
        if (string.IsNullOrWhiteSpace(Dir)) return (false, "Server directory is required.");
        if (string.IsNullOrWhiteSpace(Executable)) return (false, "Executable is required.");
        if (rconPort is < 1 or > 65535) return (false, "RCON port must be between 1 and 65535.");
        return (true, "");
    }
}
