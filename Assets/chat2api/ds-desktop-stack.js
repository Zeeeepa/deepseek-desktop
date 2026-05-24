(function () {
  "use strict";
  if (window.__dsDesktopStack) return;
  window.__dsDesktopStack = true;

  function postDesktop(type, payload) {
    var body = JSON.stringify(
      Object.assign({ type: type, __dsEmbed: true }, payload || {})
    );
    if (window.parent && window.parent !== window) {
      window.parent.postMessage(body, "*");
      return;
    }
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(body);
    }
  }

  function removeStackBar() {
    var bar = document.getElementById("ds-desktop-stack-bar");
    if (bar) bar.remove();
  }

  window.addEventListener("message", function (e) {
    try {
      var msg = typeof e.data === "string" ? JSON.parse(e.data) : e.data;
      if (msg && msg.type === "desktopStackSynced") {
        /* stack bar removed from API 管理 UI */
      }
    } catch (_) {}
  });

  function schedule() {
    removeStackBar();
    var left = 10;
    function tick() {
      removeStackBar();
      if (--left > 0) setTimeout(tick, 500);
    }
    tick();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", schedule);
  } else {
    schedule();
  }
  window.addEventListener("load", schedule);
})();
