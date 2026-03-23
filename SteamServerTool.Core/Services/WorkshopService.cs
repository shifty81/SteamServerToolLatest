using System.Net.Http;
using System.Text.Json;
using SteamServerTool.Core.Models;

namespace SteamServerTool.Core.Services;

/// <summary>
/// Wraps the Steam Web API for Workshop operations.
/// Search requires a Steam Web API key (https://steamcommunity.com/dev/apikey).
/// Collection and file-detail lookups work without a key.
/// </summary>
public class WorkshopService
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private const string BaseUrl    = "https://api.steampowered.com";
    private const int    PageSize   = 20;

    /// <summary>Optional Steam Web API key – required for <see cref="SearchAsync"/>.</summary>
    public string? ApiKey { get; set; }

    // ─────────────────────────────────────────────────────────────────────────
    //  Search  (requires API key)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches the Steam Workshop for the given <paramref name="appId"/> and
    /// <paramref name="query"/> string.  Requires <see cref="ApiKey"/> to be set.
    /// </summary>
    /// <returns>
    /// A tuple of (items, totalCount, errorMessage).
    /// <c>error</c> is non-null when the call could not be completed.
    /// </returns>
    public async Task<(List<WorkshopItem> Items, int Total, string? Error)> SearchAsync(
        int appId, string query, int page = 1)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return (new(), 0,
                "A Steam Web API key is required for search.\n" +
                "Get a free key at: https://steamcommunity.com/dev/apikey");

        var url = $"{BaseUrl}/IPublishedFileService/QueryFiles/v1/" +
                  $"?key={Uri.EscapeDataString(ApiKey)}" +
                  $"&query_type=1&page={page}&numperpage={PageSize}" +
                  $"&creator_appid={appId}&appid={appId}" +
                  $"&search_text={Uri.EscapeDataString(query)}" +
                  "&return_metadata=1&return_tags=1&return_previews=1";

        try
        {
            var json   = await _http.GetStringAsync(url);
            using var  doc  = JsonDocument.Parse(json);
            var        root = doc.RootElement.GetProperty("response");

            var total = root.TryGetProperty("total", out var t) ? t.GetInt32() : 0;

            if (!root.TryGetProperty("files", out var files))
                return (new(), total, null);

            return (ParseFileArray(files), total, null);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WorkshopService.SearchAsync failed: {ex.Message}");
            return (new(), 0, $"Search failed: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Collection details  (no API key required)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a Steam Workshop collection to its member <see cref="WorkshopItem"/>s.
    /// No API key is required.
    /// </summary>
    public async Task<(List<WorkshopItem> Items, string? Error)> GetCollectionItemsAsync(
        long collectionId)
    {
        // Step 1: get the list of child file IDs
        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("collectioncount", "1"),
            new KeyValuePair<string, string>("publishedfileids[0]", collectionId.ToString())
        });

        List<long> childIds;
        try
        {
            var resp = await _http.PostAsync(
                $"{BaseUrl}/ISteamRemoteStorage/GetCollectionDetails/v1/", form);
            var json = await resp.Content.ReadAsStringAsync();

            using var doc    = JsonDocument.Parse(json);
            var       colArr = doc.RootElement
                                  .GetProperty("response")
                                  .GetProperty("collectiondetails");

            if (colArr.GetArrayLength() == 0)
                return (new(), "Collection not found or is empty.");

            var col = colArr[0];

            if (!col.TryGetProperty("children", out var children))
                return (new(), "Collection contains no items.");

            childIds = new List<long>();
            foreach (var child in children.EnumerateArray())
            {
                if (child.TryGetProperty("publishedfileid", out var idEl) &&
                    long.TryParse(idEl.GetString(), out var id))
                    childIds.Add(id);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WorkshopService.GetCollectionItemsAsync failed: {ex.Message}");
            return (new(), $"Failed to retrieve collection: {ex.Message}");
        }

        if (childIds.Count == 0)
            return (new(), "Collection is empty.");

        // Step 2: fetch item details in batches of 100
        var items = new List<WorkshopItem>();
        foreach (var batch in childIds.Chunk(100))
        {
            var (batchItems, err) = await GetItemDetailsAsync(batch);
            if (err != null) return (items, err);
            items.AddRange(batchItems);
        }

        return (items, null);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Individual file details  (no API key required)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches details for up to 100 specific Workshop items by their IDs.
    /// No API key is required.
    /// </summary>
    public async Task<(List<WorkshopItem> Items, string? Error)> GetItemDetailsAsync(
        IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return (new(), null);

        var fields = new List<KeyValuePair<string, string>>
        {
            new("itemcount", idList.Count.ToString())
        };
        for (var i = 0; i < idList.Count; i++)
            fields.Add(new($"publishedfileids[{i}]", idList[i].ToString()));

        try
        {
            var form  = new FormUrlEncodedContent(fields);
            var resp  = await _http.PostAsync(
                $"{BaseUrl}/ISteamRemoteStorage/GetPublishedFileDetails/v1/", form);
            var json  = await resp.Content.ReadAsStringAsync();

            using var doc      = JsonDocument.Parse(json);
            var       details  = doc.RootElement
                                    .GetProperty("response")
                                    .GetProperty("publishedfiledetails");

            return (ParseFileArray(details), null);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"WorkshopService.GetItemDetailsAsync failed: {ex.Message}");
            return (new(), $"Failed to retrieve item details: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  JSON helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static List<WorkshopItem> ParseFileArray(JsonElement array)
    {
        var items = new List<WorkshopItem>();
        foreach (var el in array.EnumerateArray())
        {
            // Skip items that failed to load
            if (el.TryGetProperty("result", out var res) && res.GetInt32() != 1)
                continue;

            var item = new WorkshopItem
            {
                Title       = GetStr(el, "title"),
                Description = GetStr(el, "description", GetStr(el, "short_description")),
                PreviewUrl  = GetStr(el, "preview_url"),
                TimeUpdated = GetLong(el, "time_updated")
            };

            if (el.TryGetProperty("publishedfileid", out var pfid) &&
                long.TryParse(pfid.GetString(), out var id))
                item.PublishedFileId = id;

            // Prefer lifetime_subscriptions when available
            item.Subscriptions = el.TryGetProperty("lifetime_subscriptions", out var ls)
                ? ls.GetInt64()
                : GetLong(el, "subscriptions");

            if (el.TryGetProperty("tags", out var tags))
            {
                foreach (var tag in tags.EnumerateArray())
                {
                    if (tag.TryGetProperty("tag", out var t))
                        item.Tags.Add(t.GetString() ?? "");
                }
            }

            items.Add(item);
        }
        return items;
    }

    private static string GetStr(JsonElement el, string key, string fallback = "")
        => el.TryGetProperty(key, out var v) ? (v.GetString() ?? fallback) : fallback;

    private static long GetLong(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt64() : 0;
}
