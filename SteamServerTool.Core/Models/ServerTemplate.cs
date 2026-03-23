namespace SteamServerTool.Core.Models;

public class ServerTemplate
{
    public string Name        { get; set; } = "";
    public string Icon        { get; set; } = "🎮";
    public string Description { get; set; } = "";
    public int    AppId       { get; set; }
    public string Executable  { get; set; } = "";
    public string LaunchArgs  { get; set; } = "";
    public string DefaultDir  { get; set; } = "";
    public int    RconPort    { get; set; } = 27020;
    public int    QueryPort   { get; set; } = 27015;
    public int    MaxPlayers  { get; set; } = 0;
    public string Group       { get; set; } = "";
    public string RconHost    { get; set; } = "127.0.0.1";
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
            DefaultDir  = @"C:\Servers\ark_sa",
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
            DefaultDir  = @"C:\Servers\ark_se",
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
            DefaultDir  = @"C:\Servers\cs2",
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
            DefaultDir  = @"C:\Servers\valheim",
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
            DefaultDir  = @"C:\Servers\rust",
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
            DefaultDir  = @"C:\Servers\7dtd",
            RconPort    = 8081,
            QueryPort   = 26900,
            MaxPlayers  = 8,
        },
        new()
        {
            Name        = "Minecraft Java",
            Icon        = "⛏️",
            Description = "Minecraft Java Edition server (requires JRE)",
            AppId       = 0,
            Executable  = "java",
            LaunchArgs  = "-Xmx4G -Xms2G -jar server.jar nogui",
            DefaultDir  = @"C:\Servers\minecraft",
            RconPort    = 25575,
            QueryPort   = 25565,
            MaxPlayers  = 20,
        },
        new()
        {
            Name        = "Enshrouded",
            Icon        = "🌫️",
            Description = "Enshrouded dedicated server (App ID 2278520)",
            AppId       = 2278520,
            Executable  = "enshrouded_server",
            LaunchArgs  = "",
            DefaultDir  = @"C:\Servers\enshrouded",
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
            DefaultDir  = @"C:\Servers\vrising",
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
            DefaultDir  = @"C:\Servers\palworld",
            RconPort    = 25575,
            QueryPort   = 8211,
            MaxPlayers  = 32,
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
    };
}
