param(
    [string]$ExePath = "",
    [int]$TimeoutSec = 360
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "Get-PublishDir.ps1")
$appData = Join-Path $env:LOCALAPPDATA "deepseek_desktop"

if (-not $ExePath) {
    $ExePath = Get-DeepSeekPublishExe -RepoRoot $root
}

if (-not (Test-Path $ExePath)) {
    throw "agent-hello-test: executable not found at publish\DeepSeek.exe. Run .\build.ps1 first."
}

$cfgPath = Join-Path $appData "config.json"
if (-not (Test-Path $cfgPath)) {
    $legacyCfg = Join-Path $env:LOCALAPPDATA "DeepSeekEdge\config.json"
    if (Test-Path $legacyCfg) { $cfgPath = $legacyCfg }
}
if (-not (Test-Path $cfgPath)) {
    throw "agent-hello-test: config not found (login once manually)"
}
$cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($cfg.webUserToken)) {
    throw "agent-hello-test: webUserToken empty — open DeepSeek, login on chat page, then retry"
}

$harnessSrc = Join-Path $root "DeepSeek.Core\Services\Harness\DeepSeekHarnessRunner.cs"
if (-not (Test-Path $harnessSrc)) {
    throw "agent-hello-test: native Harness sources missing"
}
Write-Host "agent-hello-test: using native C# Harness (no deepseek-tui.exe)"

$logDir = Join-Path $appData "logs"
if (-not (Test-Path $logDir)) {
    $legacyLog = Join-Path $env:LOCALAPPDATA "DeepSeekEdge\logs"
    if (Test-Path $legacyLog) { $logDir = $legacyLog }
}
$traceLog = Join-Path $logDir "work-mode-trace.log"
if (Test-Path $traceLog) { Remove-Item -Force $traceLog }

Write-Host "agent-hello-test: launching $ExePath (DEEPSEEK_DESKTOP_VERIFY_AGENT=1)"
$env:DEEPSEEK_DESKTOP_VERIFY_AGENT = "1"
$p = Start-Process -FilePath $ExePath -PassThru -WindowStyle Minimized

try {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ($p.HasExited) {
            if ($p.ExitCode -eq 0) {
                Write-Host "agent-hello-test: PASS (exit 0)"
                if (Test-Path $traceLog) {
                    Get-Content $traceLog -Tail 10 | ForEach-Object { Write-Host "  $_" }
                }
                exit 0
            }
            throw "agent-hello-test: process exited with code $($p.ExitCode)"
        }
        if (Test-Path $traceLog) {
            $tail = Get-Content $traceLog -Tail 30 -ErrorAction SilentlyContinue
            if ($tail -match "AgentSelfTest: PASS") {
                Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
                Write-Host "agent-hello-test: PASS"
                exit 0
            }
            if ($tail -match "AgentSelfTest: FAIL") {
                $fail = ($tail | Select-String "AgentSelfTest: FAIL" | Select-Object -Last 1).Line
                throw "agent-hello-test: $fail"
            }
        }
        Start-Sleep -Seconds 2
    }
    throw "agent-hello-test: timed out after ${TimeoutSec}s"
}
finally {
    if (-not $p.HasExited) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
    Get-Process deepseek*, DeepSeek* -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Remove-Item Env:\DEEPSEEK_DESKTOP_VERIFY_AGENT -ErrorAction SilentlyContinue
    Remove-Item Env:\DEEPSEEK_EDGE_VERIFY_AGENT -ErrorAction SilentlyContinue
}
