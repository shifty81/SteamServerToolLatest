using System.Diagnostics;
using System.Text.Json;
using SteamServerTool.Core.Models;

namespace SteamServerTool.Core.Services;

public enum ServerStatus
{
    Stopped,
    Starting,
    Running,
    Crashed
}

public class ServerStatusChangedEventArgs : EventArgs
{
    public string ServerName { get; }
    public ServerStatus Status { get; }

    public ServerStatusChangedEventArgs(string serverName, ServerStatus status)
    {
        ServerName = serverName;
        Status = status;
    }
}

public class ServerCrashedEventArgs : EventArgs
{
    public string ServerName { get; }
    public int ExitCode { get; }

    public ServerCrashedEventArgs(string serverName, int exitCode)
    {
        ServerName = serverName;
        ExitCode = exitCode;
    }
}

public class ServerManager
{
    private readonly Dictionary<string, Process>       _processes    = new();
    private readonly Dictionary<string, DateTime>      _startTimes   = new();
    private readonly Dictionary<string, ServerStatus>  _statuses     = new();
    private readonly Dictionary<string, StreamWriter>  _stdinWriters = new();

    // CPU tracking – previous sample per server
    private readonly Dictionary<string, (TimeSpan CpuTime, DateTime Wall)> _cpuSamples = new();

    public List<ServerConfig> Servers { get; private set; } = new();

    public event EventHandler<ServerStatusChangedEventArgs>? ServerStatusChanged;
    public event EventHandler<ServerCrashedEventArgs>? ServerCrashed;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public void LoadConfig(string path)
    {
        if (!File.Exists(path))
        {
            AppLogger.Info($"Config file not found at '{path}' — starting with empty server list.");
            Servers = new List<ServerConfig>();
            return;
        }

        AppLogger.Info($"Loading config from '{path}'.");
        var json = File.ReadAllText(path);
        Servers = JsonSerializer.Deserialize<List<ServerConfig>>(json, _jsonOptions)
                  ?? new List<ServerConfig>();

        AppLogger.Info($"Loaded {Servers.Count} server(s).");
        foreach (var server in Servers)
            _statuses[server.Name] = ServerStatus.Stopped;
    }

    public void SaveConfig(string path)
    {
        var json = JsonSerializer.Serialize(Servers, _jsonOptions);
        File.WriteAllText(path, json);
        AppLogger.Info($"Config saved to '{path}'.");
    }

    public ServerStatus GetStatus(string name)
    {
        return _statuses.TryGetValue(name, out var status) ? status : ServerStatus.Stopped;
    }

    public bool IsRunning(string name)
    {
        return _processes.TryGetValue(name, out var process)
               && process != null
               && !process.HasExited;
    }

    public TimeSpan? GetUptime(string name)
    {
        if (!IsRunning(name)) return null;
        return _startTimes.TryGetValue(name, out var start)
            ? DateTime.UtcNow - start
            : null;
    }

    /// <summary>Returns approximate CPU usage percent for a running server process.</summary>
    public double GetCpuPercent(string name)
    {
        if (!_processes.TryGetValue(name, out var process) || process.HasExited)
            return 0;

        try
        {
            process.Refresh();
            var newCpu  = process.TotalProcessorTime;
            var newWall = DateTime.UtcNow;

            // CPU% = (cpu_time_delta_ms / (wall_time_delta_ms × core_count)) × 100
            // This normalises the raw multi-core processor time to a 0-100% single-core-equivalent figure.
            if (_cpuSamples.TryGetValue(name, out var prev))
            {
                var cpuDelta  = (newCpu  - prev.CpuTime).TotalMilliseconds;
                var wallDelta = (newWall - prev.Wall).TotalMilliseconds;
                _cpuSamples[name] = (newCpu, newWall);

                if (wallDelta <= 0) return 0;
                return Math.Min(100, cpuDelta / (wallDelta * Environment.ProcessorCount) * 100.0);
            }

            _cpuSamples[name] = (newCpu, newWall);
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Returns working-set memory in megabytes for a running server process.</summary>
    public long GetMemoryMb(string name)
    {
        if (!_processes.TryGetValue(name, out var process) || process.HasExited)
            return 0;
        try
        {
            process.Refresh();
            return process.WorkingSet64 / (1024 * 1024);
        }
        catch { return 0; }
    }

    public virtual void StartServer(ServerConfig config)
    {
        if (IsRunning(config.Name))
            return;

        var (isValid, error) = config.Validate();
        if (!isValid)
        {
            AppLogger.Error($"Cannot start '{config.Name}': {error}");
            throw new InvalidOperationException($"Cannot start server: {error}");
        }

        AppLogger.Info($"Starting server '{config.Name}' (AppID {config.AppId}).");

        var useStdin = !string.IsNullOrWhiteSpace(config.StdinStopCommand);

        var psi = new ProcessStartInfo
        {
            FileName               = Path.Combine(config.Dir, config.Executable),
            Arguments              = config.LaunchArgs,
            WorkingDirectory       = config.Dir,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            RedirectStandardInput  = useStdin,
        };

        foreach (var kv in config.EnvironmentVariables)
            psi.EnvironmentVariables[kv.Key] = kv.Value;

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.Exited += (_, _) =>
        {
            var exitCode = process.ExitCode;
            _processes.Remove(config.Name);
            _stdinWriters.Remove(config.Name);

            if (exitCode != 0)
            {
                config.TotalCrashes++;
                config.LastCrashTime = DateTime.UtcNow.ToString("o");
                AppLogger.Crash($"Server '{config.Name}' crashed with exit code {exitCode}. Total crashes: {config.TotalCrashes}.");
                SetStatus(config.Name, ServerStatus.Crashed);
                ServerCrashed?.Invoke(this, new ServerCrashedEventArgs(config.Name, exitCode));
            }
            else
            {
                AppLogger.Info($"Server '{config.Name}' stopped cleanly (exit code 0).");
                SetStatus(config.Name, ServerStatus.Stopped);
            }
        };

        SetStatus(config.Name, ServerStatus.Starting);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (useStdin)
            _stdinWriters[config.Name] = process.StandardInput;

        _processes[config.Name] = process;
        _startTimes[config.Name] = DateTime.UtcNow;
        SetStatus(config.Name, ServerStatus.Running);
        AppLogger.Info($"Server '{config.Name}' is now running (PID {process.Id}).");
    }

    public void StopServer(string name)
    {
        if (!_processes.TryGetValue(name, out var process) || process.HasExited)
        {
            SetStatus(name, ServerStatus.Stopped);
            return;
        }

        var config  = Servers.FirstOrDefault(s => s.Name == name);
        var graceful = config?.GracefulShutdownSeconds ?? 15;

        AppLogger.Info($"Stopping server '{name}' (graceful timeout: {graceful}s).");
        try
        {
            // Prefer stdin-based graceful stop (e.g. "/stop" for Vintage Story, "stop" for
            // Minecraft) so the server can save world data before exiting cleanly.
            if (!string.IsNullOrWhiteSpace(config?.StdinStopCommand) &&
                _stdinWriters.TryGetValue(name, out var stdin))
            {
                AppLogger.Info($"Sending graceful stop command to '{name}' via stdin.");
                try
                {
                    stdin.WriteLine(config.StdinStopCommand);
                    stdin.Flush();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to write stop command to '{name}': {ex.Message}");
                }
            }
            else
            {
                // Fall back to closing the main window (works for windowed / Source-engine servers).
                process.CloseMainWindow();
            }

            if (!process.WaitForExit(graceful * 1000))
            {
                AppLogger.Warn($"Server '{name}' did not exit within {graceful}s — force-killing.");
                process.Kill(true);
            }
        }
        catch
        {
            try { process.Kill(true); } catch { /* already dead */ }
        }

        _processes.Remove(name);
        _stdinWriters.Remove(name);

        if (_startTimes.TryGetValue(name, out var start) && config != null)
        {
            config.TotalUptimeSeconds += (long)(DateTime.UtcNow - start).TotalSeconds;
            _startTimes.Remove(name);
        }

        AppLogger.Info($"Server '{name}' stopped.");
        SetStatus(name, ServerStatus.Stopped);
    }

    /// <summary>Immediately terminates the server process without a graceful shutdown period.</summary>
    public void ForceKillServer(string name)
    {
        if (!_processes.TryGetValue(name, out var process) || process.HasExited)
        {
            SetStatus(name, ServerStatus.Stopped);
            return;
        }

        AppLogger.Warn($"Force-killing server '{name}'.");
        var config = Servers.FirstOrDefault(s => s.Name == name);

        try { process.Kill(true); }
        catch { /* already dead */ }

        _processes.Remove(name);
        _stdinWriters.Remove(name);

        if (_startTimes.TryGetValue(name, out var start) && config != null)
        {
            config.TotalUptimeSeconds += (long)(DateTime.UtcNow - start).TotalSeconds;
            _startTimes.Remove(name);
        }

        AppLogger.Info($"Server '{name}' force-killed.");
        SetStatus(name, ServerStatus.Stopped);
    }

    public void RestartServer(string name)
    {
        AppLogger.Info($"Restarting server '{name}'.");
        var config = Servers.FirstOrDefault(s => s.Name == name)
                     ?? throw new InvalidOperationException($"Server '{name}' not found.");

        StopServer(name);
        StartServer(config);
    }

    private void SetStatus(string name, ServerStatus status)
    {
        _statuses[name] = status;
        AppLogger.Info($"[Status] '{name}' → {status}");
        ServerStatusChanged?.Invoke(this, new ServerStatusChangedEventArgs(name, status));
    }
}
