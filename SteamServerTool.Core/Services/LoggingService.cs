using System.Text;

namespace SteamServerTool.Core.Services;

/// <summary>
/// Thread-safe file-based rolling logger. Writes one log file per calendar day to the
/// configured log directory (e.g. <c>logs/app-2025-06-01.log</c>).
/// </summary>
public class LoggingService
{
    private readonly string _logDir;
    private readonly object _lock = new();

    public LoggingService(string logDir)
    {
        _logDir = logDir;
        try { Directory.CreateDirectory(_logDir); } catch { /* best effort */ }
    }

    /// <summary>The directory where log files are written.</summary>
    public string LogDirectory => _logDir;

    /// <summary>Full path to today's log file.</summary>
    public string CurrentLogFile =>
        Path.Combine(_logDir, $"app-{DateTime.Now:yyyy-MM-dd}.log");

    // ── Write helpers ─────────────────────────────────────────────────────
    public void Info(string message)  => Write("INFO ", message);
    public void Warn(string message)  => Write("WARN ", message);
    public void Error(string message) => Write("ERROR", message);
    public void Crash(string message) => Write("CRASH", message);

    public void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
        lock (_lock)
        {
            try
            {
                File.AppendAllText(CurrentLogFile, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Never let logging failures crash the application.
            }
        }
    }
}
