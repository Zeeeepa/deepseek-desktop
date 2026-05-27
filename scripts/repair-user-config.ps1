# Repair user config.json before integration verify (UTF-8 / token wrapper / truncated paths)
param(
    [string]$Configuration = "Release",
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Continue"
$root = if ($RepoRoot) { $RepoRoot } else { Split-Path $PSScriptRoot -Parent }
$cfgPath = Join-Path $env:LOCALAPPDATA "deepseek_desktop\config.json"
if (-not (Test-Path $cfgPath)) {
    Write-Host "repair-user-config: no user config"
    exit 0
}

$outcome = & (Join-Path $PSScriptRoot "Invoke-ConfigFileRepair.ps1") -Configuration $Configuration -RepoRoot $root
switch ($outcome) {
    "Repaired" { Write-Host "repair-user-config: repaired $cfgPath"; exit 0 }
    "OkNoChanges" { Write-Host "repair-user-config: OK (no changes)"; exit 0 }
    "StillInvalid" {
        Write-Host "repair-user-config: WARN still invalid JSON — delete or fix $cfgPath manually"
        exit 0
    }
    "skip" {
        Write-Host "repair-user-config: WARN skipped (build Infrastructure first)"
        exit 0
    }
    default { Write-Host "repair-user-config: $outcome"; exit 0 }
}
