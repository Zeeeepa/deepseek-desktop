using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

public static class AgentModeHelper
{
    public const string AgentModel = "deepseek-v4-pro";

    /// <summary>Agent 专用页默认模型与步数；深度思考/联网由配置或 UI 传入，不由客户端预判消息类型。</summary>
    public static void ApplyAgentDefaults(AppConfig config)
    {
        config.Model = AgentModel;
        if (config.MaxAgentSteps < 25)
            config.MaxAgentSteps = 25;
    }

    public static void ApplyChatMode(AppConfig config, string mode, bool deepThink, bool smartSearch)
    {
        config.AgentDeepThinking = deepThink;
        config.AgentWebSearch = smartSearch;
        switch (mode)
        {
            case "专家":
                config.Model = AgentModel;
                config.MaxAgentSteps = 30;
                break;
            case "识图":
                config.Model = AgentModel;
                config.MaxAgentSteps = 20;
                break;
            default:
                config.Model = AgentModel;
                config.MaxAgentSteps = 15;
                break;
        }
    }
}
