param(
    [string]$PublishDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "Get-PublishDir.ps1")
if (-not $PublishDir) { $PublishDir = Get-DeepSeekPublishDir -RepoRoot $root }

Write-Host "agent-harness-smoke: $PublishDir"

$mainDll = Join-Path $PublishDir "DeepSeek.dll"
if (-not (Test-Path $mainDll)) { $mainDll = Join-Path $PublishDir "DeepSeek.Core.dll" }
if (-not (Test-Path $mainDll)) { throw "missing main assembly under publish dir" }

$srcHarness = Join-Path $root "DeepSeek.Core\Services\Harness"
$p2Files = @(
    "HarnessToolOutputSpill.cs",
    "HarnessVerifyChain.cs",
    "HarnessRegistryReload.cs",
    "HarnessPostMortemWriter.cs"
)
foreach ($f in $p2Files) {
    if (-not (Test-Path (Join-Path $srcHarness $f))) { throw "P2 Harness file missing: $f" }
}
Write-Host "  OK P2 Harness modules present"

$legacyTui = Join-Path $PublishDir "Assets\tools\deepseek-tui.exe"
if (Test-Path $legacyTui) {
    Write-Warning "legacy deepseek-tui.exe still bundled (not required for native Harness)"
} else {
    Write-Host "  OK no deepseek-tui.exe required"
}

$agentApp = Join-Path $PublishDir "Assets\agent\agent-app.js"
if (-not (Test-Path $agentApp)) { throw "missing agent-app.js" }
$js = [System.IO.File]::ReadAllText($agentApp, [System.Text.UTF8Encoding]::new($false))
if ($js -notmatch 'agentHarnessState') { throw "agent-app.js missing agentHarnessState handler" }
if ($js -notmatch '/reload') { throw "agent-app.js missing /reload command" }
if ($js -notmatch 'agentHarnessReload') { throw "agent-app.js missing agentHarnessReload IPC" }
Write-Host "  OK agentHarnessState + /reload in agent UI"

$cfgPath = Join-Path $env:LOCALAPPDATA "deepseek_desktop\config.json"
if (Test-Path $cfgPath) {
    $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
    if ($null -ne $cfg.UseNativeHarness -and $cfg.UseNativeHarness -eq $false) {
        Write-Warning "UseNativeHarness=false in user config (legacy TUI path disabled in build)"
    }
}

Write-Host "agent-harness-smoke: PASS"
