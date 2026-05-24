# 手动验收清单（WPF 桌面端）
# 用法：先运行 .\build.ps1，再运行 .\publish\DeepSeek.exe

Write-Host @"
DeepSeek Desktop 手动验收清单
============================

自动化（build.ps1 末尾会跑部分项）：
  [ ] dotnet test DeepSeek.Core.Tests
  [ ] scripts/verify-integration.ps1   (TUI 二进制 + 5111 未监听)
  [ ] scripts/smoke-test.ps1           (进程启动存活)
  [ ] scripts/agent-tui-smoke.ps1      (Agent 发送 hello，需 config 中 webUserToken)

Submodule（首次克隆）：
  git submodule update --init third-party/DeepSeek-TUI
  可选：.\build.ps1 -BuildTuiFromSource  （Rust 1.88+）

请在本机交互环境下逐项确认：

1. 对话
   - 打开「对话」，chat.deepseek.com 正常加载
   - 登录后悬浮按钮可切换 Agent

2. Agent
   - 先在对话页登录
   - 发送 hello，应收到正常 AI 回复（非 TUI Win32/鉴权错误）
   - 深度思考 / 联网 / 策略切换可用
   - 停止按钮可中断

3. 设置
   - 保存工作区、审批模式
   - 外部 API 默认关闭；开启后可生成 Key
   - API 管理打开 Chat2API 控制台（非 DeepSeek 设置页）

4. 架构
   - 5111 端口不应监听（Get-NetTCPConnection -LocalPort 5111）
   - Agent 运行期间临时使用 17425 转发（任务结束即停）
   - TUI Runtime 7878 仅在 Agent 预热/运行期间由 deepseek serve 监听

数据目录：%LocalAppData%\deepseek_desktop\（旧版 DeepSeekEdge 会在首次启动时自动迁移）
日志：%LocalAppData%\deepseek_desktop\logs\
"@
