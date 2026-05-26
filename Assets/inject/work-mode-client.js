/**
 * 工作模式前端客户端（纯视图 + 意图上报，状态由 C# WorkModeCoordinator 推送）。
 */
(function () {
  if (window.DsWorkMode) return;

  var state = null;
  var lastRevision = 0;
  var listeners = [];
  var lastToggleAt = 0;
  var switchInFlight = false;
  var switchInFlightTimer = null;

  function armSwitchTimeout() {
    if (switchInFlightTimer) clearTimeout(switchInFlightTimer);
    switchInFlightTimer = setTimeout(function () {
      switchInFlightTimer = null;
      if (!switchInFlight) return;
      switchInFlight = false;
      notify();
      post("requestWorkModeState", {});
    }, 4000);
  }

  function syncFloaterSwitchingClass() {
    var switching = !!switchInFlight;
    var ids = ["ds-agent-mode-float", "mode-float"];
    for (var i = 0; i < ids.length; i++) {
      var el = document.getElementById(ids[i]);
      if (!el) continue;
      el.classList.toggle("ds-switching", switching);
    }
  }

  function notify() {
    syncFloaterSwitchingClass();
    for (var i = 0; i < listeners.length; i++) {
      try {
        listeners[i](state);
      } catch (_) {}
    }
  }

  function normalizeIncoming(msg) {
    if (!msg) return null;
    if (msg.type !== "workModeState") return null;
    var rev = typeof msg.revision === "number" ? msg.revision : 0;
    return {
      mode: msg.mode === "agent" || msg.mode === "plan" ? msg.mode : "chat",
      surface: msg.surface === "agent" ? "agent" : "chat",
      label: msg.label || (msg.surface === "agent" ? "Agent" : "普通"),
      title: msg.title || "",
      highlight: !!msg.highlight,
      isAgentLike: !!msg.isAgentLike,
      revision: rev,
    };
  }

  function applyState(msg) {
    var next = normalizeIncoming(msg);
    if (!next) return;
    if (next.revision > 0 && next.revision < lastRevision) return;
    if (next.revision > 0) lastRevision = next.revision;
    state = next;
    switchInFlight = false;
    if (switchInFlightTimer) {
      clearTimeout(switchInFlightTimer);
      switchInFlightTimer = null;
    }
    notify();
  }

  function optimisticToggle() {
    var goingAgent = !state || state.surface !== "agent";
    var nextSurface = goingAgent ? "agent" : "chat";
    state = {
      mode: goingAgent ? "agent" : "chat",
      surface: nextSurface,
      label: goingAgent ? "Agent" : "普通",
      title: goingAgent
        ? "当前为 Agent 模式，点击切换到普通对话"
        : "当前为普通对话，点击切换到 Agent",
      highlight: nextSurface === "agent",
      isAgentLike: goingAgent,
      revision: lastRevision,
    };
    switchInFlight = true;
    armSwitchTimeout();
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
    var extra = { skipNavigate: true };
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
    if (switchInFlight) return;
    var now = Date.now();
    if (now - lastToggleAt < 350) return;
    lastToggleAt = now;
    optimisticToggle();
    var extra = readTokenExtra();
    var goingAgent = state && state.surface === "agent";
    if (!goingAgent) {
      extra = { skipNavigate: true };
      try {
        if (window.__dsAgentWebChatSessionId)
          extra.webChatSessionId = window.__dsAgentWebChatSessionId;
      } catch (_) {}
    }
    // 使用 setWorkMode 携带目标 mode，避免与原生 isAgentVisible 不同步时 toggle 反向。
    post("setWorkMode", Object.assign({ mode: state.mode }, extra));
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
      if (switchInFlight) return;
      lastToggleAt = Date.now();
      optimisticToggle();
      var body = Object.assign({ mode: state.mode, skipNavigate: true }, extra || {});
      post("setWorkMode", body);
    },
    requestSet: function (mode, extra) {
      post("setWorkMode", Object.assign({ mode: mode }, extra || {}));
    },
    flushPending: flushPending,
    flushOutbound: flushOutbound,
  };

  window.__dsApplyWorkModeState = applyState;
})();
