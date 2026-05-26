namespace DeepSeekBrowser.Services;

/// <summary>在 chat.deepseek.com 上强制挂载模式切换浮钮（与 document-created 脚本配合）。</summary>
public static class ChatModeFloaterScript
{
    /// <summary>最小挂载（不依赖外部 JS 文件；CSP 下 script 标签加载 ds-inject.local 会失败）。</summary>
    public const string MinimalMount =
        "(function(){try{"
        + "if(!/chat\\.deepseek\\.com/i.test(location.hostname||''))return;"
        + "window.__dsChatModeFloaterBootstrapped=true;"
        + "var host=document.getElementById('ds-desktop-overlay-root');"
        + "if(!host){host=document.createElement('div');host.id='ds-desktop-overlay-root';"
        + "host.style.cssText='position:fixed;inset:0;pointer-events:none;z-index:2147483647';"
        + "document.documentElement.appendChild(host);}"
        + "var b=document.getElementById('ds-agent-mode-float');"
        + "if(!b){b=document.createElement('button');b.type='button';b.id='ds-agent-mode-float';"
        + "b.className='ds-mode-float ds-agent-mode-float ds-chat-mode-floater';"
        + "b.innerHTML='<span class=\"ds-mode-float-icon\" aria-hidden=\"true\">"
        + "<svg viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\">"
        + "<path d=\"M12 2a4 4 0 0 1 4 4v1h1a3 3 0 0 1 3 3v9a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3V10a3 3 0 0 1 3-3h1V6a4 4 0 0 1 4-4z\"/></svg></span>"
        + "<span id=\"ds-agent-mode-float-label\">普通</span>';"
        + "b.style.cssText='position:fixed;bottom:24px;right:20px;top:auto;left:auto;z-index:2147483647;"
        + "display:inline-flex;align-items:center;gap:6px;height:34px;min-width:88px;padding:0 14px;"
        + "border-radius:9999px;border:1px solid #e5e7eb;background:rgba(255,255,255,.96);"
        + "cursor:pointer;pointer-events:auto;font:13px sans-serif;color:#374151;"
        + "box-shadow:0 4px 16px rgba(0,0,0,.06);transition:opacity .12s ease,transform .12s ease,border-color .12s ease,background .12s ease';"
        + "b.addEventListener('click',function(e){e.preventDefault();e.stopPropagation();"
        + "if(window.DsWorkMode&&window.DsWorkMode.activateFloater){window.DsWorkMode.activateFloater();return;}"
        + "if(window.chrome&&window.chrome.webview)"
        + "window.chrome.webview.postMessage(JSON.stringify({type:'toggleWorkMode'}));},true);"
        + "host.appendChild(b);}"
        + "b.style.setProperty('display','inline-flex','important');"
        + "window.__dsEnsureChatModeFloater=function(){"
        + "var x=document.getElementById('ds-agent-mode-float');if(x)x.style.setProperty('display','inline-flex','important');};"
        + "}catch(e){console.warn('[DeepSeek Desktop] MinimalMount',e);}})();";

    public const string LoadFromVirtualHost =
        "(function(){try{"
        + "if(!/chat\\.deepseek\\.com/i.test(location.hostname||''))return;"
        + "if(window.__dsChatModeFloaterBootstrapped)return;"
        + "var s=document.createElement('script');"
        + "s.src='https://ds-inject.local/chat-mode-floater.js';"
        + "s.onload=function(){if(window.__dsChatModeFloaterBoot)window.__dsChatModeFloaterBoot();"
        + "if(window.__dsEnsureChatModeFloater)window.__dsEnsureChatModeFloater();};"
        + "(document.head||document.documentElement).appendChild(s);"
        + "}catch(e){console.warn('[DeepSeek Desktop] load chat-mode-floater',e);}})();";

    public const string Ensure =
        "(function(){try{"
        + "if(!/chat\\.deepseek\\.com/i.test(location.hostname||''))return;"
        + "if(window.__dsEnsureChatModeFloater)window.__dsEnsureChatModeFloater();"
        + "else if(window.__dsMountModeFloater)window.__dsMountModeFloater();"
        + "}catch(e){console.warn('[DeepSeek Desktop] EnsureChatModeFloater',e);}})();";

    /// <summary>自检：返回 JSON { ok, boot, url, width, height }。</summary>
    public const string Probe =
        "(function(){try{"
        + "if(window.__dsEnsureChatModeFloater)window.__dsEnsureChatModeFloater();"
        + "var b=document.getElementById('ds-agent-mode-float');"
        + "if(!b)return JSON.stringify({ok:false,reason:'no-button',boot:!!window.__dsChatModeFloaterBootstrapped,url:location.href});"
        + "var r=b.getBoundingClientRect();"
        + "var st=window.getComputedStyle(b);"
        + "return JSON.stringify({ok:r.width>0&&r.height>0&&st.display!=='none'&&st.visibility!=='hidden',"
        + "boot:!!window.__dsChatModeFloaterBootstrapped,url:location.href,width:r.width,height:r.height,display:st.display});"
        + "}catch(e){return JSON.stringify({ok:false,error:String(e)});}})();";
}
