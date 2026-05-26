# Publish DeepSeek Desktop source + release zip to GitHub.
# Requires: git, network to github.com, GitHub CLI (gh) authenticated via `gh auth login`
param(
    [string]$Version = "2.4.0",
    [string]$Tag = "v2.4.0",
    [switch]$SkipBuild,
    [switch]$SkipPush
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

. (Join-Path $PSScriptRoot "Get-PublishDir.ps1")

$zipName = "DeepSeek-Desktop-v$Version-win-x64.zip"
$zipPath = Join-Path $RepoRoot $zipName
$releaseNotes = Join-Path $RepoRoot "RELEASE_v$Version.md"

if (-not $SkipBuild) {
    Write-Host "Building publish/ ..."
    & (Join-Path $RepoRoot "build.ps1")
}

$publishDir = Get-DeepSeekPublishDir -RepoRoot $RepoRoot
$exe = Get-DeepSeekPublishExe -RepoRoot $RepoRoot
if (-not (Test-Path $exe)) {
    throw "Missing $exe — run .\build.ps1 first."
}

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Write-Host "Creating $zipName from publish/ ..."
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Zip: $zipPath ($([math]::Round((Get-Item $zipPath).Length / 1MB, 2)) MB)"

if (-not $SkipPush) {
    $status = git status --porcelain
    if ($status) {
        Write-Warning "Working tree has uncommitted changes. Push will include only committed history."
        Write-Host $status
    }

    Write-Host "Pushing main and tag $Tag ..."
    git push origin main
    git push origin $Tag 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Creating tag $Tag (if missing) ..."
        git tag -a $Tag -m "DeepSeek Desktop $Version" -f
        git push origin $Tag
    }
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Host ""
    Write-Host "GitHub CLI (gh) not found. Install: winget install GitHub.cli"
    Write-Host "Then: gh auth login"
    Write-Host "Upload release manually: https://github.com/fanstars2318/deepseek-desktop/releases/new?tag=$Tag"
    Write-Host "Asset: $zipPath"
    exit 0
}

$desc = "DeepSeek Desktop (DD): WPF/Qt Hybrid shell + C# Harness + DSD API + MCP, local workspace sandbox."
Write-Host "Updating repo description ..."
gh repo edit fanstars2318/deepseek-desktop --description $desc 2>$null

$body = if (Test-Path $releaseNotes) { Get-Content $releaseNotes -Raw } else { "DeepSeek Desktop $Version" }

Write-Host "Creating GitHub Release $Tag ..."
$existing = gh release view $Tag 2>$null
if ($LASTEXITCODE -eq 0) {
    gh release upload $Tag $zipPath --clobber
    Write-Host "Release $Tag already exists; uploaded/updated zip asset."
} else {
    gh release create $Tag $zipPath --title "DeepSeek Desktop $Version" --notes-file $releaseNotes
}

Write-Host "Done: https://github.com/fanstars2318/deepseek-desktop/releases/tag/$Tag"
