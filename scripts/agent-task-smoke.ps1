param(
    [string]$ExePath = "",
    [int]$TimeoutSec = 480
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "Get-PublishDir.ps1")
$appData = Join-Path $env:LOCALAPPDATA "deepseek_desktop"

if (-not $ExePath) {
    $ExePath = Get-DeepSeekPublishExe -RepoRoot $root
}

if (-not (Test-Path $ExePath)) {
    throw "agent-task-smoke: executable not found. Run .\build.ps1 first."
}

$cfgPath = Join-Path $appData "config.json"
if (-not (Test-Path $cfgPath)) {
    $legacyCfg = Join-Path $env:LOCALAPPDATA "DeepSeekEdge\config.json"
    if (Test-Path $legacyCfg) { $cfgPath = $legacyCfg }
}
if (-not (Test-Path $cfgPath)) {
    throw "agent-task-smoke: config not found (login once manually)"
}
$cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($cfg.webUserToken)) {
    throw "agent-task-smoke: webUserToken empty — login on chat page first"
}

$logDir = Join-Path $appData "logs"
if (-not (Test-Path $logDir)) {
    $legacyLog = Join-Path $env:LOCALAPPDATA "DeepSeekEdge\logs"
    if (Test-Path $legacyLog) { $logDir = $legacyLog }
}
$traceLog = Join-Path $logDir "work-mode-trace.log"
if (Test-Path $traceLog) { Remove-Item -Force $traceLog }

Write-Host "agent-task-smoke: launching $ExePath (DEEPSEEK_DESKTOP_VERIFY_AGENT_TASK=1)"
$env:DEEPSEEK_DESKTOP_VERIFY_AGENT_TASK = "1"
$p = Start-Process -FilePath $ExePath -WorkingDirectory (Split-Path $ExePath -Parent) -PassThru -WindowStyle Minimized

try {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ($p.HasExited) {
            if ($p.ExitCode -eq 0) {
                Write-Host "agent-task-smoke: PASS (exit 0)"
                exit 0
            }
            throw "agent-task-smoke: process exited with code $($p.ExitCode)"
        }
        if (Test-Path $traceLog) {
            $tail = Get-Content $traceLog -Tail 40 -ErrorAction SilentlyContinue
            if ($tail -match "AgentTaskTest: PASS") {
                Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
                Write-Host "agent-task-smoke: PASS"
                exit 0
            }
            if ($tail -match "AgentTaskTest: FAIL") {
                $fail = ($tail | Select-String "AgentTaskTest: FAIL" | Select-Object -Last 1).Line
                throw "agent-task-smoke: $fail"
            }
        }
        Start-Sleep -Seconds 2
    }
    throw "agent-task-smoke: timed out after ${TimeoutSec}s"
}
finally {
    if (-not $p.HasExited) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
    Remove-Item Env:\DEEPSEEK_DESKTOP_VERIFY_AGENT_TASK -ErrorAction SilentlyContinue
}
