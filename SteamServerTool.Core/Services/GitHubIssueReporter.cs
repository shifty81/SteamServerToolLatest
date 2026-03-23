using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SteamServerTool.Core.Services;

/// <summary>
/// Opens the user's default browser to a pre-filled GitHub Issues creation page so crash
/// reports and bug reports can be submitted to the repository without requiring any
/// GitHub authentication inside the application.
/// </summary>
public static class GitHubIssueReporter
{
    private const string RepoOwner = "shifty81";
    private const string RepoName  = "SteamServerToolLatest";

    /// <summary>
    /// Opens the default browser to a new-issue page with <paramref name="title"/> and
    /// <paramref name="body"/> pre-filled.
    /// </summary>
    public static void OpenNewIssue(string title, string body)
    {
        var url = BuildIssueUrl(title, body);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // If the browser cannot be opened there is nothing more we can do here;
            // the caller should have already written the report to the log file.
        }
    }

    /// <summary>Returns the GitHub new-issue URL with query parameters pre-populated.</summary>
    public static string BuildIssueUrl(string title, string body)
    {
        // GitHub truncates very long URLs, so cap the body to keep the URL usable.
        const int maxBodyChars = 4_000;
        if (body.Length > maxBodyChars)
            body = body[..maxBodyChars] + "\n\n*(report truncated — see local log file for full details)*";

        var encodedTitle = Uri.EscapeDataString(title);
        var encodedBody  = Uri.EscapeDataString(body);
        return $"https://github.com/{RepoOwner}/{RepoName}/issues/new" +
               $"?title={encodedTitle}&body={encodedBody}&labels=bug";
    }

    // ── Report formatters ─────────────────────────────────────────────────

    /// <summary>Formats a server-process crash into a markdown GitHub issue body.</summary>
    public static string FormatServerCrashReport(string serverName, int exitCode)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Server Crash Report");
        sb.AppendLine();
        sb.AppendLine($"**Server:** {serverName}");
        sb.AppendLine($"**Exit Code:** {exitCode}");
        sb.AppendLine($"**Time (UTC):** {DateTime.UtcNow:u}");
        sb.AppendLine($"**OS:** {RuntimeInformation.OSDescription}");
        sb.AppendLine($"**Runtime:** {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine();
        sb.AppendLine("### Steps to Reproduce");
        sb.AppendLine("*(Please describe what the server was doing before it crashed.)*");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*This report was generated automatically by SteamServerTool.*");
        return sb.ToString();
    }

    /// <summary>Formats an unhandled application exception into a markdown GitHub issue body.</summary>
    public static string FormatAppCrashReport(Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Application Crash Report");
        sb.AppendLine();
        sb.AppendLine($"**Exception:** `{ex.GetType().FullName}`");
        sb.AppendLine($"**Message:** {ex.Message}");
        sb.AppendLine($"**Time (UTC):** {DateTime.UtcNow:u}");
        sb.AppendLine($"**OS:** {RuntimeInformation.OSDescription}");
        sb.AppendLine($"**Runtime:** {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine();
        sb.AppendLine("### Stack Trace");
        sb.AppendLine("```");
        sb.AppendLine(ex.ToString());
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("### Steps to Reproduce");
        sb.AppendLine("*(Please describe what you were doing when the crash occurred.)*");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*This report was generated automatically by SteamServerTool.*");
        return sb.ToString();
    }
}
