# Builds the embedded API console from an electron-vite renderer checkout.
param(
    [string]$RendererSource = "",
    [switch]$ForceRebuild,
    [switch]$ApplyLegacyBundlePatches
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$repoRoot = Split-Path $root -Parent
$pathFile = Join-Path $PSScriptRoot "renderer-source.path"

if (-not $RendererSource -and (Test-Path $pathFile)) {
    $RendererSource = (Get-Content $pathFile -Raw).Trim()
}

if (-not $RendererSource) {
    $RendererSource = Join-Path (Split-Path $repoRoot -Parent) "Chat2API-main\Chat2API-main"
}

if (-not (Test-Path $RendererSource)) {
    throw @"
Renderer source not found. Clone Chat2API renderer and set path in:
  web/dsd-api-renderer/renderer-source.path
Or pass -RendererSource <path>
"@
}

& (Join-Path $repoRoot "scripts\build-dsd-api-ui.ps1") `
    -DsdApiRendererSource $RendererSource `
    -ForceRebuild:$ForceRebuild `
    -ApplyLegacyBundlePatches:$ApplyLegacyBundlePatches
