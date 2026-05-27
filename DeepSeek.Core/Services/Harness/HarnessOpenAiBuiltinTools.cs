using System.Text.Json.Nodes;

namespace DeepSeekBrowser.Services.Harness;

public static class HarnessOpenAiBuiltinTools
{
    public static IReadOnlyList<object> GetDefinitions(
        bool includeShell = true,
        bool includeDelegate = true,
        bool includeParallelExplore = false)
    {
        var list = new List<object>
        {
            Tool("read", "Read files from the workspace (text, images, PDF). Supports line offset, limit, and PDF pages.", new
            {
                type = "object",
                properties = new
                {
                    file_path = Prop("Path to file (virtual or relative)"),
                    offset = new { type = "integer", description = "1-based line number to start reading" },
                    limit = new { type = "integer", description = "Number of lines to read (default 2000)" },
                    pages = Prop("PDF page range e.g. 1-5 or single page number")
                },
                required = new[] { "file_path" },
                additionalProperties = false
            }),
            Tool("image_analyze", "Analyze an image in the workspace using the configured vision model.", new
            {
                type = "object",
                properties = new
                {
                    file_path = Prop("Path to image file"),
                    prompt = Prop("Analysis prompt (optional)")
                },
                required = new[] { "file_path" },
                additionalProperties = false
            }),
            Tool("write", "Create or overwrite a file with full content. Prefer edit for existing files.", new
            {
                type = "object",
                properties = new
                {
                    file_path = Prop("Path to file"),
                    content = Prop("Complete file content")
                },
                required = new[] { "file_path", "content" },
                additionalProperties = false
            }),
            Tool("edit", "Perform scoped string replacements in a file.", new
            {
                type = "object",
                properties = new
                {
                    file_path = Prop("Path to file"),
                    old_string = Prop("Exact text to replace"),
                    new_string = Prop("Replacement text"),
                    replace_all = new { type = "boolean", description = "Replace all occurrences" }
                },
                required = new[] { "old_string", "new_string" },
                additionalProperties = false
            }),
            Tool("list_dir", "List files in a directory.", new
            {
                type = "object",
                properties = new { path = Prop("Directory path, default workspace root") },
                additionalProperties = false
            }),
            Tool("grep", "Search file contents with a regex pattern.", new
            {
                type = "object",
                properties = new
                {
                    pattern = Prop("Regex pattern"),
                    path = Prop("Optional file or directory scope")
                },
                required = new[] { "pattern" },
                additionalProperties = false
            }),
            Tool("glob", "Find files matching a glob pattern.", new
            {
                type = "object",
                properties = new
                {
                    pattern = Prop("Glob like **/*.cs"),
                    path = Prop("Optional root directory")
                },
                required = new[] { "pattern" },
                additionalProperties = false
            }),
            Tool("AskUserQuestion", "Pause and ask the user a clarifying question with options.", new
            {
                type = "object",
                properties = new
                {
                    question = Prop("Question text"),
                    options = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                label = Prop("Option label"),
                                description = Prop("Optional hint")
                            },
                            required = new[] { "label" }
                        }
                    }
                },
                required = new[] { "question", "options" },
                additionalProperties = false
            }),
            Tool("UpdatePlan", "Update the in-session markdown task plan.", new
            {
                type = "object",
                properties = new
                {
                    plan = Prop("Complete markdown checklist"),
                    explanation = Prop("Optional reason for the change")
                },
                required = new[] { "plan" },
                additionalProperties = false
            })
        };

        if (includeDelegate)
        {
            list.Add(Tool("delegate_agent",
                "Spawn a focused sub-agent (explore/plan/review/implementer/verifier/engineer). Returns when done.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        role = Prop("Role: general|explore|plan|review|implementer|verifier|engineer|product-manager|architect|advocate|critic"),
                        task = Prop("Task for the sub-agent"),
                        context = Prop("Optional context handoff from lead agent")
                    },
                    required = new[] { "role", "task" },
                    additionalProperties = false
                }));

            if (includeParallelExplore)
            {
                list.Add(Tool("parallel_explore",
                    "Fan out parallel read-only explore sub-agents (structure/dependencies/risks) and return merged brief.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            task = Prop("Exploration question (defaults to current user goal if omitted)"),
                            fan_out = new { type = "integer", description = "Number of parallel explorers (1-10)" }
                        },
                        additionalProperties = false
                    }));
            }
        }

        if (includeShell)
        {
            list.Add(Tool("bash", "Run a shell command in the workspace (Windows cmd).", new
            {
                type = "object",
                properties = new { command = Prop("Shell command to execute") },
                required = new[] { "command" },
                additionalProperties = false
            }));
        }

        return list;
    }

    public static string MapToBuiltinExecutorName(string openAiName)
    {
        var n = HarnessXmlToolCallParser.NormalizeToolName(openAiName);
        return n.ToLowerInvariant() switch
        {
            "read" => "read_file",
            "write" => "write_file",
            "edit" => "edit_file",
            "bash" => "run_shell",
            "list_dir" => "list_dir",
            "grep" => "grep",
            "glob" => "glob",
            "image_analyze" => "image_analyze",
            _ => n
        };
    }

    private static object Tool(string name, string description, object parameters) => new
    {
        type = "function",
        function = new { name, description, parameters }
    };

    private static object Prop(string description) => new { type = "string", description };
}
