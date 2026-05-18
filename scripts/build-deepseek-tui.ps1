# 从本地 DeepSeek-TUI 源码构建 deepseek.exe + deepseek-tui.exe，并复制到 publish/Assets/tools
param(
    [string]$TuiSourcePath = "C:\Users\xiaow\Desktop\DSD\DeepSeek-TUI-main\DeepSeek-TUI-main",
    [string]$ToolsOut = "",
    [switch]$SkipCargo
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot([string]$path) {
    if (-not $path) { return $null }
    $full = [System.IO.Path]::GetFullPath($path.Trim())
    if ((Test-Path (Join-Path $full "Cargo.toml")) -and (Test-Path (Join-Path $full "crates\tui"))) {
        return $full
    }
    $nested = Join-Path $full "DeepSeek-TUI-main"
    if ((Test-Path (Join-Path $nested "Cargo.toml")) -and (Test-Path (Join-Path $nested "crates\tui"))) {
        return $nested
    }
    return $null
}

$root = Resolve-RepoRoot $TuiSourcePath
if (-not $root) {
    throw "未找到 DeepSeek-TUI 源码根目录（需含 Cargo.toml 与 crates\tui）: $TuiSourcePath"
}

if (-not $ToolsOut) {
    $ToolsOut = Join-Path $PSScriptRoot "..\publish\Assets\tools" | Resolve-Path -ErrorAction SilentlyContinue
    if (-not $ToolsOut) {
        $ToolsOut = Join-Path (Split-Path $PSScriptRoot -Parent) "publish\Assets\tools"
    }
}
New-Item -ItemType Directory -Force -Path $ToolsOut | Out-Null

if (-not $SkipCargo) {
    $cargo = Get-Command cargo -ErrorAction SilentlyContinue
    if (-not $cargo) { throw "未找到 cargo，请安装 Rust: https://rustup.rs" }
    Write-Host "cargo build --release (repo: $root)"
    Push-Location $root
    & cargo build --release -p deepseek-tui-cli -p deepseek-tui
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "cargo build 失败" }
    Pop-Location
}

$dispatcher = Join-Path $root "target\release\deepseek.exe"
$runtime = Join-Path $root "target\release\deepseek-tui.exe"
if (-not (Test-Path $dispatcher)) { throw "缺少 $dispatcher" }
if (-not (Test-Path $runtime)) { throw "缺少 $runtime" }

Copy-Item -Force $dispatcher (Join-Path $ToolsOut "deepseek.exe")
Copy-Item -Force $runtime (Join-Path $ToolsOut "deepseek-tui.exe")
$ver = & $dispatcher --version 2>$null
if (-not $ver) { $ver = "local-source" }
Set-Content -Path (Join-Path $ToolsOut "version.txt") -Value "$ver (local source)" -Encoding utf8
Write-Host "OK: $ToolsOut"
