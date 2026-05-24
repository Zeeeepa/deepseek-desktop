using System.Text.Json;
using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeekBrowser.Services.Harness.Interop;

/// <summary>
/// 内置工具 OpenAI function schema（与 MCP / Chat Completions tools 格式对齐）。
/// </summary>
public static class HarnessMarketToolSchema
{
    public static string BuildOpenAiBuiltinToolsJson()
    {
        var tools = new object[]
        {
            Fn("read_file", "Read a UTF-8 text file under workspace", Obj(
                ("path", Str("Virtual path e.g. " + HarnessVirtualPathMapper.WorkspaceVirtual + "/file")))),
            Fn("write_file", "Write UTF-8 text to a file under workspace", Obj(
                ("path", Str("Virtual path under workspace or outputs; not uploads")),
                ("content", Str("File content")))),
            Fn("list_dir", "List directory entries under workspace", Obj(
                ("path", Str("Relative directory path, default .")))),
            Fn("grep", "Search file contents by regex under workspace", Obj(
                ("pattern", Str("Regex pattern")),
                ("path", Str("Relative path, default .")))),
            Fn("glob", "Find files by glob pattern under workspace", Obj(
                ("pattern", Str("Glob like **/*.cs")),
                ("path", Str("Relative directory, default .")))),
            Fn("run_shell", "Run a shell command in workspace (Windows cmd)", Obj(
                ("command", Str("Command line"))))
        };

        return JsonSerializer.Serialize(tools, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object Fn(string name, string description, object parameters) => new
    {
        type = "function",
        function = new { name, description, parameters }
    };

    private static object Obj(params (string name, object schema)[] props)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();
        foreach (var (name, schema) in props)
        {
            properties[name] = schema;
            required.Add(name);
        }

        return new
        {
            type = "object",
            properties,
            required
        };
    }

    private static object Str(string description) => new { type = "string", description };
}
