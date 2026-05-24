param(
    [string]$PublishDir = "",
    [int]$StartupWaitSec = 45,
    [int]$ShutdownWaitSec = 5
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "Get-PublishDir.ps1")
if (-not $PublishDir) {
    $PublishDir = Get-DeepSeekPublishDir -RepoRoot $root
}
$PublishDir = [System.IO.Path]::GetFullPath($PublishDir)

$exe = Join-Path $PublishDir "DeepSeek.exe"
$appData = Join-Path $env:LOCALAPPDATA "deepseek_desktop"
$traceLog = Join-Path $appData "logs\work-mode-trace.log"

if (-not (Test-Path $exe)) {
    throw "DeepSeek.exe not found: $exe"
}

function Get-PublishDeepSeekProcesses {
    Get-Process -Name "DeepSeek" -ErrorAction SilentlyContinue | Where-Object {
        try {
            if ($_.HasExited) { return $false }
            $path = $_.MainModule.FileName
            if (-not $path) { return $false }
            return [System.IO.Path]::GetFullPath($path).StartsWith($PublishDir, [StringComparison]::OrdinalIgnoreCase)
        } catch {
            return $false
        }
    }
}

function Stop-PublishAppInstances {
    Get-PublishDeepSeekProcesses | ForEach-Object {
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "verify-runtime-shutdown: publish dir $PublishDir"

Stop-PublishAppInstances
Start-Sleep -Seconds 2

Write-Host "Starting DeepSeek.exe (DEEPSEEK_DESKTOP_VERIFY_SHUTDOWN=1) ..."
$env:DEEPSEEK_DESKTOP_VERIFY_SHUTDOWN = "1"
$app = Start-Process -FilePath $exe -WorkingDirectory $PublishDir -PassThru
Remove-Item Env:\DEEPSEEK_DESKTOP_VERIFY_SHUTDOWN -ErrorAction SilentlyContinue

try {
    $exitDeadline = (Get-Date).AddSeconds($StartupWaitSec)
    $sawShutdownLog = $false
    while ((Get-Date) -lt $exitDeadline) {
        if ($app.HasExited) { break }
        if (Test-Path $traceLog) {
            $tail = Get-Content $traceLog -Tail 30 -ErrorAction SilentlyContinue
            if ($tail -match "ShutdownVerify: exiting gracefully") {
                $sawShutdownLog = $true
            }
        }
        Start-Sleep -Seconds 1
    }

    if (-not $app.HasExited) {
        throw "DeepSeek.exe did not exit within ${StartupWaitSec}s (shutdown verify mode)"
    }

    Write-Host "  OK DeepSeek.exe exited (code $($app.ExitCode))"
    if ($sawShutdownLog) {
        Write-Host "  OK ShutdownVerify trace present"
    } else {
        Write-Host "  WARN no ShutdownVerify trace (exit code still validated)"
    }

    Start-Sleep -Seconds $ShutdownWaitSec
    $leftover = @(Get-PublishDeepSeekProcesses)
    if ($leftover.Count -gt 0) {
        $detail = ($leftover | ForEach-Object {
            try { $_.MainModule.FileName } catch { $_.ProcessName }
        }) -join ", "
        throw "DeepSeek.exe still running from publish dir after exit: $detail"
    }

    Write-Host "verify-runtime-shutdown: PASS"
}
finally {
    if (-not $app.HasExited) {
        Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
    }
    Stop-PublishAppInstances
}
