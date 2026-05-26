(function () {
  "use strict";
  if (window.__dsI18nZh) return;
  window.__dsI18nZh = true;

  var LOCALE = "zh-CN";

  function fixStoredLocale() {
    try {
      localStorage.setItem("i18nextLng", LOCALE);
      var raw = localStorage.getItem("dsd-api-settings");
      if (raw) {
        var data = JSON.parse(raw);
        if (data && data.state) {
          data.state.language = LOCALE;
          localStorage.setItem("dsd-api-settings", JSON.stringify(data));
        }
      }
    } catch (_) {}
    document.documentElement.lang = LOCALE;
  }

  var REPLACEMENTS = [
    ["Multi-platform AI Service", "多厂商 AI API 服务"],
    ["DeepSeek API Management", "API 管理"],
    ["DeepSeek API 管理", "API 管理"],
    ["DeepSeek API", "API 管理"],
    ["DSD API XML", "API 管理 XML"],
    ["DSD API", "API 管理"],
    ["Provider Management", "供应商管理"],
    ["Proxy Settings", "代理设置"],
    ["API Keys", "API 密钥"],
    ["Session Management", "会话管理"],
    ["General Settings", "通用设置"],
    ["Appearance", "外观"],
    ["Quick Actions", "快捷操作"],
    ["Add Provider", "添加供应商"],
    ["Edit Provider", "编辑供应商"],
    ["Delete Provider", "删除供应商"],
    ["No providers found", "未找到供应商"],
    ["Save Changes", "保存更改"],
    ["Loading...", "加载中..."],
    ["Application Error", "应用程序错误"],
    ["Component Stack", "组件堆栈"],
    ["Reload", "重新加载"],
    ["Service Running", "服务运行中"],
    ["Service Stopped", "服务已停止"],
    ["API Endpoint", "API 端点"],
    ["Open Dashboard", "打开主界面"],
    ["No accounts", "暂无账户"],
    ["Built-in Providers", "内置供应商"],
    ["Custom Providers", "自定义供应商"],
    ["Search providers", "搜索供应商"],
    ["Check for Updates", "检查更新"],
    ["Check for updates", "检查更新"],
    ["Application Updates", "应用更新"],
    ["Current Version", "当前版本"],
    ["GitHub Repository", "GitHub 仓库"],
    ["Documentation", "文档"],
    ["Report Issue", "反馈问题"],
    ["Switch to English", "切换到英文"],
    ["Switch to Chinese", "切换到中文"],
    ["Dashboard", "仪表盘"],
    ["Providers", "供应商"],
    ["Models", "模型管理"],
    ["Logs", "日志"],
    ["Settings", "设置"],
    ["About", "关于"],
    ["Session", "会话管理"],
    ["Save", "保存"],
    ["Cancel", "取消"],
    ["Delete", "删除"],
    ["Edit", "编辑"],
    ["Add", "添加"],
    ["Close", "关闭"],
    ["Confirm", "确认"],
    ["Success", "成功"],
    ["Error", "错误"],
    ["Warning", "警告"],
    ["Refresh", "刷新"],
    ["Search", "搜索"],
    ["Filter", "筛选"],
    ["Copy", "复制"],
    ["Copied", "已复制"],
    ["Enabled", "已启用"],
    ["Disabled", "已禁用"],
    ["Status", "状态"],
    ["Actions", "操作"],
    ["Details", "详情"],
    ["Back", "返回"],
    ["Next", "下一步"],
    ["Previous", "上一步"],
    ["Submit", "提交"],
    ["Reset", "重置"],
    ["Optional", "可选"],
    ["Running", "运行中"],
    ["Stopped", "已停止"],
    ["Start Proxy", "启动代理"],
    ["Stop Proxy", "停止代理"],
    ["Start", "启动"],
    ["Stop", "停止"],
    ["Quit", "退出"],
    ["Active", "在线"],
    ["Inactive", "离线"],
    ["Online", "在线"],
    ["Offline", "离线"],
    ["Unknown", "未知"],
    ["Checking", "检测中"],
    ["Light", "浅色"],
    ["Dark", "深色"],
    ["System", "跟随系统"],
    ["Debug", "调试"],
    ["Info", "信息"],
    ["Warn", "警告"],
    ["Original:", "原始："],
    ["Masked:", "脱敏："],
    ["providers.loginFailed", "登录失败"],
    ["providers.validateFailed", "验证失败"]
  ];

  REPLACEMENTS.sort(function (a, b) {
    return b[0].length - a[0].length;
  });

  var SKIP_TAGS = { SCRIPT: 1, STYLE: 1, NOSCRIPT: 1 };

  function replaceText(value) {
    if (!value) return value;
    if (value === "providers.loginFailed") return "登录失败";
    if (value === "providers.validateFailed") return "验证失败";
    if (!/[A-Za-z]/.test(value)) return value;
    var next = value;
    for (var i = 0; i < REPLACEMENTS.length; i++) {
      var from = REPLACEMENTS[i][0];
      var to = REPLACEMENTS[i][1];
      if (next.indexOf(from) >= 0) next = next.split(from).join(to);
    }
    return next;
  }

  function walk(node) {
    if (!node) return;
    if (node.nodeType === 3) {
      var updated = replaceText(node.nodeValue);
      if (updated !== node.nodeValue) node.nodeValue = updated;
      return;
    }
    if (node.nodeType !== 1 || SKIP_TAGS[node.tagName]) return;
    if (node.getAttribute && node.getAttribute("data-ds-i18n") === "1") return;
    var attrs = ["title", "placeholder", "aria-label"];
    for (var a = 0; a < attrs.length; a++) {
      var name = attrs[a];
      var val = node.getAttribute(name);
      if (!val) continue;
      var patched = replaceText(val);
      if (patched !== val) node.setAttribute(name, patched);
    }
    var child = node.firstChild;
    while (child) {
      walk(child);
      child = child.nextSibling;
    }
  }

  function applyDomLocale() {
    fixStoredLocale();
    walk(document.body);
  }

  function schedule() {
    applyDomLocale();
    var left = 60;
    function tick() {
      applyDomLocale();
      if (--left > 0) setTimeout(tick, 400);
    }
    tick();
  }

  fixStoredLocale();

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", schedule);
  } else {
    schedule();
  }

  window.addEventListener("load", applyDomLocale);

  function startObserver() {
    var root = document.getElementById("root");
    if (!root || root.__dsI18nObs) return;
    root.__dsI18nObs = true;
    new MutationObserver(function () {
      applyDomLocale();
    }).observe(root, { childList: true, subtree: true, characterData: true });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", startObserver);
  } else {
    startObserver();
  }
})();
