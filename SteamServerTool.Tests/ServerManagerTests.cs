using System.Text.Json;
using SteamServerTool.Core.Models;
using SteamServerTool.Core.Services;
using Xunit;

namespace SteamServerTool.Tests;

public class ServerManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public ServerManagerTests()
    {
        _tempDir    = Path.Combine(Path.GetTempPath(), $"sst_test_{Guid.NewGuid():N}");
        _configPath = Path.Combine(_tempDir, "servers.json");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ─── LoadConfig ────────────────────────────────────────────────────────
    [Fact]
    public void LoadConfig_ValidJson_PopulatesServers()
    {
        var configs = new[]
        {
            new ServerConfig
            {
                Name       = "ARK Server",
                AppId      = 2430930,
                Dir        = "/servers/ark",
                Executable = "ShooterGameServer",
                Rcon       = new() { Port = 27020 }
            }
        };

        File.WriteAllText(_configPath, JsonSerializer.Serialize(configs));

        var manager = new ServerManager();
        manager.LoadConfig(_configPath);

        Assert.Single(manager.Servers);
        Assert.Equal("ARK Server", manager.Servers[0].Name);
        Assert.Equal(2430930, manager.Servers[0].AppId);
    }

    [Fact]
    public void LoadConfig_EmptyArray_ReturnsEmptyList()
    {
        File.WriteAllText(_configPath, "[]");

        var manager = new ServerManager();
        manager.LoadConfig(_configPath);

        Assert.Empty(manager.Servers);
    }

    [Fact]
    public void LoadConfig_FileNotFound_ReturnsEmptyList()
    {
        var manager = new ServerManager();
        manager.LoadConfig(Path.Combine(_tempDir, "nonexistent.json"));

        Assert.Empty(manager.Servers);
    }

    [Fact]
    public void LoadConfig_MultipleServers_LoadsAll()
    {
        var configs = new[]
        {
            new ServerConfig { Name = "Server1", AppId = 730,     Dir = "/s1", Executable = "cs2",               Rcon = new() { Port = 27015 } },
            new ServerConfig { Name = "Server2", AppId = 2430930, Dir = "/s2", Executable = "ShooterGameServer",  Rcon = new() { Port = 27020 } },
            new ServerConfig { Name = "Server3", AppId = 258550,  Dir = "/s3", Executable = "rust_server",       Rcon = new() { Port = 28016 } }
        };

        File.WriteAllText(_configPath, JsonSerializer.Serialize(configs));

        var manager = new ServerManager();
        manager.LoadConfig(_configPath);

        Assert.Equal(3, manager.Servers.Count);
        Assert.Equal("Server1", manager.Servers[0].Name);
        Assert.Equal("Server2", manager.Servers[1].Name);
        Assert.Equal("Server3", manager.Servers[2].Name);
    }

    // ─── SaveConfig ────────────────────────────────────────────────────────
    [Fact]
    public void SaveConfig_WritesValidJson()
    {
        var manager = new ServerManager();
        manager.Servers.Add(new ServerConfig
        {
            Name       = "CS2 Server",
            AppId      = 730,
            Dir        = "/servers/cs2",
            Executable = "cs2",
            Rcon       = new() { Port = 27015 }
        });

        manager.SaveConfig(_configPath);

        Assert.True(File.Exists(_configPath));
        var json = File.ReadAllText(_configPath);
        var loaded = JsonSerializer.Deserialize<List<ServerConfig>>(json);
        Assert.NotNull(loaded);
        Assert.Single(loaded);
        Assert.Equal("CS2 Server", loaded[0].Name);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesData()
    {
        var original = new ServerConfig
        {
            Name                   = "Rust Server",
            AppId                  = 258550,
            Dir                    = "/servers/rust",
            Executable             = "rust_server",
            LaunchArgs             = "-batchmode +server.port 28015",
            BackupFolder           = "/backups/rust",
            KeepBackups            = 7,
            AutoUpdate             = true,
            MaxPlayers             = 100,
            Group                  = "Rust Servers",
            TotalCrashes           = 3,
            Rcon                   = new() { Host = "localhost", Port = 28016, Password = "secret" },
            Mods                   = new() { 12345, 67890 },
            Tags                   = new() { "prod", "rust" },
            EnvironmentVariables   = new() { { "LD_LIBRARY_PATH", "/lib" } }
        };

        var manager = new ServerManager();
        manager.Servers.Add(original);
        manager.SaveConfig(_configPath);

        var manager2 = new ServerManager();
        manager2.LoadConfig(_configPath);

        Assert.Single(manager2.Servers);
        var loaded = manager2.Servers[0];

        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.AppId, loaded.AppId);
        Assert.Equal(original.Dir, loaded.Dir);
        Assert.Equal(original.Executable, loaded.Executable);
        Assert.Equal(original.LaunchArgs, loaded.LaunchArgs);
        Assert.Equal(original.KeepBackups, loaded.KeepBackups);
        Assert.Equal(original.AutoUpdate, loaded.AutoUpdate);
        Assert.Equal(original.MaxPlayers, loaded.MaxPlayers);
        Assert.Equal(original.TotalCrashes, loaded.TotalCrashes);
        Assert.Equal(original.Rcon.Host, loaded.Rcon.Host);
        Assert.Equal(original.Rcon.Port, loaded.Rcon.Port);
        Assert.Equal(original.Rcon.Password, loaded.Rcon.Password);
        Assert.Equal(2, loaded.Mods.Count);
        Assert.Contains(12345L, loaded.Mods);
        Assert.Contains(67890L, loaded.Mods);
        Assert.Equal(2, loaded.Tags.Count);
        Assert.Equal("/lib", loaded.EnvironmentVariables["LD_LIBRARY_PATH"]);
    }

    // ─── IsRunning ─────────────────────────────────────────────────────────
    [Fact]
    public void IsRunning_UnknownServer_ReturnsFalse()
    {
        var manager = new ServerManager();
        Assert.False(manager.IsRunning("NonExistentServer"));
    }

    [Fact]
    public void IsRunning_KnownButNotStarted_ReturnsFalse()
    {
        var manager = new ServerManager();
        manager.Servers.Add(new ServerConfig
        {
            Name       = "Test",
            AppId      = 730,
            Dir        = "/tmp",
            Executable = "test",
            Rcon       = new() { Port = 27015 }
        });

        Assert.False(manager.IsRunning("Test"));
    }

    // ─── GetStatus ─────────────────────────────────────────────────────────
    [Fact]
    public void GetStatus_UnknownServer_ReturnsStopped()
    {
        var manager = new ServerManager();
        Assert.Equal(ServerStatus.Stopped, manager.GetStatus("NoSuchServer"));
    }

    [Fact]
    public void GetStatus_AfterLoad_ReturnsStopped()
    {
        var configs = new[] { new ServerConfig { Name = "S1", AppId = 1, Dir = "/d", Executable = "e", Rcon = new() { Port = 1024 } } };
        File.WriteAllText(_configPath, JsonSerializer.Serialize(configs));

        var manager = new ServerManager();
        manager.LoadConfig(_configPath);

        Assert.Equal(ServerStatus.Stopped, manager.GetStatus("S1"));
    }

    // ─── GetUptime ─────────────────────────────────────────────────────────
    [Fact]
    public void GetUptime_NotRunning_ReturnsNull()
    {
        var manager = new ServerManager();
        Assert.Null(manager.GetUptime("AnyServer"));
    }

    // ─── ServerStatusChanged event ─────────────────────────────────────────
    [Fact]
    public void LoadConfig_AfterReload_ResetsServers()
    {
        var configs1 = new[] { new ServerConfig { Name = "S1", AppId = 730, Dir = "/d", Executable = "e", Rcon = new() { Port = 1024 } } };
        File.WriteAllText(_configPath, JsonSerializer.Serialize(configs1));

        var manager = new ServerManager();
        manager.LoadConfig(_configPath);
        Assert.Single(manager.Servers);

        var configs2 = new[]
        {
            new ServerConfig { Name = "A", AppId = 1, Dir = "/a", Executable = "a", Rcon = new() { Port = 1024 } },
            new ServerConfig { Name = "B", AppId = 2, Dir = "/b", Executable = "b", Rcon = new() { Port = 1025 } }
        };
        File.WriteAllText(_configPath, JsonSerializer.Serialize(configs2));
        manager.LoadConfig(_configPath);

        Assert.Equal(2, manager.Servers.Count);
    }

    // ─── ServerConfig serialization edge cases ─────────────────────────────
    [Fact]
    public void ServerConfig_JsonSerialization_UsescamelCasePropertyNames()
    {
        var cfg = new ServerConfig { Name = "Test", AppId = 730, Dir = "/d", Executable = "e" };
        var json = JsonSerializer.Serialize(cfg);

        Assert.Contains("\"name\"", json);
        Assert.Contains("\"appId\"", json);
        Assert.Contains("\"dir\"", json);
        Assert.Contains("\"executable\"", json);
    }

    [Fact]
    public void ServerConfig_WithScheduledCommands_RoundTrips()
    {
        var cfg = new ServerConfig
        {
            Name       = "S",
            AppId      = 1,
            Dir        = "/d",
            Executable = "e",
            Rcon       = new() { Port = 27015 },
            ScheduledRconCommands = new()
            {
                new() { Command = "say hello", IntervalMinutes = 30 }
            }
        };

        var manager = new ServerManager();
        manager.Servers.Add(cfg);
        manager.SaveConfig(_configPath);

        var manager2 = new ServerManager();
        manager2.LoadConfig(_configPath);

        Assert.Single(manager2.Servers[0].ScheduledRconCommands);
        Assert.Equal("say hello", manager2.Servers[0].ScheduledRconCommands[0].Command);
        Assert.Equal(30, manager2.Servers[0].ScheduledRconCommands[0].IntervalMinutes);
    }
}
