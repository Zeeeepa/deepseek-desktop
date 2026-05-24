# DeepSeek Desktop v2.0.0

## 亮点

- **原生 C# Agent Harness**：进程内 ReAct / Blueprint，无需 `deepseek-tui.exe`
- **本地工作区沙盒**：虚拟路径映射、Shell 防护、懒加载沙盒
- **Automations**：定时 / Webhook 触发后台 Agent 任务
- **消息渲染**：Markdown + KaTeX 公式 + 代码高亮
- **构建**：`build.ps1` 唯一发布目录 `publish/`

## 安装

1. 下载 `DeepSeek-Desktop-v2.0.0-win-x64.zip`
2. 解压到任意目录
3. 运行 `DeepSeek.exe`
4. 需已安装 [WebView2 运行时](https://developer.microsoft.com/microsoft-edge/webview2/)

## 升级说明

- V2 不再依赖 DeepSeek-TUI 子进程；`third-party/DeepSeek-TUI` 仅为可选参考 submodule
- 配置与数据仍在 `%LocalAppData%\deepseek_desktop\`
