param(
    [string]$PublishDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "Get-PublishDir.ps1")
if (-not $PublishDir) { $PublishDir = Get-DeepSeekPublishDir -RepoRoot $root }

function Assert-File($path, $label) {
    if (-not (Test-Path $path)) { throw "missing $label : $path" }
    Write-Host "  OK $label"
}

Write-Host "verify-integration: publish dir $PublishDir"
Assert-File (Join-Path $PublishDir "DeepSeek.exe") "main exe"
Assert-File (Join-Path $PublishDir "Assets\inject\bridge.js") "inject bridge"
Assert-File (Join-Path $PublishDir "Assets\inject\overlay.js") "inject overlay"
$harnessSrc = Join-Path $root "DeepSeek.Core\Services\Harness\DeepSeekHarnessRunner.cs"
Assert-File $harnessSrc "native Harness sources"
Write-Host "  OK native Harness (no deepseek-tui.exe required)"

$legacyTui = Join-Path $PublishDir "Assets\tools\deepseek-tui.exe"
if (Test-Path $legacyTui) {
    Write-Warning "legacy deepseek-tui.exe still present in publish (optional)"
}

Assert-File (Join-Path $PublishDir "Assets\chat2api\index.html") "Chat2API console UI"

function Assert-TextFileContains {
    param([string]$Path, [string]$Pattern, [string]$Label)
    if (-not (Test-Path $Path)) { throw "missing $Label : $Path" }
    $text = [System.IO.File]::ReadAllText($Path, [System.Text.UTF8Encoding]::new($false))
    if ($text -notlike "*$Pattern*") { throw "$Label missing text: $Pattern" }
    Write-Host "  OK $Label"
}

$agentIndex = Join-Path $PublishDir "Assets\agent\index.html"
$agentApp = Join-Path $PublishDir "Assets\agent\agent-app.js"
$settingsEmbed = Join-Path $PublishDir "Assets\agent\settings-embed.js"
Assert-TextFileContains $agentIndex "slash-palette" "Agent slash command palette"
Assert-TextFileContains $agentApp "slashPaletteHandleKeydown" "Agent slash palette keyboard"
Assert-TextFileContains $agentIndex "ctx-workspace" "Agent context bar workspace chip"
Assert-TextFileContains $agentApp "agentWorkspaceGet" "Agent workspace IPC"
Assert-TextFileContains $agentApp "initWorkspaceUi" "Agent workspace UI init"
Assert-TextFileContains $agentIndex "session-list" "Agent sidebar session list"
Assert-TextFileContains $agentApp "openSessionMenu" "Agent session context menu"
Assert-TextFileContains $agentApp "agentSessionList" "Agent session native storage"
Assert-TextFileContains $agentApp "agentHarnessState" "Agent harness state sync"
$autoEmbed = Join-Path $PublishDir "Assets\agent\automations-embed.html"
$autoJs = Join-Path $PublishDir "Assets\agent\automations-embed.js"
Assert-File $autoEmbed "Agent automations embed"
Assert-TextFileContains $agentIndex "auto-intro" "Agent automations intro modal"
Assert-TextFileContains $agentIndex "message-render.js" "Agent message render"
Assert-TextFileContains $agentIndex "katex" "Agent KaTeX math"
Assert-TextFileContains $agentApp 'openEmbeddedPanel("automations")' "Agent automations panel entry"
Assert-TextFileContains $autoJs "agentAutomationsList" "Agent automations IPC"
Assert-TextFileContains $autoJs "agentAutomationsTest" "Agent automations test run"
Assert-TextFileContains $agentApp 'openEmbeddedPanel("chat2api")' "Agent API 管理 opens via same-origin iframe"
Assert-File (Join-Path $PublishDir "Assets\agent\chat2api\index.html") "Agent same-origin Chat2API embed"
Assert-TextFileContains (Join-Path $PublishDir "Assets\chat2api\ds-ui-trim.js") "ds-agent-back-btn" "Chat2API Agent back button"
Assert-TextFileContains $settingsEmbed "isInAgentIframe" "Settings embed iframe bridge"
Assert-TextFileContains (Join-Path $PublishDir "Assets\chat2api\webview-preload.js") "isInAgentIframe" "Chat2API embed iframe bridge"
Assert-TextFileContains (Join-Path $PublishDir "Assets\chat2api\webview-preload.js") "disabledUpdateStatus" "Chat2API update check disabled"
$chat2apiBundle = Get-ChildItem (Join-Path $PublishDir "Assets\chat2api\assets") -Filter "index-*.js" -ErrorAction SilentlyContinue |
    Sort-Object Length -Descending |
    Select-Object -First 1
if ($chat2apiBundle) {
    $bundleText = [System.IO.File]::ReadAllText($chat2apiBundle.FullName, [System.Text.UTF8Encoding]::new($false))
    if ($bundleText -match 'titleKey: "nav\.about"|path: "/about", element:|import\("\./About-') {
        throw "Chat2API bundle must not include About page: $($chat2apiBundle.Name)"
    }
    Write-Host "  OK Chat2API About page removed from bundle"
    if ($bundleText -notmatch 'fallbackLng:\s*"zh-CN"') {
        throw "Chat2API bundle must default to zh-CN locale"
    }
    if ($bundleText -match '"en-US":\s*\{\s*translation:\s*enUS') {
        throw "Chat2API bundle must not ship en-US locale"
    }
    Write-Host "  OK Chat2API default locale zh-CN"
}
Assert-TextFileContains (Join-Path $PublishDir "Assets\chat2api\ds-i18n-zh.js") "fixStoredLocale" "Chat2API Chinese locale helper"
Assert-TextFileContains (Join-Path $PublishDir "Assets\chat2api\ds-ui-trim.js") "hideLanguageControls" "Chat2API language settings hidden"
Assert-TextFileContains (Join-Path $PublishDir "Assets\chat2api\ds-desktop-stack.js") "removeStackBar" "Chat2API desktop stack bar removed"
Assert-TextFileContains (Join-Path $PublishDir "Assets\chat2api\ds-ui-trim.js") "removeStackBar" "Chat2API UI trim removes stack bar"
Assert-TextFileContains (Join-Path $PublishDir "Assets\chat2api\ds-theme-override.css") "#ds-desktop-stack-bar" "Chat2API theme hides stack bar"

$mainDll = Join-Path $PublishDir "DeepSeek.dll"
if (Test-Path $mainDll) {
    $mainCs = Join-Path (Split-Path $PSScriptRoot -Parent) "MainWindow.xaml.cs"
    $agentCs = Join-Path (Split-Path $PSScriptRoot -Parent) "Services\DesktopAgentHost.cs"
    foreach ($f in @($mainCs, $agentCs)) {
        if (Test-Path $f) {
            $src = [System.IO.File]::ReadAllText($f, [System.Text.UTF8Encoding]::new($false))
            if ($src -match 'ScheduleWarmup\s*\(') {
                throw "startup must not call ScheduleWarmup: $f"
            }
        }
    }
    Write-Host "  OK no startup API console warmup"
    $ilspycmd = Get-Command ilspycmd -ErrorAction SilentlyContinue
    if ($ilspycmd) {
        $agentHost = & ilspycmd -t DeepSeekBrowser.Services.DesktopAgentHost $mainDll 2>&1 | Out-String
        if ($agentHost -notmatch 'ShowEmbeddedPanelAsync') {
            throw "Agent host missing ShowEmbeddedPanelAsync for embedded API/settings"
        }
        if ($agentHost -notmatch 'HandleEmbeddedIpcInvokeAsync') {
            throw "Agent host missing HandleEmbeddedIpcInvokeAsync for embedded Chat2API"
        }
        if ($agentHost -notmatch 'SyncChat2ApiStackAsync') {
            throw "Agent host missing SyncChat2ApiStackAsync for desktop stack linkage"
        }
        if ($agentHost -notmatch 'OpenAgentConfigFile') {
            throw "Agent host missing OpenAgentConfigFile for ~/.deepseek config"
        }
        if ($agentHost -notmatch 'GetHarnessRunner') {
            throw "Agent host missing native Harness runner"
        }
        if ($agentHost -notmatch 'EnsureAgentAndShowEmbeddedPanelAsync') {
            throw "Agent host must open Chat2API via embedded panel (iframe)"
        }
        if ($agentHost -notmatch 'EnsureAgentScopedListening') {
            throw "RunAgentAsync path missing EnsureAgentScopedListening"
        }
        Write-Host "  OK embedded panel (API + settings in Agent page)"
        Write-Host "  OK Agent scoped LLM bridge"
    }
}

$cfgPath = Join-Path $env:LOCALAPPDATA "deepseek_desktop\config.json"
if (-not (Test-Path $cfgPath)) {
    $legacyCfg = Join-Path $env:LOCALAPPDATA "DeepSeekEdge\config.json"
    if (Test-Path $legacyCfg) { $cfgPath = $legacyCfg }
}
if (Test-Path $cfgPath) {
    $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
    if ($cfg.LocalApiPort -eq 5111) {
        Write-Warning "config still has LocalApiPort=5111 (legacy); consider resetting to 0"
    }
}

# Ensure no listener on legacy 5111 when app is not running
$legacy = Get-NetTCPConnection -LocalPort 5111 -State Listen -ErrorAction SilentlyContinue
if ($legacy) {
    Write-Warning "port 5111 is listening (legacy proxy may still be running from an old build)"
} else {
    Write-Host "  OK port 5111 not listening"
}

Write-Host "verify-integration: PASS"
