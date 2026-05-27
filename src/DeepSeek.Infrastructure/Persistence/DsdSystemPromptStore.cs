using System.Text.Json;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>系统提示词存储（对齐 Chat2API storeManager systemPrompts）。</summary>
public static class DsdSystemPromptStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly object Gate = new();

    public sealed class SystemPromptRecord
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Prompt { get; set; } = "";
        public string Type { get; set; } = "general";
        public bool IsBuiltin { get; set; }
        public string? Emoji { get; set; }
        public List<string>? Groups { get; set; }
        public long CreatedAt { get; set; }
        public long UpdatedAt { get; set; }
    }

    private static string FilePath =>
        Path.Combine(ConfigStore.ConfigDirectory, "system-prompts.json");

    public static IReadOnlyList<SystemPromptRecord> GetAll()
    {
        lock (Gate)
        {
            var list = new List<SystemPromptRecord>();
            list.AddRange(GetBuiltin());
            list.AddRange(LoadCustom());
            return list;
        }
    }

    public static IReadOnlyList<SystemPromptRecord> GetBuiltin() => Array.Empty<SystemPromptRecord>();

    public static IReadOnlyList<SystemPromptRecord> GetCustom()
    {
        lock (Gate) return LoadCustom();
    }

    public static SystemPromptRecord? GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return GetAll().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<SystemPromptRecord> GetByType(string type) =>
        GetAll().Where(p => string.Equals(p.Type, type, StringComparison.OrdinalIgnoreCase)).ToList();

    public static SystemPromptRecord Add(SystemPromptRecord draft)
    {
        lock (Gate)
        {
            var list = LoadCustom();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var rec = new SystemPromptRecord
            {
                Id = "prompt-" + Guid.NewGuid().ToString("N")[..10],
                Name = draft.Name.Trim(),
                Description = draft.Description ?? "",
                Prompt = draft.Prompt ?? "",
                Type = string.IsNullOrWhiteSpace(draft.Type) ? "general" : draft.Type,
                IsBuiltin = false,
                Emoji = draft.Emoji,
                Groups = draft.Groups,
                CreatedAt = now,
                UpdatedAt = now
            };
            list.Add(rec);
            SaveCustom(list);
            return rec;
        }
    }

    public static SystemPromptRecord? Update(string id, Action<SystemPromptRecord> mutate)
    {
        lock (Gate)
        {
            var list = LoadCustom();
            var rec = list.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (rec is null || rec.IsBuiltin) return null;
            mutate(rec);
            rec.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SaveCustom(list);
            return rec;
        }
    }

    public static bool Delete(string id)
    {
        lock (Gate)
        {
            var list = LoadCustom();
            var removed = list.RemoveAll(p =>
                string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase) && !p.IsBuiltin);
            if (removed == 0) return false;
            SaveCustom(list);
            return true;
        }
    }

    public static object ToUi(SystemPromptRecord rec) => new
    {
        id = rec.Id,
        name = rec.Name,
        description = rec.Description,
        prompt = rec.Prompt,
        type = rec.Type,
        isBuiltin = rec.IsBuiltin,
        emoji = rec.Emoji,
        groups = rec.Groups ?? new List<string>(),
        createdAt = rec.CreatedAt,
        updatedAt = rec.UpdatedAt
    };

    private static List<SystemPromptRecord> LoadCustom()
    {
        if (!File.Exists(FilePath))
            return new List<SystemPromptRecord>();
        try
        {
            return JsonSerializer.Deserialize<List<SystemPromptRecord>>(File.ReadAllText(FilePath), JsonOptions)
                   ?? new List<SystemPromptRecord>();
        }
        catch
        {
            return new List<SystemPromptRecord>();
        }
    }

    private static void SaveCustom(List<SystemPromptRecord> list)
    {
        Directory.CreateDirectory(ConfigStore.ConfigDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(list, JsonOptions));
    }
}
