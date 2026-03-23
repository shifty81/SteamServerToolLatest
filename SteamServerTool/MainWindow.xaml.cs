using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SteamServerTool.Core.Models;
using SteamServerTool.Core.Services;

namespace SteamServerTool;

public partial class MainWindow : Window
{
    // ─── Services ───────────────────────────────────────────────────────────
    private readonly ServerManager _serverManager = new();
    private readonly BackupService _backupService = new();
    private readonly SteamCmdService  _steamCmdService  = new();
    private readonly WorkshopService  _workshopService  = new();
    private RconClient? _rconClient;

    // ─── State ──────────────────────────────────────────────────────────────
    private ServerConfig? _selectedConfig;
    private readonly DispatcherTimer _refreshTimer;
    private const string ConfigPath = "servers.json";
    private const int MaxRconHistorySize = 100;

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

        LoadAndPopulate();
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
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TOOLBAR BUTTONS
    // ═══════════════════════════════════════════════════════════════════════
    private void BtnNewServer_Click(object sender, RoutedEventArgs e)
    {
        var cfg = new ServerConfig { Name = "New Server", AppId = 0 };
        _serverManager.Servers.Add(cfg);
        PopulateServerList();
        SelectServer(cfg);
        Log("Created new server entry.");
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        LoadAndPopulate();
    }

    private void BtnStartAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var s in _serverManager.Servers)
            TryStartServer(s);
    }

    private void BtnStopAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var s in _serverManager.Servers)
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

    private void BtnInstallSteamCmd_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedConfig == null) { Log("No server selected."); return; }

        var progress = new Progress<string>(msg => Dispatcher.InvokeAsync(() => Log($"[SteamCMD] {msg}")));
        Log($"[SteamCMD] Starting install/update for {_selectedConfig.Name}...");
        _ = _steamCmdService.InstallOrUpdateServer(_selectedConfig, progress);
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
