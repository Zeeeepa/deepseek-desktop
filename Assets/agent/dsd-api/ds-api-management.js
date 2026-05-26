(function () {
  "use strict";
  if (window.__dsApiManagement) return;
  window.__dsApiManagement = true;

  function patchI18n() {
    try {
      if (window.i18next && typeof window.i18next.addResourceBundle === "function") {
        window.i18next.addResourceBundle(
          "zh-CN",
          "translation",
          {
            providers: {
              loginFailed: "登录失败",
              validateFailed: "验证失败",
              customProviders: "自定义供应商",
              customProviderNotSupported: "",
              createCustomProvider: "添加 OpenAI 兼容供应商",
              createCustomProviderDesc:
                "配置 Base URL 与 API Key，以 OpenAI 兼容方式接入第三方模型"
            }
          },
          true,
          true
        );
      }
    } catch (_) {}
  }

  function isCustomTab(el) {
    if (!el || el.getAttribute("role") !== "tab") return false;
    var text = (el.textContent || "").replace(/\s+/g, " ");
    return /自定义|Custom\s*Provider/i.test(text);
  }

  function activateCustomTab(tab) {
    tab.removeAttribute("disabled");
    tab.setAttribute("aria-disabled", "false");
    tab.removeAttribute("data-disabled");
    tab.style.pointerEvents = "auto";
    tab.style.opacity = "1";
    tab.style.cursor = "pointer";

    tab.click();

    var list = tab.closest("[role='tablist']");
    if (!list) return;
    var root = list.parentElement;
    if (!root) return;

    var panels = root.querySelectorAll("[role='tabpanel']");
    panels.forEach(function (panel) {
      var isCustom = panel.getAttribute("value") === "custom" || panel.getAttribute("data-value") === "custom";
      if (isCustom) {
        panel.removeAttribute("hidden");
        panel.style.display = "";
        panel.setAttribute("data-state", "active");
      } else {
        panel.setAttribute("hidden", "");
        panel.style.display = "none";
        panel.setAttribute("data-state", "inactive");
      }
    });

    list.querySelectorAll("[role='tab']").forEach(function (t) {
      var on = t === tab;
      t.setAttribute("data-state", on ? "active" : "inactive");
      t.setAttribute("aria-selected", on ? "true" : "false");
    });
  }

  function enableCustomTabs() {
    document.querySelectorAll("[role='tablist']").forEach(function (list) {
      var tabs = list.querySelectorAll("[role='tab']");
      if (tabs.length < 2) return;
      var custom = tabs[1];
      if (!isCustomTab(custom)) return;

      custom.removeAttribute("disabled");
      custom.setAttribute("aria-disabled", "false");
      custom.removeAttribute("data-disabled");
      custom.classList.remove("pointer-events-none", "opacity-50", "cursor-not-allowed");

      custom.querySelectorAll("span").forEach(function (span) {
        if (/暂不支持|Not supported/i.test(span.textContent || "")) span.remove();
      });

      if (!custom.__dsCustomBound) {
        custom.__dsCustomBound = true;
        custom.addEventListener(
          "click",
          function (e) {
            if (custom.hasAttribute("disabled")) {
              e.preventDefault();
              e.stopImmediatePropagation();
            }
            activateCustomTab(custom);
          },
          true
        );
      }
    });
  }

  function run() {
    patchI18n();
    enableCustomTabs();
  }

  document.addEventListener(
    "click",
    function (e) {
      var tab = e.target && e.target.closest ? e.target.closest("[role='tab']") : null;
      if (!tab || !isCustomTab(tab)) return;
      if (tab.hasAttribute("disabled") || tab.getAttribute("aria-disabled") === "true") {
        e.preventDefault();
        e.stopImmediatePropagation();
        activateCustomTab(tab);
      }
    },
    true
  );

  function schedule() {
    run();
    var n = 40;
    (function tick() {
      run();
      if (--n > 0) setTimeout(tick, 350);
    })();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", schedule);
  } else {
    schedule();
  }

  var root = document.getElementById("root");
  if (root) {
    new MutationObserver(run).observe(root, { childList: true, subtree: true, attributes: true });
  }
})();
