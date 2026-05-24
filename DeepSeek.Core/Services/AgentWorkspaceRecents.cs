using System.Text.Json;

namespace DeepSeekBrowser.Services;

/// <summary>Agent 最近打开的工作区列表（Cursor 风格 Recents）。</summary>
public static class AgentWorkspaceRecents
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object Gate = new();
    private static List<string>? _cache;

    public static string StorePath =>
        Path.Combine(DeepSeekDesktopApp.LocalAppDataRoot, "workspace-recents.json");

    public static IReadOnlyList<string> List()
    {
        lock (Gate)
        {
            EnsureLoaded();
            return _cache!.ToList();
        }
    }

    public static void Touch(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var full = Normalize(path);
        if (!Directory.Exists(full)) return;

        lock (Gate)
        {
            EnsureLoaded();
            _cache!.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
            _cache.Insert(0, full);
            while (_cache.Count > 12)
                _cache.RemoveAt(_cache.Count - 1);
            Save();
        }
    }

    public static string Normalize(string path) =>
        Path.GetFullPath(path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public static string DisplayName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private static void EnsureLoaded()
    {
        if (_cache is not null) return;
        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                var list = JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
                _cache = list?
                    .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                    .Select(Normalize)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? [];
            }
            else
                _cache = [];
        }
        catch
        {
            _cache = [];
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(_cache, JsonOptions));
        }
        catch
        {
            // ignore
        }
    }
}
