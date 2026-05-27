# DeepSeek Desktop 安装与升级指南

> 撰写：**Auto**（Cursor Agent） · 对应发布：**v2.5.0** · 仓库：[fanstars2318/deepseek-desktop](https://github.com/fanstars2318/deepseek-desktop)

---

## 1. 系统要求

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows 10/11（64 位） |
| 运行时 | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)（框架依赖发布） |
| Web 引擎 | [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) |
| 磁盘 | 解压后约 70 MB（含 `Assets`、`runtimes/` 与依赖 DLL） |
| 网络 | 首次使用需访问 DeepSeek 网页或供应商 OAuth 完成登录 |

---

## 2. 从 GitHub Release 安装

1. 打开 [Releases](https://github.com/fanstars2318/deepseek-desktop/releases)。
2. 在最新版本（**v2.5.0**）的 **Assets** 中下载：
   - **`DeepSeek-Desktop-v2.5.0-win-x64.zip`** — WPF 主线 Windows x64 便携包
3. 将 zip **完整解压**到目标文件夹，例如 `D:\Apps\DeepSeekDesktop\`。
4. 双击 **`DeepSeek.exe`** 启动。
5. 首次启动在嵌入网页中登录 DeepSeek 账号；在 Agent / **DSD API** 设置中可添加其他 OpenAI 兼容供应商（支持内嵌 OAuth）。

**不要**只复制单个 `DeepSeek.exe`：必须与同目录下的 `DeepSeek.dll`、`DeepSeek.Core.dll`、`Assets\`、`WebView2Loader.dll` 等一并保留。

---

## 3. 解压后的目录说明

```
DeepSeekDesktop/
├── DeepSeek.exe              # 主程序入口
├── DeepSeek.dll
├── DeepSeek.Core.dll
├── Assets/                   # Agent、inject、dsd-api 等 Web 资源
├── WebView2Loader.dll
├── Microsoft.Web.WebView2.*  # WebView2 托管程序集
├── e_sqlite3.dll 等          # 本地数据库依赖
└── DISCLAIMER.md             # 免责声明（若发布包内包含）
```

---

## 4. 用户数据与配置位置

| 路径 | 内容 |
|------|------|
| `%LocalAppData%\deepseek_desktop\` | 桌面壳配置、日志、DSD API 请求日志 |
| `%UserProfile%\.deepseek\` | Agent / Harness 配置（如 `config.json`） |
| `%UserProfile%\.deepseek\provider-accounts.json` | DSD API 多供应商账户（若已配置） |

升级 Release 时**无需**删除上述目录；若遇异常可备份后清理缓存再试。

---

## 5. 从源码安装（开发者）

```powershell
git clone https://github.com/fanstars2318/deepseek-desktop.git
cd deepseek-desktop
.\build.ps1
.\publish\DeepSeek.exe
```

本地打包 zip（不上传 GitHub）：

```powershell
.\scripts\package-release.ps1 -Version 2.4.0
```

---

## 6. 升级

1. 关闭正在运行的 `DeepSeek.exe`（含托盘图标）。
2. 下载新版本 zip，解压到新文件夹或覆盖旧目录（建议先备份自定义修改）。
3. 再次运行 `DeepSeek.exe`；配置目录会自动沿用。

跨版本说明见 [Releases](https://github.com/fanstars2318/deepseek-desktop/releases) 中各版本的 Release Notes。

---

## 7. 常见问题

### 启动闪退或提示缺少 .NET

安装 **.NET 10 Desktop Runtime**（x64），然后重启应用。

### 窗口空白或无法加载网页

安装或修复 **WebView2 Runtime**；企业环境需允许 `msedgewebview2.exe` 运行。

### OAuth 登录窗口无法打开

确认 WebView2 正常、网络可访问供应商授权页；在 DSD API 设置中重试「连接账户」。

### SmartScreen 拦截

本软件为社区构建、未代码签名。若你信任维护者 [@fanstars2318](https://github.com/fanstars2318) 与本仓库源码，可在「更多信息」中选择仍要运行；亦可自行 `git clone` 后执行 `.\build.ps1` 本地编译。

### Agent 或 DSD API 无响应

确认网页 DeepSeek 已登录；在 Agent 设置中运行 **doctor**；检查 `%LocalAppData%\deepseek_desktop\logs\` 日志。

---

## 8. 卸载

1. 退出 `DeepSeek.exe`。
2. 删除解压目录。
3. （可选）删除 `%LocalAppData%\deepseek_desktop\` 与 `%UserProfile%\.deepseek\` 以清除本地数据。

---

## 9. 法律与免责

使用本软件前请阅读仓库根目录 **[DISCLAIMER.md](../DISCLAIMER.md)**。本软件为**非官方**第三方工具，与 DeepSeek 官方无隶属关系。
