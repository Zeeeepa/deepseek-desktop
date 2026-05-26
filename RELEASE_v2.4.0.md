# DeepSeek Desktop v2.4.0

> Release 说明撰写：**Auto**（Cursor Agent）  
> 发布维护：**[@fanstars2318](https://github.com/fanstars2318)**

---

## 概述

**v2.4.0** 对应当前 `main` 分支与本地编译产物 **`DDpublish`**（WPF + WebView2，Windows x64 便携包，2026-05-26 构建）。在 v2.3.0 的 **DSD API** 迁移基础上，本版本补齐 OAuth 内嵌登录、多供应商模型同步与桌面壳 IPC 整合。

---

## 亮点

- **OAuth 内嵌登录**：`DsdOAuthInAppLoginService` + `OAuthLoginWindow`，在桌面内完成供应商 OAuth，无需外跳浏览器。
- **API 管理增强**：模型目录同步（`ProviderModelCatalog`）、账户凭证校验、导入导出、GLM / MiniMax 等内置供应商适配。
- **DSD API 会话**：`DsdApiSessionCoordinator`、会话记录与上下文配置存储，Agent 与本地 OpenAI 兼容层共用账户状态。
- **桌面 UX**：工作模式浮层与 overlay 脚本更新；Agent 设置页整合 `dsd-api` 与 `ds-oauth-embedded` 模块。
- **文档精简**：公开说明以 `README.md` 与 `docs/INSTALL.md` 为主，便于 GitHub 访客下载即用。

---

## 下载与安装

| 文件 | 说明 |
|------|------|
| `DeepSeek-Desktop-v2.4.0-win-x64.zip` | 完整便携目录（由 `DDpublish` 打包） |

**步骤：**

1. 下载 zip 并解压。
2. 运行 `DeepSeek.exe`。
3. 安装 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) 与 [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)（若尚未安装）。

详细说明：[docs/INSTALL.md](./docs/INSTALL.md)

---

## 升级自 v2.3.0 / v2.2.0

| 项目 | 说明 |
|------|------|
| 配置与数据 | 仍在 `%LocalAppData%\deepseek_desktop\` 与 `~/.deepseek/` |
| DSD API | 设置与 IPC 命名已统一；旧版 Chat2API 路径仅作兼容保留 |
| 发布包 | 请使用本 Release 的 zip，勿混用旧目录中的 `DDpublish` |

---

## 从源码构建

```powershell
git clone https://github.com/fanstars2318/deepseek-desktop.git
cd deepseek-desktop
git checkout v2.4.0
.\build.ps1
.\publish\DeepSeek.exe
```

---

## 免责声明

本软件为社区维护的**非官方**工具。使用前请阅读 [DISCLAIMER.md](./DISCLAIMER.md)。
