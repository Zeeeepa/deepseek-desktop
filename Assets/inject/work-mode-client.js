/**
 * 工作模式前端客户端（纯视图 + 意图上报，状态由 C# WorkModeCoordinator 推送）。
 */
(function () {
  if (window.DsWorkMode) return;

  var state = null;
  var listeners = [];
  var lastToggleAt = 0;

  function notify() {
    for (var i = 0; i < listeners.length; i++) {
      try {
        listeners[i](state);
      } catch (_) {}
    }
  }

  function normalizeIncoming(msg) {
    if (!msg) return null;
    if (msg.type !== "workModeState") return null;
    return {
      mode: msg.mode === "agent" || msg.mode === "plan" ? msg.mode : "chat",
      surface: msg.surface === "agent" ? "agent" : "chat",
      label: msg.label || (msg.surface === "agent" ? "Agent" : "普通"),
      title: msg.title || "",
      highlight: !!msg.highlight,
      isAgentLike: !!msg.isAgentLike,
    };
  }

  function shouldIgnoreStaleState(next) {
    if (!state || !next) return false;
    if (/ds-agent\.local/i.test(location.hostname)) {
      return state.surface === "agent" && next.surface === "chat";
    }
    if (/chat\.deepseek\.com/i.test(location.hostname)) {
      return state.surface === "chat" && next.surface === "agent";
    }
    return false;
  }

  function applyState(msg) {
    var next = normalizeIncoming(msg);
    if (!next) return;
    if (shouldIgnoreStaleState(next)) return;
    state = next;
    notify();
  }

  function queueOutbound(body) {
    window.__dsPendingOutbound = window.__dsPendingOutbound || [];
    window.__dsPendingOutbound.push(body);
  }

  function flushOutbound() {
    if (!window.chrome || !window.chrome.webview) return;
    var q = window.__dsPendingOutbound;
    if (!Array.isArray(q) || !q.length) return;
    window.__dsPendingOutbound = [];
    for (var i = 0; i < q.length; i++) {
      try {
        window.chrome.webview.postMessage(JSON.stringify(q[i]));
      } catch (_) {}
    }
  }

  function post(type, payload) {
    var body = { type: type };
    if (payload) {
      for (var k in payload) {
        if (Object.prototype.hasOwnProperty.call(payload, k)) body[k] = payload[k];
      }
    }
    if (!window.chrome || !window.chrome.webview) {
      queueOutbound(body);
      return;
    }
    try {
      window.chrome.webview.postMessage(JSON.stringify(body));
      flushOutbound();
    } catch (_) {
      queueOutbound(body);
    }
  }

  function readTokenExtra() {
    var extra = {};
    try {
      if (!/chat\.deepseek\.com/i.test(location.hostname)) return extra;
      var raw = localStorage.getItem("userToken");
      if (!raw) return extra;
      var token = raw;
      try {
        var parsed = JSON.parse(raw);
        if (typeof parsed === "string") token = parsed;
        else if (parsed && typeof parsed === "object" && typeof parsed.value === "string")
          token = parsed.value;
      } catch (_) {}
      if (token) extra.token = token;
    } catch (_) {}
    return extra;
  }

  function activateFloater() {
    var now = Date.now();
    if (now - lastToggleAt < 350) return;
    lastToggleAt = now;
    var extra = readTokenExtra();
    var goingAgent = !state || state.surface !== "agent";
    if (!goingAgent) {
      extra = {};
      try {
        if (window.__dsAgentWebChatSessionId)
          extra.webChatSessionId = window.__dsAgentWebChatSessionId;
      } catch (_) {}
    }
    post("toggleWorkMode", extra);
  }

  function findFloaterTarget(target) {
    if (!target || !target.closest) return null;
    return target.closest("#ds-agent-mode-float, #mode-float, [data-ds-workmode-floater='1']");
  }

  function onFloaterPointer(e) {
    if (e.button !== 0) return;
    if (!findFloaterTarget(e.target)) return;
    e.preventDefault();
    e.stopPropagation();
    try {
      e.stopImmediatePropagation();
    } catch (_) {}
    activateFloater();
  }

  function installFloaterHandlers() {
    if (window.__dsFloaterHandlersInstalled) return;
    window.__dsFloaterHandlersInstalled = true;
    window.addEventListener("pointerdown", onFloaterPointer, true);
    if (document.readyState === "loading") {
      document.addEventListener("DOMContentLoaded", flushOutbound, { once: true });
    } else {
      setTimeout(flushOutbound, 0);
    }
    setInterval(flushOutbound, 2000);
  }

  function markFloater(btn) {
    if (!btn) return;
    btn.setAttribute("data-ds-workmode-floater", "1");
    btn.style.pointerEvents = "auto";
    try {
      btn.style.setProperty("pointer-events", "auto", "important");
    } catch (_) {}
  }

  function flushPending() {
    var q = window.__dsPendingNativeMessages;
    if (!Array.isArray(q) || !q.length) return;
    window.__dsPendingNativeMessages = [];
    for (var i = 0; i < q.length; i++) {
      var m = q[i];
      if (m && m.type === "workModeState") applyState(m);
      else if (typeof window.dsDesktopOnMessage === "function") window.dsDesktopOnMessage(m);
    }
  }

  installFloaterHandlers();

  window.DsWorkMode = {
    getState: function () {
      return state;
    },
    isAgentLike: function () {
      return !!(state && state.isAgentLike);
    },
    onChange: function (fn) {
      if (typeof fn !== "function") return function () {};
      listeners.push(fn);
      if (state) fn(state);
      return function () {
        listeners = listeners.filter(function (f) {
          return f !== fn;
        });
      };
    },
    applyState: applyState,
    activateFloater: activateFloater,
    markFloater: markFloater,
    requestToggle: function (extra) {
      lastToggleAt = Date.now();
      post("toggleWorkMode", extra || {});
    },
    requestSet: function (mode, extra) {
      post("setWorkMode", Object.assign({ mode: mode }, extra || {}));
    },
    flushPending: flushPending,
    flushOutbound: flushOutbound,
  };

  window.__dsApplyWorkModeState = applyState;
})();
