# DEPRECATED — do not run. Former edge -> desktop robocopy /MIR overwrote newer desktop.
$ErrorActionPreference = "Stop"

Write-Host @"

[已停用] sync-to-deepseek-desktop.ps1

  deepseek_desktop 是 WPF 主线仓库，日常只在此开发并 git commit。

  勿再从 deepseek-edge 向本目录做镜像同步：
    robocopy /MIR 会删除 desktop 独有文件，并用较旧的 edge 覆盖较新源码。

  若需从归档 deepseek-edge 取回单个文件，请手动复制后 diff/合并。

  详见 docs/MIGRATION.md

"@ -ForegroundColor Yellow

throw "sync-to-deepseek-desktop.ps1 is retired; use deepseek_desktop as the sole source of truth."
