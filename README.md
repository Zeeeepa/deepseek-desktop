# DeepSeek Desktop

Windows 桌面客户端：内嵌 DeepSeek 网页会话、本地 **Agent Harness**、**DSD API**（OpenAI 兼容层）、MCP 工具与工作区沙盒。

| 项目 | 说明 |
|------|------|
| **仓库** | [github.com/fanstars2318/deepseek-desktop](https://github.com/fanstars2318/deepseek-desktop) |
| **维护者** | [@fanstars2318](https://github.com/fanstars2318) |
| **当前主线** | WPF + WebView2（`build.ps1` 单入口） |
| **最新发布** | [Releases → v2.5.0](https://github.com/fanstars2318/deepseek-desktop/releases/latest) |

> **文档说明**：本 README、`docs/INSTALL.md` 及 `RELEASE_v*.md` 由 **Auto**（Cursor Agent）根据源码与 `DDpublish` 发布包整理撰写。

---

## 功能概览

- **双模式桌面**：DeepSeek 网页聊天与 **Agent** 面板一键切换，双 WebView 保活。
- **Harness**：本地智能体编排、Patch 引擎、工具审批、工作区文件/Shell、Skills 与多智能体工作流。
- **DSD API**：本地 OpenAI 兼容 HTTP；多供应商账户、内嵌 OAuth、模型同步与请求日志。
- **Agent 工作台**：Monaco 代码编辑、终端面板、差异预览（内嵌 Web UI）。
- **MCP**：可挂载 Model Context Protocol 服务器扩展工具面。

使用前请阅读 [DISCLAIMER.md](./DISCLAIMER.md)（非 DeepSeek 官方产品）。

---

## 快速安装

1. 打开 [Releases](https://github.com/fanstars2318/deepseek-desktop/releases)，下载 **`DeepSeek-Desktop-v2.5.0-win-x64.zip`**。
2. 解压后运行 **`DeepSeek.exe`**。
3. 需要时安装 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) 与 [WebView2 运行时](https://developer.microsoft.com/microsoft-edge/webview2/)。

详细步骤见 **[docs/INSTALL.md](./docs/INSTALL.md)**。

---

## 从源码构建

**环境**：Windows 10/11 x64、.NET 10 SDK、WebView2 Runtime。

```powershell
git clone https://github.com/fanstars2318/deepseek-desktop.git
cd deepseek-desktop
.\build.ps1
# 默认发布到 ../DDpublish（见 scripts/Get-DsdPaths.ps1）
```

发布 zip（本地）：

```powershell
.\scripts\package-release.ps1 -Version 2.5.0
```

---

## 仓库结构

| 路径 | 说明 |
|------|------|
| `src/DeepSeek.Domain/` | 领域模型 |
| `src/DeepSeek.Application/` | IPC 与应用编排 |
| `src/DeepSeek.Infrastructure/` | 配置与持久化 |
| `DeepSeek.Core/` | Harness、ApiManagement 核心 |
| `Assets/` | 运行时 Web 资产（inject、agent、dsd-api） |
| `Services/`、`Views/` | WPF 壳、WebView、OAuth |
| `web/dsd-api-renderer/` | DSD API 控制台源码 |
| `scripts/` | 构建、验收、发布 |
| `docs/ARCHITECTURE.md` | 分层架构说明 |
| `CONTRIBUTING.md` | 贡献与目录约定 |

---

## 贡献与反馈

- 代码与 Release 由 [@fanstars2318](https://github.com/fanstars2318) 维护。
- 问题反馈：[GitHub Issues](https://github.com/fanstars2318/deepseek-desktop/issues)。
