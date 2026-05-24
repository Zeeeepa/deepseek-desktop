param(
    [string]$Root = ""
)

$ErrorActionPreference = "Stop"
if (-not $Root) { $Root = Split-Path $PSScriptRoot -Parent }
$src = Join-Path $Root "Assets\chat2api"
$dest = Join-Path $Root "Assets\agent\chat2api"

if (-not (Test-Path $src)) {
    throw "Chat2API assets missing: $src (run scripts/build-chat2api-ui.ps1)"
}

if (Test-Path $dest) {
    Remove-Item -Recurse -Force $dest
}
Copy-Item -Recurse -Force $src $dest
Write-Host "Synced Chat2API UI to $dest (same-origin Agent embed)"
