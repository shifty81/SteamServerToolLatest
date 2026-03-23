using SteamServerTool.Core.Models;
using Xunit;

namespace SteamServerTool.Tests;

public class ServerConfigTests
{
    private static ServerConfig ValidConfig() => new()
    {
        Name       = "Test Server",
        AppId      = 730,
        Dir        = "/servers/cs2",
        Executable = "cs2",
        Rcon       = new() { Host = "127.0.0.1", Port = 27015, Password = "pw" }
    };

    [Fact]
    public void Validate_ValidConfig_ReturnsValid()
    {
        var cfg = ValidConfig();
        var (isValid, error) = cfg.Validate();
        Assert.True(isValid);
        Assert.Equal("", error);
    }

    [Fact]
    public void Validate_EmptyName_ReturnsInvalid()
    {
        var cfg = ValidConfig();
        cfg.Name = "";
        var (isValid, error) = cfg.Validate();
        Assert.False(isValid);
        Assert.Contains("Name", error);
    }

    [Fact]
    public void Validate_WhitespaceName_ReturnsInvalid()
    {
        var cfg = ValidConfig();
        cfg.Name = "   ";
        var (isValid, error) = cfg.Validate();
        Assert.False(isValid);
        Assert.Contains("Name", error);
    }

    [Fact]
    public void Validate_ZeroAppId_ReturnsValid()
    {
        // AppId=0 is valid for non-SteamCMD servers (e.g. Minecraft Java)
        var cfg = ValidConfig();
        cfg.AppId = 0;
        var (isValid, _) = cfg.Validate();
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_NegativeAppId_ReturnsInvalid()
    {
        var cfg = ValidConfig();
        cfg.AppId = -1;
        var (isValid, error) = cfg.Validate();
        Assert.False(isValid);
        Assert.Contains("AppID", error);
    }

    [Fact]
    public void Validate_MinecraftStyleConfig_ZeroAppId_ReturnsValid()
    {
        var cfg = new ServerConfig
        {
            Name       = "Minecraft Server",
            AppId      = 0,
            Dir        = "/servers/minecraft",
            Executable = "java",
            LaunchArgs = "-Xmx4G -Xms2G -jar server.jar nogui",
            Rcon       = new() { Host = "127.0.0.1", Port = 25575, Password = "pw" }
        };
        var (isValid, _) = cfg.Validate();
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_RemoteServer_NoAppIdNoDir_ReturnsValid()
    {
        var cfg = new ServerConfig
        {
            Name     = "Remote ARK",
            IsRemote = true,
            Rcon     = new() { Host = "192.168.1.10", Port = 27020, Password = "secret" }
        };
        var (isValid, _) = cfg.Validate();
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_RemoteServer_InvalidRconPort_ReturnsInvalid()
    {
        var cfg = new ServerConfig
        {
            Name     = "Remote ARK",
            IsRemote = true,
            Rcon     = new() { Host = "192.168.1.10", Port = 0, Password = "secret" }
        };
        var (isValid, error) = cfg.Validate();
        Assert.False(isValid);
        Assert.Contains("RCON port", error);
    }

    [Fact]
    public void Validate_EmptyDir_ReturnsInvalid()
    {
        var cfg = ValidConfig();
        cfg.Dir = "";
        var (isValid, error) = cfg.Validate();
        Assert.False(isValid);
        Assert.Contains("directory", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_EmptyExecutable_ReturnsInvalid()
    {
        var cfg = ValidConfig();
        cfg.Executable = "";
        var (isValid, error) = cfg.Validate();
        Assert.False(isValid);
        Assert.Contains("Executable", error);
    }

    [Fact]
    public void Validate_RconPortZero_ReturnsInvalid()
    {
        var cfg = ValidConfig();
        cfg.Rcon.Port = 0;
        var (isValid, error) = cfg.Validate();
        Assert.False(isValid);
        Assert.Contains("RCON port", error);
    }

    [Fact]
    public void Validate_RconPortTooHigh_ReturnsInvalid()
    {
        var cfg = ValidConfig();
        cfg.Rcon.Port = 65536;
        var (isValid, error) = cfg.Validate();
        Assert.False(isValid);
        Assert.Contains("RCON port", error);
    }

    [Fact]
    public void Validate_RconPortMin_ReturnsValid()
    {
        var cfg = ValidConfig();
        cfg.Rcon.Port = 1;
        var (isValid, _) = cfg.Validate();
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_RconPortMax_ReturnsValid()
    {
        var cfg = ValidConfig();
        cfg.Rcon.Port = 65535;
        var (isValid, _) = cfg.Validate();
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_NegativeRconPort_ReturnsInvalid()
    {
        var cfg = ValidConfig();
        cfg.Rcon.Port = -1;
        var (isValid, error) = cfg.Validate();
        Assert.False(isValid);
        Assert.Contains("RCON port", error);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var cfg = new ServerConfig();
        Assert.Equal("", cfg.Name);
        Assert.Equal(0, cfg.AppId);
        Assert.Equal(10, cfg.KeepBackups);
        Assert.Equal(15, cfg.GracefulShutdownSeconds);
        Assert.Equal(90.0, cfg.CpuAlertThreshold);
        Assert.NotNull(cfg.Mods);
        Assert.NotNull(cfg.DisabledMods);
        Assert.NotNull(cfg.Tags);
        Assert.NotNull(cfg.ScheduledRconCommands);
        Assert.NotNull(cfg.EnvironmentVariables);
        Assert.False(cfg.IsRemote);
        Assert.Equal("", cfg.ServerType);
        Assert.Equal("", cfg.StdinStopCommand);
        Assert.Equal("", cfg.ConfigDir);
    }

    [Fact]
    public void StdinStopCommand_RoundTrips_InJson()
    {
        var cfg = ValidConfig();
        cfg.StdinStopCommand = "/stop";

        var json = System.Text.Json.JsonSerializer.Serialize(cfg);
        var back = System.Text.Json.JsonSerializer.Deserialize<ServerConfig>(json)!;

        Assert.Equal("/stop", back.StdinStopCommand);
    }

    [Fact]
    public void ConfigDir_RoundTrips_InJson()
    {
        var cfg = ValidConfig();
        cfg.ConfigDir = "data";

        var json = System.Text.Json.JsonSerializer.Serialize(cfg);
        var back = System.Text.Json.JsonSerializer.Deserialize<ServerConfig>(json)!;

        Assert.Equal("data", back.ConfigDir);
    }

    [Fact]
    public void VintageStoryTemplate_HasExpectedStopCommand()
    {
        var tmpl = ServerTemplates.All.FirstOrDefault(t => t.Name == "Vintage Story");
        Assert.NotNull(tmpl);
        Assert.Equal("/stop", tmpl!.StdinStopCommand);
        Assert.Equal("data", tmpl.ConfigDir);
    }

    [Fact]
    public void MinecraftTemplate_HasExpectedStopCommand()
    {
        var tmpl = ServerTemplates.All.FirstOrDefault(t => t.Name == "Minecraft Java");
        Assert.NotNull(tmpl);
        Assert.Equal("stop", tmpl!.StdinStopCommand);
    }

    [Fact]
    public void Validate_VintageStoryConfig_ZeroAppId_ReturnsValid()
    {
        var cfg = new ServerConfig
        {
            Name       = "Vintage Story",
            ServerType = "Vintage Story",
            AppId      = 0,
            Dir        = "/servers/vintagestory",
            Executable = "VintagestoryServer.exe",
            LaunchArgs = "--dataPath ./data --port 42420 --maxclients 16",
            Rcon       = new() { Host = "127.0.0.1", Port = 42425, Password = "pw" }
        };
        var (isValid, _) = cfg.Validate();
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_NullRcon_ReturnsInvalid()
    {
        var cfg = ValidConfig();
        cfg.Rcon = null!;   // simulate malformed JSON deserialization
        var (isValid, error) = cfg.Validate();
        Assert.False(isValid);
        Assert.Contains("RCON port", error);
    }

    [Fact]
    public void RconConfig_DefaultValues_AreCorrect()
    {
        var rcon = new RconConfig();
        Assert.Equal("127.0.0.1", rcon.Host);
        Assert.Equal(27020, rcon.Port);
        Assert.Equal("", rcon.Password);
    }

    [Fact]
    public void ScheduledRconCommand_DefaultValues_AreCorrect()
    {
        var cmd = new ScheduledRconCommand();
        Assert.Equal("", cmd.Command);
        Assert.Equal(60, cmd.IntervalMinutes);
    }
}
