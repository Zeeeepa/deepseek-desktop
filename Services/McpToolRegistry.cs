using System.Text;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

/// <summary>
/// Qwen ReAct 风格 MCP 工具注册表（参考 Qwen-main/examples/react_prompt.md、react_demo.py）。
/// </summary>
public sealed class McpToolRegistry
{
  private const string QwenToolDescTemplate =
      "{name}: Call this tool to interact with the MCP API. {description} Parameters: {parameters} Format the arguments as a JSON object.";

  private readonly IReadOnlyList<AgentToolDescriptor> _tools;

  private McpToolRegistry(IReadOnlyList<AgentToolDescriptor> tools) => _tools = tools;

  public int Count => _tools.Count;

  public IReadOnlyList<AgentToolDescriptor> Tools => _tools;

  public IEnumerable<AgentToolDescriptor> All => _tools;

  public static McpToolRegistry FromDescriptors(IReadOnlyList<AgentToolDescriptor> tools) =>
      new(tools);

  public McpToolRegistry FilterByNames(IEnumerable<string> names)
  {
      var set = new HashSet<string>(names.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
      if (set.Count == 0) return new McpToolRegistry([]);

      var filtered = _tools.Where(t => set.Contains(t.ExposedName)).ToList();
      return new McpToolRegistry(filtered);
  }

  public McpToolRegistry FilterByHints(IEnumerable<string>? hints)
  {
      var hintList = hints?.Where(h => !string.IsNullOrWhiteSpace(h)).ToList();
      if (hintList is not { Count: > 0 })
          return this;

      var filtered = _tools.Where(t => hintList.Any(h =>
          t.ExposedName.Equals(h, StringComparison.OrdinalIgnoreCase)
          || t.ExposedName.EndsWith("__" + h, StringComparison.OrdinalIgnoreCase)
          || t.ExposedName.Contains(h, StringComparison.OrdinalIgnoreCase))).ToList();

      return filtered.Count > 0 ? new McpToolRegistry(filtered) : this;
  }

  public string BuildQwenToolDescBlock()
  {
      if (_tools.Count == 0)
          return "（无可用工具）";

      var sb = new StringBuilder();
      foreach (var t in _tools)
      {
          sb.AppendLine(QwenToolDescTemplate
              .Replace("{name}", t.ExposedName)
              .Replace("{description}", t.Description)
              .Replace("{parameters}", t.ParametersJson));
      }

      return sb.ToString().TrimEnd();
  }

  public string BuildToolNamesCsv() =>
      string.Join(",", _tools.Select(t => t.ExposedName));

  public string BuildCompactList()
  {
      if (_tools.Count == 0) return "（无）";
      return string.Join("\n", _tools.Select(t => "- " + t.ExposedName + ": " + t.Description));
  }
}
