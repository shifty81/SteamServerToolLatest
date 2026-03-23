using System.Windows;
using SteamServerTool.Core.Models;

namespace SteamServerTool.Dialogs;

public partial class AddRemoteServerDialog : Window
{
    /// <summary>The configured remote server; populated on success.</summary>
    public ServerConfig? Result { get; private set; }

    public AddRemoteServerDialog()
    {
        InitializeComponent();
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtName.Text.Trim();
        var host = TxtHost.Text.Trim();
        var pass = TxtPassword.Password;

        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Please enter a display name.", "Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(host))
        {
            MessageBox.Show("Please enter the RCON host / IP address.", "Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtPort.Text.Trim(), out var port) || port < 1 || port > 65535)
        {
            MessageBox.Show("Please enter a valid RCON port (1–65535).", "Invalid Port",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new ServerConfig
        {
            Name     = name,
            IsRemote = true,
            Rcon     = new RconConfig { Host = host, Port = port, Password = pass }
        };

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
