param(
    [string]$LogDir = "",
    [int]$TailFiles = 5
)

$ErrorActionPreference = "Stop"
if (-not $LogDir) { $LogDir = Join-Path $env:USERPROFILE ".deepseek\logs" }
if (-not (Test-Path $LogDir)) {
    Write-Host "No log dir: $LogDir"
    exit 0
}

$logs = Get-ChildItem $LogDir -Filter "agent-*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First $TailFiles
$metrics = @{
    files = @()
    thinkingTrue = 0
    searchTrue = 0
    llmCompletionMs = @()
    toolRounds = @()
}

foreach ($log in $logs) {
    $text = Get-Content $log.FullName -Raw -Encoding UTF8
    $entry = @{ file = $log.Name; thinking = $false; search = $false; llmMs = @(); tools = 0 }
    if ($text -match "thinking=True") { $entry.thinking = $true; $metrics.thinkingTrue++ }
    if ($text -match "search=True") { $entry.search = $true; $metrics.searchTrue++ }
    foreach ($m in [regex]::Matches($text, "llm\.completion\s+(\d+)ms")) {
        $entry.llmMs += [int]$m.Groups[1].Value
        $metrics.llmCompletionMs += [int]$m.Groups[1].Value
    }
    $toolMatches = [regex]::Matches($text, "工具:")
    $entry.tools = $toolMatches.Count
    $metrics.toolRounds += $entry.tools
    $metrics.files += $entry
}

Write-Host "Baseline metrics ($($logs.Count) logs)"
$metrics.files | ForEach-Object {
    Write-Host ("  {0}: thinking={1} search={2} tools={3} llmSamples={4}" -f $_.file, $_.thinking, $_.search, $_.tools, $_.llmMs.Count)
}
if ($metrics.llmCompletionMs.Count -gt 0) {
    $avg = ($metrics.llmCompletionMs | Measure-Object -Average).Average
    Write-Host ("Avg llm.completion ms: {0:N0}" -f $avg)
}

$outDir = Join-Path $env:USERPROFILE ".deepseek\evals"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$outPath = Join-Path $outDir ("baseline-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".json")
$metrics | ConvertTo-Json -Depth 5 | Set-Content $outPath -Encoding UTF8
Write-Host "Wrote $outPath"
