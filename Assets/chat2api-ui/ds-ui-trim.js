(function () {
  "use strict";
  if (window.__dsUiTrim) return;
  window.__dsUiTrim = true;

  var HOME_HASH = "#/providers";
  var HIDDEN_NAV = /^(代理设置|Proxy Settings|仪表盘|Dashboard|API Keys|API 密钥|请求日志|Logs|日志|关于|About)$/i;
  var HIDDEN_TAB = /^(状态监控|基础配置|负载均衡|高级配置|Status Monitoring|Basic Configuration|Load Balanc|Advanced Configuration)$/i;
  var PROXY_TEXT = /代理服务|Proxy service|启动代理|停止代理|Start Proxy|Stop Proxy|proxyPort|负载均衡|Load Balanc/i;

  function hide(el) {
    if (!el || el.getAttribute("data-ds-hidden") === "1") return;
    el.setAttribute("data-ds-hidden", "1");
    el.style.setProperty("display", "none", "important");
  }

  function navRoot(el) {
    return (
      el.closest("[data-sidebar='menu-button']") ||
      el.closest(".sidebar-nav-item") ||
      el.closest("li") ||
      el.parentElement ||
      el
    );
  }

  function isAboutLocation() {
    var hash = location.hash || "";
    var path = location.pathname || "";
    return (
      hash.indexOf("#/about") === 0 ||
      /#\/about(\/|$|\?)/.test(hash) ||
      /\/about(\/|$|\?)/.test(path)
    );
  }

  function ensureEmbeddedRoute() {
    var hash = location.hash || "";
    if (isAboutLocation()) {
      location.replace(HOME_HASH);
      return;
    }
    if (!hash || hash === "#/" || hash === "#" || hash.indexOf("#/proxy") === 0) {
      if (hash !== HOME_HASH) location.replace(HOME_HASH);
      return;
    }
    if (hash.indexOf("#/api-keys") === 0 || hash.indexOf("#/logs") === 0) {
      location.replace(HOME_HASH);
    }
  }

  var ABOUT_PAGE = /Chat2API|Multi-platform AI Service|检查更新|Check for Updates|Application Updates|GitHub Repository|Report Issue|LINKS/i;

  function hideAboutPage() {
    if (isAboutLocation()) location.replace(HOME_HASH);
    document.querySelectorAll("main").forEach(function (main) {
      var text = (main.textContent || "").slice(0, 400);
      if (ABOUT_PAGE.test(text)) hide(main);
    });
  }

  function hideSidebarNav() {
    document.querySelectorAll(".glass-sidebar a, .glass-sidebar button, aside a, aside button").forEach(function (el) {
      var href = (el.getAttribute("href") || "") + " " + (el.getAttribute("to") || "");
      var text = (el.textContent || "").replace(/\s+/g, " ").trim();
      if (/\/proxy|#\/proxy/i.test(href)) hide(navRoot(el));
      if (HIDDEN_NAV.test(text)) hide(navRoot(el));
      if (/#\/($|\?)|href=\"\/\"|href='#\/'/i.test(href) && /Dashboard|仪表盘/i.test(text)) hide(navRoot(el));
    });
  }

  function hideProxyTabsAndPanels() {
    document.querySelectorAll("[role='tab'], button, a").forEach(function (el) {
      var text = (el.textContent || "").replace(/\s+/g, " ").trim();
      if (HIDDEN_TAB.test(text)) hide(el);
    });
    document.querySelectorAll("main h1, main h2, main h3, .glass-card, [class*='glass-card']").forEach(function (el) {
      var text = (el.textContent || "").replace(/\s+/g, " ").slice(0, 120);
      if (/^代理设置$|^Proxy Settings$/i.test(text.trim())) {
        var page = el.closest("main") || document.querySelector("main");
        if (page && (location.hash || "").indexOf("#/proxy") === 0) hide(page);
      }
    });
  }

  function hideTopbarProxyControls() {
    document.querySelectorAll(".glass-topbar button, .glass-topbar .rounded-full, .glass-topbar [class*='toggle']").forEach(function (el) {
      var text = (el.textContent || "").replace(/\s+/g, " ");
      if (PROXY_TEXT.test(text) || /127\.0\.0\.1:\d+/.test(text)) hide(el);
      if (el.classList && (el.classList.contains("proxy-toggle-active") || el.classList.contains("proxy-toggle-btn-active"))) {
        hide(el.closest(".rounded-full") || el.parentElement || el);
      }
    });
  }

  function hideDashboardProxyWidgets() {
    document.querySelectorAll(".glass-card, .glass-card-hover, [class*='glass-card']").forEach(function (card) {
      var text = (card.textContent || "").replace(/\s+/g, " ");
      if (/快捷操作|Quick Actions/i.test(text) && PROXY_TEXT.test(text)) hide(card);
      if (/代理状态|Proxy Status/i.test(text)) hide(card);
      if (/启动代理|停止代理|Start Proxy|Stop Proxy/i.test(text)) hide(card);
    });
  }

  function hideSettingsProxySections() {
    document.querySelectorAll("label, h3, h4, p, span, div").forEach(function (el) {
      var t = (el.textContent || "").trim();
      if (/^自动启动代理$|^Auto[- ]?start Proxy$/i.test(t)) {
        hide(el.closest("[class*='flex']") || el.closest(".grid") || el.parentElement);
      }
      if (/^网络代理$|^Network Proxy$|^OAuth 登录代理$/i.test(t)) {
        hide(el.closest("section") || el.closest("[class*='card']") || el.parentElement?.parentElement);
      }
      if (t === "127.0.0.1" || /^127\.0\.0\.1:\d+$/.test(t)) hide(el.closest("motion.div") || el.parentElement);
    });
  }

  function isChat2ApiHostPage() {
    var h = (location.hostname || "").toLowerCase();
    var p = location.pathname || "";
    return h === "ds-chat2api.local" || (h === "ds-agent.local" && /\/chat2api\//i.test(p));
  }

  function injectAgentBackButton() {
    if (!isChat2ApiHostPage()) return;
    if (window.parent !== window) return;
    if (document.getElementById("ds-agent-back-btn")) return;
    var btn = document.createElement("button");
    btn.id = "ds-agent-back-btn";
    btn.type = "button";
    btn.textContent = "← 返回 Agent";
    btn.style.cssText =
      "position:fixed;top:10px;left:12px;z-index:99999;padding:8px 14px;border-radius:8px;" +
      "border:1px solid #c7d2fe;background:#fff;color:#1e40af;font-size:13px;cursor:pointer;" +
      "box-shadow:0 2px 8px rgba(0,0,0,.08);";
    btn.addEventListener("click", function () {
      var body = JSON.stringify({ type: "openAgentFromChat2Api", __dsEmbed: true });
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(body);
      } else if (window.parent && window.parent !== window) {
        window.parent.postMessage(body, "*");
      }
    });
    document.body.appendChild(btn);
  }

  function removeStackBar() {
    var bar = document.getElementById("ds-desktop-stack-bar");
    if (bar) bar.remove();
    document.querySelectorAll(".ds-desktop-stack-bar").forEach(function (el) {
      el.remove();
    });
    var banner = document.getElementById("ds-embedded-banner");
    if (banner) banner.remove();
  }

  function hideLanguageControls() {
    try {
      localStorage.setItem("i18nextLng", "zh-CN");
    } catch (_) {}
    document.querySelectorAll(".glass-topbar button").forEach(function (btn) {
      if (btn.querySelector(".lucide-languages, svg.lucide-languages")) hide(btn);
    });
    document.querySelectorAll(".glass-card, [class*='glass-card'], section").forEach(function (card) {
      var text = (card.textContent || "").replace(/\s+/g, " ");
      if (/设置应用程序的显示语言|Set the display language of the application/i.test(text)) {
        hide(card);
      }
    });
    document.querySelectorAll("label, p, h3, h4").forEach(function (el) {
      var t = (el.textContent || "").trim();
      if (t === "语言" || t === "Language") {
        var card = el.closest(".glass-card") || el.closest("[class*='glass-card']") || el.closest("section");
        if (card && /设置应用程序的显示语言|Set the display language|简体中文|Simplified Chinese/i.test(card.textContent || "")) {
          hide(card);
        }
      }
    });
  }

  function trimOnce() {
    ensureEmbeddedRoute();
    hideAboutPage();
    hideLanguageControls();
    hideSidebarNav();
    hideProxyTabsAndPanels();
    hideTopbarProxyControls();
    hideDashboardProxyWidgets();
    hideSettingsProxySections();
    injectAgentBackButton();
    removeStackBar();

    document.querySelectorAll('a[href="/proxy"], a[href="#/proxy"], a[href="/about"], a[href="#/about"], [href*="/proxy"], [href*="/about"]').forEach(function (a) {
      hide(navRoot(a));
    });
  }

  function schedule() {
    trimOnce();
    var left = 40;
    function tick() {
      trimOnce();
      if (--left > 0) setTimeout(tick, 350);
    }
    tick();
  }

  window.addEventListener("hashchange", function () {
    ensureEmbeddedRoute();
    setTimeout(trimOnce, 0);
  });

  function startObserver() {
    var root = document.getElementById("root");
    if (!root || root.__dsTrimObs) return;
    root.__dsTrimObs = true;
    new MutationObserver(function () {
      trimOnce();
    }).observe(root, { childList: true, subtree: true });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", function () {
      schedule();
      startObserver();
    });
  } else {
    schedule();
    startObserver();
  }
})();
