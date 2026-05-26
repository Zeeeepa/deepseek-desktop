# deepseek_desktop — WPF 单入口构建（无 Qt / WinUI / Bridge）
param(
    [switch]$DeployToDesktop,
    [string]$DeployDir = "",
    [switch]$SkipUiVerify
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
. (Join-Path $root "scripts\Get-PublishDir.ps1")
$out = Get-DeepSeekPublishDir -RepoRoot $root

function Exit-BuildScript {
    param([int]$Code = 0)
    if ($Host.Name -eq "ConsoleHost" -and -not $env:CI -and -not $env:DEEPSEEK_BUILD_NO_PAUSE) {
        if ($Code -ne 0) {
            Write-Host ""
            Write-Host "构建失败 (exit $Code)。按 Enter 关闭窗口..." -ForegroundColor Red
        } else {
            Write-Host ""
            Write-Host "按 Enter 关闭窗口..."
        }
        Read-Host | Out-Null
    }
    exit $Code
}

trap {
    Write-Host $_ -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkRed
    Exit-BuildScript 1
}

if (Test-Path $out) {
    Get-Process -Name "DeepSeek" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    try { Remove-Item -Recurse -Force $out -ErrorAction Stop }
    catch {
        $empty = Join-Path $env:TEMP "deepseek-publish-empty"
        New-Item -ItemType Directory -Force -Path $empty | Out-Null
        robocopy $empty $out /MIR /NFL /NDL /NJH /NJS /NC /NS | Out-Null
    }
}

Push-Location $root
if (Test-Path (Join-Path $root "scripts\build-dsd-api-ui.ps1")) {
    & (Join-Path $root "scripts\build-dsd-api-ui.ps1")
}
if (Test-Path (Join-Path $root "scripts\sync-agent-dsd-api.ps1")) {
    & (Join-Path $root "scripts\sync-agent-dsd-api.ps1") -Root $root
}

dotnet publish DeepSeekBrowser.csproj -c Release -r win-x64 --self-contained false -o $out "-p:UseAppHost=true"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Remove-Item -Force (Join-Path $out "DeepSeek.App.exe") -ErrorAction SilentlyContinue
foreach ($stale in @("DeepSeek.Bridge.exe", "DeepSeek.Bridge.dll", "DeepSeek.Qt.exe")) {
    Remove-Item -Force (Join-Path $out $stale) -ErrorAction SilentlyContinue
}
Remove-Item -Force (Join-Path $out "Assets\tools\deepseek-tui.exe") -ErrorAction SilentlyContinue

$required = @(
    (Join-Path $out "DeepSeek.exe"),
    (Join-Path $out "DeepSeek.dll"),
    (Join-Path $out "Assets\inject\bridge.js"),
    (Join-Path $out "Assets\inject\overlay.js"),
    (Join-Path $out "Assets\inject\chat-mode-floater.js")
)
foreach ($p in $required) {
    if (-not (Test-Path $p)) { throw "publish missing: $p" }
}

dotnet test DeepSeek.Core.Tests\DeepSeek.Core.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "unit tests failed" }
& (Join-Path $root "scripts\verify-integration.ps1") -PublishDir $out
& (Join-Path $root "scripts\agent-harness-smoke.ps1") -PublishDir $out
if (-not $SkipUiVerify) {
    & (Join-Path $root "scripts\verify-workmode-ui.ps1") -PublishDir $out
    & (Join-Path $root "scripts\verify-desktop-smoothness.ps1") -PublishDir $out
}
Pop-Location

Write-Host "Build OK: $(Join-Path $out 'DeepSeek.exe')"
Write-Host "Publish: $out"

if ($DeployToDesktop -or $DeployDir) {
    $target = if ($DeployDir) { $DeployDir } else { Join-Path ([Environment]::GetFolderPath("Desktop")) "DeepSeek_desktop" }
    if (Test-Path $target) { Remove-Item -Recurse -Force $target -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item -Path (Join-Path $out '*') -Destination $target -Recurse -Force
    Write-Host "Deployed: $target"
}

Exit-BuildScript 0
