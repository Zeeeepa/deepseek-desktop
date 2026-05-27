# Architecture guardrails for DeepSeek Desktop layered refactor.
param(
    [int]$MaxIpcBridgeLines = 2500,
    [switch]$StrictBundleHash
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

$failures = @()

$ipcFacade = Join-Path $root "Services\DsdApiIpcBridge.cs"
if (Test-Path $ipcFacade) {
    $lines = (Get-Content $ipcFacade).Count
    if ($lines -gt 250) {
        $failures += "DsdApiIpcBridge.cs facade has $lines lines (target <= 250). Keep routing/cache only; move channels to AppLayer.Ipc."
    }
}

$ipcLegacy = Join-Path $root "Services\DsdApiIpcBridge.LegacyDispatch.cs"
if (Test-Path $ipcLegacy) {
    $legacyLines = (Get-Content $ipcLegacy).Count
    if ($legacyLines -gt $MaxIpcBridgeLines) {
        $failures += "DsdApiIpcBridge.LegacyDispatch.cs has $legacyLines lines (limit $MaxIpcBridgeLines). Continue extraction into DeepSeek.Application/Ipc."
    }
}

$dsdApiAssets = Join-Path $root "Assets\dsd-api\assets"
if (Test-Path $dsdApiAssets) {
    if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
        Write-Warning "node not found; skipping JS syntax check on dsd-api bundles."
    } else {
        Get-ChildItem $dsdApiAssets -Filter "*.js" -ErrorAction SilentlyContinue | ForEach-Object {
            & node --check $_.FullName 2>&1 | Out-Null
            if ($LASTEXITCODE -ne 0) {
                $failures += "Syntax check failed: $($_.FullName)"
            }
        }
    }
}

$requiredProjects = @(
    "src\DeepSeek.Domain\DeepSeek.Domain.csproj",
    "src\DeepSeek.Infrastructure\DeepSeek.Infrastructure.csproj",
    "src\DeepSeek.Application\DeepSeek.Application.csproj"
)
foreach ($rel in $requiredProjects) {
    $p = Join-Path $root $rel
    if (-not (Test-Path $p)) {
        $failures += "Missing project: $rel"
    }
}

$sln = @(
    (Join-Path $root "DeepSeek.sln"),
    (Join-Path $root "DeepSeek.slnx")
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $sln) {
    $failures += "Missing solution: DeepSeek.sln or DeepSeek.slnx"
}

if ($failures.Count -gt 0) {
    Write-Host "Architecture verification failed:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "Architecture verification OK."
exit 0
