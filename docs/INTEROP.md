# DSD Harness 市场互操作（Interop）

**自研 Harness，标准接口。** 运行时与 Phase/记忆/Playbook 为 DeepSeek Desktop 原创；工具与 Skill **按业界通用格式**接入，避免绑定单一厂商。

## 架构

```
┌─────────────────────────────────────────┐
│  DSD Harness（自研）                      │
│  Orchestrator · Phase · Memory · Gate   │
└───────────────┬─────────────────────────┘
                │ Interop 适配层
    ┌───────────┼───────────┬──────────────┐
    ▼           ▼           ▼              ▼
  MCP       SKILL.md    OpenAI tools    Playbook
 (标准)     (Cursor/     (JSON schema)   (DSD YAML)
           Claude/…)
```

| 模块 | 路径 | 说明 |
|------|------|------|
| `HarnessSkillRegistry` | `Harness/Interop/` | 扫描市场 SKILL.md |
| `McpConfigInterop` | 同上 | 合并 Cursor/Claude `mcpServers` |
| `HarnessMarketToolSchema` | 同上 | 内置工具 OpenAI function JSON |
| `HarnessPlaybookRegistry` | `Harness/` | DSD 原创剧本（非 skill 拷贝） |

## Skill（市场标准）

支持 **只读加载** 下列路径中的 `SKILL.md`（YAML frontmatter + Markdown 正文）：

| 来源 | 路径 |
|------|------|
| Cursor | `~/.cursor/skills/<name>/SKILL.md` |
| Claude | `~/.claude/skills/<name>/SKILL.md` |
| Codex | `~/.codex/skills/<name>/SKILL.md` |
| DSD | `~/.deepseek/skills/<name>/SKILL.md` |
| 项目 | `.cursor/skills/` · `.agents/skills/` · `.deepseek/skills/` |

Agent UI：`/skills` 列表 · `/skill <id>` 启用 · `/skills <id>` 简写

Harness 将 Skill 正文注入 **Composer**（与 Playbook 并列），**不修改** skill 源文件。

## MCP（市场标准）

连接时合并（`AppConfig.AgentImportMarketMcp`，默认 **true**）：

1. 桌面设置中启用的 MCP
2. `~/.deepseek/mcp.json`（桌面同步写出，Claude 同款结构）
3. `~/.cursor/mcp.json`
4. Claude Desktop `claude_desktop_config.json` 中的 `mcpServers`

格式均为：

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\\path"]
    }
  }
}
```

MCP 工具经 `McpHub` 暴露给 Model，调用名与 Cursor/Claude 一致（`server__tool` 命名）。

## OpenAI Tools Schema

内置 FS/Shell 工具的 OpenAI `function` JSON 导出：

`~/.deepseek/tools/builtin-openai-tools.json`

供外部脚本、OpenAI 兼容客户端或文档生成使用；Harness 运行时仍使用 DeepSeek 网页 `<tool_calling>` 协议。

## Playbook vs Skill

| | **Playbook（DSD 原创）** | **Skill（市场标准）** |
|--|--------------------------|------------------------|
| 格式 | `~/.deepseek/playbooks/*.yaml` | `SKILL.md` + frontmatter |
| 用途 | Phase 工作流、Verify 命令 | 领域流程与知识 |
| 谁写 | 你 / 团队 | Cursor/Claude 生态 |

二者可同时启用：`/playbook blueprint-repo` + `/skill code-review`

## 配置

`AppConfig`：

- `AgentImportMarketMcp` — 是否合并 Cursor/Claude MCP（默认 true）

关闭后仅使用桌面设置中的 MCP 列表。

## 代码入口

```
DeepSeek.Core/Services/Harness/Interop/
  HarnessInteropPaths.cs
  HarnessSkillRegistry.cs
  HarnessSkillParser.cs
  McpConfigInterop.cs
  HarnessMarketToolSchema.cs
```
