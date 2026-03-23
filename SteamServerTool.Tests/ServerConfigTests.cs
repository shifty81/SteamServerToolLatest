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
    public void Validate_ZeroAppId_ReturnsInvalid()
    {
        var cfg = ValidConfig();
        cfg.AppId = 0;
        var (isValid, error) = cfg.Validate();
        Assert.False(isValid);
        Assert.Contains("AppID", error);
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
