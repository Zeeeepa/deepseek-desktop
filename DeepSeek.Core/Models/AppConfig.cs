using System.Text.Json.Serialization;

namespace DeepSeekBrowser.Models;

public sealed class AppConfig
{
    public string DeepSeekApiKey { get; set; } = "";
    public string WebUserToken { get; set; } = "";
    public string Model { get; set; } = "deepseek-v4-pro";
    public string ApiBaseUrl { get; set; } = "https://api.deepseek.com";
    public string WebApiBaseUrl { get; set; } = "https://chat.deepseek.com/api";
    /// <summary>外部 OpenAI 兼容 API 端口；0 表示使用内置默认（17425）。桌面模块间通信不经此端口。</summary>
    public int LocalApiPort { get; set; }

    /// <summary>允许本机第三方应用调用 OpenAI 兼容 HTTP API（可选，默认关闭）。</summary>
    public bool EnableExternalOpenAiApi { get; set; }

    /// <summary>启用后，外部调用本地 OpenAI 兼容 API 须携带 Bearer / X-API-Key。</summary>
    public bool EnableLocalApiKeyAuth { get; set; }

    /// <summary>本地 OpenAI 兼容 API 的访问密钥列表。</summary>
    public List<LocalApiKey> LocalApiKeys { get; set; } = new();

    /// <summary>Chat2API 会话模式：single（单轮，默认）| multi（多轮，需 session_id）。</summary>
    public string Chat2ApiSessionMode { get; set; } = "single";

    public int Chat2ApiSessionTimeoutMinutes { get; set; } = 30;

    public int Chat2ApiMaxMessagesPerSession { get; set; } = 100;

    /// <summary>模型映射（对齐 chat2api-doc 模型映射）。</summary>
    public List<ModelMappingEntry> ModelMappings { get; set; } = new();

    public bool PreferWebSessionForApi { get; set; } = true;
    public int MaxAgentSteps { get; set; } = 25;
    /// <summary>chat = 网页对话；agent / plan = 进程内 Harness + Chat2API</summary>
    public string DefaultWorkMode { get; set; } = "chat";
    /// <summary>blueprint = Explore→Blueprint；execute = Execute 单阶段。plan/react 为兼容别名。</summary>
    public string DefaultAgentStrategy { get; set; } = "execute";

    /// <summary>true：首次 run_shell 时再初始化本地沙盒；false：任务开始前初始化。</summary>
    public bool AgentSandboxLazyInit { get; set; } = true;

    /// <summary>Agent：Chat2API 深度思考；默认关，由 UI 或用户显式开启，不由客户端预判消息类型。</summary>
    public bool AgentDeepThinking { get; set; }

    /// <summary>Agent：Chat2API 联网搜索；默认关，由 UI 或用户显式开启。</summary>
    public bool AgentWebSearch { get; set; }

    /// <summary>Agent 运行时写入调试日志（%LocalAppData%\deepseek_desktop\logs）。</summary>
    public bool AgentDebugLogEnabled { get; set; } = true;

    /// <summary>Agent 调试日志是否弹出 CMD 窗口实时 tail。</summary>
    public bool AgentDebugLogConsole { get; set; } = true;
    public bool EnableSubAgents { get; set; } = true;
    public int MaxSubAgentSteps { get; set; } = 10;

    /// <summary>工作区根目录；Harness 内置工具限制在此目录下。</summary>
    [JsonPropertyName("qwenCodeWorkspaceRoot")]
    public string AgentWorkspaceRoot { get; set; } = "";

    /// <summary>审批模式：smart | readonly | always | never（同步到 ~/.deepseek/config.toml）。</summary>
    [JsonPropertyName("qwenCodeApprovalMode")]
    public string AgentApprovalMode { get; set; } = "smart";

    [JsonPropertyName("qwenCodeAutoApproveReadOnly")]
    public bool AgentAutoApproveReadOnly { get; set; } = true;

    [JsonPropertyName("qwenCodeAllowShell")]
    public bool AgentAllowShell { get; set; } = true;

    /// <summary>Execute 完成后自动运行 Verify 验收命令（可被 Playbook 覆盖）。</summary>
    public bool AgentVerifyAfterExecute { get; set; }

    /// <summary>默认 Verify 命令，如 dotnet test。</summary>
    public string AgentVerifyCommand { get; set; } = "";

    public int AgentVerifyTimeoutSeconds { get; set; } = 120;

    /// <summary>Verify 失败时不阻断任务（仅附加警告）。</summary>
    public bool AgentVerifyOptional { get; set; }

    /// <summary>连接 MCP 时合并 ~/.cursor/mcp.json、Claude Desktop 等市场配置（默认开启）。</summary>
    public bool AgentImportMarketMcp { get; set; } = true;

    /// <summary>工具输出超过阈值时落盘到 .deepseek/runs/ 并注入摘要。</summary>
    public bool AgentToolOutputSpill { get; set; } = true;

    public int AgentToolOutputInlineMaxChars { get; set; } = 6000;

    /// <summary>任务完成后写入 .deepseek/runs/&lt;runId&gt;/postmortem.md</summary>
    public bool AgentWritePostMortem { get; set; } = true;

    /// <summary>多步 Verify（Execute 完成后按序执行；非空时优先于 AgentVerifyCommand）。</summary>
    public List<string> AgentVerifyCommands { get; set; } = new();

    /// <summary>网页对话输出截断时自动续写。</summary>
    public bool EnableAdaptiveOutputEscalation { get; set; } = true;

    /// <summary>Agent 对话保留天数，0 = 不按时间自动删除。</summary>
    public int AgentSessionRetentionDays { get; set; } = 30;

    /// <summary>Agent 对话本地占用上限（GB），超出则删除最久未更新的对话；0 = 不限制。</summary>
    public double AgentSessionMaxStorageGb { get; set; } = 2;

    /// <summary>启动与保存后是否按上述规则自动清理。</summary>
    public bool AgentSessionAutoCleanup { get; set; } = true;

    /// <summary>Agent 自动化 Webhook 监听端口（仅 127.0.0.1）。</summary>
    public int AgentAutomationsWebhookPort { get; set; } = 17426;

    public List<McpServerConfig> McpServers { get; set; } = new()
    {
        new()
        {
            Name = "本地文件系统",
            Arguments = ["-y", "@modelcontextprotocol/server-filesystem", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)]
        }
    };
}
