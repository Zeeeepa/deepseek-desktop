using System.Text;
using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>L1 能力层：内置工具 schema + MCP 目录文本（供系统提示注入）。</summary>
public static class HarnessToolRegistry
{
    public static string BuildBuiltinToolsSection()
    {
        var sb = new StringBuilder();
        sb.AppendLine("内置工具（通过 <tool_calling> 调用，arguments 必须是合法 JSON）：");
        AppendTool(sb, "read_file", "path: string（如 " + HarnessVirtualPathMapper.WorkspaceVirtual + "/README.md）");
        AppendTool(sb, "write_file", "path: string, content: string（勿写入 " + HarnessVirtualPathMapper.UploadsVirtual + "）");
        AppendTool(sb, "list_dir", "path: string（可选，默认 " + HarnessVirtualPathMapper.WorkspaceVirtual + "）");
        AppendTool(sb, "grep", "pattern: string, path: string（可选）");
        AppendTool(sb, "glob", "pattern: string（如 **/*.cs）, path: string（可选）");
        AppendTool(sb, "run_shell", "command: string（Windows cmd，Blueprint/Explore 阶段禁用）");
        sb.AppendLine();
        sb.AppendLine("调用格式示例：");
        sb.AppendLine("<tool_calling>");
        sb.AppendLine("<name>read_file</name>");
        sb.AppendLine("<arguments>{\"path\":\"README.md\"}</arguments>");
        sb.AppendLine("</tool_calling>");
        return sb.ToString().TrimEnd();
    }

    private static void AppendTool(StringBuilder sb, string name, string args) =>
        sb.AppendLine("- " + name + ": " + args);

    public static string BuildMcpSection(string? mcpCatalog) =>
        string.IsNullOrWhiteSpace(mcpCatalog) ? "" : "\n\n" + mcpCatalog.Trim();
}
