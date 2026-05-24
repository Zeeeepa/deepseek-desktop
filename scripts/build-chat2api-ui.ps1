param(
    [string]$Chat2ApiSource = "C:\Users\xiaow\Desktop\DSD\Chat2API-main\Chat2API-main",
    [string]$DestDir = "",
    [switch]$ForceRebuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $DestDir) { $DestDir = Join-Path $root "Assets\chat2api" }
$uiSrc = Join-Path $root "Assets\chat2api-ui"
$rendererOut = Join-Path $Chat2ApiSource "out\renderer"
$indexPath = Join-Path $DestDir "index.html"
$useExistingBundle = (Test-Path $indexPath) -and -not $ForceRebuild

if ($useExistingBundle) {
    Write-Host "Using existing Chat2API bundle at $DestDir (skip external npm build). Pass -ForceRebuild to rebuild from source."
}
else {
    if (-not (Test-Path $rendererOut)) {
        Write-Host "Building Chat2API renderer in $Chat2ApiSource ..."
        Push-Location $Chat2ApiSource
        if (-not (Test-Path "node_modules")) {
            npm install --ignore-scripts
        }
        npm run build
        Pop-Location
    }

    if (-not (Test-Path $rendererOut)) {
        throw "Chat2API renderer build missing: $rendererOut"
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
Copy-Item -Force (Join-Path $uiSrc "ds-i18n-zh.js") (Join-Path $DestDir "ds-i18n-zh.js")
Copy-Item -Force (Join-Path $uiSrc "ds-desktop-stack.js") (Join-Path $DestDir "ds-desktop-stack.js")
Copy-Item -Force (Join-Path $uiSrc "ds-boot-guard.js") (Join-Path $DestDir "ds-boot-guard.js")
Copy-Item -Force (Join-Path $uiSrc "deepseek-brand.svg") (Join-Path $DestDir "deepseek-brand.svg")

function Get-Chat2ApiMainBundle {
    param([string]$AssetsDir)
    Get-ChildItem (Join-Path $AssetsDir "assets") -Filter "index-*.js" -ErrorAction SilentlyContinue |
        Sort-Object Length -Descending |
        Select-Object -First 1
}

if (-not (Test-Path $indexPath)) {
    throw "Chat2API bundle missing: $indexPath"
}
$html = Get-Content $indexPath -Raw -Encoding UTF8
$html = $html -replace '<title>Chat2API</title>', '<title>DeepSeek API</title>'
$html = $html -replace '<html lang="en" data-theme="dark">', '<html lang="zh-CN" data-theme="light" class="ds-console">'
$html = $html -replace '<html lang="zh-CN" data-theme="light">', '<html lang="zh-CN" data-theme="light" class="ds-console">'
# WebView2 虚拟域名下 crossorigin 会导致 ES module 无法加载（白屏）
$html = $html -replace '\s+crossorigin', ''
$inject = @"
    <link rel="stylesheet" href="./ds-theme-override.css" />
    <script src="./webview-preload.js"></script>
    <script src="./ds-boot-guard.js"></script>
    <script>
      (function () {
        var h = location.hash || "";
        if (!h || h === "#" || h === "#/" || h.indexOf("#/proxy") === 0 || h.indexOf("#/about") === 0) {
          location.replace(location.pathname + location.search + "#/providers");
        }
      })();
    </script>
"@
$html = $html -replace '(<meta charset="UTF-8" />)', "`$1`n$inject"
$html = $html -replace '</body>', "  <script src=`"./ds-i18n-zh.js`"></script>`n  <script src=`"./ds-ui-trim.js`"></script>`n  <script src=`"./ds-ready.js`"></script>`n  <script src=`"./ds-rebrand.js`"></script>`n  <script src=`"./ds-desktop-stack.js`"></script>`n  </body>"
[System.IO.File]::WriteAllText($indexPath, $html, [System.Text.UTF8Encoding]::new($false))

function Remove-Chat2ApiAboutPage {
    param([string]$AssetsDir)
    $aboutChunks = Get-ChildItem (Join-Path $AssetsDir "assets") -Filter "About-*.js" -ErrorAction SilentlyContinue
    foreach ($chunk in $aboutChunks) {
        Remove-Item -Force $chunk.FullName
    }
    $indexJs = Get-Chat2ApiMainBundle -AssetsDir $AssetsDir
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

function Remove-Chat2ApiLanguageSettings {
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

    $indexJs = Get-Chat2ApiMainBundle -AssetsDir $AssetsDir
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

function Fix-Chat2ApiThemeProvider {
    param([string]$AssetsDir)
    $indexJs = Get-Chat2ApiMainBundle -AssetsDir $AssetsDir
    if (-not $indexJs) { return }
    $content = [System.IO.File]::ReadAllText($indexJs.FullName, [System.Text.UTF8Encoding]::new($false))
    $content = $content -replace 'return /\* @__PURE__ \*/ jsxRuntimeExports\.jsx\(jsxRuntimeExports\.Fragment, \{ children \}\);(\s*\})', 'return children;$1'
    [System.IO.File]::WriteAllText($indexJs.FullName, $content, [System.Text.UTF8Encoding]::new($false))
}

function Apply-Chat2ApiChineseLocale {
    param([string]$AssetsDir)
    $indexJs = Get-Chat2ApiMainBundle -AssetsDir $AssetsDir
    if (-not $indexJs) { return }
    $content = [System.IO.File]::ReadAllText($indexJs.FullName, [System.Text.UTF8Encoding]::new($false))
    $content = $content -replace '(?s)\s*"en-US":\s*\{\s*translation:\s*enUS\s*\},?', ''
    $content = $content.Replace('instance.use(Browser).use(initReactI18next).init({', 'instance.use(initReactI18next).init({')
    $content = $content -replace 'fallbackLng: "en-US",', "fallbackLng: `"zh-CN`",`r`n  lng: `"zh-CN`","
    $content = $content -replace '(?s)detection:\s*\{.*?\}', "detection: {`r`n    order: [],`r`n    caches: []`r`n  }"
    $content = $content.Replace('const isZh = language === "zh-CN";', 'const isZh = true;')
    $content = $content.Replace('appName: "Chat2API",', 'appName: "DeepSeek API",')
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
        [System.IO.File]::WriteAllText($settingsJs.FullName, $settings, [System.Text.UTF8Encoding]::new($false))
    }
}

Remove-Chat2ApiAboutPage -AssetsDir $DestDir
Remove-Chat2ApiLanguageSettings -AssetsDir $DestDir
Fix-Chat2ApiThemeProvider -AssetsDir $DestDir
Apply-Chat2ApiChineseLocale -AssetsDir $DestDir

& (Join-Path (Split-Path $PSScriptRoot -Parent) "scripts\sync-agent-chat2api.ps1") -Root (Split-Path $PSScriptRoot -Parent)

$mainBundle = Get-Chat2ApiMainBundle -AssetsDir $DestDir
if ($mainBundle -and (Get-Command node -ErrorAction SilentlyContinue)) {
    & node --check $mainBundle.FullName
    if ($LASTEXITCODE -ne 0) { throw "Chat2API bundle syntax check failed: $($mainBundle.Name)" }
    Write-Host "  OK Chat2API bundle syntax ($($mainBundle.Name))"
}

Write-Host "Chat2API UI copied to $DestDir"
