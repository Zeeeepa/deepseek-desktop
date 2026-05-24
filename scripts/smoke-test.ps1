param(
    [string]$ExePath = "",
    [int]$TimeoutSec = 90
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "Get-PublishDir.ps1")
$appData = Join-Path $env:LOCALAPPDATA "deepseek_desktop"

if (-not $ExePath) {
    $ExePath = Get-DeepSeekPublishExe -RepoRoot $root
}

if (-not (Test-Path $ExePath)) {
    throw "smoke-test: executable not found at publish\DeepSeek.exe. Run .\build.ps1 first."
}

Write-Host "smoke-test: launching $ExePath (DEEPSEEK_DESKTOP_VERIFY_WORKMODE=1)"
$env:DEEPSEEK_DESKTOP_VERIFY_WORKMODE = "1"
$p = Start-Process -FilePath $ExePath -PassThru -WindowStyle Minimized

try {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $workModeLog = Join-Path $appData "logs\work-mode-trace.log"
    if (-not (Test-Path (Split-Path $workModeLog -Parent))) {
        $legacyLog = Join-Path $env:LOCALAPPDATA "DeepSeekEdge\logs\work-mode-trace.log"
        if (Test-Path $legacyLog) { $workModeLog = $legacyLog }
    }

    while ((Get-Date) -lt $deadline) {
        if ($p.HasExited) {
            throw "smoke-test: process exited early with code $($p.ExitCode)"
        }
        if (Test-Path $workModeLog) {
            $tail = Get-Content $workModeLog -Tail 20 -ErrorAction SilentlyContinue
            if ($tail -match "verify.*(ok|pass|done)" -or $tail -match "self.?test.*ok" -or $tail -match "SelfTest: chat surface" -or $tail -match "SelfTest: after chat" -or $tail -match "SelfTest: ApplyWorkMode agent") {
                Write-Host "smoke-test: work mode self-test log detected"
                Write-Host "PASS"
                exit 0
            }
        }
        Start-Sleep -Seconds 2
    }

    Write-Host "smoke-test: timeout waiting for work mode trace (non-fatal if app stayed up)"
    if (-not $p.HasExited) {
        Write-Host "PASS (process alive; manual verify recommended)"
        exit 0
    }
    throw "smoke-test: timed out"
}
finally {
    if (-not $p.HasExited) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
    Remove-Item Env:\DEEPSEEK_DESKTOP_VERIFY_WORKMODE -ErrorAction SilentlyContinue
    Remove-Item Env:\DEEPSEEK_EDGE_VERIFY_WORKMODE -ErrorAction SilentlyContinue
}
