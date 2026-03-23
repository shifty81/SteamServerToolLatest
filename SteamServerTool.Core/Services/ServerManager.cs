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
    private readonly Dictionary<string, Process>  _processes  = new();
    private readonly Dictionary<string, DateTime> _startTimes = new();
    private readonly Dictionary<string, ServerStatus> _statuses = new();

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
            Servers = new List<ServerConfig>();
            return;
        }

        var json = File.ReadAllText(path);
        Servers = JsonSerializer.Deserialize<List<ServerConfig>>(json, _jsonOptions)
                  ?? new List<ServerConfig>();

        foreach (var server in Servers)
            _statuses[server.Name] = ServerStatus.Stopped;
    }

    public void SaveConfig(string path)
    {
        var json = JsonSerializer.Serialize(Servers, _jsonOptions);
        File.WriteAllText(path, json);
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
            throw new InvalidOperationException($"Cannot start server: {error}");

        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(config.Dir, config.Executable),
            Arguments = config.LaunchArgs,
            WorkingDirectory = config.Dir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var kv in config.EnvironmentVariables)
            psi.EnvironmentVariables[kv.Key] = kv.Value;

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.Exited += (_, _) =>
        {
            var exitCode = process.ExitCode;
            _processes.Remove(config.Name);

            if (exitCode != 0)
            {
                config.TotalCrashes++;
                config.LastCrashTime = DateTime.UtcNow.ToString("o");
                SetStatus(config.Name, ServerStatus.Crashed);
                ServerCrashed?.Invoke(this, new ServerCrashedEventArgs(config.Name, exitCode));
            }
            else
            {
                SetStatus(config.Name, ServerStatus.Stopped);
            }
        };

        SetStatus(config.Name, ServerStatus.Starting);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _processes[config.Name] = process;
        _startTimes[config.Name] = DateTime.UtcNow;
        SetStatus(config.Name, ServerStatus.Running);
    }

    public void StopServer(string name)
    {
        if (!_processes.TryGetValue(name, out var process) || process.HasExited)
        {
            SetStatus(name, ServerStatus.Stopped);
            return;
        }

        var config = Servers.FirstOrDefault(s => s.Name == name);
        var graceful = config?.GracefulShutdownSeconds ?? 15;

        try
        {
            process.CloseMainWindow();
            if (!process.WaitForExit(graceful * 1000))
                process.Kill(true);
        }
        catch
        {
            try { process.Kill(true); } catch { /* already dead */ }
        }

        _processes.Remove(name);

        if (_startTimes.TryGetValue(name, out var start) && config != null)
        {
            config.TotalUptimeSeconds += (long)(DateTime.UtcNow - start).TotalSeconds;
            _startTimes.Remove(name);
        }

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

        var config = Servers.FirstOrDefault(s => s.Name == name);

        try { process.Kill(true); }
        catch { /* already dead */ }

        _processes.Remove(name);

        if (_startTimes.TryGetValue(name, out var start) && config != null)
        {
            config.TotalUptimeSeconds += (long)(DateTime.UtcNow - start).TotalSeconds;
            _startTimes.Remove(name);
        }

        SetStatus(name, ServerStatus.Stopped);
    }

    public void RestartServer(string name)
    {
        var config = Servers.FirstOrDefault(s => s.Name == name)
                     ?? throw new InvalidOperationException($"Server '{name}' not found.");

        StopServer(name);
        StartServer(config);
    }

    private void SetStatus(string name, ServerStatus status)
    {
        _statuses[name] = status;
        ServerStatusChanged?.Invoke(this, new ServerStatusChangedEventArgs(name, status));
    }
}
