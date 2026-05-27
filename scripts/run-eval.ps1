param(
    [string]$CasesPath = "",
    [string]$LogDir = "",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $CasesPath) { $CasesPath = Join-Path $PSScriptRoot "evals\cases.json" }
if (-not $LogDir) { $LogDir = Join-Path $env:USERPROFILE ".deepseek\logs" }

if (-not (Test-Path $CasesPath)) { throw "missing cases: $CasesPath" }
$cases = Get-Content $CasesPath -Raw -Encoding UTF8 | ConvertFrom-Json

Write-Host "run-eval: $($cases.Count) cases from $CasesPath"
$passed = 0
$failed = 0

foreach ($case in $cases) {
    $id = $case.id
    Write-Host "`n[$id] prompt: $($case.prompt.Substring(0, [Math]::Min(60, $case.prompt.Length)))..."
    if ($DryRun) {
        Write-Host "  DRY-RUN skip"
        continue
    }

    # Offline heuristic: scan recent agent logs for expected tokens after manual/CI runs.
    $ok = $false
    if (Test-Path $LogDir) {
        $logs = Get-ChildItem $LogDir -Filter "agent-*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 3
        foreach ($log in $logs) {
            $text = Get-Content $log.FullName -Raw -Encoding UTF8
            $matchAll = $true
            foreach ($needle in $case.expectedContains) {
                if ($text -notlike "*$needle*") { $matchAll = $false; break }
            }
            if ($matchAll) { $ok = $true; break }
        }
    }

    if ($ok) {
        Write-Host "  PASS (log heuristic)"
        $passed++
    } else {
        Write-Host "  SKIP/FAIL — run agent manually then re-run eval"
        $failed++
    }
}

Write-Host "`nSummary: pass=$passed fail/skip=$failed"
if ($failed -gt 0 -and -not $DryRun) { exit 2 }
