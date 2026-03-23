namespace SteamServerTool.Core.Services;

public record IniEntry(string Section, string Key, string Value, int LineNumber);

public record IniFileInfo(string Path, string FileName);

/// <summary>
/// Scans server directories for INI/CFG config files, parses them into
/// editable key-value entries, and manages a rolling history of up to
/// 16 archived versions per file so changes can be reverted.
/// </summary>
public class IniFileService
{
    private const int MaxHistory = 16;

    private static readonly string[] ScanPatterns = { "*.ini", "*.cfg" };

    // ─── Discovery ────────────────────────────────────────────────────────
    public List<IniFileInfo> GetConfigFiles(string serverDir)
    {
        if (string.IsNullOrWhiteSpace(serverDir) || !Directory.Exists(serverDir))
            return new List<IniFileInfo>();

        var results = new List<IniFileInfo>();
        foreach (var pattern in ScanPatterns)
        {
            try
            {
                var files = Directory.GetFiles(serverDir, pattern, SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    // Skip history archives stored by this service
                    if (file.Contains(HistoryDir(serverDir), StringComparison.OrdinalIgnoreCase))
                        continue;
                    results.Add(new IniFileInfo(file, Path.GetFileName(file)));
                }
            }
            catch { /* skip inaccessible folders */ }
        }

        return results.OrderBy(f => f.FileName).ToList();
    }

    // ─── Parsing ──────────────────────────────────────────────────────────
    public List<IniEntry> ParseFile(string filePath)
    {
        if (!File.Exists(filePath))
            return new List<IniEntry>();

        var entries  = new List<IniEntry>();
        var section  = "";
        var lineNum  = 0;

        foreach (var raw in File.ReadLines(filePath))
        {
            lineNum++;
            var line = raw.TrimEnd();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            if (line.StartsWith('[') && line.Contains(']'))
            {
                var closeIdx = line.IndexOf(']');
                if (closeIdx > 1)
                    section = line.Substring(1, closeIdx - 1).Trim();
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();

            if (!string.IsNullOrEmpty(key))
                entries.Add(new IniEntry(section, key, val, lineNum));
        }

        return entries;
    }

    // ─── Saving with history ──────────────────────────────────────────────
    public void SaveFile(string filePath, IReadOnlyList<IniEntry> entries)
    {
        ArchiveCurrentVersion(filePath);
        WriteEntries(filePath, entries);
    }

    private static void WriteEntries(string filePath, IReadOnlyList<IniEntry> entries)
    {
        var lines  = new List<string>();
        var lastSec = (string?)null;

        foreach (var entry in entries)
        {
            if (entry.Section != lastSec)
            {
                if (lastSec != null) lines.Add("");   // blank line between sections
                if (!string.IsNullOrEmpty(entry.Section))
                    lines.Add($"[{entry.Section}]");
                lastSec = entry.Section;
            }
            lines.Add($"{entry.Key}={entry.Value}");
        }

        File.WriteAllLines(filePath, lines);
    }

    // ─── History ──────────────────────────────────────────────────────────
    public List<string> GetHistory(string filePath, string serverDir)
    {
        var dir = HistoryDir(serverDir);
        if (!Directory.Exists(dir)) return new List<string>();

        var safeName = SafeFileName(filePath);
        return Directory.GetFiles(dir, $"{safeName}_*.bak")
            .OrderByDescending(f => f)
            .Take(MaxHistory)
            .ToList();
    }

    public void RevertToHistory(string filePath, string historyPath)
    {
        if (!File.Exists(historyPath))
            throw new FileNotFoundException($"History file not found: {historyPath}");

        File.Copy(historyPath, filePath, overwrite: true);
    }

    // ─── Internals ────────────────────────────────────────────────────────
    private static void ArchiveCurrentVersion(string filePath)
    {
        if (!File.Exists(filePath)) return;

        var serverDir = Path.GetDirectoryName(filePath)!;
        var histDir   = HistoryDir(serverDir);
        Directory.CreateDirectory(histDir);

        var safeName  = SafeFileName(filePath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var archivePath = Path.Combine(histDir, $"{safeName}_{timestamp}.bak");

        File.Copy(filePath, archivePath, overwrite: false);

        // Prune to MaxHistory entries per file
        var existing = Directory.GetFiles(histDir, $"{safeName}_*.bak")
            .OrderByDescending(f => f)
            .Skip(MaxHistory)
            .ToList();

        foreach (var old in existing)
        {
            try { File.Delete(old); } catch { /* best effort */ }
        }
    }

    private static string HistoryDir(string serverDir) =>
        Path.Combine(serverDir, ".sst_history");

    private static string SafeFileName(string filePath) =>
        string.Concat(Path.GetFileName(filePath).Split(Path.GetInvalidFileNameChars()));
}
