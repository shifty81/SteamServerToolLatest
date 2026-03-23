namespace SteamServerTool.Core.Services;

public record IniEntry(string Section, string Key, string Value, int LineNumber);

public record IniFileInfo(string Path, string FileName, string RelativePath = "");

/// <summary>
/// Scans server directories for config files (INI/CFG/properties/YAML/TOML/JSON/XML),
/// parses them into editable key-value entries, and manages a rolling history of up to
/// 16 archived versions per file so changes can be reverted.
/// </summary>
public class IniFileService
{
    private const int MaxHistory = 16;

    /// <summary>
    /// File extensions considered plain key=value config files that the INI editor can parse.
    /// </summary>
    private static readonly string[] ScanPatterns =
    {
        "*.ini", "*.cfg", "*.conf", "*.config",
        "*.properties",          // Minecraft server.properties, Java .properties
        "*.toml",                // Rust, Valheim, and many modern game servers
        "*.yaml", "*.yml",       // Squad, various game servers
    };

    /// <summary>Additional extensions we surface as read-only raw text tabs.</summary>
    private static readonly string[] TextPatterns =
    {
        "*.json", "*.xml", "*.txt",
    };

    // ─── Discovery ────────────────────────────────────────────────────────
    public List<IniFileInfo> GetConfigFiles(string serverDir)
    {
        if (string.IsNullOrWhiteSpace(serverDir) || !Directory.Exists(serverDir))
            return new List<IniFileInfo>();

        var results = new List<IniFileInfo>();
        foreach (var pattern in ScanPatterns)
            CollectFiles(serverDir, pattern, results);

        return results.OrderBy(f => f.RelativePath).ToList();
    }

    /// <summary>
    /// Returns text files (JSON, XML, TXT) that exist in the server directory tree.
    /// These are shown as raw text, not as key=value INI editors.
    /// </summary>
    public List<IniFileInfo> GetTextFiles(string serverDir)
    {
        if (string.IsNullOrWhiteSpace(serverDir) || !Directory.Exists(serverDir))
            return new List<IniFileInfo>();

        var results = new List<IniFileInfo>();
        foreach (var pattern in TextPatterns)
            CollectFiles(serverDir, pattern, results);

        return results.OrderBy(f => f.RelativePath).ToList();
    }

    /// <summary>
    /// Resolves a manually-specified file path into an IniFileInfo, verifying it exists.
    /// Returns null if the file does not exist or cannot be accessed.
    /// </summary>
    public IniFileInfo? ResolveManualFile(string filePath, string serverDir)
    {
        if (!File.Exists(filePath)) return null;

        // Try to make a relative path for the label
        string rel = filePath;
        try
        {
            if (!string.IsNullOrEmpty(serverDir) &&
                filePath.StartsWith(serverDir, StringComparison.OrdinalIgnoreCase))
            {
                rel = filePath[serverDir.Length..].TrimStart(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            }
        }
        catch { /* fall back to full path */ }

        return new IniFileInfo(filePath, Path.GetFileName(filePath), rel);
    }

    private void CollectFiles(string serverDir, string pattern, List<IniFileInfo> results)
    {
        try
        {
            var histDir = HistoryDir(serverDir);
            var files   = Directory.GetFiles(serverDir, pattern, SearchOption.AllDirectories);
            foreach (var file in files)
            {
                // Skip history archives and the tool's own data folder
                if (file.Contains(histDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                var relative = file;
                try
                {
                    if (file.StartsWith(serverDir, StringComparison.OrdinalIgnoreCase))
                        relative = file[serverDir.Length..].TrimStart(
                            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch { /* keep absolute path */ }

                // Avoid duplicates (a file could match multiple patterns)
                if (results.Any(r => r.Path.Equals(file, StringComparison.OrdinalIgnoreCase)))
                    continue;

                results.Add(new IniFileInfo(file, Path.GetFileName(file), relative));
            }
        }
        catch { /* skip inaccessible folders */ }
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

            // INI section header  [SectionName]
            if (line.StartsWith('[') && line.Contains(']'))
            {
                var closeIdx = line.IndexOf(']');
                if (closeIdx > 1)
                    section = line.Substring(1, closeIdx - 1).Trim();
                continue;
            }

            // Key = Value  (also handles Key: Value for YAML-style)
            int sep = line.IndexOf('=');
            if (sep <= 0)
            {
                // YAML/TOML colon separator: "key: value"
                sep = line.IndexOf(':');
                if (sep <= 0 || line.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                             || line.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var key = line[..sep].Trim();
            var val = line[(sep + 1)..].Trim();

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
