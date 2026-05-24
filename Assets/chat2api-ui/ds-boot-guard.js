(function () {
  "use strict";
  if (window.__dsBootGuard) return;
  window.__dsBootGuard = true;

  function showFatal(msg) {
    if (document.getElementById("ds-boot-fatal")) return;
    var box = document.createElement("div");
    box.id = "ds-boot-fatal";
    box.style.cssText =
      "position:fixed;inset:48px 16px 16px;z-index:100000;padding:16px;" +
      "background:#fff;border:1px solid #fecaca;border-radius:12px;color:#991b1b;" +
      "font:13px/1.5 system-ui,sans-serif;overflow:auto;box-shadow:0 8px 24px rgba(0,0,0,.08);";
    box.innerHTML =
      "<strong>API 管理界面未能启动</strong><p style='margin:8px 0 0'></p>" +
      "<pre style='white-space:pre-wrap;margin:8px 0 0;color:#374151'></pre>";
    box.querySelector("p").textContent = msg;
    document.body.appendChild(box);
  }

  window.addEventListener(
    "error",
    function (e) {
      var file = e.filename || "";
      if (file.indexOf("index-") !== -1 || file.indexOf("assets/") !== -1) {
        showFatal((e.message || "脚本错误") + "\n" + file);
      }
    },
    true
  );

  window.addEventListener("unhandledrejection", function (e) {
    var reason = e.reason && (e.reason.message || String(e.reason));
    if (reason) showFatal("Promise 拒绝: " + reason);
  });

  function checkRoot() {
    var root = document.getElementById("root");
    if (!root) {
      showFatal("缺少 #root 容器");
      return;
    }
    if (root.childElementCount > 0) return;
    showFatal(
      "React 未挂载。常见原因：ES Module 在 WebView2 中加载失败。请重新运行 build.ps1 部署，或查看开发者工具 Network 面板中 index-*.js 是否 404。"
    );
  }

  setTimeout(checkRoot, 4500);
})();
