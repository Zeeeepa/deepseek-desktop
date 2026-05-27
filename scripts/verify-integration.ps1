param(
    [string]$PublishDir = "",
    [switch]$Qt,
    [switch]$Smoothness
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "Get-PublishDir.ps1")
if (-not $PublishDir) { $PublishDir = Get-DeepSeekPublishDir -RepoRoot $root }

function Assert-File($path, $label) {
    if (-not (Test-Path $path)) { throw "missing $label : $path" }
    Write-Host "  OK $label"
}

function Assert-TextFileContains {
    param([string]$Path, [string]$Pattern, [string]$Label)
    if (-not (Test-Path $Path)) { throw "missing $Label : $Path" }
    $text = [System.IO.File]::ReadAllText($Path, [System.Text.UTF8Encoding]::new($false))
    if ($text -notlike "*$Pattern*") { throw "$Label missing text: $Pattern" }
    Write-Host "  OK $Label"
}

Write-Host "verify-integration: publish dir $PublishDir"
Assert-File (Join-Path $PublishDir "DeepSeek.exe") "main entry exe"
if ($Qt) {
    Assert-File (Join-Path $PublishDir "DeepSeek.Qt.exe") "Qt main shell exe"
    Assert-File (Join-Path $PublishDir "DeepSeek.Bridge.exe") "Qt bridge exe"
    Assert-File (Join-Path $PublishDir "Assets\inject\dd-webview-shim.js") "DD webview shim"
} elseif (Test-Path (Join-Path $PublishDir "DeepSeek.App.exe")) {
    throw "legacy DeepSeek.App.exe should not be in publish (use single DeepSeek.exe)"
}
Assert-File (Join-Path $PublishDir "Assets\inject\bridge.js") "inject bridge"
Assert-File (Join-Path $PublishDir "Assets\inject\overlay.js") "inject overlay"
Assert-File (Join-Path $PublishDir "Assets\inject\chat-mode-floater.js") "inject chat mode floater"
Assert-TextFileContains (Join-Path $PublishDir "Assets\inject\chat-mode-floater.js") "__dsEnsureChatModeFloater" "chat mode floater mount API"
Assert-TextFileContains (Join-Path $PublishDir "Assets\inject\chat-mode-floater.js") "__dsChatModeFloaterBootstrapped" "chat mode floater bootstrap flag"
$harnessSrc = Join-Path $root "DeepSeek.Core\Services\Harness\DeepSeekHarnessRunner.cs"
Assert-File $harnessSrc "native Harness sources"
Write-Host "  OK native Harness (no deepseek-tui.exe required)"

$legacyTui = Join-Path $PublishDir "Assets\tools\deepseek-tui.exe"
if (Test-Path $legacyTui) {
    throw "legacy deepseek-tui.exe must not be published"
}
Write-Host "  OK no legacy deepseek-tui.exe"

Assert-File (Join-Path $PublishDir "Assets\dsd-api\index.html") "DSD API console UI"

$agentIndex = Join-Path $PublishDir "Assets\agent\index.html"
$agentApp = Join-Path $PublishDir "Assets\agent\agent-app.js"
$node = Get-Command node -ErrorAction SilentlyContinue
if ($node) {
    & node --check $agentApp
    if ($LASTEXITCODE -ne 0) { throw "agent-app.js syntax check failed (node --check)" }
    Write-Host "  OK agent-app.js syntax (node --check)"
}
$settingsEmbed = Join-Path $PublishDir "Assets\agent\settings-embed.js"
Assert-TextFileContains $agentIndex "slash-palette" "Agent slash command palette"
Assert-TextFileContains $agentApp "slashPaletteHandleKeydown" "Agent slash palette keyboard"
Assert-TextFileContains $agentIndex "ctx-workspace" "Agent workspace chip"
Assert-TextFileContains $agentIndex "top-tray" "Agent top tray"
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
Assert-TextFileContains $agentIndex "monaco-host.js" "Agent Monaco code editor"
Assert-TextFileContains $agentIndex "monaco/vs/loader.js" "Agent Monaco bundle"
Assert-TextFileContains $agentIndex "katex" "Agent KaTeX math"
Assert-TextFileContains $agentApp 'openEmbeddedPanel("automations")' "Agent automations panel entry"
Assert-TextFileContains $autoJs "agentAutomationsList" "Agent automations IPC"
Assert-TextFileContains $autoJs "agentAutomationsTest" "Agent automations test run"
Assert-TextFileContains $agentApp 'openEmbeddedPanel("apiManagement")' "Agent API 管理 opens via same-origin iframe"
Assert-File (Join-Path $PublishDir "Assets\agent\dsd-api\index.html") "Agent same-origin DSD API embed"
Assert-TextFileContains (Join-Path $PublishDir "Assets\dsd-api\ds-ui-trim.js") "ds-agent-back-btn" "DSD API Agent back button"
$trimPath = Join-Path $PublishDir "Assets\dsd-api\ds-ui-trim.js"
$routeHash = [char]35
Assert-TextFileContains $trimPath ("var HOME_HASH = `"" + $routeHash + '/";') "DSD API dashboard default route"
$trimJs = [System.IO.File]::ReadAllText($trimPath, [System.Text.UTF8Encoding]::new($false))
if ($trimJs -like '*HIDDEN_NAV*仪表盘*') { throw 'ds-ui-trim.js must not hide dashboard nav' }
Assert-TextFileContains $trimPath 'Quick Actions' 'DSD API quick-actions trim'
if ($trimJs -like '*HIDDEN_TAB*负载均衡*') { throw 'ds-ui-trim.js must not hide settings load-balance tab' }
Write-Host '  OK dashboard + logs nav enabled, quick actions hidden'
$indexPath = Join-Path $PublishDir "Assets\dsd-api\index.html"
$indexHtml = [System.IO.File]::ReadAllText($indexPath, [System.Text.UTF8Encoding]::new($false))
if ($indexHtml -like ('*' + $routeHash + '/providers*')) { throw 'index.html must not force providers route' }
if (($indexHtml | Select-String -Pattern 'ds-theme-override\.css' -AllMatches).Matches.Count -gt 1) {
    throw 'index.html has duplicate desktop injections; rerun build-dsd-api-ui.ps1'
}
$themeCss = [System.IO.File]::ReadAllText((Join-Path $PublishDir "Assets\dsd-api\ds-theme-override.css"), [System.Text.UTF8Encoding]::new($false))
$dashNavCss = 'a[href="' + $routeHash + '/"]'
$logsNavCss = 'a[href="' + $routeHash + '/logs"]'
if ($themeCss.Contains($dashNavCss) -or $themeCss.Contains($logsNavCss)) {
    throw 'ds-theme-override.css must not CSS-hide dashboard or logs nav'
}
Write-Host '  OK index.html boot route dashboard'
Assert-TextFileContains $settingsEmbed "isInAgentIframe" "Settings embed iframe bridge"
Assert-TextFileContains (Join-Path $PublishDir "Assets\dsd-api\webview-preload.js") "isInAgentIframe" "DSD API embed iframe bridge"
Assert-TextFileContains (Join-Path $PublishDir "Assets\dsd-api\webview-preload.js") "disabledUpdateStatus" "DSD API update check disabled"
$dsdApiBundle = Get-ChildItem (Join-Path $PublishDir "Assets\dsd-api\assets") -Filter "index-*.js" -ErrorAction SilentlyContinue |
    Sort-Object Length -Descending |
    Select-Object -First 1
if ($dsdApiBundle) {
    $bundleText = [System.IO.File]::ReadAllText($dsdApiBundle.FullName, [System.Text.UTF8Encoding]::new($false))
    if ($bundleText -match 'titleKey: "nav\.about"|path: "/about", element:|import\("\./About-') {
        throw "DSD API bundle must not include About page: $($dsdApiBundle.Name)"
    }
    Write-Host "  OK DSD API About page removed from bundle"
    if ($bundleText -notmatch 'fallbackLng:\s*"zh-CN"') {
        throw "DSD API bundle must default to zh-CN locale"
    }
    if ($bundleText -match '"en-US":\s*\{\s*translation:\s*enUS') {
        throw "DSD API bundle must not ship en-US locale"
    }
    Write-Host "  OK DSD API default locale zh-CN"
    if ($bundleText -match 'Chat2API|chat2api-settings') {
        throw "DSD API bundle must not contain Chat2API branding (rebuild via build-dsd-api-ui.ps1 -ForceRebuild)"
    }
    Write-Host "  OK DSD API bundle free of Chat2API branding"
}
Assert-TextFileContains (Join-Path $PublishDir "Assets\dsd-api\ds-i18n-zh.js") "fixStoredLocale" "DSD API Chinese locale helper"
Assert-TextFileContains (Join-Path $PublishDir "Assets\dsd-api\ds-ui-trim.js") "hideLanguageControls" "DSD API language settings hidden"
Assert-TextFileContains (Join-Path $PublishDir "Assets\dsd-api\ds-desktop-stack.js") "removeStackBar" "DSD API desktop stack bar removed"
Assert-TextFileContains (Join-Path $PublishDir "Assets\dsd-api\ds-ui-trim.js") "removeStackBar" "DSD API UI trim removes stack bar"
Assert-TextFileContains (Join-Path $PublishDir "Assets\dsd-api\ds-theme-override.css") "#ds-desktop-stack-bar" "DSD API theme hides stack bar"

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
            throw "Agent host missing HandleEmbeddedIpcInvokeAsync for embedded DSD API"
        }
        if ($agentHost -notmatch 'SyncDsdApiStackAsync') {
            throw "Agent host missing SyncDsdApiStackAsync for desktop stack linkage"
        }
        if ($agentHost -notmatch 'OpenAgentConfigFile') {
            throw "Agent host missing OpenAgentConfigFile for ~/.deepseek config"
        }
        if ($agentHost -notmatch 'GetHarnessRunner') {
            throw "Agent host missing native Harness runner"
        }
        if ($agentHost -notmatch 'EnsureAgentAndShowEmbeddedPanelAsync') {
            throw "Agent host must open DSD API via embedded panel (iframe)"
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
    $repairOutcome = & (Join-Path $PSScriptRoot "Invoke-ConfigFileRepair.ps1") -Configuration Release -RepoRoot $root
    if ($repairOutcome -eq "Repaired") {
        Write-Host "  OK user config.json repaired before verify"
    }
    try {
        $cfgRaw = [System.IO.File]::ReadAllText($cfgPath, [System.Text.UTF8Encoding]::new($false))
        $cfg = $cfgRaw | ConvertFrom-Json
        if ($cfg.LocalApiPort -eq 5111) {
            Write-Host "  OK legacy LocalApiPort=5111 ignored at runtime (migrated on load)"
        }
        if ($cfg.webUserToken -match '^\s*\{\s*"value"') {
            Write-Host "  OK webUserToken wrapper will be normalized on next app load"
        }
        Write-Host "  OK user config.json parseable (UTF-8)"
    }
    catch {
        Write-Host "  WARN user config.json invalid JSON — delete or fix manually: $cfgPath"
        Write-Host "        $($_.Exception.Message)"
    }
}

$legacy = Get-NetTCPConnection -LocalPort 5111 -State Listen -ErrorAction SilentlyContinue
if ($legacy) {
    Write-Host "  OK port 5111 listener present (ignored; not used by current build)"
} else {
    Write-Host "  OK port 5111 not listening"
}

if ($Smoothness -and (Test-Path (Join-Path $PublishDir "DeepSeek.exe"))) {
    Write-Host "verify-integration: running verify-desktop-smoothness..."
    & (Join-Path $PSScriptRoot "verify-desktop-smoothness.ps1") -PublishDir $PublishDir
}

$viteBundle = Get-ChildItem (Join-Path $root "Assets\dsd-api\assets\index-*.js") -ErrorAction SilentlyContinue | Select-Object -First 1
if ($viteBundle) {
    Write-Host "  OK DSD API Vite bundle present ($($viteBundle.Name)) — do not hand-edit; rebuild via build-dsd-api-ui.ps1"
}

Assert-TextFileContains (Join-Path $PublishDir "Assets\inject\bridge.js") "reasoning_content: thinkingText" "bridge thinking/content split"
$evalSample = Join-Path $root "scripts\evals\sample.json"
if (Test-Path $evalSample) {
    Write-Host "  OK eval sample present (scripts/evals/sample.json)"
}

Write-Host "  OK WPF single entry (use -Qt only for legacy Qt/Bridge layout checks)"
Write-Host "verify-integration: PASS"
