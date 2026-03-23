using System.IO;
using System.Windows;
using System.Windows.Threading;
using SteamServerTool.Core.Services;

namespace SteamServerTool;

public partial class App : Application
{
    /// <summary>Application-wide file logger, available to all components.</summary>
    public static readonly LoggingService Logger = new(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs"));

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch unhandled exceptions on the WPF UI thread.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Catch unhandled exceptions thrown on background threads.
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        Logger.Info("Application started.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Info($"Application exiting (code {e.ApplicationExitCode}).");
        base.OnExit(e);
    }

    // ── Exception handlers ────────────────────────────────────────────────

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var ex = e.Exception;
        Logger.Crash($"Unhandled UI exception: {ex}");

        var result = MessageBox.Show(
            $"An unexpected error occurred:\n\n{ex.Message}\n\n" +
            $"Details have been written to:\n{Logger.CurrentLogFile}\n\n" +
            "Would you like to submit a bug report on GitHub?",
            "SteamServerTool – Unexpected Error",
            MessageBoxButton.YesNo,
            MessageBoxImage.Error);

        if (result == MessageBoxResult.Yes)
            GitHubIssueReporter.OpenNewIssue(
                $"[Bug] {ex.GetType().Name}: {ex.Message}",
                GitHubIssueReporter.FormatAppCrashReport(ex));

        // Mark as handled so the application can attempt to continue.
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Logger.Crash($"Unhandled background exception (IsTerminating={e.IsTerminating}): {ex}");
    }
}
