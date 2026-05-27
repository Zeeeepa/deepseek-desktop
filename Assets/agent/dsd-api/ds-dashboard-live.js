(function () {
  "use strict";
  function bind() {
    if (!window.electronAPI || !window.electronAPI.requestLogs) return;
    var hook = window.electronAPI.requestLogs.onNewLog;
    if (typeof hook !== "function") return;
    hook(function () {
      try {
        window.dispatchEvent(new CustomEvent("dsd-stats-changed"));
      } catch (_) {}
    });
  }
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", bind);
  } else {
    bind();
  }
})();
