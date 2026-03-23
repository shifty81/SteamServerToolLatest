using System.Text.Json.Serialization;

namespace SteamServerTool.Core.Models;

public class ScheduledRconCommand
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("intervalMinutes")]
    public int IntervalMinutes { get; set; } = 60;
}
