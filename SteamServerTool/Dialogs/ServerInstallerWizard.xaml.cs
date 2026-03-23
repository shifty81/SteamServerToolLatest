using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SteamServerTool.Core.Models;
using SteamServerTool.Core.Services;

namespace SteamServerTool.Dialogs;

/// <summary>
/// Multi-step Server Installer Wizard.
/// Step 1 — Choose template
/// Step 2 — Configure server settings
/// Step 3 — Deploy (install via SteamCMD / download)
/// </summary>
public partial class ServerInstallerWizard : Window
{
    // ─── Services ─────────────────────────────────────────────────────────
    private readonly SteamCmdService      _steamCmdService;
    private readonly MinecraftService     _minecraftService;
    private readonly VintageStoryService  _vintageStoryService;

    /// <summary>The new server config produced by the wizard, set when Finish is clicked.</summary>
    public ServerConfig? Result { get; private set; }

    // ─── State ────────────────────────────────────────────────────────────
    private int             _currentStep   = 1;
    private ServerTemplate? _selectedTemplate;
    private bool            _deploySuccess = false;

    private static readonly string ServersBaseDir =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Servers");

    private static readonly string BackupsBaseDir =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups");

    // ─── Constructor ──────────────────────────────────────────────────────
    public ServerInstallerWizard(
        SteamCmdService     steamCmdService,
        MinecraftService    minecraftService,
        VintageStoryService vintageStoryService)
    {
        InitializeComponent();
        _steamCmdService     = steamCmdService;
        _minecraftService    = minecraftService;
        _vintageStoryService = vintageStoryService;

        BuildTemplateGallery();
        UpdateStepUI();
    }

    // ─── Template gallery ─────────────────────────────────────────────────
    private void BuildTemplateGallery()
    {
        TemplateGallery.Children.Clear();

        foreach (var tmpl in ServerTemplates.All)
        {
            var tile = BuildTemplateTile(tmpl);
            TemplateGallery.Children.Add(tile);
        }
    }

    private Border BuildTemplateTile(ServerTemplate tmpl)
    {
        var icon = new TextBlock
        {
            Text      = tmpl.Icon,
            FontSize  = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin    = new Thickness(0, 0, 0, 6)
        };

        var name = new TextBlock
        {
            Text                = tmpl.Name,
            FontWeight          = FontWeights.SemiBold,
            FontSize            = 12,
            TextWrapping        = TextWrapping.Wrap,
            TextAlignment       = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth            = 110
        };

        var desc = new TextBlock
        {
            Text                = tmpl.Description,
            FontSize            = 10,
            TextWrapping        = TextWrapping.Wrap,
            TextAlignment       = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground          = (Brush)FindResource("DimForegroundBrush"),
            MaxWidth            = 110,
            Margin              = new Thickness(0, 4, 0, 0)
        };

        // SteamCMD badge or download badge
        var badge = new Border
        {
            CornerRadius        = new CornerRadius(3),
            Padding             = new Thickness(6, 2, 6, 2),
            Margin              = new Thickness(0, 6, 0, 0),
            Background          = tmpl.RequiresSteamCmd
                ? new SolidColorBrush(Color.FromRgb(0x00, 0x5A, 0x9E))
                : new SolidColorBrush(Color.FromRgb(0x20, 0x7A, 0x3A)),
            Child = new TextBlock
            {
                Text       = tmpl.RequiresSteamCmd ? "SteamCMD" : "Direct Download",
                FontSize   = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            }
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(8)
        };
        stack.Children.Add(icon);
        stack.Children.Add(name);
        stack.Children.Add(desc);
        stack.Children.Add(badge);

        var tile = new Border
        {
            Width           = 140,
            Height          = 160,
            Background      = (Brush)FindResource("PanelBgBrush"),
            BorderBrush     = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(2),
            CornerRadius    = new CornerRadius(8),
            Margin          = new Thickness(0, 0, 10, 10),
            Cursor          = System.Windows.Input.Cursors.Hand,
            Child           = stack
        };
        tile.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black, Direction = 270,
            ShadowDepth = 2, BlurRadius = 6, Opacity = 0.25
        };

        tile.MouseEnter += (_, _) =>
        {
            tile.BorderBrush = (Brush)FindResource("AccentBrush");
            tile.Background  = (Brush)FindResource("ControlBgBrush");
        };
        tile.MouseLeave += (_, _) =>
        {
            if (_selectedTemplate != tmpl)
            {
                tile.BorderBrush = (Brush)FindResource("BorderBrush");
                tile.Background  = (Brush)FindResource("PanelBgBrush");
            }
        };

        tile.MouseLeftButtonUp += (_, _) => SelectTemplate(tmpl, tile);

        // Store template reference in Tag for easy access
        tile.Tag = tmpl;

        return tile;
    }

    private void SelectTemplate(ServerTemplate tmpl, Border tile)
    {
        // Deselect all tiles
        foreach (Border t in TemplateGallery.Children.OfType<Border>())
        {
            t.BorderBrush = (Brush)FindResource("BorderBrush");
            t.Background  = (Brush)FindResource("PanelBgBrush");
        }

        // Select this tile
        tile.BorderBrush = (Brush)FindResource("AccentBrush");
        tile.Background  = (Brush)FindResource("SelectionBrush");

        _selectedTemplate = tmpl;
        BtnNext.IsEnabled = true;
    }

    // ─── Step navigation ──────────────────────────────────────────────────
    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep == 1)
        {
            if (_selectedTemplate == null)
            {
                MessageBox.Show("Please choose a server template to continue.",
                    "Template Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            GoToStep(2);
        }
        else if (_currentStep == 2)
        {
            if (!ValidateStep2(out var err))
            {
                MessageBox.Show(err, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            GoToStep(3);
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1)
            GoToStep(_currentStep - 1);
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
        Close();
    }

    private void BtnFinish_Click(object sender, RoutedEventArgs e)
    {
        if (!_deploySuccess) return;

        Result = BuildConfig();
        DialogResult = true;
        Close();
    }

    private void GoToStep(int step)
    {
        _currentStep = step;
        UpdateStepUI();
    }

    private void UpdateStepUI()
    {
        // Step panels
        Step1Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;

        // Step dots
        var accentColor  = ((SolidColorBrush)FindResource("AccentBrush")).Color;
        var controlColor = ((SolidColorBrush)FindResource("ControlBgBrush")).Color;
        StepDot1.Background = new SolidColorBrush(_currentStep >= 1 ? accentColor : controlColor);
        StepDot2.Background = new SolidColorBrush(_currentStep >= 2 ? accentColor : controlColor);
        StepDot3.Background = new SolidColorBrush(_currentStep >= 3 ? accentColor : controlColor);

        // Buttons
        BtnBack.IsEnabled = _currentStep > 1;
        BtnNext.Visibility = _currentStep < 3 ? Visibility.Visible : Visibility.Collapsed;
        BtnFinish.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;

        // Description
        TxtStepDescription.Text = _currentStep switch
        {
            1 => "Step 1 of 3 — Choose a Server Template",
            2 => "Step 2 of 3 — Configure Server Settings",
            3 => "Step 3 of 3 — Deploy & Install",
            _ => ""
        };

        // When entering step 2, populate form
        if (_currentStep == 2 && _selectedTemplate != null)
            PopulateStep2FromTemplate(_selectedTemplate);

        // When entering step 3, populate summary
        if (_currentStep == 3)
            PopulateStep3Summary();
    }

    // ─── Step 2: form population & validation ─────────────────────────────
    private void PopulateStep2FromTemplate(ServerTemplate tmpl)
    {
        TxtTemplateIcon.Text = tmpl.Icon;
        TxtTemplateName.Text = tmpl.Name;
        TxtTemplateDesc.Text = tmpl.Description;

        if (string.IsNullOrEmpty(WizName.Text) || WizName.Text == "New Server")
            WizName.Text = tmpl.Name + " Server";

        if (string.IsNullOrEmpty(WizDir.Text) && !string.IsNullOrEmpty(tmpl.DefaultDir))
            WizDir.Text = Path.Combine(ServersBaseDir, tmpl.DefaultDir);

        WizRconPort.Text   = tmpl.RconPort.ToString();
        WizQueryPort.Text  = tmpl.QueryPort.ToString();
        WizMaxPlayers.Text = tmpl.MaxPlayers > 0 ? tmpl.MaxPlayers.ToString() : "20";
        WizLaunchArgs.Text = tmpl.LaunchArgs;

        if (!string.IsNullOrEmpty(tmpl.Group))
            WizGroup.Text = tmpl.Group;
    }

    private bool ValidateStep2(out string error)
    {
        if (string.IsNullOrWhiteSpace(WizName.Text))
        { error = "Server name is required."; return false; }

        if (string.IsNullOrWhiteSpace(WizDir.Text))
        { error = "Install directory is required."; return false; }

        if (!int.TryParse(WizRconPort.Text, out var rcon) || rcon < 1 || rcon > 65535)
        { error = "RCON port must be between 1 and 65535."; return false; }

        error = "";
        return true;
    }

    private void BtnBrowseDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select Server Install Folder" };
        if (dlg.ShowDialog() == true)
            WizDir.Text = dlg.FolderName;
    }

    /// <summary>Handles "Remote Server" banner click — opens the AddRemoteServerDialog directly.</summary>
    private void BtnAddRemoteInWizard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var dlg = new AddRemoteServerDialog { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            Result = dlg.Result;
            DialogResult = true;
            Close();
        }
    }

    /// <summary>Generates a secure random RCON password and populates the wizard password field.</summary>
    private void BtnGenerateWizPassword_Click(object sender, RoutedEventArgs e)
    {
        WizRconPassword.Text = PasswordHelper.Generate(20);
    }

    // ─── Step 3: summary & deploy ─────────────────────────────────────────
    private void PopulateStep3Summary()
    {
        SumServer.Text   = WizName.Text;
        SumTemplate.Text = _selectedTemplate?.Name ?? "Custom";
        SumDir.Text      = WizDir.Text;

        DeployBannerTitle.Text = "Ready to Deploy";
        DeployBannerSub.Text   = "Click '🚀 Deploy' to begin installation.";
        DeployBannerIcon.Text  = "⏳";
        SetDeployBannerColor(BannerState.Idle);

        BtnDeploy.IsEnabled  = true;
        BtnFinish.IsEnabled  = false;
        TxtDeployLog.Clear();
        TxtDeployStatus.Text = "";
        _deploySuccess = false;
    }

    private async void BtnDeploy_Click(object sender, RoutedEventArgs e)
    {
        var cfg = BuildConfig();

        BtnDeploy.IsEnabled = false;
        BtnBack.IsEnabled   = false;
        BtnCancel.IsEnabled = false;
        TxtDeployLog.Clear();
        TxtDeployStatus.Text = "Installing…";

        DeployBannerTitle.Text = "Installation in Progress…";
        DeployBannerSub.Text   = "Please wait — do not close this window.";
        DeployBannerIcon.Text  = "⏳";
        SetDeployBannerColor(BannerState.InProgress);

        var progress = new Progress<string>(msg => Dispatcher.InvokeAsync(() => LogDeploy(msg)));

        bool ok;

        try
        {
            if (IsMinecraftServer(cfg))
            {
                ok = await _minecraftService.DownloadServerAsync(cfg.Dir, progress);
            }
            else if (IsVintageStoryServer(cfg))
            {
                ok = await _vintageStoryService.DownloadServerAsync(cfg.Dir, progress);
            }
            else
            {
                // SteamCMD path
                if (!_steamCmdService.IsSteamCmdInstalled())
                {
                    LogDeploy("[INFO] SteamCMD not found — launching SteamCMD setup…");
                    var setupDlg = new FirstRunSetupDialog(_steamCmdService) { Owner = this };
                    setupDlg.ShowDialog();

                    if (string.IsNullOrEmpty(setupDlg.ResolvedSteamCmdPath))
                    {
                        LogDeploy("[WARN] SteamCMD setup aborted — deployment cancelled.");
                        OnDeployResult(false);
                        return;
                    }
                    LogDeploy($"[Setup] SteamCMD ready: {setupDlg.ResolvedSteamCmdPath}");
                }
                ok = await _steamCmdService.InstallOrUpdateServer(cfg, progress);
            }
        }
        catch (Exception ex)
        {
            LogDeploy($"[ERROR] Unexpected error: {ex.Message}");
            ok = false;
        }

        OnDeployResult(ok);
    }

    private void OnDeployResult(bool success)
    {
        _deploySuccess = success;

        if (success)
        {
            DeployBannerTitle.Text = "✔ Installation Successful!";
            DeployBannerSub.Text   = "Server deployed successfully. Click Finish to add it to your server list.";
            DeployBannerIcon.Text  = "✔";
            SetDeployBannerColor(BannerState.Success);
            TxtDeployStatus.Text   = "";
        }
        else
        {
            DeployBannerTitle.Text = "✖ Installation Failed";
            DeployBannerSub.Text   = "See the log above for details. You can retry or go back to adjust settings.";
            DeployBannerIcon.Text  = "✖";
            SetDeployBannerColor(BannerState.Failure);
            TxtDeployStatus.Text   = "";
        }

        BtnDeploy.IsEnabled  = !success;   // allow retry on failure
        BtnBack.IsEnabled    = true;
        BtnCancel.IsEnabled  = true;
        BtnFinish.IsEnabled  = success;    // greyed out until success
    }

    private void LogDeploy(string msg)
    {
        TxtDeployLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        TxtDeployLog.ScrollToEnd();
    }

    // ─── Config builder ───────────────────────────────────────────────────
    private ServerConfig BuildConfig()
    {
        var name = WizName.Text.Trim();
        var cfg = new ServerConfig
        {
            Name       = name,
            Dir        = WizDir.Text.Trim(),
            Group      = WizGroup.Text.Trim(),
            LaunchArgs = WizLaunchArgs.Text,
            MaxPlayers = int.TryParse(WizMaxPlayers.Text, out var mp) ? mp : 0,
            QueryPort  = int.TryParse(WizQueryPort.Text, out var qp) ? qp : 0,
            AutoStartOnLaunch = WizAutoStart.IsChecked == true,
            // Default backup folder to Backups/<ServerName> next to the executable.
            BackupFolder = Path.Combine(BackupsBaseDir, string.Concat(name.Split(Path.GetInvalidFileNameChars()))),
            Rcon = new Core.Models.RconConfig
            {
                Host     = "127.0.0.1",
                Port     = int.TryParse(WizRconPort.Text, out var rp) ? rp : 27020,
                Password = WizRconPassword.Text.Trim()
            }
        };

        if (_selectedTemplate != null)
        {
            cfg.AppId            = _selectedTemplate.AppId;
            cfg.Executable       = _selectedTemplate.Executable;
            cfg.ConfigDir        = _selectedTemplate.ConfigDir;
            cfg.StdinStopCommand = _selectedTemplate.StdinStopCommand;
            if (!_selectedTemplate.RequiresSteamCmd)
                cfg.ServerType = _selectedTemplate.Name;
        }

        return cfg;
    }

    // ─── Server type helpers ──────────────────────────────────────────────
    private static bool IsMinecraftServer(ServerConfig cfg)
        => cfg.ServerType == "Minecraft Java" ||
           (cfg.AppId == 0 &&
            cfg.Executable.Equals("java", StringComparison.OrdinalIgnoreCase) &&
            cfg.LaunchArgs.Contains("server.jar", StringComparison.OrdinalIgnoreCase));

    private static bool IsVintageStoryServer(ServerConfig cfg)
        => cfg.ServerType == "Vintage Story" ||
           (cfg.AppId == 0 &&
            cfg.Executable.Contains("VintagestoryServer", StringComparison.OrdinalIgnoreCase));

    // ─── Banner color helper ──────────────────────────────────────────────
    private enum BannerState { Idle, InProgress, Success, Failure }

    // Shared accent colors that drive both the banner border and the icon foreground.
    private static readonly Color ColorSuccess    = Color.FromRgb(0x4E, 0xC9, 0x4E);
    private static readonly Color ColorFailure    = Color.FromRgb(0xE0, 0x52, 0x52);
    private static readonly Color ColorInProgress = Color.FromRgb(0x00, 0x7A, 0xCC);
    private static readonly Color ColorIdle       = Color.FromRgb(0xAA, 0xAA, 0xAA);

    private void SetDeployBannerColor(BannerState state)
    {
        (Color bg, Color accent) = state switch
        {
            BannerState.Success    => (Color.FromRgb(0x1A, 0x3A, 0x1A), ColorSuccess),
            BannerState.Failure    => (Color.FromRgb(0x3A, 0x1A, 0x1A), ColorFailure),
            BannerState.InProgress => (Color.FromRgb(0x1A, 0x2A, 0x3A), ColorInProgress),
            _                      => (Color.FromRgb(0x2A, 0x2A, 0x2A), ColorIdle)
        };

        DeployBanner.Background         = new SolidColorBrush(bg);
        DeployBanner.BorderBrush        = new SolidColorBrush(accent);
        DeployBannerIcon.Foreground     = new SolidColorBrush(accent);
    }
}
