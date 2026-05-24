# 仓库唯一发布输出目录：<repo>/publish
function Get-DeepSeekPublishDir {
    param(
        [string]$RepoRoot = ""
    )

    if (-not $RepoRoot) {
        $RepoRoot = Split-Path -Parent $PSScriptRoot
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot "publish"))
}

function Get-DeepSeekPublishExe {
    param(
        [string]$RepoRoot = "",
        [string]$ExeName = "DeepSeek.exe"
    )

    return Join-Path (Get-DeepSeekPublishDir -RepoRoot $RepoRoot) $ExeName
}
