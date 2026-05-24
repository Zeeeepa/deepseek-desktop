param(
    [switch]$UseLocalTui,
    [switch]$BuildTuiFromSource,
    [switch]$LegacyWpf,
    [switch]$WinUi,
    [string]$TuiSourcePath = "",
    [switch]$DeployToDesktop,
    [string]$DeployDir = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
. (Join-Path $root "scripts\Get-PublishDir.ps1")
$out = Get-DeepSeekPublishDir -RepoRoot $root
if (-not $TuiSourcePath) {
    $TuiSourcePath = Join-Path $root "third-party\DeepSeek-TUI"
}

# 清空 publish，避免旧版 WPF (net10) 的 DeepSeek.dll / runtimeconfig 与 WinUI 混用导致无法启动
if (Test-Path $out) {
    Get-Process -Name "deepseek" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    try {
        Remove-Item -Recurse -Force $out -ErrorAction Stop
    }
    catch {
        Write-Host "WARN: publish 目录被占用，尝试 robocopy 清空..."
        $empty = Join-Path $env:TEMP "deepseek-publish-empty"
        New-Item -ItemType Directory -Force -Path $empty | Out-Null
        robocopy $empty $out /MIR /NFL /NDL /NJH /NJS /NC /NS | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "无法清空 publish: $_" }
    }
}

Push-Location $root
# 默认使用已验证可运行的 WPF 壳；WinUI 需本机 Windows App Runtime 正常，可用 -WinUi 尝试
$useWpf = $LegacyWpf -or (-not $WinUi)
if ($useWpf) {
    if (Test-Path (Join-Path $root "scripts\build-chat2api-ui.ps1")) {
        & (Join-Path $root "scripts\build-chat2api-ui.ps1")
    }
    if (Test-Path (Join-Path $root "scripts\sync-agent-chat2api.ps1")) {
        & (Join-Path $root "scripts\sync-agent-chat2api.ps1") -Root $root
    }
    dotnet publish DeepSeekBrowser.csproj -c Release -r win-x64 --self-contained false -o $out "-p:UseAppHost=true"
    $exeName = "DeepSeek.exe"
}
else {
    dotnet publish DeepSeek.Desktop\DeepSeek.Desktop.csproj -c Release -r win-x64 --self-contained false -o $out
    # 启动别名：必须同时复制 runtimeconfig / deps，否则会误读旧版 WPF 配置
    Copy-Item -Force (Join-Path $out "DeepSeek.Desktop.exe") (Join-Path $out "DeepSeek.exe")
    Copy-Item -Force (Join-Path $out "DeepSeek.Desktop.runtimeconfig.json") (Join-Path $out "DeepSeek.runtimeconfig.json")
    Copy-Item -Force (Join-Path $out "DeepSeek.Desktop.deps.json") (Join-Path $out "DeepSeek.deps.json")
    $exeName = "DeepSeek.exe"
}
Pop-Location

# Publish output only under publish/ (never bin/)
$exePath = Join-Path $out $exeName
if (-not (Test-Path $exePath)) {
    throw "publish failed: $exeName not found under $out"
}

$legacyBin = Join-Path $root 'bin'
if (Test-Path $legacyBin) {
    try {
        Remove-Item -Recurse -Force $legacyBin -ErrorAction Stop
        Write-Host 'Removed legacy bin/ (canonical output: publish/)'
    }
    catch {
        Write-Host "WARN: could not remove bin/ (in use?): $legacyBin"
    }
}

# WinUI 发布目录不应包含旧 WPF 主程序集
$staleWpf = Join-Path $out "DeepSeek.dll"
if ((Test-Path $staleWpf) -and -not $useWpf) {
    Remove-Item -Force $staleWpf, (Join-Path $out "DeepSeek.pdb") -ErrorAction SilentlyContinue
}

if ($useWpf -and -not (Test-Path (Join-Path $out "DeepSeek.dll"))) {
    throw "publish 缺少 DeepSeek.dll，请重新运行 build.ps1"
}

$required = @(
    (Join-Path $out "Assets\inject\overlay.css"),
    (Join-Path $out "Assets\inject\bridge.js")
)
if (-not $useWpf) {
    $required += (Join-Path $out "DeepSeek.Desktop.runtimeconfig.json")
}
foreach ($p in $required) {
    if (-not (Test-Path $p)) { throw "publish 缺少资源: $p" }
}

# Agent 使用进程内 C# Harness，不再打包 deepseek-tui.exe
$toolsOut = Join-Path $out "Assets\tools"
New-Item -ItemType Directory -Force -Path $toolsOut | Out-Null
Write-Host "Agent engine: native C# Harness (no DeepSeek-TUI binary required)"

Write-Host "Running unit tests..."
Push-Location $root
dotnet test DeepSeek.Core.Tests\DeepSeek.Core.Tests.csproj -c Release
& (Join-Path $root "scripts\verify-integration.ps1") -PublishDir $out
& (Join-Path $root "scripts\agent-harness-smoke.ps1") -PublishDir $out
Pop-Location

Write-Host "Build OK: $(Join-Path $out $exeName)"
Write-Host "Publish directory: $out"

$shouldDeploy = $DeployToDesktop -or -not [string]::IsNullOrWhiteSpace($DeployDir)
if (-not $shouldDeploy) {
    Write-Host "Run: .\publish\DeepSeek.exe"
    if ($useWpf) {
        Write-Host "Tip: WPF build (stable). Use build.ps1 -WinUi for WinUI 3 experimental shell."
    } else {
        Write-Host "Tip: WinUI build requires .NET 9 + Windows App SDK 2.x runtime."
    }
    return
}

$desktop = [Environment]::GetFolderPath("Desktop")
$installFolder = "DeepSeek_desktop"
if ($DeployDir) {
    $target = [System.IO.Path]::GetFullPath($DeployDir)
} elseif ($DeployToDesktop) {
    $target = Join-Path $desktop $installFolder
} else {
    return
}
$skipCopy = $false

# 完整替换部署目录，删除残留的 WPF 文件
if (Test-Path $target) {
    try { Remove-Item -Recurse -Force $target -ErrorAction Stop }
    catch {
        Write-Host "目标目录被占用，使用 robocopy 镜像同步..."
        New-Item -ItemType Directory -Force -Path (Join-Path $target ".sync") | Out-Null
        robocopy $out $target /MIR /NFL /NDL /NJH /NJS /NC /NS | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy 部署失败 (exit $LASTEXITCODE)" }
        # 跳过 robocopy 成功分支的 Copy-Item
        $skipCopy = $true
    }
}
if (-not $skipCopy) {
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item -Path (Join-Path $out '*') -Destination $target -Recurse -Force
}

$Wsh = New-Object -ComObject WScript.Shell
$lnk = Join-Path $desktop "$installFolder.lnk"
$sc = $Wsh.CreateShortcut($lnk)
$sc.TargetPath = Join-Path $target $exeName
$sc.WorkingDirectory = $target
$ico = Join-Path $target "Assets\AppIcon.ico"
if (-not (Test-Path $ico)) { $ico = Join-Path $target "Assets\deepseek.ico" }
if (Test-Path $ico) { $sc.IconLocation = "$ico,0" }
$sc.Description = "DeepSeek Desktop"
$sc.Save()

Write-Host "Deployed copy: $(Join-Path $target $exeName)"
Write-Host "Canonical publish: $out"
if ($useWpf) {
    Write-Host "Tip: WPF build (stable). Use build.ps1 -WinUi for WinUI 3 experimental shell."
} else {
    Write-Host "Tip: WinUI build requires .NET 9 + Windows App SDK 2.x runtime."
}
