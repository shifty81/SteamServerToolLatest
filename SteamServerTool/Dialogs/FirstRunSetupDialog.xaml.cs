using System.IO;
using System.Windows;
using Microsoft.Win32;
using SteamServerTool.Core.Services;

namespace SteamServerTool.Dialogs;

public partial class FirstRunSetupDialog : Window
{
    private readonly SteamCmdService _steamCmdService;

    /// <summary>The verified path to the SteamCMD executable after setup.</summary>
    public string? ResolvedSteamCmdPath { get; private set; }

    public FirstRunSetupDialog(SteamCmdService steamCmdService)
    {
        InitializeComponent();
        _steamCmdService = steamCmdService;

        // Default install dir — use LocalApplicationData (%LOCALAPPDATA%) so that
        // no administrator privileges are required to create the directory and write files.
        TxtInstallPath.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamCMD");

        SetBanner(found: false);
    }

    // ─── Banner helper ────────────────────────────────────────────────────
    private void SetBanner(bool found, string? customText = null)
    {
        if (found)
        {
            BannerBorder.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1A, 0x3A, 0x1A));
            BannerIcon.Text         = "✔";
            BannerIcon.Foreground   = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x4E, 0xC9, 0x4E));
            BannerText.Text         = customText ?? "SteamCMD is ready.";
        }
        else
        {
            BannerBorder.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x3A, 0x2A, 0x10));
            BannerIcon.Text         = "⚠";
            BannerIcon.Foreground   = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE0, 0xA0, 0x20));
            BannerText.Text         = customText ??
                "SteamCMD was not found on this system. Download it below, or locate " +
                "an existing installation using the 'Locate Existing…' button.";
        }
    }

    // ─── Handlers ─────────────────────────────────────────────────────────
    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select SteamCMD install folder" };
        if (dlg.ShowDialog() == true)
            TxtInstallPath.Text = dlg.FolderName;
    }

    private void BtnLocate_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title  = "Locate steamcmd.exe",
            Filter = OperatingSystem.IsWindows()
                ? "SteamCMD|steamcmd.exe|All files|*.*"
                : "SteamCMD|steamcmd.sh|All files|*.*"
        };

        if (ofd.ShowDialog() != true) return;

        _steamCmdService.SteamCmdPath = ofd.FileName;
        ResolvedSteamCmdPath          = ofd.FileName;
        SetBanner(found: true, customText: $"SteamCMD located: {ofd.FileName}");
        LogLine($"Using existing SteamCMD: {ofd.FileName}");
        BtnDownload.IsEnabled = false;
    }

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        var installDir = TxtInstallPath.Text.Trim();
        if (string.IsNullOrEmpty(installDir))
        {
            MessageBox.Show("Please enter a valid install directory.", "Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnDownload.IsEnabled = false;
        BtnLocate.IsEnabled   = false;
        BtnSkip.IsEnabled     = false;

        TxtProgress.Clear();

        var progress = new Progress<string>(msg => Dispatcher.InvokeAsync(() => LogLine(msg)));

        var ok = await _steamCmdService.DownloadSteamCmdAsync(installDir, progress);

        if (ok)
        {
            ResolvedSteamCmdPath = _steamCmdService.SteamCmdPath;
            SetBanner(found: true, customText: $"SteamCMD installed to: {installDir}");
        }
        else
        {
            SetBanner(found: false, customText: "Download failed — see log for details. You can try again or use 'Locate Existing…'.");
            BtnDownload.IsEnabled = true;
        }

        BtnLocate.IsEnabled = true;
        BtnSkip.IsEnabled   = true;
    }

    private void BtnSkip_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;   // close dialog; ResolvedSteamCmdPath may be null
        Close();
    }

    private void LogLine(string msg) =>
        TxtProgress.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
}
