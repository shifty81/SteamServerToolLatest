namespace SteamServerTool.Core.Models;

public class ServerTemplate
{
    public string Name             { get; set; } = "";
    public string Icon             { get; set; } = "🎮";
    public string Description      { get; set; } = "";
    public int    AppId            { get; set; }
    public string Executable       { get; set; } = "";
    public string LaunchArgs       { get; set; } = "";
    /// <summary>
    /// Subfolder name only (e.g. "ark_sa").  The UI resolves this against the
    /// user's base Servers directory at template-apply time.
    /// </summary>
    public string DefaultDir       { get; set; } = "";
    public int    RconPort         { get; set; } = 27020;
    public int    QueryPort        { get; set; } = 27015;
    public int    MaxPlayers       { get; set; } = 0;
    public string Group            { get; set; } = "";
    public string RconHost         { get; set; } = "127.0.0.1";
    /// <summary>
    /// When false the server is not installed/updated via SteamCMD (e.g. Minecraft Java).
    /// </summary>
    public bool   RequiresSteamCmd { get; set; } = true;

    /// <summary>
    /// Optional subdirectory (relative to the install directory) that the Config File
    /// editor should scan.  Empty means scan the whole install directory.
    /// </summary>
    public string ConfigDir        { get; set; } = "";

    /// <summary>
    /// Console command to send via stdin for a graceful save-and-stop sequence.
    /// Empty means fall back to the default CloseMainWindow() behaviour.
    /// </summary>
    public string StdinStopCommand { get; set; } = "";
}

/// <summary>Built-in server template library.</summary>
public static class ServerTemplates
{
    public static readonly IReadOnlyList<ServerTemplate> All = new List<ServerTemplate>
    {
        new()
        {
            Name        = "ARK: Survival Ascended",
            Icon        = "🦕",
            Description = "ARK SA dedicated server (App ID 2430930)",
            AppId       = 2430930,
            Executable  = "ShooterGameServer",
            LaunchArgs  = "TheIsland_WP?listen?MaxPlayers=70 -server -log",
            DefaultDir  = "ark_sa",
            RconPort    = 27020,
            QueryPort   = 27015,
            MaxPlayers  = 70,
        },
        new()
        {
            Name        = "ARK: Survival Evolved",
            Icon        = "🦕",
            Description = "ARK SE dedicated server (App ID 376030)",
            AppId       = 376030,
            Executable  = "ShooterGameServer",
            LaunchArgs  = "TheIsland?listen?MaxPlayers=70 -server -log",
            DefaultDir  = "ark_se",
            RconPort    = 27020,
            QueryPort   = 27015,
            MaxPlayers  = 70,
        },
        new()
        {
            Name        = "Counter-Strike 2",
            Icon        = "🔫",
            Description = "CS2 dedicated server (App ID 730)",
            AppId       = 730,
            Executable  = "cs2",
            LaunchArgs  = "-dedicated +map de_dust2 +maxplayers 20",
            DefaultDir  = "cs2",
            RconPort    = 27015,
            QueryPort   = 27015,
            MaxPlayers  = 20,
        },
        new()
        {
            Name        = "Valheim",
            Icon        = "⚔️",
            Description = "Valheim dedicated server (App ID 896660)",
            AppId       = 896660,
            Executable  = "valheim_server",
            LaunchArgs  = "-name \"My Server\" -port 2456 -world \"Dedicated\" -password \"secret\" -nographics -batchmode",
            DefaultDir  = "valheim",
            RconPort    = 2457,
            QueryPort   = 2456,
            MaxPlayers  = 10,
        },
        new()
        {
            Name        = "Rust",
            Icon        = "🏗️",
            Description = "Rust dedicated server (App ID 258550)",
            AppId       = 258550,
            Executable  = "RustDedicated",
            LaunchArgs  = "-batchmode +server.hostname \"My Rust Server\" +server.maxplayers 100 +server.worldsize 3500 +rcon.web 1 +rcon.port 28016",
            DefaultDir  = "rust",
            RconPort    = 28016,
            QueryPort   = 28015,
            MaxPlayers  = 100,
        },
        new()
        {
            Name        = "7 Days to Die",
            Icon        = "🧟",
            Description = "7DTD dedicated server (App ID 294420)",
            AppId       = 294420,
            Executable  = "7DaysToDieServer",
            LaunchArgs  = "-configfile=serverconfig.xml -dedicated",
            DefaultDir  = "7dtd",
            RconPort    = 8081,
            QueryPort   = 26900,
            MaxPlayers  = 8,
        },
        new()
        {
            Name             = "Minecraft Java",
            Icon             = "⛏️",
            Description      = "Minecraft Java Edition server (requires JRE — no SteamCMD)",
            AppId            = 0,
            Executable       = "java",
            LaunchArgs       = "-Xmx4G -Xms2G -jar server.jar nogui",
            DefaultDir       = "minecraft",
            RconPort         = 25575,
            QueryPort        = 25565,
            MaxPlayers       = 20,
            RequiresSteamCmd = false,
            StdinStopCommand = "stop",
        },
        new()
        {
            Name        = "Enshrouded",
            Icon        = "🌫️",
            Description = "Enshrouded dedicated server (App ID 2278520)",
            AppId       = 2278520,
            Executable  = "enshrouded_server",
            LaunchArgs  = "",
            DefaultDir  = "enshrouded",
            RconPort    = 11002,
            QueryPort   = 15636,
            MaxPlayers  = 16,
        },
        new()
        {
            Name        = "V Rising",
            Icon        = "🧛",
            Description = "V Rising dedicated server (App ID 1829350)",
            AppId       = 1829350,
            Executable  = "VRisingServer",
            LaunchArgs  = "-persistentDataPath .\\save-data -serverName \"My V Rising Server\"",
            DefaultDir  = "vrising",
            RconPort    = 25575,
            QueryPort   = 9876,
            MaxPlayers  = 40,
        },
        new()
        {
            Name        = "Palworld",
            Icon        = "🐾",
            Description = "Palworld dedicated server (App ID 2394010)",
            AppId       = 2394010,
            Executable  = "PalServer",
            LaunchArgs  = "-port=8211 -useperfthreads -NoAsyncLoadingThread -UseMultithreadForDS",
            DefaultDir  = "palworld",
            RconPort    = 25575,
            QueryPort   = 8211,
            MaxPlayers  = 32,
        },
        new()
        {
            Name             = "Vintage Story",
            Icon             = "🪨",
            Description      = "Vintage Story dedicated server (no SteamCMD — downloaded from vintagestory.at)",
            AppId            = 0,
            Executable       = OperatingSystem.IsWindows() ? "VintagestoryServer.exe" : "VintagestoryServer",
            LaunchArgs       = "--dataPath ./data --port 42420 --maxclients 16",
            DefaultDir       = "vintagestory",
            RconPort         = 42425,
            QueryPort        = 42420,
            MaxPlayers       = 16,
            RequiresSteamCmd = false,
            ConfigDir        = "data",
            StdinStopCommand = "/stop",
        },
        new()
        {
            Name        = "Custom Server",
            Icon        = "⚙️",
            Description = "Start with a blank configuration",
            AppId       = 0,
            Executable  = "",
            LaunchArgs  = "",
            DefaultDir  = "",
            RconPort    = 27020,
            QueryPort   = 27015,
            MaxPlayers  = 0,
        },
        new()
        {
            Name        = "Team Fortress 2",
            Icon        = "🎩",
            Description = "TF2 dedicated server (App ID 232250)",
            AppId       = 232250,
            Executable  = OperatingSystem.IsWindows() ? "srcds.exe" : "srcds_run",
            LaunchArgs  = "-game tf +maxplayers 24 +map cp_dustbowl",
            DefaultDir  = "tf2",
            RconPort    = 27015,
            QueryPort   = 27015,
            MaxPlayers  = 24,
        },
        new()
        {
            Name        = "Garry's Mod",
            Icon        = "🔧",
            Description = "Garry's Mod dedicated server (App ID 4020)",
            AppId       = 4020,
            Executable  = OperatingSystem.IsWindows() ? "srcds.exe" : "srcds_run",
            LaunchArgs  = "-game garrysmod +maxplayers 16 +map gm_flatgrass",
            DefaultDir  = "gmod",
            RconPort    = 27015,
            QueryPort   = 27015,
            MaxPlayers  = 16,
        },
        new()
        {
            Name        = "Don't Starve Together",
            Icon        = "🌙",
            Description = "DST dedicated server (App ID 343050)",
            AppId       = 343050,
            Executable  = OperatingSystem.IsWindows() ? "dontstarve_dedicated_server_nullrenderer_x64.exe"
                                                       : "dontstarve_dedicated_server_nullrenderer_x64",
            LaunchArgs  = "-cluster MyCluster -shard Master",
            DefaultDir  = "dst",
            RconPort    = 27016,
            QueryPort   = 10999,
            MaxPlayers  = 6,
        },
        new()
        {
            Name        = "Project Zomboid",
            Icon        = "🧟",
            Description = "Project Zomboid dedicated server (App ID 108600)",
            AppId       = 108600,
            Executable  = OperatingSystem.IsWindows() ? "StartServer64.bat" : "start-server.sh",
            LaunchArgs  = "-servername MyServer",
            DefaultDir  = "projectzomboid",
            RconPort    = 27015,
            QueryPort   = 16261,
            MaxPlayers  = 32,
        },
        new()
        {
            Name        = "Space Engineers",
            Icon        = "🚀",
            Description = "Space Engineers dedicated server (App ID 298740)",
            AppId       = 298740,
            Executable  = "SpaceEngineersDedicated",
            LaunchArgs  = "",
            DefaultDir  = "spaceengineers",
            RconPort    = 27016,
            QueryPort   = 27016,
            MaxPlayers  = 16,
        },
        new()
        {
            Name        = "Left 4 Dead 2",
            Icon        = "🧟",
            Description = "L4D2 dedicated server (App ID 222860)",
            AppId       = 222860,
            Executable  = OperatingSystem.IsWindows() ? "srcds.exe" : "srcds_run",
            LaunchArgs  = "-game left4dead2 +maxplayers 8 +map c1m1_hotel",
            DefaultDir  = "l4d2",
            RconPort    = 27015,
            QueryPort   = 27015,
            MaxPlayers  = 8,
        },
        new()
        {
            Name        = "Squad",
            Icon        = "🎖️",
            Description = "Squad dedicated server (App ID 403240)",
            AppId       = 403240,
            Executable  = OperatingSystem.IsWindows() ? "SquadGameServer.exe" : "SquadGameServer",
            LaunchArgs  = "Port=7787 QueryPort=27165 MULTIHOME=0.0.0.0 MaxPlayers=80 log",
            DefaultDir  = "squad",
            RconPort    = 21114,
            QueryPort   = 27165,
            MaxPlayers  = 80,
        },
        new()
        {
            Name        = "Terraria (TShock)",
            Icon        = "⛏️",
            Description = "Terraria/TShock server (App ID 105600)",
            AppId       = 105600,
            Executable  = OperatingSystem.IsWindows() ? "TerrariaServer.exe" : "TerrariaServer",
            LaunchArgs  = "-port 7777 -maxplayers 16 -world ./worlds/World1.wld",
            DefaultDir  = "terraria",
            RconPort    = 7777,
            QueryPort   = 7777,
            MaxPlayers  = 16,
        },
        new()
        {
            Name        = "Conan Exiles",
            Icon        = "⚔️",
            Description = "Conan Exiles dedicated server (App ID 443030)",
            AppId       = 443030,
            Executable  = "ConanSandboxServer",
            LaunchArgs  = "-MaxPlayers=40 -nosteamclient -game -server -log",
            DefaultDir  = "conanexiles",
            RconPort    = 25575,
            QueryPort   = 27015,
            MaxPlayers  = 40,
        },
        new()
        {
            Name        = "Satisfactory",
            Icon        = "🏭",
            Description = "Satisfactory dedicated server (App ID 1690800)",
            AppId       = 1690800,
            Executable  = OperatingSystem.IsWindows() ? "FactoryServer.exe" : "FactoryServer.sh",
            LaunchArgs  = "-log -unattended",
            DefaultDir  = "satisfactory",
            RconPort    = 15777,
            QueryPort   = 7777,
            MaxPlayers  = 4,
        },
    };
}
