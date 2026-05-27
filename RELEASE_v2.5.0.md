# DeepSeek Desktop v2.5.0

> Release 说明撰写：**Auto**（Cursor Agent）  
> 发布维护：**[@fanstars2318](https://github.com/fanstars2318)**

---

## 概述

**v2.5.0** 对应当前 `main` 分支源码与本地编译产物 **`DDpublish`**（WPF + WebView2，Windows x64 便携包，2026-05-27 构建）。在 v2.4.0 的 DSD API / OAuth 能力之上，本版本完成分层架构落地、Agent 桌面体验增强与 Harness 可靠性加固。

---

## 亮点

### 架构

- **分层解决方案**：新增 `DeepSeek.Domain`、`DeepSeek.Application`、`DeepSeek.Infrastructure`（`src/`），配置与 DSD API 会话持久化迁入 Infrastructure；IPC 处理器迁入 Application。
- **文档**：[`docs/ARCHITECTURE.md`](./docs/ARCHITECTURE.md) 描述依赖规则与 WebView2 进程模型；[`CONTRIBUTING.md`](./CONTRIBUTING.md) 说明各目录职责与 Web UI 构建流程。

### Agent 桌面体验

- **Monaco 与终端**：Agent 面板集成 `monaco-host.js`、`terminal-panel.js`、`diff-host.js`，支持内嵌代码编辑、差异预览与终端输出。
- **WPF 壳拆分**：`MainWindow` 拆为 Navigation / AgentDrop / Verify 等 partial；`DesktopCompositionRoot` 统一依赖注入入口。

### Harness 与 API

- **Harness 加固**：Patch 引擎、循环守卫、空回复/执行回复防护、密钥扫描、XML 工具调用解析、LSP 客户端、并行工具策略等。
- **DSD API IPC**：`DsdApiIpcBridge` 模块化（Legacy 分发 + IpcHost）；账户负载均衡与 DeepSeek 模型路由测试补齐。
- **配置修复**：`tools/ConfigRepair` 与 `scripts/repair-user-config.ps1` 用于损坏 JSON 配置的备份与修复。

### 发布包

- 便携 zip 由 **`DDpublish`** 打包，含完整 `Assets/`、`runtimes/` 与 .NET 依赖（解压后约 **70 MB**）。

---

## 下载与安装

| 文件 | 说明 |
|------|------|
| `DeepSeek-Desktop-v2.5.0-win-x64.zip` | 完整便携目录（由 `DDpublish` 打包） |

**步骤：**

1. 下载 zip 并解压。
2. 运行 `DeepSeek.exe`。
3. 安装 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) 与 [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)（若尚未安装）。

详细说明：[docs/INSTALL.md](./docs/INSTALL.md)

---

## 升级自 v2.4.0

| 项目 | 说明 |
|------|------|
| 配置与数据 | 仍在 `%LocalAppData%\deepseek_desktop\` 与 `~/.deepseek/` |
| 架构迁移 | 旧 `DeepSeek.Core/Models/*` 配置类型已迁至 `src/DeepSeek.Domain`；无需手动迁移 |
| 发布包体积 | 因包含 `runtimes/`，zip 大于 v2.4.0，请完整解压后运行 |

---

## 从源码构建

```powershell
git clone https://github.com/fanstars2318/deepseek-desktop.git
cd deepseek-desktop
git checkout v2.5.0
.\build.ps1
# 默认输出目录见 scripts/Get-DsdPaths.ps1（通常为 ../DDpublish）
```

---

## 免责声明

本软件为社区维护的**非官方**工具。使用前请阅读 [DISCLAIMER.md](./DISCLAIMER.md)。
