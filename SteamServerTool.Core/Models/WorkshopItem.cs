using System.Text.Json.Serialization;

namespace SteamServerTool.Core.Models;

/// <summary>Represents a single item retrieved from the Steam Workshop.</summary>
public class WorkshopItem
{
    public long   PublishedFileId { get; set; }
    public string Title          { get; set; } = "";
    public string Description    { get; set; } = "";
    public string PreviewUrl     { get; set; } = "";
    public long   Subscriptions  { get; set; }
    public long   TimeUpdated    { get; set; }
    public List<string> Tags     { get; set; } = new();

    /// <summary>Description trimmed to 150 chars for list display.</summary>
    public string ShortDescription =>
        Description.Length > 150 ? Description[..150].TrimEnd() + "…" : Description;

    /// <summary>Human-readable subscriber count (e.g. "12.3K subscribers").</summary>
    public string SubscriptionLabel => Subscriptions switch
    {
        >= 1_000_000 => $"{Subscriptions / 1_000_000.0:F1}M subscribers",
        >= 1_000     => $"{Subscriptions / 1_000.0:F1}K subscribers",
        _            => $"{Subscriptions} subscribers"
    };

    /// <summary>Last-updated date as a short local date string.</summary>
    public string UpdatedStr => TimeUpdated > 0
        ? DateTimeOffset.FromUnixTimeSeconds(TimeUpdated).LocalDateTime.ToString("yyyy-MM-dd")
        : "";
}
