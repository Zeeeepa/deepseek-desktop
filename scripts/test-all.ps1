param(
    [switch]$SkipBuild,
    [switch]$SkipDesktopSmoke,
    [switch]$SkipAgentLlmSmoke,
    [string]$PublishDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "Get-PublishDir.ps1")

if (-not $PublishDir) {
    $PublishDir = Get-DeepSeekPublishDir -RepoRoot $root
}
$PublishDir = [System.IO.Path]::GetFullPath($PublishDir)

$steps = [System.Collections.Generic.List[object]]::new()

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )
    Write-Host ""
    Write-Host "======== $Name ========"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action
        $sw.Stop()
        $steps.Add([pscustomobject]@{ Name = $Name; Status = "PASS"; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1) })
        Write-Host "$Name`: PASS ($([math]::Round($sw.Elapsed.TotalSeconds, 1))s)"
    }
    catch {
        $sw.Stop()
        $steps.Add([pscustomobject]@{ Name = $Name; Status = "FAIL"; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1); Error = $_.Exception.Message })
        Write-Host "$Name`: FAIL - $($_.Exception.Message)"
        throw
    }
}

Push-Location $root
try {
    if (-not $SkipBuild) {
        Invoke-Step "build.ps1" {
            & (Join-Path $root "build.ps1")
            if ($LASTEXITCODE -ne 0) { throw "build.ps1 exit $LASTEXITCODE" }
        }
    } else {
        Invoke-Step "dotnet test" {
            dotnet test (Join-Path $root "DeepSeek.Core.Tests\DeepSeek.Core.Tests.csproj") -c Release --verbosity minimal
            if ($LASTEXITCODE -ne 0) { throw "dotnet test exit $LASTEXITCODE" }
        }
        Invoke-Step "verify-integration" {
            & (Join-Path $root "scripts\verify-integration.ps1") -PublishDir $PublishDir
        }
        Invoke-Step "agent-harness-smoke" {
            & (Join-Path $root "scripts\agent-harness-smoke.ps1") -PublishDir $PublishDir
        }
    }

    if (-not $SkipDesktopSmoke) {
        Invoke-Step "smoke-test (work mode)" {
            & (Join-Path $root "scripts\smoke-test.ps1") -ExePath (Join-Path $PublishDir "DeepSeek.exe")
        }
        Invoke-Step "verify-runtime-shutdown" {
            & (Join-Path $root "scripts\verify-runtime-shutdown.ps1") -PublishDir $PublishDir
        }
    }

    if ($SkipAgentLlmSmoke) {
        Write-Host ""
        Write-Host "Skipping agent LLM smokes (use without -SkipAgentLlmSmoke when API is configured)."
    } else {
        $configPath = Join-Path $env:LOCALAPPDATA "deepseek_desktop\config.json"
        if (-not (Test-Path $configPath)) {
            Write-Host ""
            Write-Host "WARN: $configPath not found — agent-hello-test / agent-task-smoke need login + API."
            Write-Host "      Re-run with -SkipAgentLlmSmoke or configure the app first."
        } else {
            Invoke-Step "agent-hello-test" {
                & (Join-Path $root "scripts\agent-hello-test.ps1") -ExePath (Join-Path $PublishDir "DeepSeek.exe")
            }
            Invoke-Step "agent-task-smoke" {
                & (Join-Path $root "scripts\agent-task-smoke.ps1") -ExePath (Join-Path $PublishDir "DeepSeek.exe")
            }
        }
    }

    Write-Host ""
    Write-Host "======== test-all summary ========"
    $steps | Format-Table -AutoSize
    Write-Host "test-all: ALL PASS"
}
finally {
    Pop-Location
}
