param(
    [switch]$UseLocalTui,
    [string]$TuiSourcePath = "C:\Users\xiaow\Desktop\DSD\DeepSeek-TUI-main\DeepSeek-TUI-main"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "publish"

Push-Location $root
dotnet publish -c Release -r win-x64 --self-contained false -o $out
Pop-Location

if (-not (Test-Path (Join-Path $out "DeepSeek.exe"))) {
    throw "publish 失败：未找到 DeepSeek.exe"
}

$required = @(
    (Join-Path $out "Assets\inject\overlay.css"),
    (Join-Path $out "Assets\inject\bridge.js"),
    (Join-Path $out "Assets\agent\index.html")
)
foreach ($p in $required) {
    if (-not (Test-Path $p)) { throw "publish 缺少资源: $p" }
}

# DeepSeek-TUI：-UseLocalTui 从 fork 编译；否则 GitHub Release
# 架构: Chat2API(5111) → ~/.deepseek/config.toml → deepseek serve --http(7878) → Agent UI
$toolsOut = Join-Path $out "Assets\tools"
New-Item -ItemType Directory -Force -Path $toolsOut | Out-Null
$dispatcher = Join-Path $toolsOut "deepseek.exe"
$tuiRuntime = Join-Path $toolsOut "deepseek-tui.exe"
$bundledLocal = $false

if ($UseLocalTui) {
    & (Join-Path $root "scripts\build-deepseek-tui.ps1") -TuiSourcePath $TuiSourcePath -ToolsOut $toolsOut
    $bundledLocal = $true
    Write-Host "Bundled DeepSeek-TUI from local source"
}
else {
    $tag = "v0.8.39"
    $base = "https://github.com/Hmbown/DeepSeek-TUI/releases/download/$tag"
    function Ensure-DeepSeekTuiBinary {
        param([string]$Url, [string]$Dest)
        if (Test-Path $Dest) { return }
        Write-Host "Downloading $(Split-Path $Dest -Leaf) ..."
        curl.exe -fL $Url -o $Dest
        if ($LASTEXITCODE -ne 0) { throw "Download failed: $Url" }
    }
    try {
        Ensure-DeepSeekTuiBinary "$base/deepseek-windows-x64.exe" $dispatcher
        Ensure-DeepSeekTuiBinary "$base/deepseek-tui-windows-x64.exe" $tuiRuntime
        Set-Content -Path (Join-Path $toolsOut "version.txt") -Value "0.8.39" -Encoding utf8
        Write-Host "Bundled DeepSeek-TUI $tag (deepseek.exe + deepseek-tui.exe)"
    }
    catch {
        $releaseDir = Join-Path $TuiSourcePath "target\release"
        if ((Test-Path (Join-Path $releaseDir "deepseek.exe")) -and (Test-Path (Join-Path $releaseDir "deepseek-tui.exe"))) {
            Copy-Item -Force (Join-Path $releaseDir "deepseek.exe") $dispatcher
            Copy-Item -Force (Join-Path $releaseDir "deepseek-tui.exe") $tuiRuntime
            $bundledLocal = $true
            Write-Host "Bundled from existing cargo release (fallback)"
        }
        else {
            $npmDir = Join-Path $env:APPDATA "npm\node_modules\deepseek-tui\bin\downloads"
            if (Test-Path (Join-Path $npmDir "deepseek.exe")) {
                Copy-Item -Force (Join-Path $npmDir "deepseek.exe") $dispatcher
                Copy-Item -Force (Join-Path $npmDir "deepseek-tui.exe") $tuiRuntime -ErrorAction SilentlyContinue
                Write-Host "Bundled from npm cache (fallback)"
            } else {
                Write-Host "WARN: DeepSeek-TUI download failed; will retry on first Agent run."
            }
        }
    }
}

$desktop = [Environment]::GetFolderPath("Desktop")
$target = Join-Path $desktop "DeepSeek-Edge"

function Sync-PublishToTarget {
    param([string]$Source, [string]$Dest)
    New-Item -ItemType Directory -Force -Path $Dest | Out-Null
    # robocopy: 0-7 = success; >=8 = error
    $rc = (robocopy $Source $Dest /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /nc /ns /np)
    if ($rc -ge 8) {
        throw "同步到 $Dest 失败 (robocopy exit $rc)"
    }
}

$removed = $false
if (Test-Path $target) {
    try {
        Remove-Item -Recurse -Force $target -ErrorAction Stop
        $removed = $true
    }
    catch {
        Write-Host "目标目录被占用，改为就地同步（请先关闭 DeepSeek.exe）..."
    }
}

if ($removed -or -not (Test-Path $target)) {
    Copy-Item -Recurse -Force $out $target
}
else {
    Sync-PublishToTarget -Source $out -Dest $target
}

# 无论上面是否成功，强制覆盖 Assets（避免只剩 dll 的残缺目录）
$srcAssets = Join-Path $out "Assets"
$dstAssets = Join-Path $target "Assets"
if (Test-Path $srcAssets) {
    Copy-Item -Recurse -Force $srcAssets $dstAssets
}

foreach ($p in $required) {
    $rel = $p.Substring($out.Length).TrimStart('\')
    $onTarget = Join-Path $target $rel
    if (-not (Test-Path $onTarget)) {
        throw "部署后仍缺少: $onTarget"
    }
}

$exe = Join-Path $target "DeepSeek.exe"
$Wsh = New-Object -ComObject WScript.Shell
$lnk = Join-Path $desktop "DeepSeek-Edge.lnk"
$sc = $Wsh.CreateShortcut($lnk)
$sc.TargetPath = $exe
$sc.WorkingDirectory = $target
$ico = Join-Path $target "Assets\deepseek.ico"
if (Test-Path $ico) { $sc.IconLocation = "$ico,0" } else { $sc.IconLocation = "$exe,0" }
$sc.Description = "DeepSeek Browser"
$sc.Save()
Write-Host "Build OK: $exe"
Write-Host "Assets: $dstAssets"
