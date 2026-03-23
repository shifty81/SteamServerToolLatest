using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SteamServerTool.Core.Models;
using SteamServerTool.Core.Services;

namespace SteamServerTool;

/// <summary>
/// Steam Workshop browser dialog.  Call <see cref="SelectedIds"/> after
/// <c>ShowDialog()</c> returns <c>true</c> to get the IDs the user chose.
/// </summary>
public partial class WorkshopBrowserWindow : Window
{
    private readonly WorkshopService _workshop = new();
    private readonly int             _appId;

    // The list of view-model rows currently shown in each tab
    private List<WorkshopResultItem> _searchItems     = new();
    private List<WorkshopResultItem> _collectionItems = new();
    private List<WorkshopResultItem> _lookupItems     = new();

    // Pagination state for the Search tab
    private int    _searchPage  = 1;
    private int    _searchTotal = 0;
    private string _lastQuery   = "";
    private const  int PageSize = 20;

    // ── Public result ────────────────────────────────────────────────────────
    /// <summary>Workshop IDs the user selected before clicking "Add Selected".</summary>
    public IReadOnlyList<long> SelectedIds { get; private set; } = Array.Empty<long>();

    public WorkshopBrowserWindow(int appId)
    {
        InitializeComponent();
        _appId = appId;
        TxtAppHeader.Text = $"App ID: {appId}";
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  HEADER
    // ═════════════════════════════════════════════════════════════════════════
    private void BtnGetApiKey_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://steamcommunity.com/dev/apikey") { UseShellExecute = true }); }
        catch { /* best effort */ }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  SEARCH TAB
    // ═════════════════════════════════════════════════════════════════════════
    private async void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        var q = TxtSearchQuery.Text.Trim();
        if (string.IsNullOrEmpty(q)) return;

        _lastQuery  = q;
        _searchPage = 1;
        await RunSearch();
    }

    private async void TxtSearchQuery_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) BtnSearch_Click(sender, e);
    }

    private async void BtnPrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_searchPage <= 1) return;
        _searchPage--;
        await RunSearch();
    }

    private async void BtnNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_searchPage * PageSize >= _searchTotal) return;
        _searchPage++;
        await RunSearch();
    }

    private async Task RunSearch()
    {
        _workshop.ApiKey = TxtApiKey.Text.Trim();
        SetSearchBusy(true);

        var (items, total, error) = await _workshop.SearchAsync(_appId, _lastQuery, _searchPage);

        if (error != null)
        {
            TxtSearchStatus.Text = $"⚠ {error}";
            SetSearchBusy(false);
            return;
        }

        _searchTotal  = total;
        _searchItems  = items.Select(i => new WorkshopResultItem(i)).ToList();
        SetItems(_searchItems, LbSearchResults);

        var pages  = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        TxtPageInfo.Text        = $"Page {_searchPage} / {pages}";
        TxtSearchStatus.Text    = $"{total} results found.";
        BtnPrevPage.IsEnabled   = _searchPage > 1;
        BtnNextPage.IsEnabled   = _searchPage < pages;

        SetSearchBusy(false);
    }

    private void SetSearchBusy(bool busy)
    {
        BtnSearch_Click_IsEnabled(!busy);
        if (busy) TxtSearchStatus.Text = "Searching…";
    }

    private void BtnSearch_Click_IsEnabled(bool enabled)
    {
        // Simple helper – disable the search button while loading
        if (FindName("BtnSearch") is Button btn) btn.IsEnabled = enabled;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  COLLECTION TAB
    // ═════════════════════════════════════════════════════════════════════════
    private async void BtnBrowseCollection_Click(object sender, RoutedEventArgs e)
    {
        var raw = TxtCollectionId.Text.Trim();
        if (string.IsNullOrEmpty(raw)) return;

        // Accept full URL like https://steamcommunity.com/sharedfiles/filedetails/?id=12345
        if (!long.TryParse(raw, out var collectionId))
        {
            var match = System.Text.RegularExpressions.Regex.Match(raw, @"[?&]id=(\d+)");
            if (!match.Success || !long.TryParse(match.Groups[1].Value, out collectionId))
            {
                TxtCollectionStatus.Text = "⚠ Invalid collection ID or URL.";
                return;
            }
        }

        TxtCollectionStatus.Text = "Loading collection…";
        LbCollectionResults.IsEnabled = false;

        var (items, error) = await _workshop.GetCollectionItemsAsync(collectionId);

        LbCollectionResults.IsEnabled = true;

        if (error != null)
        {
            TxtCollectionStatus.Text = $"⚠ {error}";
            return;
        }

        _collectionItems = items.Select(i => new WorkshopResultItem(i)).ToList();
        SetItems(_collectionItems, LbCollectionResults);
        TxtCollectionStatus.Text = $"{items.Count} items in collection.";
    }

    private void TxtCollectionId_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) BtnBrowseCollection_Click(sender, e);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  LOOKUP TAB
    // ═════════════════════════════════════════════════════════════════════════
    private async void BtnLookup_Click(object sender, RoutedEventArgs e)
    {
        var raw = TxtLookupIds.Text;
        if (string.IsNullOrWhiteSpace(raw)) return;

        var ids = raw
            .Split(new[] { ',', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => long.TryParse(s, out _))
            .Select(long.Parse)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            TxtLookupStatus.Text = "⚠ No valid Workshop IDs found.";
            return;
        }

        TxtLookupStatus.Text = $"Looking up {ids.Count} item(s)…";
        LbLookupResults.IsEnabled = false;

        var (items, error) = await _workshop.GetItemDetailsAsync(ids);

        LbLookupResults.IsEnabled = true;

        if (error != null)
        {
            TxtLookupStatus.Text = $"⚠ {error}";
            return;
        }

        _lookupItems = items.Select(i => new WorkshopResultItem(i)).ToList();
        SetItems(_lookupItems, LbLookupResults);
        TxtLookupStatus.Text = $"Found {items.Count} item(s).";
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  SELECTION TRACKING
    // ═════════════════════════════════════════════════════════════════════════
    private IEnumerable<WorkshopResultItem> AllItems()
        => _searchItems.Concat(_collectionItems).Concat(_lookupItems);

    private void UpdateSelectionCount()
    {
        var total = AllItems().Count(i => i.IsSelected);
        TxtSelectionCount.Text = total == 0
            ? "No mods selected"
            : $"{total} mod{(total == 1 ? "" : "s")} selected";
    }

    private void SetItems(List<WorkshopResultItem> items, ListBox lb)
    {
        foreach (var item in items)
            item.PropertyChanged += (_, _) => UpdateSelectionCount();
        lb.ItemsSource = items;
        UpdateSelectionCount();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  FOOTER
    // ═════════════════════════════════════════════════════════════════════════
    private void BtnAddSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = AllItems()
            .Where(i => i.IsSelected)
            .Select(i => i.PublishedFileId)
            .Distinct()
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show(
                "Check the box next to each mod you want to add.",
                "No mods selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedIds = selected;
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  VIEW-MODEL for a single Workshop result row
// ─────────────────────────────────────────────────────────────────────────────
public class WorkshopResultItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isSelected;

    public long   PublishedFileId { get; }
    public string Title           { get; }
    public string Description     { get; }
    public string SubscriptionLabel { get; }
    public string UpdatedStr      { get; }
    public string IdStr           => PublishedFileId.ToString();
    public string TagsLabel       { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public WorkshopResultItem(WorkshopItem item)
    {
        PublishedFileId   = item.PublishedFileId;
        Title             = item.Title;
        Description       = item.ShortDescription;
        SubscriptionLabel = item.SubscriptionLabel;
        UpdatedStr        = item.UpdatedStr;
        TagsLabel         = item.Tags.Count > 0
            ? string.Join(" · ", item.Tags.Take(4))
            : "";
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
