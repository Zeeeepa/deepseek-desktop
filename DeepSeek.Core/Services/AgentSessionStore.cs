using System.Text.Json;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

public sealed class AgentSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = AgentSessionJson.Options;

    private readonly string _directory;

    public AgentSessionStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(DeepSeekDesktopApp.LocalAppDataRoot, "agent-sessions");
        Directory.CreateDirectory(_directory);
    }

    public string DirectoryPath => _directory;

    public IReadOnlyList<AgentSessionMeta> ListMetas()
    {
        var metas = new List<AgentSessionMeta>();
        if (!Directory.Exists(_directory))
            return metas;

        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var session = LoadFile(path);
                if (session is null || string.IsNullOrWhiteSpace(session.Id))
                    continue;
                metas.Add(ToMeta(session));
            }
            catch
            {
                // skip corrupt files
            }
        }

        return metas
            .OrderByDescending(m => m.Pinned)
            .ThenByDescending(m => m.UpdatedAt)
            .ThenByDescending(m => m.CreatedAt)
            .ToList();
    }

    public AgentSessionData? Load(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var path = SessionPath(id);
        return !File.Exists(path) ? null : LoadFile(path);
    }

    public void Save(AgentSessionData session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(session.Id))
            throw new ArgumentException("Session id is required.", nameof(session));

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (session.CreatedAt <= 0)
            session.CreatedAt = now;
        session.UpdatedAt = now;

        var json = JsonSerializer.Serialize(session, JsonOptions);
        File.WriteAllText(SessionPath(session.Id), json);
    }

    public bool Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var path = SessionPath(id);
        if (!File.Exists(path))
            return false;

        File.Delete(path);
        return true;
    }

    public bool Rename(string id, string title)
    {
        var session = Load(id);
        if (session is null)
            return false;

        session.Title = string.IsNullOrWhiteSpace(title) ? "新对话" : title.Trim();
        Save(session);
        return true;
    }

    public bool SetPinned(string id, bool pinned)
    {
        var session = Load(id);
        if (session is null)
            return false;

        session.Pinned = pinned;
        Save(session);
        return true;
    }

    public int ApplyRetention(int retentionDays, double maxStorageGb)
    {
        var removed = 0;
        var metas = ListMetas().ToList();
        if (retentionDays > 0)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToUnixTimeMilliseconds();
            foreach (var meta in metas.Where(m => !m.Pinned && m.UpdatedAt < cutoff))
            {
                if (Delete(meta.Id))
                    removed++;
            }
        }

        if (maxStorageGb > 0)
        {
            var maxBytes = (long)(maxStorageGb * 1024 * 1024 * 1024);
            long total = 0;
            var files = Directory.Exists(_directory)
                ? Directory.EnumerateFiles(_directory, "*.json")
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList()
                : [];

            total = files.Sum(f => f.Length);
            foreach (var file in files.AsEnumerable().Reverse())
            {
                if (total <= maxBytes)
                    break;

                var session = LoadFile(file.FullName);
                if (session?.Pinned == true)
                    continue;

                total -= file.Length;
                file.Delete();
                removed++;
            }
        }

        return removed;
    }

    private static AgentSessionMeta ToMeta(AgentSessionData session) => new()
    {
        Id = session.Id,
        Title = session.Title,
        CreatedAt = session.CreatedAt,
        UpdatedAt = session.UpdatedAt,
        Pinned = session.Pinned
    };

    private static AgentSessionData? LoadFile(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AgentSessionData>(json, JsonOptions);
    }

    private string SessionPath(string id)
    {
        var safe = SanitizeId(id);
        return Path.Combine(_directory, safe + ".json");
    }

    private static string SanitizeId(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.Trim().Where(c => !invalid.Contains(c)).ToArray();
        var safe = new string(chars);
        return string.IsNullOrWhiteSpace(safe) ? "session" : safe;
    }
}
