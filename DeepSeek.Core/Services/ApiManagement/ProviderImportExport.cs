using System.Text.Json;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>自定义供应商 JSON 导入/导出（对齐 Chat2API CustomProviderManager）。</summary>
public static class ProviderImportExport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string Export(AppConfig config, string providerId)
    {
        var entry = ApiProviderRegistry.Get(config, providerId)
                    ?? throw new InvalidOperationException($"供应商不存在: {providerId}");

        if (BuiltinProviderCatalog.Find(entry.Id) is not null
            && entry.Kind != ApiProviderKinds.Custom)
            throw new InvalidOperationException("内置供应商不支持导出，请复制后编辑自定义副本");

        var payload = new
        {
            name = entry.DisplayName,
            authType = MapAuthTypeForExport(entry),
            apiEndpoint = string.IsNullOrWhiteSpace(entry.BaseUrl) ? "https://api.openai.com/v1" : entry.BaseUrl,
            headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            description = BuiltinProviderCatalog.Find(entry.Id)?.DescriptionZh ?? entry.DisplayName,
            supportedModels = entry.Models.ToArray(),
            credentialFields = BuildCredentialFields(entry)
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static ApiProviderEntry Import(AppConfig config, string jsonData)
    {
        if (string.IsNullOrWhiteSpace(jsonData))
            throw new InvalidOperationException("JSON 不能为空");

        using var doc = JsonDocument.Parse(jsonData);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("无效的 JSON 格式");

        var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("缺少 name 字段");

        var apiEndpoint = root.TryGetProperty("apiEndpoint", out var ep) ? ep.GetString() : null;
        if (string.IsNullOrWhiteSpace(apiEndpoint))
            throw new InvalidOperationException("缺少 apiEndpoint 字段");

        if (!Uri.TryCreate(apiEndpoint, UriKind.Absolute, out _))
            throw new InvalidOperationException("apiEndpoint 格式无效");

        var models = new List<string>();
        if (root.TryGetProperty("supportedModels", out var sm) && sm.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in sm.EnumerateArray())
            {
                if (m.ValueKind == JsonValueKind.String)
                {
                    var s = m.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        models.Add(s!);
                }
            }
        }

        var newId = "custom-" + Guid.NewGuid().ToString("N")[..10];
        var entry = new ApiProviderEntry
        {
            Id = newId,
            DisplayName = name.Trim(),
            Kind = ApiProviderKinds.Custom,
            RouteMode = ApiRouteModes.DirectApi,
            BaseUrl = apiEndpoint.Trim(),
            Enabled = true,
            Models = models
        };

        ApiProviderRegistry.AddOrUpdate(config, entry);
        return entry;
    }

    private static string MapAuthTypeForExport(ApiProviderEntry entry) =>
        entry.RouteMode switch
        {
            ApiRouteModes.EmbeddedWeb => "userToken",
            _ => "token"
        };

    private static object[] BuildCredentialFields(ApiProviderEntry entry)
    {
        var builtin = BuiltinProviderCatalog.Find(entry.Id);
        if (builtin is not null)
        {
            return builtin.CredentialFields.Select(f => new
            {
                name = f.Name,
                label = f.Label,
                type = "password",
                required = f.Required,
                placeholder = f.Placeholder,
                helpText = f.Help
            }).ToArray();
        }

        return
        [
            new
            {
                name = "apiKey",
                label = "API Key",
                type = "password",
                required = true,
                placeholder = "sk-...",
                helpText = (string?)null
            }
        ];
    }
}
