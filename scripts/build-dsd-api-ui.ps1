param(
    [string]$DsdApiRendererSource = "",
    [string]$DestDir = "",
    [switch]$ForceRebuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $DestDir) { $DestDir = Join-Path $root "Assets\dsd-api" }
$uiSrc = Join-Path $root "Assets\dsd-api-ui"
$defaultRenderer = Join-Path (Split-Path $root -Parent) "Chat2API-main\Chat2API-main"
if (-not $DsdApiRendererSource -and (Test-Path $defaultRenderer)) {
    $DsdApiRendererSource = $defaultRenderer
}
$rendererOut = if ($DsdApiRendererSource) { Join-Path $DsdApiRendererSource "out\renderer" } else { "" }
$indexPath = Join-Path $DestDir "index.html"
$useExistingBundle = (Test-Path $indexPath) -and -not $ForceRebuild

if ($useExistingBundle) {
    Write-Host "Using existing DSD API bundle at $DestDir (skip external npm build). Pass -ForceRebuild to rebuild from source."
}
else {
    if (-not $DsdApiRendererSource) {
        throw "DSD API bundle missing at $DestDir. Pass -DsdApiRendererSource <electron-ui-repo> or place Chat2API-main next to deepseek_desktop-source."
    }
    & (Join-Path $PSScriptRoot "patch-dsd-renderer-source.ps1") -RendererSource $DsdApiRendererSource
    Write-Host "Building DSD API renderer in $DsdApiRendererSource ..."
    Push-Location $DsdApiRendererSource
    if (-not (Test-Path "node_modules\electron-vite")) {
        npm install --ignore-scripts
        if ($LASTEXITCODE -ne 0) { throw "npm install failed" }
    }
    npx --yes electron-vite build
    if ($LASTEXITCODE -ne 0) { throw "electron-vite build failed" }
    Pop-Location

    if (-not (Test-Path $rendererOut)) {
        throw "DSD API renderer build missing: $rendererOut"
    }

    if (Test-Path $DestDir) {
        Remove-Item -Recurse -Force $DestDir
    }
    New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
    Copy-Item -Recurse -Force (Join-Path $rendererOut "*") $DestDir
}

Copy-Item -Force (Join-Path $uiSrc "webview-preload.js") (Join-Path $DestDir "webview-preload.js")
Copy-Item -Force (Join-Path $uiSrc "ds-theme-override.css") (Join-Path $DestDir "ds-theme-override.css")
Copy-Item -Force (Join-Path $uiSrc "ds-rebrand.js") (Join-Path $DestDir "ds-rebrand.js")
Copy-Item -Force (Join-Path $uiSrc "ds-ready.js") (Join-Path $DestDir "ds-ready.js")
Copy-Item -Force (Join-Path $uiSrc "ds-ui-trim.js") (Join-Path $DestDir "ds-ui-trim.js")
Copy-Item -Force (Join-Path $uiSrc "ds-api-management.js") (Join-Path $DestDir "ds-api-management.js")
Copy-Item -Force (Join-Path $uiSrc "ds-oauth-embedded.js") (Join-Path $DestDir "ds-oauth-embedded.js")
Copy-Item -Force (Join-Path $uiSrc "ds-i18n-zh.js") (Join-Path $DestDir "ds-i18n-zh.js")
Copy-Item -Force (Join-Path $uiSrc "ds-desktop-stack.js") (Join-Path $DestDir "ds-desktop-stack.js")
Copy-Item -Force (Join-Path $uiSrc "ds-boot-guard.js") (Join-Path $DestDir "ds-boot-guard.js")
Copy-Item -Force (Join-Path $uiSrc "ds-settings-loadbalance.js") (Join-Path $DestDir "ds-settings-loadbalance.js")
Copy-Item -Force (Join-Path $uiSrc "deepseek-brand.svg") (Join-Path $DestDir "deepseek-brand.svg")

function Get-DsdApiMainBundle {
    param([string]$AssetsDir)
    Get-ChildItem (Join-Path $AssetsDir "assets") -Filter "index-*.js" -ErrorAction SilentlyContinue |
        Sort-Object Length -Descending |
        Select-Object -First 1
}

function Repair-DsdApiIndexHtml {
    param([string]$AssetsDir)

    $indexPath = Join-Path $AssetsDir "index.html"
    $assetsFolder = Join-Path $AssetsDir "assets"
    if (-not (Test-Path $assetsFolder)) {
        throw "DSD API assets folder missing: $assetsFolder"
    }

    $indexJs = Get-DsdApiMainBundle -AssetsDir $AssetsDir
    if (-not $indexJs) { throw "DSD API bundle missing index-*.js under $assetsFolder" }

    $indexCss = Get-ChildItem $assetsFolder -Filter "index-*.css" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $indexCss) { throw "DSD API bundle missing index-*.css under $assetsFolder" }

    $icon = Get-ChildItem $assetsFolder -Filter "icons-*.png" -ErrorAction SilentlyContinue | Select-Object -First 1
    $iconHref = if ($icon) { "./assets/$($icon.Name)" } else { "./assets/icons-DJH5QOMG.png" }

    $headInject = @"
    <link rel="stylesheet" href="./ds-theme-override.css" />
    <script src="./webview-preload.js"></script>
    <script src="./ds-boot-guard.js"></script>
    <script>
      (function () {
        var h = location.hash || "";
        if (!h || h === "#" || h.indexOf("#/proxy") === 0 || h.indexOf("#/about") === 0) {
          location.replace(location.pathname + location.search + "#/");
        }
      })();
    </script>
"@

    $footerScripts = @"
  <script src="./ds-i18n-zh.js"></script>
  <script src="./ds-ui-trim.js"></script>
  <script src="./ds-api-management.js"></script>
  <script src="./ds-oauth-embedded.js"></script>
  <script src="./ds-ready.js"></script>
  <script src="./ds-rebrand.js"></script>
  <script src="./ds-desktop-stack.js"></script>
  <script src="./ds-settings-loadbalance.js"></script>
"@

    $html = @"
<!DOCTYPE html>
<html lang="zh-CN" data-theme="light" class="ds-console">
  <head>
    <meta charset="UTF-8" />
$headInject
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <link rel="icon" type="image/png" href="$iconHref" />
    <title>DeepSeek API</title>
    <script type="module" src="./assets/$($indexJs.Name)"></script>
    <link rel="stylesheet" href="./assets/$($indexCss.Name)">
  </head>
  <body>
    <div id="root"></div>
$footerScripts
  </body>
</html>
"@

    [System.IO.File]::WriteAllText($indexPath, $html, [System.Text.UTF8Encoding]::new($false))
    Write-Host "  OK repaired index.html (single boot inject, default #/ dashboard)"
}

if (-not (Test-Path $indexPath)) {
    throw "DSD API bundle missing: $indexPath"
}
Repair-DsdApiIndexHtml -AssetsDir $DestDir

function Remove-DsdApiAboutPage {
    param([string]$AssetsDir)
    $aboutChunks = Get-ChildItem (Join-Path $AssetsDir "assets") -Filter "About-*.js" -ErrorAction SilentlyContinue
    foreach ($chunk in $aboutChunks) {
        Remove-Item -Force $chunk.FullName
    }
    $indexJs = Get-DsdApiMainBundle -AssetsDir $AssetsDir
    if (-not $indexJs) { return }
    $content = [System.IO.File]::ReadAllText($indexJs.FullName, [System.Text.UTF8Encoding]::new($false))
    $content = $content -replace ',\s*\{ titleKey: "nav\.about", href: "/about", icon: Info \}', ''
    $content = $content -replace 'const About = reactExports\.lazy\(\(\) => __vitePreload\(\(\) => import\("\./About-[^"]+"\)[^;]+;', ''
    $content = $content -replace ',"\./About-[^"]+\.js"', ''
    $lines = $content -split "`r?`n"
    $lines = $lines | Where-Object { $_ -notmatch 'path:\s*"/about"' }
    $content = ($lines -join "`n").TrimEnd()
    [System.IO.File]::WriteAllText($indexJs.FullName, $content, [System.Text.UTF8Encoding]::new($false))
}

function Remove-DsdApiTrayAndNotificationSettings {
    param([string]$AssetsDir)
    $settingsJs = Get-ChildItem (Join-Path $AssetsDir "assets") -Filter "Settings-*.js" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($settingsJs) {
        $settings = [System.IO.File]::ReadAllText($settingsJs.FullName, [System.Text.UTF8Encoding]::new($false))
        $minimizeIcon = 'jsxRuntimeExports.jsx(Minimize2, { className: "h-5 w-5" })'
        $globeIcon = 'jsxRuntimeExports.jsx(Globe, { className: "h-5 w-5" })'
        $start = $settings.IndexOf($minimizeIcon)
        if ($start -ge 0) {
            $cardStart = $settings.LastIndexOf('/* @__PURE__ */ jsxRuntimeExports.jsxs(Card, { children: [', $start)
            $globeStart = $settings.IndexOf($globeIcon, $start)
            $cardStart2 = $settings.LastIndexOf('/* @__PURE__ */ jsxRuntimeExports.jsxs(Card, { children: [', $globeStart)
            if ($cardStart -ge 0 -and $cardStart2 -gt $cardStart) {
                $settings = $settings.Remove($cardStart, $cardStart2 - $cardStart)
            }
        }
        $settings = $settings.Replace(',`r`n    minimizeToTray,`r`n    setMinimizeToTray,`r`n    closeBehavior,`r`n    setCloseBehavior,`r`n    enableNotifications,`r`n    setEnableNotifications', '')
        $settings = $settings.Replace(', minimizeToTray, setMinimizeToTray, closeBehavior, setCloseBehavior, enableNotifications, setEnableNotifications', '')
        [System.IO.File]::WriteAllText($settingsJs.FullName, $settings, [System.Text.UTF8Encoding]::new($false))
    }

    $indexJs = Get-DsdApiMainBundle -AssetsDir $AssetsDir
    if (-not $indexJs) { return }
    $content = [System.IO.File]::ReadAllText($indexJs.FullName, [System.Text.UTF8Encoding]::new($false))
    $content = $content -replace '(?s)\s*minimizeToTray: true,\s*setMinimizeToTray: \(enabled\) => set\(\{ minimizeToTray: enabled \}\),\s*closeBehavior: "minimize",\s*setCloseBehavior: \(behavior\) => set\(\{ closeBehavior: behavior \}\),\s*enableNotifications: true,\s*setEnableNotifications: \(enabled\) => set\(\{ enableNotifications: enabled \}\),', ''
    [System.IO.File]::WriteAllText($indexJs.FullName, $content, [System.Text.UTF8Encoding]::new($false))
}

function Remove-DsdApiLanguageSettings {
    param([string]$AssetsDir)
    $settingsJs = Get-ChildItem (Join-Path $AssetsDir "assets") -Filter "Settings-*.js" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($settingsJs) {
        $settings = [System.IO.File]::ReadAllText($settingsJs.FullName, [System.Text.UTF8Encoding]::new($false))
        $langIcon = 'jsxRuntimeExports.jsx(Languages, { className: "h-4 w-4 text-[var(--accent-primary)]" })'
        $sidebarIcon = 'jsxRuntimeExports.jsx(PanelLeft, { className: "h-4 w-4 text-[var(--accent-primary)]" })'
        $start = $settings.IndexOf($langIcon)
        if ($start -ge 0) {
            $cardStart = $settings.LastIndexOf('/* @__PURE__ */ jsxRuntimeExports.jsxs(Card, { children: [', $start)
            $sidebarStart = $settings.IndexOf($sidebarIcon, $start)
            $cardStart2 = $settings.LastIndexOf('/* @__PURE__ */ jsxRuntimeExports.jsxs(Card, { children: [', $sidebarStart)
            if ($cardStart -ge 0 -and $cardStart2 -gt $cardStart) {
                $settings = $settings.Remove($cardStart, $cardStart2 - $cardStart)
            }
        }
        $settings = $settings.Replace(',`r`n    language,`r`n    setLanguage', '')
        $settings = $settings.Replace(', language, setLanguage', '')
        [System.IO.File]::WriteAllText($settingsJs.FullName, $settings, [System.Text.UTF8Encoding]::new($false))
    }

    $indexJs = Get-DsdApiMainBundle -AssetsDir $AssetsDir
    if (-not $indexJs) { return }
    $content = [System.IO.File]::ReadAllText($indexJs.FullName, [System.Text.UTF8Encoding]::new($false))
    $content = $content -replace 'fallbackLng: "en-US",', "fallbackLng: `"zh-CN`",`r`n  lng: `"zh-CN`","
    $content = $content -replace '(?s)convertDetectedLanguage: \(lng\) => \{.*?\}', 'convertDetectedLanguage: () => "zh-CN"'
    $content = $content -replace 'language: "en-US",', 'language: "zh-CN",'
    $content = $content -replace 'language: config\.language \|\| "en-US"', 'language: "zh-CN"'
    $content = $content -replace 'const \{ language, setLanguage \} = useSettingsStore\(\);', ''
    $content = $content -replace '(?s)const toggleLanguage = \(\) => \{.*?\};', ''
    $content = $content -replace '(?s)/\* @__PURE__ \*/ jsxRuntimeExports\.jsx\(\s*"button",\s*\{\s*onClick: toggleLanguage,.*?\}\s*\),', ''
    $content = $content -replace '(?s)setLanguage: async \(language\) => \{.*?\},', "setLanguage: async () => {`r`n        set({ language: `"zh-CN`" });`r`n        await instance.changeLanguage(`"zh-CN`");`r`n      },"
    [System.IO.File]::WriteAllText($indexJs.FullName, $content, [System.Text.UTF8Encoding]::new($false))
}

function Fix-DsdApiThemeProvider {
    param([string]$AssetsDir)
    $indexJs = Get-DsdApiMainBundle -AssetsDir $AssetsDir
    if (-not $indexJs) { return }
    $content = [System.IO.File]::ReadAllText($indexJs.FullName, [System.Text.UTF8Encoding]::new($false))
    $content = $content -replace 'return /\* @__PURE__ \*/ jsxRuntimeExports\.jsx\(jsxRuntimeExports\.Fragment, \{ children \}\);(\s*\})', 'return children;$1'
    [System.IO.File]::WriteAllText($indexJs.FullName, $content, [System.Text.UTF8Encoding]::new($false))
}

function Apply-DsdApiChineseLocale {
    param([string]$AssetsDir)
    $indexJs = Get-DsdApiMainBundle -AssetsDir $AssetsDir
    if (-not $indexJs) { return }
    $content = [System.IO.File]::ReadAllText($indexJs.FullName, [System.Text.UTF8Encoding]::new($false))
    $content = $content -replace '(?s)\s*"en-US":\s*\{\s*translation:\s*enUS\s*\},?', ''
    $content = $content.Replace('instance.use(Browser).use(initReactI18next).init({', 'instance.use(initReactI18next).init({')
    $content = $content -replace 'fallbackLng: "en-US",', "fallbackLng: `"zh-CN`",`r`n  lng: `"zh-CN`","
    $content = $content -replace '(?s)detection:\s*\{.*?\}', "detection: {`r`n    order: [],`r`n    caches: []`r`n  }"
    $content = $content.Replace('const isZh = language === "zh-CN";', 'const isZh = true;')
    $content = $content.Replace('appName: "DSD API",', 'appName: "DeepSeek Desktop",')
    $content = $content.Replace('appName: "Chat2API",', 'appName: "DeepSeek Desktop",')
    $onRehydrateOld = "onRehydrateStorage: () => (state) => {`r`n        if (state?.language) {`r`n          instance.changeLanguage(state.language);`r`n        }`r`n      }"
    $onRehydrateNew = "onRehydrateStorage: () => () => {`r`n        instance.changeLanguage(`"zh-CN`");`r`n      }"
    if ($content.Contains($onRehydrateOld)) {
        $content = $content.Replace($onRehydrateOld, $onRehydrateNew)
    } else {
        $content = $content -replace '(?s)onRehydrateStorage: \(\) => \(\) => \{\s*instance\.changeLanguage\("zh-CN"\);\s*\}\s*\}\s*(\}\s*\)\s*\);)', "onRehydrateStorage: () => () => {`r`n        instance.changeLanguage(`"zh-CN`");`r`n      }`r`n    `$1"
    }
    [System.IO.File]::WriteAllText($indexJs.FullName, $content, [System.Text.UTF8Encoding]::new($false))

    $settingsJs = Get-ChildItem (Join-Path $AssetsDir "assets") -Filter "Settings-*.js" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($settingsJs) {
        $settings = [System.IO.File]::ReadAllText($settingsJs.FullName, [System.Text.UTF8Encoding]::new($false))
        $settings = $settings.Replace('children: "Debug"', 'children: "调试"')
        $settings = $settings.Replace('children: "Info"', 'children: "信息"')
        $settings = $settings.Replace('children: "Warn"', 'children: "警告"')
        $settings = $settings.Replace('children: "Error"', 'children: "错误"')
        $settings = $settings.Replace('children: "璋冭瘯"', 'children: "调试"')
        $settings = $settings.Replace('children: "淇℃伅"', 'children: "信息"')
        $settings = $settings.Replace('children: "璀﹀憡"', 'children: "警告"')
        $settings = $settings.Replace('children: "閿欒"', 'children: "错误"')
        $settings = $settings.Replace('t("settings.managementApi.title")', '"负载均衡"')
        $settings = $settings.Replace('value: "managementApi"', 'value: "loadbalance"')
        $settings = $settings -replace 'TabsContent, \{ value: "managementApi"', 'TabsContent, { value: "loadbalance"'
        if ($settings -notmatch 'function LoadBalanceMount\(') {
            $settings = $settings.Replace(
                "function Settings() {",
                "function LoadBalanceMount() {`r`n  return /* @__PURE__ */ jsxRuntimeExports.jsx(`"div`", { id: `"ds-loadbalance-root`", className: `"space-y-4`" });`r`n}`r`nfunction Settings() {"
            )
        }
        $settings = $settings -replace 'ManagementApiSettings, \{\}\)', 'LoadBalanceMount, {})'
        [System.IO.File]::WriteAllText($settingsJs.FullName, $settings, [System.Text.UTF8Encoding]::new($false))
    }
}

function Patch-DsdApiProvidersLoginFailed {
    param([string]$AssetsDir)
    $indexJs = Get-DsdApiMainBundle -AssetsDir $AssetsDir
    if (-not $indexJs) { return }
    $content = [System.IO.File]::ReadAllText($indexJs.FullName, [System.Text.UTF8Encoding]::new($false))
    $orig = $content
    if ($content -notmatch 'providers\$1\s*=\s*\{[^}]*loginFailed:') {
        $content = $content -replace '(loginWindowAlreadyOpen:\s*"登录窗口已打开",\s*\r?\n)(\s*loggingIn:)', "`$1  loginFailed: `"登录失败`",`n`$2"
        $content = $content -replace '(loginWindowAlreadyOpen:\s*"A login window is already open",\s*\r?\n)(\s*loggingIn:)', "`$1  loginFailed: `"Login failed`",`n`$2"
    }
    if ($content -ne $orig) {
        [System.IO.File]::WriteAllText($indexJs.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        Write-Host "  OK providers.loginFailed patched in $($indexJs.Name)"
    }
}

function Enable-DsdApiCustomProviderTab {
    param([string]$AssetsDir)
    $providersJs = Get-ChildItem (Join-Path $AssetsDir "assets") -Filter "Providers-*.js" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $providersJs) { return }
    $p = [System.IO.File]::ReadAllText($providersJs.FullName, [System.Text.UTF8Encoding]::new($false))
    $disabledTab = 'jsxRuntimeExports.jsxs(TabsTrigger, { value: "custom", disabled: true, className: "gap-1", children: [
        t("providers.customProviders"),
        /* @__PURE__ */ jsxRuntimeExports.jsxs("span", { className: "text-[10px] text-muted-foreground", children: [
          "(",
          t("providers.customProviderNotSupported"),
          ")"
        ] })
      ] })'
    $enabledTab = 'jsxRuntimeExports.jsx(TabsTrigger, { value: "custom", className: "gap-1", children: t("providers.customProviders") })'
    if ($p.Contains('value: "custom", disabled: true')) {
        $p = $p.Replace($disabledTab, $enabledTab)
    }
    $p = $p -replace 'customProviderNotSupported:\s*"暂不支持"', 'customProviderNotSupported: ""'
    $p = $p -replace 'customProviderNotSupported:\s*"Not supported yet"', 'customProviderNotSupported: ""'
    [System.IO.File]::WriteAllText($providersJs.FullName, $p, [System.Text.UTF8Encoding]::new($false))

    $indexJs = Get-DsdApiMainBundle -AssetsDir $AssetsDir
    if ($indexJs) {
        $content = [System.IO.File]::ReadAllText($indexJs.FullName, [System.Text.UTF8Encoding]::new($false))
        $content = $content -replace 'customProviderNotSupported:\s*"暂不支持"', 'customProviderNotSupported: ""'
        $content = $content -replace 'customProviderNotSupported:\s*"Not supported yet"', 'customProviderNotSupported: ""'
        [System.IO.File]::WriteAllText($indexJs.FullName, $content, [System.Text.UTF8Encoding]::new($false))
    }
}

function Apply-DsdDesktopBundleBranding {
    param([string]$AssetsDir)
    $assetsFolder = Join-Path $AssetsDir "assets"
    if (-not (Test-Path $assetsFolder)) { return }

    $pairs = @(
        @("Chat2API XML", "DeepSeek Desktop XML"),
        @("Chat2API", "DeepSeek Desktop"),
        @("Chat2Api", "DeepSeek Desktop"),
        @("chat2api-settings", "dsd-desktop-api-settings"),
        @("chat2api-config", "dsd-api-config")
    )

    foreach ($js in Get-ChildItem $assetsFolder -Filter "*.js" -ErrorAction SilentlyContinue) {
        $content = [System.IO.File]::ReadAllText($js.FullName, [System.Text.UTF8Encoding]::new($false))
        $orig = $content
        foreach ($pair in $pairs) {
            $content = $content.Replace($pair[0], $pair[1])
        }
        if ($content -ne $orig) {
            [System.IO.File]::WriteAllText($js.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        }
    }
    Write-Host "  OK DeepSeek Desktop (DPDT) branding applied to renderer bundles"
}

Remove-DsdApiAboutPage -AssetsDir $DestDir
Remove-DsdApiTrayAndNotificationSettings -AssetsDir $DestDir
Remove-DsdApiLanguageSettings -AssetsDir $DestDir
Fix-DsdApiThemeProvider -AssetsDir $DestDir
Apply-DsdApiChineseLocale -AssetsDir $DestDir
Patch-DsdApiProvidersLoginFailed -AssetsDir $DestDir
Enable-DsdApiCustomProviderTab -AssetsDir $DestDir
Apply-DsdDesktopBundleBranding -AssetsDir $DestDir

$brandingHits = Select-String -Path (Join-Path $DestDir "assets\*.js") -Pattern "Chat2API|chat2api-settings" -SimpleMatch -ErrorAction SilentlyContinue
if ($brandingHits) {
    throw "DSD API bundle still contains legacy Chat2API branding: $($brandingHits[0].Filename)"
}

& (Join-Path (Split-Path $PSScriptRoot -Parent) "scripts\sync-agent-dsd-api.ps1") -Root (Split-Path $PSScriptRoot -Parent)

$mainBundle = Get-DsdApiMainBundle -AssetsDir $DestDir
if (Get-Command node -ErrorAction SilentlyContinue) {
    if ($mainBundle) {
        & node --check $mainBundle.FullName
        if ($LASTEXITCODE -ne 0) { throw "DSD API bundle syntax check failed: $($mainBundle.Name)" }
        Write-Host "  OK DSD API bundle syntax ($($mainBundle.Name))"
    }
    $settingsBundle = Get-ChildItem (Join-Path $DestDir "assets") -Filter "Settings-*.js" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($settingsBundle) {
        & node --check $settingsBundle.FullName
        if ($LASTEXITCODE -ne 0) { throw "DSD API bundle syntax check failed: $($settingsBundle.Name)" }
        Write-Host "  OK DSD API bundle syntax ($($settingsBundle.Name))"
    }
}

Write-Host "DSD API UI copied to $DestDir"
Write-Host "Reminder: bump DeepSeek.Core/Services/AppNavigation.cs EmbeddedUiBuild after UI asset changes."
