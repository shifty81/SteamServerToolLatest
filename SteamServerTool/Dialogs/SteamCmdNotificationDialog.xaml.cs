using System.Windows;

namespace SteamServerTool.Dialogs;

public partial class SteamCmdNotificationDialog : Window
{
    public SteamCmdNotificationDialog()
    {
        InitializeComponent();
    }

    private void ChkAgree_Changed(object sender, RoutedEventArgs e)
    {
        BtnInstall.IsEnabled = ChkAgree.IsChecked == true;
        TxtHint.Visibility   = BtnInstall.IsEnabled ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BtnInstall_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
