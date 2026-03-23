namespace SteamServerTool.Core.Services;

/// <summary>
/// Application-wide static logging facade.
/// <para>
/// Call <see cref="Configure"/> once at startup (e.g. in <c>App.OnStartup</c>) to wire up
/// the underlying <see cref="LoggingService"/>.  All Core services and the WPF layer can
/// then call the static helpers without needing constructor injection.
/// </para>
/// </summary>
public static class AppLogger
{
    private static LoggingService? _service;

    /// <summary>
    /// Wires the static facade to the given <see cref="LoggingService"/> instance.
    /// Must be called before any logging statements are reached.
    /// </summary>
    public static void Configure(LoggingService service) => _service = service;

    // ── Convenience helpers ───────────────────────────────────────────────
    public static void Info(string message)  => _service?.Info(message);
    public static void Warn(string message)  => _service?.Warn(message);
    public static void Error(string message) => _service?.Error(message);
    public static void Crash(string message) => _service?.Crash(message);

    /// <summary>Log a message with an explicit level label.</summary>
    public static void Write(string level, string message) => _service?.Write(level, message);
}
