using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SteamServerTool.Core.Models;
using SteamServerTool.Core.Services;
using SteamServerTool.Dialogs;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace SteamServerTool;

public partial class MainWindow : Window
{
    // ─── Services ───────────────────────────────────────────────────────────
    private readonly ServerManager      _serverManager      = new();
    private readonly BackupService      _backupService      = new();
    private readonly SteamCmdService    _steamCmdService    = new();
    private readonly WorkshopService    _workshopService    = new();
    private readonly IniFileService     _iniFileService     = new();
    private readonly MinecraftService   _minecraftService   = new();
    private readonly VintageStoryService _vintageStoryService = new();

    /// <summary>
    /// Base directory for all locally-installed servers.
    /// Defaults to a "Servers" folder next to the application executable.
    /// </summary>
    private static readonly string ServersBaseDir =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Servers");
    private RconClient? _rconClient;

    // ─── State ──────────────────────────────────────────────────────────────
    private ServerConfig? _selectedConfig;
    private readonly DispatcherTimer _refreshTimer;
    private const string ConfigPath = "servers.json";
    private const int MaxRconHistorySize = 100;
    private bool _isDashboardMode = false;

    // ─── INI editor state ────────────────────────────────────────────────────
    // key: filePath → live entry list currently shown in the UI
    private readonly Dictionary<string, List<IniEntry>> _iniEditorEntries = new();

    // All discovered config files for the current server (used for filtering)
    private List<IniFileInfo> _allConfigFiles = new();

    // ─── RCON command history ────────────────────────────────────────────────
    private readonly List<string> _rconHistory = new();
    private int _rconHistoryIndex = -1;

    // ─── Constructor ────────────────────────────────────────────────────────
    public MainWindow()
    {
        InitializeComponent();

        _serverManager.ServerStatusChanged += OnServerStatusChanged;
        _serverManager.ServerCrashed       += OnServerCrashed;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += (_, _) => RefreshStatus();
        _refreshTimer.Start();

        // Populate template gallery
        TemplateList.ItemsSource = ServerTemplates.All;

        LoadAndPopulate();

        // First-run: check SteamCMD after the window is shown
        Loaded += async (_, _) => await CheckFirstRunAsync();
    }

    // ─── First-run SteamCMD check ─────────────────────────────────────────
    private async Task CheckFirstRunAsync()
    {
        if (_steamCmdService.IsSteamCmdInstalled()) return;

        var dlg = new FirstRunSetupDialog(_steamCmdService) { Owner = this };
        dlg.ShowDialog();

        if (!string.IsNullOrEmpty(dlg.ResolvedSteamCmdPath))
            Log($"[Setup] SteamCMD configured: {dlg.ResolvedSteamCmdPath}");
        else
            Log("[Setup] SteamCMD not configured — install/update features will be unavailable.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LOAD / POPULATE
    // ═══════════════════════════════════════════════════════════════════════
    private void LoadAndPopulate()
    {
        try
        {
            _serverManager.LoadConfig(ConfigPath);
            PopulateServerList();
            Log($"Loaded {_serverManager.Servers.Count} server(s) from {ConfigPath}.");

            foreach (var s in _serverManager.Servers.Where(s => s.AutoStartOnLaunch))
            {
                TryStartServer(s);
            }
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to load config: {ex.Message}");
        }
    }

    private void PopulateServerList(string filter = "")
    {
        LbServers.ItemsSource = null;
        var items = _serverManager.Servers
            .Where(s => string.IsNullOrEmpty(filter) ||
                        s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        s.Group.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(s => new ServerListItem(s, GetStatusBrush(s.Name)))
            .ToList();

        LbServers.ItemsSource = items;
        UpdateStatusSummary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SERVER SELECTION
    // ═══════════════════════════════════════════════════════════════════════
    private void LbServers_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LbServers.SelectedItem is ServerListItem item)
        {
            _selectedConfig = item.Config;
            ShowServerDetail(_selectedConfig);
        }
    }

    private void ShowServerDetail(ServerConfig cfg)
    {
        PnlNoSelection.Visibility = Visibility.Collapsed;
        PnlDetail.Visibility      = Visibility.Visible;

        TxtDetailTitle.Text = cfg.Name;
        RefreshDetailHeader(cfg);

        // Overview tab
        TxtOvAppId.Text       = cfg.AppId.ToString();
        TxtOvGroup.Text       = string.IsNullOrEmpty(cfg.Group) ? "—" : cfg.Group;
        TxtOvMaxPlayers.Text  = cfg.MaxPlayers > 0 ? cfg.MaxPlayers.ToString() : "—";
        TxtOvQueryPort.Text   = cfg.QueryPort > 0 ? cfg.QueryPort.ToString() : "—";
        TxtOvCrashes.Text     = cfg.TotalCrashes.ToString();
        TxtOvLastCrash.Text   = string.IsNullOrEmpty(cfg.LastCrashTime) ? "—" : cfg.LastCrashTime;
        TxtOvTags.Text        = cfg.Tags.Count > 0 ? string.Join(", ", cfg.Tags) : "—";
        TxtOvNotes.Text       = cfg.Notes;

        // Config tab
        CfgName.Text            = cfg.Name;
        CfgAppId.Text           = cfg.AppId.ToString();
        CfgDir.Text             = cfg.Dir;
        CfgExecutable.Text      = cfg.Executable;
        CfgLaunchArgs.Text      = cfg.LaunchArgs;
        CfgRconHost.Text        = cfg.Rcon.Host;
        CfgRconPort.Text        = cfg.Rcon.Port.ToString();
        CfgRconPassword.Text    = cfg.Rcon.Password;
        CfgBackupFolder.Text    = cfg.BackupFolder;
        CfgKeepBackups.Text     = cfg.KeepBackups.ToString();
        CfgBackupInterval.Text  = cfg.BackupIntervalMinutes.ToString();
        CfgRestartInterval.Text = cfg.RestartIntervalHours.ToString();
        CfgMaxPlayers.Text      = cfg.MaxPlayers.ToString();
        CfgGroup.Text           = cfg.Group;
        CfgDiscordWebhook.Text  = cfg.DiscordWebhookUrl;
        CfgShutdownSecs.Text    = cfg.GracefulShutdownSeconds.ToString();
        CfgQueryPort.Text       = cfg.QueryPort.ToString();
        CfgTags.Text            = string.Join(", ", cfg.Tags);
        CfgRestartWarnMins.Text = cfg.RestartWarningMinutes.ToString();
        CfgRestartWarnMsg.Text  = cfg.RestartWarningMessage;
        CfgCpuAlert.Text        = cfg.CpuAlertThreshold.ToString();
        CfgMemAlert.Text        = cfg.MemAlertThresholdMB.ToString();
        CfgAutoUpdate.IsChecked         = cfg.AutoUpdate;
        CfgAutoStart.IsChecked          = cfg.AutoStartOnLaunch;
        CfgFavorite.IsChecked           = cfg.Favorite;
        CfgBackupBeforeRestart.IsChecked = cfg.BackupBeforeRestart;

        // Install/download button — label and visibility depend on server type
        if (cfg.IsRemote)
        {
            BtnInstallOrDownload.Visibility = Visibility.Collapsed;
        }
        else
        {
            BtnInstallOrDownload.Visibility = Visibility.Visible;
            BtnInstallOrDownload.Content    = GetInstallButtonLabel(cfg);
        }

        // Mods tab
        RefreshModList(cfg);

        // Scheduled commands tab
        RefreshScheduledList(cfg);

        // Env vars tab
        RefreshEnvVarList(cfg);

        // Backups tab
        RefreshBackupList(cfg);

        // Logs tab
        TxtLogPath.Text = "";
        TxtLogViewer.Clear();
    }

    private void RefreshDetailHeader(ServerConfig cfg)
    {
        var status = _serverManager.GetStatus(cfg.Name);
        var brush  = GetStatusBrush(cfg.Name);

        DetailStatusDot.Fill = brush;
        TxtDetailStatus.Text = status.ToString();

        var uptime = _serverManager.GetUptime(cfg.Name);
        TxtOvStatus.Text = status.ToString();
        TxtOvUptime.Text = uptime.HasValue ? FormatTimeSpan(uptime.Value) : "—";
        TxtOvTotalUptime.Text = cfg.TotalUptimeSeconds > 0
            ? FormatTimeSpan(TimeSpan.FromSeconds(cfg.TotalUptimeSeconds))
            : "—";
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  STATUS REFRESH
    // ═══════════════════════════════════════════════════════════════════════
    private void RefreshStatus()
    {
        UpdateStatusSummary();
        if (_selectedConfig != null)
            RefreshDetailHeader(_selectedConfig);

        // Refresh list item brushes
        if (LbServers.ItemsSource is IEnumerable<ServerListItem> items)
        {
            foreach (var item in items)
                item.StatusBrush = GetStatusBrush(item.Name);
        }

        // Live-refresh dashboard badges
        if (_isDashboardMode)
            Dispatcher.InvokeAsync(RebuildDashboard);
    }

    private void UpdateStatusSummary()
    {
        var running = _serverManager.Servers.Count(s => _serverManager.IsRunning(s.Name));
        TxtStatusSummary.Text = $"{running} / {_serverManager.Servers.Count} servers online";
        SummaryDot.Fill = running > 0
            ? (SolidColorBrush)FindResource("StatusRunningBrush")
            : (SolidColorBrush)FindResource("StatusStoppedBrush");
    }

    private Brush GetStatusBrush(string name)
    {
        return _serverManager.GetStatus(name) switch
        {
            ServerStatus.Running  => (Brush)FindResource("StatusRunningBrush"),
            ServerStatus.Starting => (Brush)FindResource("StatusStartingBrush"),
            ServerStatus.Crashed  => (Brush)FindResource("StatusCrashedBrush"),
            _                     => (Brush)FindResource("StatusStoppedBrush")
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  EVENT HANDLERS — ServerManager
    // ═══════════════════════════════════════════════════════════════════════
    private void OnServerStatusChanged(object? sender, ServerStatusChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            Log($"[{e.ServerName}] Status → {e.Status}");
            RefreshStatus();
            PopulateServerList(TxtSearch.Text);
        });
    }

    private void OnServerCrashed(object? sender, ServerCrashedEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            Log($"[CRASH] {e.ServerName} exited with code {e.ExitCode}.");

            var result = MessageBox.Show(
                $"Server '{e.ServerName}' crashed (exit code {e.ExitCode}).\n\n" +
                "Would you like to submit a bug report on GitHub?",
                "Server Crash Detected",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var body = GitHubIssueReporter.FormatServerCrashReport(e.ServerName, e.ExitCode);
                GitHubIssueReporter.OpenNewIssue(
                    $"[Crash] {e.ServerName} exited with code {e.ExitCode}", body);
            }
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TOOLBAR BUTTONS
    // ═══════════════════════════════════════════════════════════════════════
    private void BtnNewServer_Click(object sender, RoutedEventArgs e)
    {
        var wizard = new Dialogs.ServerInstallerWizard(
            _steamCmdService, _minecraftService, _vintageStoryService)
        { Owner = this };

        if (wizard.ShowDialog() == true && wizard.Result != null)
        {
            _serverManager.Servers.Add(wizard.Result);
            PopulateServerList();
            SelectServer(wizard.Result);
            Log($"[Wizard] Added server '{wizard.Result.Name}' via Installer Wizard.");
            _serverManager.SaveConfig(ConfigPath);
        }
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        LoadAndPopulate();
    }

    private void BtnStartAll_Click(object sender, RoutedEventArgs e)
    {
        // Skip remote servers — they have no local process to start.
        foreach (var s in _serverManager.Servers.Where(s => !s.IsRemote))
            TryStartServer(s);
    }

    private void BtnStopAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var s in _serverManager.Servers.Where(s => !s.IsRemote))
            TryStopServer(s.Name);
    }

    private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _serverManager.SaveConfig(ConfigPath);
            Log($"Configuration saved to {ConfigPath}.");
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Save failed: {ex.Message}");
        }
    }

    // ─── SteamCMD setup (toolbar button — on-demand) ──────────────────────
    private void BtnSetupSteamCmd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new FirstRunSetupDialog(_steamCmdService) { Owner = this };
        dlg.ShowDialog();
        if (!string.IsNullOrEmpty(dlg.ResolvedSteamCmdPath))
            Log($"[Setup] SteamCMD configured: {dlg.ResolvedSteamCmdPath}");
    }

    /// <summary>
    /// Ensures SteamCMD is available, prompting the user to install it if not.
    /// Returns true when SteamCMD is ready to use, false when the user aborted.
    /// </summary>
    private bool EnsureSteamCmdAvailable()
    {
        if (_steamCmdService.IsSteamCmdInstalled()) return true;

        Log("[Setup] SteamCMD not found — opening setup …");
        var dlg = new FirstRunSetupDialog(_steamCmdService) { Owner = this };
        dlg.ShowDialog();

        if (!string.IsNullOrEmpty(dlg.ResolvedSteamCmdPath))
        {
            Log($"[Setup] SteamCMD configured: {dlg.ResolvedSteamCmdPath}");
            return true;
        }

        Log("[WARN] SteamCMD not configured — install/update aborted.");
        MessageBox.Show(
            "SteamCMD is required to install/update this server.\n\n" +
            "Use the '⚙ SteamCMD' toolbar button to configure it.",
            "SteamCMD Required", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private async void BtnInstallSteamCmd_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null) { Log("No server selected."); return; }

        // Minecraft Java — download via Mojang API, no SteamCMD needed
        if (IsMinecraftServer(_selectedConfig))
        {
            if (string.IsNullOrWhiteSpace(_selectedConfig.Dir))
            {
                Log("[WARN] Set the server directory on the Config tab before downloading.");
                MessageBox.Show("Set the server directory on the Config tab before downloading.",
                    "Directory Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var mcProgress = new Progress<string>(msg => Dispatcher.InvokeAsync(() => Log($"[Minecraft] {msg}")));
            Log($"[Minecraft] Downloading server to {_selectedConfig.Dir} …");
            var ok = await _minecraftService.DownloadServerAsync(_selectedConfig.Dir, mcProgress);
            if (!ok) Log("[ERROR] Minecraft download failed — see log above.");
            return;
        }

        // Vintage Story — download via Anego Studios CDN, no SteamCMD needed
        if (IsVintageStoryServer(_selectedConfig))
        {
            if (string.IsNullOrWhiteSpace(_selectedConfig.Dir))
            {
                Log("[WARN] Set the server directory on the Config tab before downloading.");
                MessageBox.Show("Set the server directory on the Config tab before downloading.",
                    "Directory Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var vsProgress = new Progress<string>(msg => Dispatcher.InvokeAsync(() => Log($"[VintageStory] {msg}")));
            Log($"[VintageStory] Downloading server to {_selectedConfig.Dir} …");
            var ok = await _vintageStoryService.DownloadServerAsync(_selectedConfig.Dir, vsProgress);
            if (!ok) Log("[ERROR] Vintage Story download failed — see log above.");
            return;
        }

        // All other local servers — ensure SteamCMD is installed, then deploy
        if (!EnsureSteamCmdAvailable()) return;

        var notification = new SteamCmdNotificationDialog { Owner = this };
        if (notification.ShowDialog() != true) return;

        var progress = new Progress<string>(msg => Dispatcher.InvokeAsync(() => Log($"[SteamCMD] {msg}")));
        Log($"[SteamCMD] Starting install/update for {_selectedConfig.Name}...");
        var success = await _steamCmdService.InstallOrUpdateServer(_selectedConfig, progress);
        if (!success) Log("[ERROR] SteamCMD install/update failed — see log above.");
    }

    /// <summary>Returns true when the config represents a Minecraft Java server (no SteamCMD).</summary>
    private static bool IsMinecraftServer(ServerConfig cfg)
        => cfg.ServerType == "Minecraft Java" ||
           (string.IsNullOrEmpty(cfg.ServerType) &&
            cfg.AppId == 0 &&
            cfg.Executable.Equals("java", StringComparison.OrdinalIgnoreCase) &&
            cfg.LaunchArgs.Contains("server.jar", StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns true when the config represents a Vintage Story server (no SteamCMD).</summary>
    private static bool IsVintageStoryServer(ServerConfig cfg)
        => cfg.ServerType == "Vintage Story" ||
           (string.IsNullOrEmpty(cfg.ServerType) &&
            cfg.AppId == 0 &&
            cfg.Executable.Contains("VintagestoryServer", StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the correct label for the install/download button based on server type.</summary>
    private static string GetInstallButtonLabel(ServerConfig cfg)
    {
        if (IsMinecraftServer(cfg))   return "⬇ Download Minecraft Server";
        if (IsVintageStoryServer(cfg)) return "⬇ Download Vintage Story Server";
        return "⬇ Install/Update via SteamCMD";
    }

    // ─── Dashboard toggle ─────────────────────────────────────────────────
    private void BtnToggleView_Click(object sender, RoutedEventArgs e)
    {
        _isDashboardMode = !_isDashboardMode;

        if (_isDashboardMode)
        {
            DashboardView.Visibility = Visibility.Visible;
            ConfigView.Visibility    = Visibility.Collapsed;
            BtnToggleView.Content    = "⚙ Config Mode";
            RebuildDashboard();
        }
        else
        {
            DashboardView.Visibility = Visibility.Collapsed;
            ConfigView.Visibility    = Visibility.Visible;
            BtnToggleView.Content    = "📊 Dashboard";
        }
    }

    // ─── Dashboard ────────────────────────────────────────────────────────
    private void RebuildDashboard()
    {
        DashboardPanel.Children.Clear();
        RemoteDashboardPanel.Children.Clear();

        var localServers  = _serverManager.Servers.Where(s => !s.IsRemote).ToList();
        var remoteServers = _serverManager.Servers.Where(s =>  s.IsRemote).ToList();

        if (localServers.Count == 0)
        {
            var empty = new Border
            {
                Background      = (Brush)FindResource("ControlBgBrush"),
                BorderBrush     = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(8),
                Padding         = new Thickness(24, 20, 24, 20),
                Margin          = new Thickness(0, 8, 0, 0)
            };
            var emptyStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            emptyStack.Children.Add(new TextBlock
            {
                Text      = "🧙",
                FontSize  = 40,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin    = new Thickness(0, 0, 0, 8)
            });
            emptyStack.Children.Add(new TextBlock
            {
                Text      = "No servers yet",
                FontSize  = 16,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            emptyStack.Children.Add(new TextBlock
            {
                Text      = "Click '🧙 Add Server' to launch the Server Installer Wizard",
                FontSize  = 12,
                Foreground = (Brush)FindResource("DimForegroundBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin    = new Thickness(0, 4, 0, 0)
            });
            empty.Child = emptyStack;
            DashboardPanel.Children.Add(empty);
        }

        // ── LOCAL servers grouped by cluster ─────────────────────────────
        var groups = localServers
            .GroupBy(s => string.IsNullOrEmpty(s.Group) ? "" : s.Group)
            .OrderBy(g => string.IsNullOrEmpty(g.Key) ? "ZZZZZ" : g.Key);

        foreach (var group in groups)
        {
            // ── Group container ──
            var groupContainer = new Border
            {
                Background      = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                BorderBrush     = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(10),
                Margin          = new Thickness(0, 8, 0, 0),
                Padding         = new Thickness(12, 8, 12, 12)
            };

            var groupStack = new StackPanel();

            // ── Group header ──
            var groupKey = string.IsNullOrEmpty(group.Key) ? "Standalone" : group.Key;
            var headerPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

            var accentBar = new Border
            {
                Width           = 3,
                Background      = (Brush)FindResource(
                    string.IsNullOrEmpty(group.Key) ? "BorderBrush" : "AccentBrush"),
                CornerRadius    = new CornerRadius(1.5),
                Margin          = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var groupLabel = new TextBlock
            {
                Text       = groupKey.ToUpperInvariant(),
                FontWeight = FontWeights.Bold,
                FontSize   = 10,
                Foreground = string.IsNullOrEmpty(group.Key)
                    ? (Brush)FindResource("DimForegroundBrush")
                    : (Brush)FindResource("AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            var serverCount = new TextBlock
            {
                Text       = $"{group.Count()} server{(group.Count() == 1 ? "" : "s")}",
                FontSize   = 10,
                Foreground = (Brush)FindResource("DimForegroundBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };

            DockPanel.SetDock(accentBar, Dock.Left);
            DockPanel.SetDock(groupLabel, Dock.Left);
            headerPanel.Children.Add(accentBar);
            headerPanel.Children.Add(groupLabel);
            headerPanel.Children.Add(serverCount);

            groupStack.Children.Add(headerPanel);

            // ── Server badges in a wrap panel ──
            var badgeRow = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var cfg in group.OrderBy(s => s.Name))
                badgeRow.Children.Add(BuildServerBadge(cfg));

            groupStack.Children.Add(badgeRow);
            groupContainer.Child = groupStack;
            DashboardPanel.Children.Add(groupContainer);
        }

        // ── REMOTE servers ────────────────────────────────────────────────
        RemoteDashboardSection.Visibility =
            remoteServers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var cfg in remoteServers.OrderBy(s => s.Name))
            RemoteDashboardPanel.Children.Add(BuildRemoteServerBadge(cfg));
    }

    // ─── Add Remote Server ────────────────────────────────────────────────
    private void BtnAddRemoteServer_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.AddRemoteServerDialog { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result == null) return;

        _serverManager.Servers.Add(dlg.Result);
        PopulateServerList();
        Log($"[Remote] Added remote server: {dlg.Result.Name} ({dlg.Result.Rcon.Host}:{dlg.Result.Rcon.Port})");

        // Immediately refresh dashboard when in dashboard mode
        if (_isDashboardMode) RebuildDashboard();
    }

    /// <summary>Builds a compact badge for a remote (RCON-only) server.</summary>
    private Border BuildRemoteServerBadge(ServerConfig cfg)
    {
        // ── Header ──
        var headerPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        headerPanel.Children.Add(new TextBlock
        {
            Text     = "🌐",
            FontSize = 20,
            Margin   = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        var remoteLabel = new TextBlock
        {
            Text       = "REMOTE",
            FontSize   = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentBrush"),
            Margin     = new Thickness(0, 0, 0, 1)
        };
        var nameBlock = new StackPanel();
        nameBlock.Children.Add(new TextBlock
        {
            Text         = cfg.Name,
            FontWeight   = FontWeights.SemiBold,
            FontSize     = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth     = 130
        });
        nameBlock.Children.Add(remoteLabel);

        DockPanel.SetDock(nameBlock, Dock.Left);
        headerPanel.Children.Add(nameBlock);

        // ── Connection info ──
        var infoPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        infoPanel.Children.Add(new TextBlock
        {
            Text       = $"Host:  {cfg.Rcon.Host}",
            FontSize   = 11,
            Foreground = (Brush)FindResource("DimForegroundBrush")
        });
        infoPanel.Children.Add(new TextBlock
        {
            Text       = $"RCON: {cfg.Rcon.Port}",
            FontSize   = 11,
            Foreground = (Brush)FindResource("DimForegroundBrush")
        });

        // ── Action buttons ──
        var btnConfigure = new Button
        {
            Content = "⚙ Configure",
            Padding = new Thickness(8, 4, 8, 4),
            ToolTip = "Edit RCON settings (Config mode)"
        };
        btnConfigure.Click += (_, _) =>
        {
            _isDashboardMode = false;
            DashboardView.Visibility = Visibility.Collapsed;
            ConfigView.Visibility    = Visibility.Visible;
            BtnToggleView.Content    = "📊 Dashboard";
            SelectServer(cfg);
        };

        var buttonRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        buttonRow.Children.Add(btnConfigure);

        // ── Assemble badge ──
        var stack = new StackPanel { Margin = new Thickness(8) };
        stack.Children.Add(headerPanel);
        stack.Children.Add(infoPanel);
        stack.Children.Add(buttonRow);

        var badge = new Border
        {
            Width           = 210,
            Background      = (Brush)FindResource("PanelBgBrush"),
            BorderBrush     = (Brush)FindResource("AccentBrush"),   // accent border distinguishes remote
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(8),
            Margin          = new Thickness(0, 0, 10, 10),
            Child           = stack
        };
        badge.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black, Direction = 270,
            ShadowDepth = 2, BlurRadius = 6, Opacity = 0.3
        };
        return badge;
    }

    private Border BuildServerBadge(ServerConfig cfg)
    {
        var status     = _serverManager.GetStatus(cfg.Name);
        var statusBrush = GetStatusBrush(cfg.Name);
        var isRunning  = _serverManager.IsRunning(cfg.Name);
        var memMb      = isRunning ? _serverManager.GetMemoryMb(cfg.Name) : 0;
        var cpuPct     = isRunning ? _serverManager.GetCpuPercent(cfg.Name) : 0;

        // Find template icon — fall back to name-match for AppId=0 templates
        var template = ServerTemplates.All.FirstOrDefault(t =>
            (t.AppId != 0 && t.AppId == cfg.AppId) ||
            (!string.IsNullOrEmpty(cfg.ServerType) && t.Name == cfg.ServerType));
        var icon = template?.Icon ?? "🖥️";

        // ── Status indicator bar (colored top strip) ──
        var statusBar = new Border
        {
            Height          = 4,
            Background      = statusBrush,
            CornerRadius    = new CornerRadius(6, 6, 0, 0),
            Margin          = new Thickness(-1, -1, -1, 0)
        };

        // ── Header row ──
        var headerPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        headerPanel.Children.Add(new TextBlock
        {
            Text     = icon,
            FontSize = 24,
            Margin   = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        var nameBlock = new StackPanel();
        nameBlock.Children.Add(new TextBlock
        {
            Text       = cfg.Name,
            FontWeight = FontWeights.SemiBold,
            FontSize   = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth   = 140
        });
        var statusLabel = new StackPanel { Orientation = Orientation.Horizontal };
        statusLabel.Children.Add(new Ellipse
        {
            Width  = 7, Height = 7,
            Fill   = statusBrush,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        statusLabel.Children.Add(new TextBlock
        {
            Text       = status.ToString(),
            FontSize   = 11,
            Foreground = statusBrush
        });
        nameBlock.Children.Add(statusLabel);
        DockPanel.SetDock(nameBlock, Dock.Left);
        headerPanel.Children.Add(nameBlock);

        // ── Metrics ──
        var metricsPanel = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        metricsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metricsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metricsPanel.RowDefinitions.Add(new RowDefinition());
        metricsPanel.RowDefinitions.Add(new RowDefinition());

        void AddMetric(string label, string val, int row, int col)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 4, 4) };
            sp.Children.Add(new TextBlock { Text = label, FontSize = 10,
                Foreground = (Brush)FindResource("DimForegroundBrush") });
            sp.Children.Add(new TextBlock { Text = val, FontSize = 12, FontWeight = FontWeights.SemiBold });
            Grid.SetRow(sp, row);
            Grid.SetColumn(sp, col);
            metricsPanel.Children.Add(sp);
        }

        AddMetric("CPU",     isRunning ? $"{cpuPct:F1}%" : "—",    0, 0);
        AddMetric("Memory",  isRunning ? $"{memMb} MB"  : "—",    0, 1);
        AddMetric("Players", cfg.MaxPlayers > 0 ? $"/ {cfg.MaxPlayers}" : "—", 1, 0);
        AddMetric("Uptime",  isRunning && _serverManager.GetUptime(cfg.Name) is TimeSpan up
            ? FormatTimeSpan(up) : "—", 1, 1);

        // ── Action buttons ──
        var btnStart = new Button
        {
            Content = "▶",
            Padding = new Thickness(8, 4, 8, 4),
            Style   = (Style)FindResource("AccentButton"),
            Margin  = new Thickness(0, 0, 4, 0),
            ToolTip = "Start"
        };
        btnStart.Click += (_, _) => TryStartServer(cfg);

        var btnStop = new Button
        {
            Content = "■",
            Padding = new Thickness(8, 4, 8, 4),
            Style   = (Style)FindResource("DangerButton"),
            Margin  = new Thickness(0, 0, 4, 0),
            ToolTip = "Stop"
        };
        btnStop.Click += (_, _) => TryStopServer(cfg.Name);

        var btnBackup = new Button
        {
            Content = "💾",
            Padding = new Thickness(8, 4, 8, 4),
            Margin  = new Thickness(0, 0, 4, 0),
            ToolTip = "Backup"
        };
        btnBackup.Click += (_, _) =>
        {
            try
            {
                var path = _backupService.CreateBackup(cfg);
                Log($"[Backup] {cfg.Name}: {path}");
            }
            catch (Exception ex) { Log($"[ERROR] Backup {cfg.Name}: {ex.Message}"); }
        };

        var btnConfigure = new Button
        {
            Content = "⚙",
            Padding = new Thickness(8, 4, 8, 4),
            ToolTip = "Configure (switch to Config mode)"
        };
        btnConfigure.Click += (_, _) =>
        {
            _isDashboardMode = false;
            DashboardView.Visibility = Visibility.Collapsed;
            ConfigView.Visibility    = Visibility.Visible;
            BtnToggleView.Content    = "📊 Dashboard";
            SelectServer(cfg);
        };

        var buttonRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        buttonRow.Children.Add(btnStart);
        buttonRow.Children.Add(btnStop);
        buttonRow.Children.Add(btnBackup);
        buttonRow.Children.Add(btnConfigure);

        // ── Assemble badge content ──
        var contentStack = new StackPanel { Margin = new Thickness(10, 8, 10, 10) };
        contentStack.Children.Add(headerPanel);
        contentStack.Children.Add(metricsPanel);
        contentStack.Children.Add(buttonRow);

        // ── Outer container with status bar at top ──
        var outerStack = new StackPanel();
        outerStack.Children.Add(statusBar);
        outerStack.Children.Add(contentStack);

        var badge = new Border
        {
            Width           = 220,
            Background      = (Brush)FindResource("PanelBgBrush"),
            BorderBrush     = statusBrush,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(8),
            Margin          = new Thickness(0, 0, 10, 10),
            Child           = outerStack,
            ClipToBounds    = true
        };
        badge.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black, Direction = 270,
            ShadowDepth = 3, BlurRadius = 8, Opacity = 0.4
        };

        return badge;
    }

    // ─── Template application ─────────────────────────────────────────────
    private void BtnApplyTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ServerTemplate tmpl)
        {
            if (_selectedConfig == null)
            {
                Log("Select or create a server first, then choose a template.");
                return;
            }

            var result = MessageBox.Show(
                $"Apply template '{tmpl.Name}'?\nThis will overwrite the current config fields.",
                "Apply Template", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            // Fill form fields from template
            CfgAppId.Text        = tmpl.AppId > 0 ? tmpl.AppId.ToString() : "";
            CfgExecutable.Text   = tmpl.Executable;
            CfgLaunchArgs.Text   = tmpl.LaunchArgs;
            if (!string.IsNullOrEmpty(tmpl.DefaultDir))
            {
                // DefaultDir is a relative subfolder name — resolve under the local Servers dir.
                CfgDir.Text = Path.Combine(ServersBaseDir, tmpl.DefaultDir);
            }
            CfgRconHost.Text     = tmpl.RconHost;
            CfgRconPort.Text     = tmpl.RconPort.ToString();
            CfgQueryPort.Text    = tmpl.QueryPort.ToString();
            CfgMaxPlayers.Text   = tmpl.MaxPlayers.ToString();
            if (!string.IsNullOrEmpty(tmpl.Group))
                CfgGroup.Text    = tmpl.Group;

            // Stamp server type so the install button routes correctly (non-SteamCMD templates).
            if (!tmpl.RequiresSteamCmd)
                _selectedConfig.ServerType = tmpl.Name;

            // Update install/download button label immediately.
            BtnInstallOrDownload.Content = GetInstallButtonLabel(_selectedConfig);

            Log($"Template '{tmpl.Name}' applied — review fields and click 'Apply Config Changes'.");
        }
    }

    // ─── Config Files (INI editor) tab ────────────────────────────────────
    private void TabConfigFiles_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig != null)
            LoadIniFileTabs(_selectedConfig);
    }

    private void BtnRescanConfigFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null) return;
        TxtConfigFileFilter.Text = "";
        LoadIniFileTabs(_selectedConfig);
    }

    /// <summary>Browse for a specific config file and add it as a tab.</summary>
    private void BtnBrowseConfigFile_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null) { Log("No server selected."); return; }

        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Open Config File",
            Filter = "Config files (*.ini;*.cfg;*.conf;*.properties;*.toml;*.yaml;*.yml;*.json;*.xml;*.txt)" +
                     "|*.ini;*.cfg;*.conf;*.properties;*.toml;*.yaml;*.yml;*.json;*.xml;*.txt" +
                     "|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(_selectedConfig.Dir) ? _selectedConfig.Dir : ""
        };

        if (ofd.ShowDialog() != true) return;

        var fileInfo = _iniFileService.ResolveManualFile(ofd.FileName, _selectedConfig.Dir);
        if (fileInfo == null)
        {
            Log($"[ERROR] Cannot open file: {ofd.FileName}");
            return;
        }

        // Add to the known list if not already there, then show the tab
        if (!_allConfigFiles.Any(f => f.Path.Equals(fileInfo.Path, StringComparison.OrdinalIgnoreCase)))
            _allConfigFiles.Add(fileInfo);

        // Switch filter off so the new file is visible
        TxtConfigFileFilter.Text = "";
        RenderConfigFileTabs(_selectedConfig, _allConfigFiles, fileInfo.Path);
        Log($"[Config] Opened: {fileInfo.RelativePath}");
    }

    private void TxtConfigFileFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_selectedConfig == null || _allConfigFiles.Count == 0) return;

        var filter   = TxtConfigFileFilter.Text.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allConfigFiles
            : _allConfigFiles
                .Where(f => f.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            f.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        RenderConfigFileTabs(_selectedConfig, filtered, null);
    }

    private void LoadIniFileTabs(ServerConfig cfg)
    {
        _iniEditorEntries.Clear();

        if (string.IsNullOrEmpty(cfg.Dir) || !Directory.Exists(cfg.Dir))
        {
            _allConfigFiles = new List<IniFileInfo>();
            ShowConfigFilePlaceholder("No directory set",
                $"Server directory is not configured or does not exist.\n\nSet a directory on the Config tab, then click ↻ Rescan.");
            UpdateConfigFileCount(0, 0);
            return;
        }

        // Discover key=value files and text files
        var kvFiles   = _iniFileService.GetConfigFiles(cfg.Dir);
        var textFiles = _iniFileService.GetTextFiles(cfg.Dir);

        // Merge, avoiding duplicates
        _allConfigFiles = kvFiles.ToList();
        foreach (var tf in textFiles)
        {
            if (!_allConfigFiles.Any(f => f.Path.Equals(tf.Path, StringComparison.OrdinalIgnoreCase)))
                _allConfigFiles.Add(tf);
        }

        var filter = TxtConfigFileFilter?.Text?.Trim() ?? "";
        var visible = string.IsNullOrEmpty(filter)
            ? _allConfigFiles
            : _allConfigFiles
                .Where(f => f.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            f.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        UpdateConfigFileCount(_allConfigFiles.Count, visible.Count);
        RenderConfigFileTabs(cfg, visible, null);
    }

    private void UpdateConfigFileCount(int total, int visible)
    {
        if (TxtConfigFileCount == null) return;
        TxtConfigFileCount.Text = total == 0
            ? ""
            : visible == total
                ? $"({total} file{(total == 1 ? "" : "s")} found)"
                : $"({visible} of {total} shown)";
    }

    private void ShowConfigFilePlaceholder(string header, string message)
    {
        IniFileTabs.Items.Clear();
        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        panel.Children.Add(new TextBlock
        {
            Text       = "📄",
            FontSize   = 36,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin     = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new TextBlock
        {
            Text       = header,
            FontSize   = 14,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text       = message,
            FontSize   = 12,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = (Brush)FindResource("DimForegroundBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin     = new Thickness(0, 6, 0, 0),
            MaxWidth   = 500
        });
        var tab = new TabItem { Header = header, Content = panel };
        IniFileTabs.Items.Add(tab);
    }

    /// <summary>
    /// Renders the given list of config files as tabs.  Re-selects <paramref name="preferredPath"/> if provided.
    /// </summary>
    private void RenderConfigFileTabs(ServerConfig cfg, IEnumerable<IniFileInfo> files, string? preferredPath)
    {
        IniFileTabs.Items.Clear();

        var fileList = files.ToList();

        if (fileList.Count == 0)
        {
            if (_allConfigFiles.Count == 0)
            {
                ShowConfigFilePlaceholder(
                    "No config files found",
                    "No *.ini, *.cfg, *.properties, *.toml, *.yaml, or similar files were found in the server directory.\n\n" +
                    "Use '📂 Open Specific File…' to browse to the file you need, or make sure the server is installed first.");
            }
            else
            {
                ShowConfigFilePlaceholder(
                    "No matches",
                    "No files match the current filter. Clear the filter box to show all files.");
            }
            UpdateConfigFileCount(_allConfigFiles.Count, 0);
            return;
        }

        int selectedIdx = 0;
        for (int i = 0; i < fileList.Count; i++)
        {
            var fileInfo = fileList[i];

            // Determine whether this is a plain key=value file or a raw text file
            var isKvFile = IsKeyValueFile(fileInfo.Path);

            TabItem tab;
            if (isKvFile)
            {
                // Parse and show the INI editor
                if (!_iniEditorEntries.TryGetValue(fileInfo.Path, out var entries))
                {
                    entries = _iniFileService.ParseFile(fileInfo.Path);
                    _iniEditorEntries[fileInfo.Path] = entries;
                }
                tab = new TabItem
                {
                    Header  = BuildConfigTabHeader(fileInfo),
                    ToolTip = fileInfo.Path,
                    Content = BuildIniEditorPanel(fileInfo.Path, entries, cfg)
                };
            }
            else
            {
                // Show as raw text viewer
                tab = new TabItem
                {
                    Header  = BuildConfigTabHeader(fileInfo),
                    ToolTip = fileInfo.Path,
                    Content = BuildRawTextPanel(fileInfo.Path, cfg)
                };
            }

            IniFileTabs.Items.Add(tab);

            if (preferredPath != null &&
                fileInfo.Path.Equals(preferredPath, StringComparison.OrdinalIgnoreCase))
                selectedIdx = i;
        }

        if (IniFileTabs.Items.Count > 0)
            IniFileTabs.SelectedIndex = selectedIdx;

        UpdateConfigFileCount(_allConfigFiles.Count, fileList.Count);
    }

    /// <summary>Builds a rich tab header showing the filename and, if in a subdirectory, the relative sub-path.</summary>
    private object BuildConfigTabHeader(IniFileInfo fileInfo)
    {
        // If the file is at the root of the server dir the relative path equals the filename
        var subDir = Path.GetDirectoryName(fileInfo.RelativePath);
        if (string.IsNullOrEmpty(subDir))
            return fileInfo.FileName;

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text     = fileInfo.FileName,
            FontSize = 12
        });
        panel.Children.Add(new TextBlock
        {
            Text       = subDir,
            FontSize   = 9,
            Foreground = (Brush)FindResource("DimForegroundBrush")
        });
        return panel;
    }

    /// <summary>Returns true when a file extension indicates a parseable key=value config.</summary>
    private static bool IsKeyValueFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".ini" or ".cfg" or ".conf" or ".config" or ".properties" or ".toml" or ".yaml" or ".yml";
    }

    /// <summary>Builds a raw text viewer panel for JSON / XML / TXT files.</summary>
    private UIElement BuildRawTextPanel(string filePath, ServerConfig cfg)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ── Toolbar ──
        var toolbar = new DockPanel { Margin = new Thickness(8, 8, 8, 4) };

        var fileLabel = new TextBlock
        {
            Text              = Path.GetFileName(filePath),
            FontSize          = 11,
            Foreground        = (Brush)FindResource("DimForegroundBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 8, 0)
        };
        DockPanel.SetDock(fileLabel, Dock.Left);
        toolbar.Children.Add(fileLabel);

        var btnOpenExternally = new Button
        {
            Content = "↗ Open in Editor",
            Padding = new Thickness(10, 4, 10, 4),
            ToolTip = "Open this file in your default text editor"
        };
        btnOpenExternally.Click += (_, _) =>
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex) { Log($"[ERROR] Could not open file: {ex.Message}"); }
        };
        DockPanel.SetDock(btnOpenExternally, Dock.Right);
        toolbar.Children.Add(btnOpenExternally);

        Grid.SetRow(toolbar, 0);
        root.Children.Add(toolbar);

        // ── Content ──
        string content;
        try   { content = File.ReadAllText(filePath); }
        catch (Exception ex) { content = $"[ERROR] Could not read file: {ex.Message}"; }

        var txtBox = new TextBox
        {
            Text                     = content,
            IsReadOnly               = true,
            TextWrapping             = TextWrapping.NoWrap,
            AcceptsReturn            = true,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily               = new System.Windows.Media.FontFamily("Consolas"),
            FontSize                 = 12,
            Background               = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            Foreground               = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)),
            BorderThickness          = new Thickness(0),
            Padding                  = new Thickness(8)
        };
        Grid.SetRow(txtBox, 1);
        root.Children.Add(txtBox);

        return root;
    }

    private UIElement BuildIniEditorPanel(string filePath, List<IniEntry> entries, ServerConfig cfg)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // top toolbar
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // search bar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // entries
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // bottom status

        // ── Top Toolbar ──
        var toolbar = new DockPanel { Margin = new Thickness(8, 8, 8, 4) };

        // File path label (left)
        var filePathLabel = new TextBlock
        {
            Text              = filePath,
            FontSize          = 10,
            Foreground        = (Brush)FindResource("DimForegroundBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            MaxWidth          = 300,
            ToolTip           = filePath,
            Margin            = new Thickness(0, 0, 8, 0)
        };
        DockPanel.SetDock(filePathLabel, Dock.Left);
        toolbar.Children.Add(filePathLabel);

        var historyCombo = new ComboBox
        {
            Width = 200, Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Revert to a previous version"
        };
        var history = _iniFileService.GetHistory(filePath, cfg.Dir);
        foreach (var h in history)
            historyCombo.Items.Add(new ComboBoxItem { Content = Path.GetFileName(h), Tag = h });
        DockPanel.SetDock(historyCombo, Dock.Right);
        toolbar.Children.Add(historyCombo);

        var btnRevert = new Button
        {
            Content = "↩ Revert",
            Margin  = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(10, 4, 10, 4)
        };
        btnRevert.Click += (_, _) =>
        {
            if (historyCombo.SelectedItem is ComboBoxItem ci && ci.Tag is string histPath)
            {
                var r = MessageBox.Show(
                    $"Revert '{Path.GetFileName(filePath)}' to:\n{Path.GetFileName(histPath)}?",
                    "Confirm Revert", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
                try
                {
                    _iniFileService.RevertToHistory(filePath, histPath);
                    Log($"[INI] Reverted {Path.GetFileName(filePath)} to {Path.GetFileName(histPath)}.");
                    LoadIniFileTabs(cfg);
                }
                catch (Exception ex) { Log($"[ERROR] Revert: {ex.Message}"); }
            }
            else
            {
                MessageBox.Show("Select a history version from the dropdown first.",
                    "No version selected", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        DockPanel.SetDock(btnRevert, Dock.Right);
        toolbar.Children.Add(btnRevert);

        var btnSave = new Button
        {
            Content = "💾 Save Changes",
            Style   = (Style)FindResource("AccentButton"),
            Margin  = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(10, 4, 10, 4)
        };
        btnSave.Click += (_, _) =>
        {
            try
            {
                _iniFileService.SaveFile(filePath, _iniEditorEntries[filePath]);
                Log($"[INI] Saved {Path.GetFileName(filePath)} (previous version archived).");
                historyCombo.Items.Clear();
                foreach (var h in _iniFileService.GetHistory(filePath, cfg.Dir))
                    historyCombo.Items.Add(new ComboBoxItem { Content = Path.GetFileName(h), Tag = h });
            }
            catch (Exception ex) { Log($"[ERROR] Save INI: {ex.Message}"); }
        };
        DockPanel.SetDock(btnSave, Dock.Right);
        toolbar.Children.Add(btnSave);

        var btnOpenExternally = new Button
        {
            Content = "↗ Open Externally",
            Margin  = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(10, 4, 10, 4),
            ToolTip = "Open this file in your default text editor"
        };
        btnOpenExternally.Click += (_, _) =>
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex) { Log($"[ERROR] Could not open file: {ex.Message}"); }
        };
        DockPanel.SetDock(btnOpenExternally, Dock.Right);
        toolbar.Children.Add(btnOpenExternally);

        Grid.SetRow(toolbar, 0);
        root.Children.Add(toolbar);

        // ── Entry search bar ──
        var searchBar = new DockPanel { Margin = new Thickness(8, 0, 8, 6) };

        var entryCountLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize          = 11,
            Foreground        = (Brush)FindResource("DimForegroundBrush"),
            Margin            = new Thickness(8, 0, 0, 0)
        };
        entryCountLabel.Text = $"{entries.Count} setting{(entries.Count == 1 ? "" : "s")}";
        DockPanel.SetDock(entryCountLabel, Dock.Right);
        searchBar.Children.Add(entryCountLabel);

        var searchBox = new TextBox
        {
            Tag    = "Search settings (key or value)…",
            Margin = new Thickness(0, 0, 0, 0)
        };
        searchBar.Children.Add(searchBox);

        Grid.SetRow(searchBar, 1);
        root.Children.Add(searchBar);

        // ── Entry list ──
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack  = new StackPanel { Margin = new Thickness(8) };

        void RenderEntries(string filter)
        {
            stack.Children.Clear();
            var filtered = string.IsNullOrEmpty(filter)
                ? entries
                : entries.Where(e =>
                    e.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    e.Value.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    e.Section.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            entryCountLabel.Text = string.IsNullOrEmpty(filter)
                ? $"{entries.Count} setting{(entries.Count == 1 ? "" : "s")}"
                : $"{filtered.Count} of {entries.Count} shown";

            string? lastSection = null;
            foreach (var entry in filtered)
            {
                if (entry.Section != lastSection)
                {
                    if (lastSection != null)
                        stack.Children.Add(new Separator { Margin = new Thickness(0, 6, 0, 6) });
                    if (!string.IsNullOrEmpty(entry.Section))
                        stack.Children.Add(new TextBlock
                        {
                            Text       = $"[{entry.Section}]",
                            FontWeight = FontWeights.SemiBold,
                            FontSize   = 11,
                            Foreground = (Brush)FindResource("AccentBrush"),
                            Margin     = new Thickness(0, 4, 0, 4)
                        });
                    lastSection = entry.Section;
                }
                stack.Children.Add(BuildIniEntryRow(entry, _iniEditorEntries[filePath]));
            }

            if (filtered.Count == 0)
                stack.Children.Add(new TextBlock
                {
                    Text       = "No matching settings.",
                    Foreground = (Brush)FindResource("DimForegroundBrush"),
                    Margin     = new Thickness(8)
                });
        }

        // Initial render
        RenderEntries("");

        searchBox.TextChanged += (_, _) => RenderEntries(searchBox.Text.Trim());

        scroll.Content = stack;
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        return root;
    }

    private static readonly HashSet<string> BoolTrueValues  = new(StringComparer.OrdinalIgnoreCase)
        { "true", "yes", "1", "on", "enabled" };
    private static readonly HashSet<string> BoolFalseValues = new(StringComparer.OrdinalIgnoreCase)
        { "false", "no", "0", "off", "disabled" };

    private UIElement BuildIniEntryRow(IniEntry entry, List<IniEntry> liveList)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var keyLabel = new TextBlock
        {
            Text      = entry.Key,
            ToolTip   = $"Section: [{entry.Section}]  Line: {entry.LineNumber}",
            VerticalAlignment = VerticalAlignment.Center,
            Margin    = new Thickness(0, 0, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(keyLabel, 0);
        row.Children.Add(keyLabel);

        // Determine best control type
        bool isBool = BoolTrueValues.Contains(entry.Value) || BoolFalseValues.Contains(entry.Value);
        int intVal  = 0;
        bool isInt  = !isBool && int.TryParse(entry.Value, out intVal);

        UIElement valueControl;

        if (isBool)
        {
            // Capture the original value format once (numeric "1"/"0" vs text "True"/"False")
            bool usesNumericFormat = entry.Value == "1" || entry.Value == "0";
            string trueVal  = usesNumericFormat ? "1" : "True";
            string falseVal = usesNumericFormat ? "0" : "False";

            var toggle = new CheckBox
            {
                IsChecked = BoolTrueValues.Contains(entry.Value),
                VerticalAlignment = VerticalAlignment.Center
            };
            toggle.Checked   += (_, _) => UpdateEntry(liveList, entry, trueVal);
            toggle.Unchecked += (_, _) => UpdateEntry(liveList, entry, falseVal);
            valueControl = toggle;
        }
        else if (isInt)
        {
            var panel = new DockPanel();
            var slider = new Slider
            {
                Minimum = 0, Maximum = Math.Max(intVal * 4, 1000),
                Value = intVal, Width = 120,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var numBox = new TextBox
            {
                Text  = intVal.ToString(),
                Width = 70,
                VerticalAlignment = VerticalAlignment.Center
            };
            slider.ValueChanged += (_, _) =>
            {
                var v = (int)slider.Value;
                numBox.Text = v.ToString();
                UpdateEntry(liveList, entry, v.ToString());
            };
            numBox.TextChanged += (_, _) =>
            {
                if (int.TryParse(numBox.Text, out var parsed))
                {
                    slider.Value = Math.Min(parsed, slider.Maximum);
                    UpdateEntry(liveList, entry, parsed.ToString());
                }
            };
            DockPanel.SetDock(slider, Dock.Left);
            panel.Children.Add(slider);
            panel.Children.Add(numBox);
            valueControl = panel;
        }
        else
        {
            var textBox = new TextBox
            {
                Text = entry.Value,
                VerticalAlignment = VerticalAlignment.Center
            };
            textBox.TextChanged += (_, _) => UpdateEntry(liveList, entry, textBox.Text);
            valueControl = textBox;
        }

        Grid.SetColumn(valueControl, 1);
        row.Children.Add(valueControl);
        return row;
    }

    private static void UpdateEntry(List<IniEntry> list, IniEntry original, string newValue)
    {
        var idx = list.FindIndex(e => e.Section == original.Section &&
                                      e.Key == original.Key &&
                                      e.LineNumber == original.LineNumber);
        if (idx >= 0)
            list[idx] = original with { Value = newValue };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SIDEBAR BUTTONS
    // ═══════════════════════════════════════════════════════════════════════
    private void BtnAddServer_Click(object sender, RoutedEventArgs e) => BtnNewServer_Click(sender, e);

    private void BtnRemoveServer_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null) return;

        var result = MessageBox.Show(
            $"Remove '{_selectedConfig.Name}' from the list?",
            "Confirm Remove", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        if (_serverManager.IsRunning(_selectedConfig.Name))
            TryStopServer(_selectedConfig.Name);

        _serverManager.Servers.Remove(_selectedConfig);
        _selectedConfig = null;
        PnlDetail.Visibility      = Visibility.Collapsed;
        PnlNoSelection.Visibility = Visibility.Visible;
        PopulateServerList();
        Log("Server removed from list.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SERVER CONTROL BUTTONS
    // ═══════════════════════════════════════════════════════════════════════
    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig != null) TryStartServer(_selectedConfig);
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig != null) TryStopServer(_selectedConfig.Name);
    }

    private void BtnRestart_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null) return;
        Log($"Restarting {_selectedConfig.Name}...");
        try
        {
            _serverManager.RestartServer(_selectedConfig.Name);
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Restart failed: {ex.Message}");
        }
    }

    private void BtnForceKill_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null) return;
        var result = MessageBox.Show(
            $"Force-kill '{_selectedConfig.Name}'? This will terminate the process immediately.",
            "Confirm Force Kill", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            _serverManager.ForceKillServer(_selectedConfig.Name);
            Log($"Force-killed {_selectedConfig.Name}.");
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Force kill '{_selectedConfig.Name}': {ex.Message}");
        }
    }

    private void TryStartServer(ServerConfig cfg)
    {
        var (valid, err) = cfg.Validate();
        if (!valid) { Log($"[WARN] Cannot start '{cfg.Name}': {err}"); return; }

        try
        {
            _serverManager.StartServer(cfg);
            Log($"Started {cfg.Name}.");
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Start '{cfg.Name}': {ex.Message}");
        }
    }

    private void TryStopServer(string name)
    {
        try
        {
            _serverManager.StopServer(name);
            Log($"Stopped {name}.");
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Stop '{name}': {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  OVERVIEW TAB
    // ═══════════════════════════════════════════════════════════════════════
    private void BtnSaveNotes_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null) return;
        _selectedConfig.Notes = TxtOvNotes.Text;
        Log("Notes saved.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONFIG TAB
    // ═══════════════════════════════════════════════════════════════════════
    private void BtnApplyConfig_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null) return;

        _selectedConfig.Name             = CfgName.Text.Trim();
        _selectedConfig.AppId            = int.TryParse(CfgAppId.Text, out var aid) ? aid : 0;
        _selectedConfig.Dir              = CfgDir.Text.Trim();
        _selectedConfig.Executable       = CfgExecutable.Text.Trim();
        _selectedConfig.LaunchArgs       = CfgLaunchArgs.Text;
        _selectedConfig.Rcon.Host        = CfgRconHost.Text.Trim();
        _selectedConfig.Rcon.Port        = int.TryParse(CfgRconPort.Text, out var rp) ? rp : 27020;
        _selectedConfig.Rcon.Password    = CfgRconPassword.Text;
        _selectedConfig.BackupFolder     = CfgBackupFolder.Text.Trim();
        _selectedConfig.KeepBackups      = int.TryParse(CfgKeepBackups.Text, out var kb) ? kb : 10;
        _selectedConfig.BackupIntervalMinutes  = int.TryParse(CfgBackupInterval.Text, out var bi) ? bi : 0;
        _selectedConfig.RestartIntervalHours   = int.TryParse(CfgRestartInterval.Text, out var ri) ? ri : 0;
        _selectedConfig.MaxPlayers             = int.TryParse(CfgMaxPlayers.Text, out var mp) ? mp : 0;
        _selectedConfig.Group                  = CfgGroup.Text.Trim();
        _selectedConfig.DiscordWebhookUrl      = CfgDiscordWebhook.Text.Trim();
        _selectedConfig.GracefulShutdownSeconds = int.TryParse(CfgShutdownSecs.Text, out var gs) ? gs : 15;
        _selectedConfig.QueryPort              = int.TryParse(CfgQueryPort.Text, out var qp) ? qp : 0;
        _selectedConfig.AutoUpdate             = CfgAutoUpdate.IsChecked == true;
        _selectedConfig.AutoStartOnLaunch      = CfgAutoStart.IsChecked == true;
        _selectedConfig.Favorite               = CfgFavorite.IsChecked == true;
        _selectedConfig.BackupBeforeRestart    = CfgBackupBeforeRestart.IsChecked == true;

        _selectedConfig.Tags = CfgTags.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _selectedConfig.RestartWarningMinutes  = int.TryParse(CfgRestartWarnMins.Text, out var rwm) ? rwm : 0;
        _selectedConfig.RestartWarningMessage  = CfgRestartWarnMsg.Text;
        _selectedConfig.CpuAlertThreshold      = double.TryParse(CfgCpuAlert.Text, out var cpu) ? cpu : 90.0;
        _selectedConfig.MemAlertThresholdMB    = long.TryParse(CfgMemAlert.Text, out var mem) ? mem : 0;

        var (valid, err) = _selectedConfig.Validate();
        if (!valid)
        {
            Log($"[WARN] Validation error: {err}");
            MessageBox.Show(err, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TxtDetailTitle.Text = _selectedConfig.Name;
        PopulateServerList(TxtSearch.Text);
        Log($"Config applied for {_selectedConfig.Name}.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MODS TAB
    // ═══════════════════════════════════════════════════════════════════════
    private void RefreshModList(ServerConfig cfg)
    {
        var items = cfg.Mods.Select(id => new ModListItem(id, false))
            .Concat(cfg.DisabledMods.Select(id => new ModListItem(id, true)))
            .OrderBy(m => m.Disabled)
            .ThenBy(m => m.ModId)
            .ToList();
        LbMods.ItemsSource = items;
    }

    private void BtnAddMod_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null) return;
        if (!long.TryParse(TxtModId.Text.Trim(), out var modId)) { Log("Invalid mod ID."); return; }
        if (_selectedConfig.Mods.Contains(modId) || _selectedConfig.DisabledMods.Contains(modId))
        { Log($"Mod {modId} already in list."); return; }

        _selectedConfig.Mods.Add(modId);
        TxtModId.Clear();
        RefreshModList(_selectedConfig);
        Log($"Mod {modId} added.");
    }

    private void BtnRemoveMod_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null || LbMods.SelectedItem is not ModListItem item) return;
        _selectedConfig.Mods.Remove(item.ModId);
        _selectedConfig.DisabledMods.Remove(item.ModId);
        RefreshModList(_selectedConfig);
        Log($"Mod {item.ModId} removed.");
    }

    private void BtnDisableMod_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null || LbMods.SelectedItem is not ModListItem item) return;
        if (item.Disabled)
        {
            _selectedConfig.DisabledMods.Remove(item.ModId);
            _selectedConfig.Mods.Add(item.ModId);
            Log($"Mod {item.ModId} enabled.");
        }
        else
        {
            _selectedConfig.Mods.Remove(item.ModId);
            _selectedConfig.DisabledMods.Add(item.ModId);
            Log($"Mod {item.ModId} disabled.");
        }
        RefreshModList(_selectedConfig);
    }

    private void BtnUpdateMod_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null || LbMods.SelectedItem is not ModListItem item) return;
        var progress = new Progress<string>(msg => Dispatcher.InvokeAsync(() => Log($"[SteamCMD] {msg}")));
        Log($"[SteamCMD] Updating mod {item.ModId} for {_selectedConfig.Name}...");
        _ = _steamCmdService.UpdateMod(_selectedConfig, item.ModId, progress);
    }

    private void BtnBrowseWorkshop_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null) { Log("No server selected."); return; }
        if (_selectedConfig.AppId <= 0)
        {
            Log("[WARN] Server has no App ID set. Configure the App ID on the Config tab first.");
            MessageBox.Show("Set the App ID on the Config tab before browsing the Workshop.",
                "App ID required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var browser = new WorkshopBrowserWindow(_selectedConfig.AppId) { Owner = this };
        if (browser.ShowDialog() != true) return;

        var added = 0;
        foreach (var id in browser.SelectedIds)
        {
            if (_selectedConfig.Mods.Contains(id) || _selectedConfig.DisabledMods.Contains(id))
            {
                Log($"Mod {id} already in list, skipped.");
                continue;
            }
            _selectedConfig.Mods.Add(id);
            added++;
        }

        if (added > 0)
        {
            RefreshModList(_selectedConfig);
            Log($"Added {added} mod(s) from Workshop browser.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SCHEDULED COMMANDS TAB
    // ═══════════════════════════════════════════════════════════════════════
    private void RefreshScheduledList(ServerConfig cfg)
    {
        LbScheduled.ItemsSource = cfg.ScheduledRconCommands
            .Select(c => new ScheduledCommandItem(c))
            .ToList();
    }

    private void BtnAddScheduled_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null) return;
        var cmd = TxtSchedCommand.Text.Trim();
        if (string.IsNullOrEmpty(cmd)) { Log("Scheduled command text is required."); return; }
        if (!int.TryParse(TxtSchedInterval.Text.Trim(), out var mins) || mins <= 0)
        { Log("Interval must be a positive integer (minutes)."); return; }

        _selectedConfig.ScheduledRconCommands.Add(new ScheduledRconCommand
        {
            Command = cmd,
            IntervalMinutes = mins
        });
        TxtSchedCommand.Clear();
        TxtSchedInterval.Clear();
        RefreshScheduledList(_selectedConfig);
        Log($"Scheduled command added: \"{cmd}\" every {mins} min.");
    }

    private void BtnRemoveScheduled_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null || LbScheduled.SelectedItem is not ScheduledCommandItem item) return;
        _selectedConfig.ScheduledRconCommands.Remove(item.Source);
        RefreshScheduledList(_selectedConfig);
        Log($"Scheduled command removed: \"{item.Command}\"");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ENV VARS TAB
    // ═══════════════════════════════════════════════════════════════════════
    private void RefreshEnvVarList(ServerConfig cfg)
    {
        LbEnvVars.ItemsSource = cfg.EnvironmentVariables
            .Select(kv => new EnvVarItem(kv.Key, kv.Value))
            .ToList();
    }

    private void BtnAddEnvVar_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null) return;
        var key = TxtEnvKey.Text.Trim();
        var val = TxtEnvValue.Text;
        if (string.IsNullOrEmpty(key)) { Log("Variable name is required."); return; }

        _selectedConfig.EnvironmentVariables[key] = val;
        TxtEnvKey.Clear();
        TxtEnvValue.Clear();
        RefreshEnvVarList(_selectedConfig);
        Log($"Environment variable set: {key}={val}");
    }

    private void BtnRemoveEnvVar_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null || LbEnvVars.SelectedItem is not EnvVarItem item) return;
        _selectedConfig.EnvironmentVariables.Remove(item.Key);
        RefreshEnvVarList(_selectedConfig);
        Log($"Environment variable removed: {item.Key}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BACKUPS TAB
    // ═══════════════════════════════════════════════════════════════════════
    private void RefreshBackupList(ServerConfig cfg)
    {
        try
        {
            var backups = _backupService.GetBackups(cfg)
                .Select(b => new BackupListItem(b))
                .ToList();
            LbBackups.ItemsSource = backups;
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Could not list backups: {ex.Message}");
        }
    }

    private void BtnCreateBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null) return;
        try
        {
            var path = _backupService.CreateBackup(_selectedConfig);
            Log($"Backup created: {path}");
            RefreshBackupList(_selectedConfig);
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Backup failed: {ex.Message}");
        }
    }

    private void BtnRestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null || LbBackups.SelectedItem is not BackupListItem item) return;

        var result = MessageBox.Show(
            $"Restore backup '{item.Name}'?\nThis will overwrite the server directory.",
            "Confirm Restore", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            _backupService.RestoreBackup(_selectedConfig, item.Path);
            Log($"Backup restored: {item.Name}");
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Restore failed: {ex.Message}");
        }
    }

    private void BtnDeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null || LbBackups.SelectedItem is not BackupListItem item) return;
        try
        {
            File.Delete(item.Path);
            Log($"Backup deleted: {item.Name}");
            RefreshBackupList(_selectedConfig);
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Delete failed: {ex.Message}");
        }
    }

    private void BtnRefreshBackups_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig != null) RefreshBackupList(_selectedConfig);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CONSOLE (RCON) TAB
    // ═══════════════════════════════════════════════════════════════════════
    private async void BtnRconConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null) return;
        _rconClient?.Disconnect();
        _rconClient = new RconClient(
            _selectedConfig.Rcon.Host,
            _selectedConfig.Rcon.Port,
            _selectedConfig.Rcon.Password);

        AppendConsole($"Connecting to {_selectedConfig.Rcon.Host}:{_selectedConfig.Rcon.Port}...");
        var ok = await _rconClient.ConnectAsync();
        AppendConsole(ok ? "Connected." : "Connection failed or authentication rejected.");
    }

    private void BtnRconDisconnect_Click(object sender, RoutedEventArgs e)
    {
        _rconClient?.Disconnect();
        _rconClient = null;
        AppendConsole("Disconnected.");
    }

    private async void BtnSendRcon_Click(object sender, RoutedEventArgs e)
    {
        await SendRconCommand();
    }

    private async void TxtConsoleInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SendRconCommand();
        }
        else if (e.Key == Key.Up && _rconHistory.Count > 0)
        {
            _rconHistoryIndex = Math.Min(_rconHistoryIndex + 1, _rconHistory.Count - 1);
            TxtConsoleInput.Text = _rconHistory[_rconHistoryIndex];
            TxtConsoleInput.CaretIndex = TxtConsoleInput.Text.Length;
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            _rconHistoryIndex = Math.Max(_rconHistoryIndex - 1, -1);
            TxtConsoleInput.Text = _rconHistoryIndex >= 0 ? _rconHistory[_rconHistoryIndex] : "";
            TxtConsoleInput.CaretIndex = TxtConsoleInput.Text.Length;
            e.Handled = true;
        }
    }

    private async Task SendRconCommand()
    {
        var cmd = TxtConsoleInput.Text.Trim();
        if (string.IsNullOrEmpty(cmd)) return;

        if (_rconClient == null || !_rconClient.IsConnected)
        {
            AppendConsole("[WARN] Not connected. Use Connect first.");
            return;
        }

        // Store in history (skip duplicates at the front)
        if (_rconHistory.Count == 0 || _rconHistory[0] != cmd)
        {
            _rconHistory.Insert(0, cmd);
            if (_rconHistory.Count > MaxRconHistorySize) _rconHistory.RemoveAt(_rconHistory.Count - 1);
        }
        _rconHistoryIndex = -1;

        AppendConsole($"> {cmd}");
        TxtConsoleInput.Clear();

        try
        {
            var response = await _rconClient.SendCommandAsync(cmd);
            AppendConsole(string.IsNullOrEmpty(response) ? "(empty response)" : response);
        }
        catch (Exception ex)
        {
            AppendConsole($"[ERROR] {ex.Message}");
        }
    }

    private void AppendConsole(string text)
    {
        TxtConsoleOutput.AppendText(text + Environment.NewLine);
        TxtConsoleOutput.ScrollToEnd();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LOGS TAB
    // ═══════════════════════════════════════════════════════════════════════
    private void BtnRefreshLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null) return;
        var logPath = FindLogFile(_selectedConfig);
        if (logPath == null)
        {
            TxtLogPath.Text = "No log file found.";
            TxtLogViewer.Clear();
            return;
        }

        try
        {
            // Read last 2000 lines to avoid loading huge files
            var lines = ReadLastLines(logPath, 2000);
            TxtLogViewer.Text = string.Join(Environment.NewLine, lines);
            TxtLogViewer.ScrollToEnd();
            TxtLogPath.Text = logPath;
        }
        catch (Exception ex)
        {
            TxtLogPath.Text = $"Error: {ex.Message}";
        }
    }

    private static string? FindLogFile(ServerConfig cfg)
    {
        if (string.IsNullOrEmpty(cfg.Dir) || !Directory.Exists(cfg.Dir))
            return null;

        var candidates = new[] { "*.log", "*.txt" };
        foreach (var pattern in candidates)
        {
            var files = Directory.GetFiles(cfg.Dir, pattern, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();
            if (files.Count > 0) return files[0];
        }
        return null;
    }

    private static IEnumerable<string> ReadLastLines(string path, int count)
    {
        using var fs     = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new System.IO.StreamReader(fs);
        var lines = reader.ReadToEnd().Split('\n');
        return lines.Length <= count ? lines : lines[^count..];
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SEARCH
    // ═══════════════════════════════════════════════════════════════════════
    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        PopulateServerList(TxtSearch.Text);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  OPERATION LOG
    // ═══════════════════════════════════════════════════════════════════════
    private void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        TxtOpLog.AppendText(line + Environment.NewLine);
        TxtOpLog.ScrollToEnd();
        // Mirror every UI log entry to the rolling file log.
        App.Logger.Info(msg);
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e) => TxtOpLog.Clear();

    // ═══════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════════
    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1) return $"{ts.Hours}h {ts.Minutes}m";
        return $"{ts.Minutes}m {ts.Seconds}s";
    }

    private void SelectServer(ServerConfig cfg)
    {
        var item = (LbServers.ItemsSource as IEnumerable<ServerListItem>)
                   ?.FirstOrDefault(i => i.Config == cfg);
        if (item != null) LbServers.SelectedItem = item;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  VIEW-MODEL HELPERS (lightweight, no full MVVM needed)
// ═══════════════════════════════════════════════════════════════════════════
public class ServerListItem
{
    public ServerConfig Config { get; }
    public string Name  => Config.Name;
    public string Group => Config.Group;
    public Brush  StatusBrush { get; set; }

    public ServerListItem(ServerConfig config, Brush statusBrush)
    {
        Config      = config;
        StatusBrush = statusBrush;
    }
}

public class ModListItem
{
    public long   ModId      { get; }
    public bool   Disabled   { get; }
    public string StatusLabel => Disabled ? "DISABLED" : "ENABLED";
    public Brush  StatusColor => Disabled
        ? new SolidColorBrush(Color.FromRgb(0xAA, 0x55, 0x55))
        : new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x4E));

    public ModListItem(long modId, bool disabled)
    {
        ModId    = modId;
        Disabled = disabled;
    }
}

public class BackupListItem
{
    public string   Path         { get; }
    public string   Name         { get; }
    public DateTime Timestamp    { get; }
    public long     SizeBytes    { get; }
    public string   TimestampStr => Timestamp.ToString("yyyy-MM-dd HH:mm");
    public string   SizeStr      => SizeBytes >= 1_048_576
        ? $"{SizeBytes / 1_048_576.0:F1} MB"
        : $"{SizeBytes / 1024.0:F1} KB";

    public BackupListItem(BackupInfo info)
    {
        Path      = info.Path;
        Name      = info.Name;
        Timestamp = info.Timestamp;
        SizeBytes = info.SizeBytes;
    }
}

public class ScheduledCommandItem
{
    public ScheduledRconCommand Source          { get; }
    public string               Command        => Source.Command;
    public int                  IntervalMinutes => Source.IntervalMinutes;
    public string               IntervalLabel  => $"every {Source.IntervalMinutes}m";

    public ScheduledCommandItem(ScheduledRconCommand source) => Source = source;
}

public class EnvVarItem
{
    public string Key   { get; }
    public string Value { get; }

    public EnvVarItem(string key, string value)
    {
        Key   = key;
        Value = value;
    }
}
