# Repair %LOCALAPPDATA%\deepseek_desktop\config.json (UTF-8, truncated paths, token wrapper, empty modelMappings)
param(
    [string]$Configuration = "Release",
    [string]$RepoRoot = ""
)

function Test-JsonStrict {
    param([string]$Json)
    if (Get-Command node -ErrorAction SilentlyContinue) {
        $tmp = [System.IO.Path]::GetTempFileName()
        try {
            [System.IO.File]::WriteAllText($tmp, $Json, [System.Text.UTF8Encoding]::new($false))
            & node -e "JSON.parse(require('fs').readFileSync(process.argv[1],'utf8'))" $tmp 2>$null | Out-Null
            return $LASTEXITCODE -eq 0
        }
        finally {
            Remove-Item $tmp -Force -ErrorAction SilentlyContinue
        }
    }
    try {
        $null = $Json | ConvertFrom-Json
        return $true
    }
    catch {
        return $false
    }
}

function Repair-QwenWorkspaceRoot {
    param([string]$Raw)
    $m = [regex]::Match($Raw, '"qwenCodeWorkspaceRoot"\s*:\s*"')
    if (-not $m.Success) { return $Raw, $false }

    $start = $m.Index + $m.Length
    $nextQuote = $Raw.IndexOf('"', $start)
    $nextComma = $Raw.IndexOf(',', $start)
    $nextBrace = $Raw.IndexOf('}', $start)
    $nextNewline = $Raw.IndexOf("`n", $start)
    $looksBroken = ($nextQuote -lt 0) -or (($nextComma -ge 0) -and ($nextQuote -gt $nextComma))
    if (-not $looksBroken) { return $Raw, $false }

    $end = $Raw.Length
    foreach ($i in @($nextComma, $nextBrace, $nextNewline)) {
        if ($i -ge $start) { $end = [Math]::Min($end, $i) }
    }
    $val = $Raw.Substring($start, $end - $start).Trim().TrimEnd('\').TrimEnd('"')
    $escaped = $val.Replace('\', '\\').Replace('"', '\"')
    $fixed = $Raw.Substring(0, $start) + $escaped + '"' + $Raw.Substring($end)
    return $fixed, $true
}

function Repair-WebUserTokenWrapper {
    param([string]$Raw)
    if ($Raw -notmatch '"webUserToken"\s*:\s*"\{[^"]*__version') { return $Raw, $false }
    $token = $null
    $accPath = Join-Path $env:LOCALAPPDATA "deepseek_desktop\provider-accounts.json"
    if (Test-Path $accPath) {
        try {
            $accRaw = [System.IO.File]::ReadAllText($accPath, [System.Text.UTF8Encoding]::new($false))
            if (Test-JsonStrict $accRaw) {
                $accounts = $accRaw | ConvertFrom-Json
                $token = ($accounts | Where-Object { $_.providerId -eq 'deepseek' } | Select-Object -First 1).credentials.token
            }
        }
        catch { }
    }
    if (-not $token) { return $Raw, $false }
    $esc = $token.Replace('\', '\\').Replace('"', '\"')
    $fixed = [regex]::Replace($Raw, '"webUserToken"\s*:\s*"(?:[^"\\]|\\.)*"', "`"webUserToken`": `"$esc`"")
    return $fixed, $true
}

function Repair-EmptyModelMappings {
    param([string]$Raw)
    if ($Raw -notmatch '"modelMappings"\s*:\s*\[') { return $Raw, $false }
    if ($Raw -match '"modelMappings"\s*:\s*\[\s*\]') { return $Raw, $false }
    if ($Raw -notmatch '"modelMappings"\s*:\s*\[\s*\{[^}]*"alias"\s*:\s*""') { return $Raw, $false }
    $fixed = [regex]::Replace($Raw, '"modelMappings"\s*:\s*\[(?:\s*\{[^}]*\}\s*,?)+\s*\]', '"modelMappings": []')
    return $fixed, ($fixed -ne $Raw)
}

function Invoke-DotNetConfigRepair {
    param([string]$Configuration, [string]$RepoRoot)
    if (-not $RepoRoot) { $RepoRoot = Split-Path $PSScriptRoot -Parent }
    $proj = Join-Path $RepoRoot "tools\ConfigRepair\ConfigRepair.csproj"
    if (-not (Test-Path $proj)) { return "skip" }

    $configDir = Join-Path $env:LOCALAPPDATA "deepseek_desktop"
    & dotnet run --project $proj -c $Configuration --no-restore:$false -- $configDir 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 2) { return "StillInvalid" }
    if ($LASTEXITCODE -ne 0) { return "skip" }
    return "Repaired"
}

$configDir = Join-Path $env:LOCALAPPDATA "deepseek_desktop"
$cfgPath = Join-Path $configDir "config.json"
if (-not (Test-Path $cfgPath)) { return "NoFile" }

$utf8 = [System.Text.UTF8Encoding]::new($false)
$raw = [System.IO.File]::ReadAllText($cfgPath, $utf8)
if (Test-JsonStrict $raw) { return "OkNoChanges" }

$changed = $false
$fixed, $c = Repair-QwenWorkspaceRoot $raw
if ($c) { $raw = $fixed; $changed = $true }
$fixed, $c = Repair-WebUserTokenWrapper $raw
if ($c) { $raw = $fixed; $changed = $true }
$fixed, $c = Repair-EmptyModelMappings $raw
if ($c) { $raw = $fixed; $changed = $true }

if (-not (Test-JsonStrict $raw)) {
    $dotnetOutcome = Invoke-DotNetConfigRepair -Configuration $Configuration -RepoRoot $RepoRoot
    if ($dotnetOutcome -eq "Repaired") {
        $raw = [System.IO.File]::ReadAllText($cfgPath, $utf8)
        $changed = $true
    }
    elseif ($dotnetOutcome -eq "StillInvalid") {
        return "StillInvalid"
    }
}

if (-not (Test-JsonStrict $raw)) { return "StillInvalid" }

if ($changed) {
    [System.IO.File]::WriteAllText($cfgPath, $raw, $utf8)
    return "Repaired"
}

return "OkNoChanges"
