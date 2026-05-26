# 澶栧洿璧勪骇瀹℃煡锛堥潪涓绘簮鐮侊級

本仓库**不包含**下列与 WPF 交付无关的内容（历史上由 edge 仓拆分；勿再对 desktop 做 edge 镜像同步）：

| 鎺掗櫎椤?| 鍘熷洜 |
|--------|------|
| `DeepSeek.Qt/` | Qt 瀹為獙澹筹紝闈為粯璁や氦浠?|
| `DeepSeek.Desktop/` | WinUI 瀹為獙澹?|
| `DeepSeek.DdBridge/` | Qt 妗ユ帴杩涚▼ |
| `DeepSeek.Launcher/` | 宸插悎骞惰繘 `DeepSeek.exe` 杩愯鏃舵娴?|
| `third-party/` | 瀛愭ā鍧?澶栭儴 TUI锛孒arness 宸插師鐢熸浛浠?|
| `bin/`銆乣obj/`銆乣publish/` | 鏋勫缓浜х墿 |
| `Assets/agent/chat2api/` | 鐢?`sync-agent-chat2api.ps1` 鐢熸垚 |

## 蹇呴』淇濈暀鐨勫鍥磋祫浜?

| 璧勪骇 | 楠屾敹瑕佺偣 |
|------|----------|
| `Assets/inject/*` | `bridge.js`銆乣overlay.js`銆乣chat-mode-floater.js` 鍦?publish 涓瓨鍦紱`node --check` 瀵?agent-app.js |
| `Assets/chat2api/` | 棰勬瀯寤?UI bundle锛沗-ForceRebuild` 闇€ Node 宸ュ叿閾?|
| `Assets/agent/` | Agent 椤典笌 settings embed锛涘嬁鎻愪氦鐢熸垚鐨?`chat2api/` |
| `Assets/chat2api-ui/` | 浠呮簮鐮侊紝鏋勫缓鏃跺鍒跺埌 `Assets/chat2api` |
| `tools/generate-icon.ps1` | 鏋勫缓鍓嶇敓鎴?`deepseek.ico` |
| `scripts/verify-integration.ps1` | 鍙戝竷鐩綍瀹屾暣鎬ч棬绂?|

## Chat2API 鍓嶇

- 璁稿彲璇侊細浠?`Assets/chat2api-ui` 鍙婃瀯寤轰骇鐗╀负鍑嗭紝鍙戝竷鍓嶆牳瀵?`package.json` / lockfile
- 瀹夊叏锛氫笉鍦ㄤ粨搴撳唴鎻愪氦 `.env`銆丄PI Key锛涚敤鎴?Token 浠呭瓨 `%LocalAppData%\deepseek_desktop`

## WebView2 杩愯鏃?

- 浜や粯鐗╀緷璧栫敤鎴锋満鍣ㄥ畨瑁?WebView2锛沗RuntimeStartup` 璐熻矗寮曞瀹夎
- 涓嶅湪浠撳簱鎹嗙粦瀹屾暣 Edge 瀹夎鍖咃紙浣撶Н涓庤鍙級

## 寤鸿闂ㄧ

1. PR锛歚audit-supply-chain.ps1` + `dotnet test` + `build.ps1`
2. Release锛氫粎鎵撳寘 `publish/` 鐩綍涓?zip锛屼笉鍚簮鐮?`obj/`
3. 瀹氭湡锛氬 `Microsoft.Web.WebView2`銆乣ModelContextProtocol` 妫€鏌?CVE
