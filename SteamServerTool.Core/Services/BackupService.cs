using System.IO.Compression;
using SteamServerTool.Core.Models;

namespace SteamServerTool.Core.Services;

public record BackupInfo(string Path, string Name, DateTime Timestamp, long SizeBytes);

public class BackupService
{
    public string CreateBackup(ServerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.BackupFolder))
            throw new InvalidOperationException("Backup folder is not configured.");

        if (!Directory.Exists(config.Dir))
            throw new DirectoryNotFoundException($"Server directory not found: {config.Dir}");

        Directory.CreateDirectory(config.BackupFolder);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var safeName = string.Concat(config.Name.Split(System.IO.Path.GetInvalidFileNameChars()));
        var backupFileName = $"{safeName}_{timestamp}.zip";
        var backupPath = System.IO.Path.Combine(config.BackupFolder, backupFileName);

        ZipFile.CreateFromDirectory(config.Dir, backupPath, CompressionLevel.Optimal, false);

        PruneBackups(config);

        return backupPath;
    }

    public List<BackupInfo> GetBackups(ServerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.BackupFolder) || !Directory.Exists(config.BackupFolder))
            return new List<BackupInfo>();

        var safeName = string.Concat(config.Name.Split(System.IO.Path.GetInvalidFileNameChars()));

        return Directory.GetFiles(config.BackupFolder, $"{safeName}_*.zip")
            .Select(path =>
            {
                var fi = new FileInfo(path);
                return new BackupInfo(path, fi.Name, fi.LastWriteTime, fi.Length);
            })
            .OrderByDescending(b => b.Timestamp)
            .ToList();
    }

    public void RestoreBackup(ServerConfig config, string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException($"Backup not found: {backupPath}");

        if (string.IsNullOrWhiteSpace(config.Dir))
            throw new InvalidOperationException("Server directory is not configured.");

        // Clear the server directory before restoring
        if (Directory.Exists(config.Dir))
        {
            foreach (var file in Directory.GetFiles(config.Dir, "*", SearchOption.AllDirectories))
                File.Delete(file);

            foreach (var dir in Directory.GetDirectories(config.Dir).OrderByDescending(d => d.Length))
                Directory.Delete(dir, true);
        }
        else
        {
            Directory.CreateDirectory(config.Dir);
        }

        ZipFile.ExtractToDirectory(backupPath, config.Dir, overwriteFiles: true);
    }

    public void PruneBackups(ServerConfig config)
    {
        if (config.KeepBackups <= 0) return;

        var backups = GetBackups(config);
        var toDelete = backups.Skip(config.KeepBackups).ToList();

        foreach (var backup in toDelete)
        {
            try { File.Delete(backup.Path); }
            catch { /* best effort */ }
        }
    }
}
