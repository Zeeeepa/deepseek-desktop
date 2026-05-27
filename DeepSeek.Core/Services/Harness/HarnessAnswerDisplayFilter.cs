using System.Text.RegularExpressions;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>
/// 从 Agent 正文回复中剥离工具调用标记（对齐 DeepSeek-TUI tool_parser.clean_text），避免 UI 渲染空代码框与 XML 泄漏。
/// </summary>
public static class HarnessAnswerDisplayFilter
{
    private static readonly Regex ToolCallingComplete = new(
        @"<tool_calling>[\s\S]*?</tool_calling>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ToolCallingOpen = new(
        @"<tool_calling>[\s\S]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ToolCallBlock = new(
        @"\[TOOL_CALL\][\s\S]*?\[/TOOL_CALL\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ToolCallOpen = new(
        @"\[TOOL_CALL\][\s\S]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex XmlToolCall = new(
        @"<(?:deepseek:)?tool_call[\s\S]*?</(?:deepseek:)?tool_call>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InvokeBlock = new(
        @"<invoke\s+name[^>]*>[\s\S]*?</invoke>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BrokenWriteJsonTail = new(
        @"""[\s,]*""content""\s*:\s*""[\s\S]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BrokenJsonObjectTail = new(
        @"\{\s*""(?:path|file_path)""[\s\S]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StrayToolClose = new(
        @"""?\}\s*</arguments>\s*</tool_calling>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EmptyJsonFence = new(
        @"```json\s*```",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OpenJsonFenceOnly = new(
        @"```json\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex JsonToolFence = new(
        @"```(?:json)?\s*\{[\s\S]*?""name""[\s\S]*?""arguments""[\s\S]*?\}\s*```",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string StripForDisplay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var t = text;
        t = ToolCallingComplete.Replace(t, "");
        t = ToolCallBlock.Replace(t, "");
        t = XmlToolCall.Replace(t, "");
        t = InvokeBlock.Replace(t, "");
        t = ToolCallingOpen.Replace(t, "");
        t = ToolCallOpen.Replace(t, "");
        t = BrokenWriteJsonTail.Replace(t, "");
        t = BrokenJsonObjectTail.Replace(t, "");
        t = StrayToolClose.Replace(t, "");
        t = EmptyJsonFence.Replace(t, "");
        t = OpenJsonFenceOnly.Replace(t, "");
        t = JsonToolFence.Replace(t, "");
        t = HarnessXmlToolCallParser.StripLooseJsonToolObjects(t);
        t = Regex.Replace(t, @"</arguments>\s*</tool_calling>", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\n{3,}", "\n\n");
        return t.Trim();
    }
}
