# DeepSeek Desktop（deepseek_desktop）

本目录为 **WPF 主线** 的瘦身工作区：仅含可交付源码与资产，不含 Qt / WinUI / Bridge 实验代码。

同级目录 `deepseek-edge` 为归档/实验仓（Qt、DdBridge 等），**已停止**向本目录同步。

## 目录约定

| 路径 | 说明 |
|------|------|
| `publish/` | 唯一编译发布输出（运行 `.\build.ps1` 生成） |
| `Assets/` | 运行时 Web 资产（inject、agent、chat2api） |
| `DeepSeek.Core/` | 共享业务与 Harness |
| `scripts/` | 构建、验收、供应链审计 |

## 快速开始

```powershell
.\build.ps1
.\publish\DeepSeek.exe
```

## 与 deepseek-edge 的关系

- **只在本目录开发**：改 WPF、Agent、注入脚本后在此 `git commit`。
- **不要**再运行 `scripts/sync-to-deepseek-desktop.ps1`（已停用；原 `robocopy /MIR` 会用较旧的 edge 覆盖本仓）。
- **不要**把 `bin/`、`obj/`、`publish/` 提交进 Git。

详见 [docs/MIGRATION.md](docs/MIGRATION.md)。

## 工程验收

见 `docs/ENGINEERING_REVIEW.md`、`docs/PERIPHERAL_AUDIT.md`。提交 PR 前请运行：

```powershell
.\scripts\audit-supply-chain.ps1
.\scripts\clean-repo-artifacts.ps1
.\build.ps1
```
