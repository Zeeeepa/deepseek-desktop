namespace DeepSeekBrowser.Models;

public static class ApiProviderKinds
{
    public const string BuiltinWeb = "builtin_web";
    public const string OpenAiCompatible = "openai_compatible";
    public const string Sidecar = "sidecar";
    public const string Custom = "custom";
}

public static class ApiRouteModes
{
    public const string EmbeddedWeb = "embedded_web";
    public const string DirectApi = "direct_api";
    public const string SidecarHttp = "sidecar_http";
}

public sealed class ApiProviderEntry
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Kind { get; set; } = ApiProviderKinds.BuiltinWeb;
    public string RouteMode { get; set; } = ApiRouteModes.EmbeddedWeb;
    public string BaseUrl { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool DefaultForAgent { get; set; }
    public bool DefaultForChat { get; set; }
    public string? SidecarCommand { get; set; }
    public string? SidecarArgs { get; set; }
    public string? SidecarHealthUrl { get; set; }
    public List<string> Models { get; set; } = new();
    public List<ModelMappingEntry> ModelMappings { get; set; } = new();
}
