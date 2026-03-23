using System.Text.Json.Serialization;

namespace SteamServerTool.Core.Models;

public class RconConfig
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "127.0.0.1";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 27020;

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";
}
