(function () {
  "use strict";

  function post(type, payload) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(JSON.stringify({ type, ...(payload || {}) }));
    }
  }

  const $ = (id) => document.getElementById(id);

  const pendingRequests = new Map();
  let reqSeq = 0;

  const state = {
    loggedIn: false,
    authResolved: false,
    running: false,
    stopping: false,
    sessionMetas: [],
    activeSession: null,
    activeSessionId: null,
    currentRun: null,
    files: [],
    strategy: "execute",
    storageBytes: 0,
    storageCount: 0,
    selectMode: false,
    selectedIds: new Set(),
    deepThink: false,
    smartSearch: false,
    embeddedPanel: null,
    playbookId: null,
    skillId: null,
  };

  function chatNavigateExtra() {
    const id = state.activeSession?.webChatSessionId;
    return id ? { webChatSessionId: id } : {};
  }

  function rememberWebChatSessionId(id) {
    if (!id) return;
    if (state.activeSession) state.activeSession.webChatSessionId = id;
    window.__dsAgentWebChatSessionId = id;
  }

  const EMBED_URLS = {
    settings: "https://ds-agent.local/settings-embed.html",
    automations: "https://ds-agent.local/automations-embed.html",
    // 与 Agent 页同源，避免 WebView2 在 iframe 中加载独立虚拟域 ds-chat2api.local 白屏
    chat2api: "https://ds-agent.local/chat2api/index.html",
  };

  const EMBED_TITLES = {
    settings: "设置",
    automations: "Automations",
    chat2api: "API 管理",
  };

  const AUTOMATIONS_INTRO_KEY = "ds-agent-automations-intro-dismissed";

  function uid() {
    return "s_" + Date.now().toString(36) + Math.random().toString(36).slice(2, 7);
  }

  function postAsync(type, payload) {
    return new Promise((resolve, reject) => {
      const reqId = "r" + ++reqSeq;
      const timer = setTimeout(() => {
        pendingRequests.delete(reqId);
        reject(new Error("存储请求超时"));
      }, 15000);
      pendingRequests.set(reqId, (msg) => {
        clearTimeout(timer);
        resolve(msg);
      });
      post(type, { ...(payload || {}), reqId });
    });
  }

  const LAST_SESSION_KEY = "ds-agent-last-session";

  function handlePendingReply(msg) {
    if (!msg?.reqId || !pendingRequests.has(msg.reqId)) return false;
    const payload =
      msg.type === "agentSession" && msg.payload && typeof msg.payload === "object"
        ? msg.payload
        : msg.type === "agentPlaybooks" ||
            msg.type === "agentCheckpoint" ||
            msg.type === "agentSkills" ||
            msg.type === "agentHarnessReload" ||
            msg.type === "agentWorkspace" ||
            msg.type === "agentAutomation"
          ? msg
          : msg;
    pendingRequests.get(msg.reqId)(payload);
    pendingRequests.delete(msg.reqId);
    return true;
  }

  function formatStorageSize(bytes) {
    if (bytes < 1024) return bytes + " B";
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + " KB";
    if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(2) + " MB";
    return (bytes / (1024 * 1024 * 1024)).toFixed(2) + " GB";
  }

  function updateStorageStats() {
    const el = $("storage-stats");
    if (el) {
      el.textContent =
        state.storageCount > 0
          ? state.storageCount + " 个对话"
          : "暂无历史对话";
    }
  }

  function sessionGroupLabel(meta) {
    if (meta?.pinned) return "置顶";
    const ts = meta?.updatedAt || meta?.createdAt || Date.now();
    const d = new Date(ts);
    const now = new Date();
    const startToday = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
    const startYesterday = startToday - 86400000;
    const t = d.getTime();
    if (t >= startToday) return "今天";
    if (t >= startYesterday) return "昨天";
    if (t >= now.getTime() - 7 * 86400000) return "7 天内";
    if (t >= now.getTime() - 30 * 86400000) return "30 天内";
    return "更早";
  }

  function rememberLastSession(id) {
    if (!id) return;
    try {
      localStorage.setItem(LAST_SESSION_KEY, id);
    } catch (_) {}
  }

  function readLastSessionId() {
    try {
      return localStorage.getItem(LAST_SESSION_KEY) || "";
    } catch (_) {
      return "";
    }
  }

  async function migrateLegacyLocalStorage() {
    try {
      localStorage.removeItem("ds-agent-sessions");
    } catch (_) {}
  }

  async function refreshSessionList() {
    try {
      const res = await postAsync("agentSessionList", {});
      state.sessionMetas = Array.isArray(res.metas) ? res.metas : [];
    } catch (_) {
      state.sessionMetas = [];
    }
    state.storageCount = state.sessionMetas.length;
    updateStorageStats();
    renderSessions();
  }

  function getActiveSessionForSave() {
    if (!state.activeSessionId || !state.activeSession) return null;
    const meta = state.sessionMetas.find((m) => m.id === state.activeSessionId);
    return {
      id: state.activeSessionId,
      title: state.activeSession.title || "新对话",
      createdAt: state.activeSession.createdAt || Date.now(),
      updatedAt: Date.now(),
      pinned: !!(meta?.pinned || state.activeSession.pinned),
      messages: state.activeSession.messages || [],
      harnessState:
        state.activeSession.harnessState || state.activeSession.tuiThreadId || null,
      tuiThreadId:
        state.activeSession.tuiThreadId || state.activeSession.harnessState || null,
      webChatSessionId: state.activeSession.webChatSessionId || null,
    };
  }

  async function persistActiveSession() {
    if (!state.activeSession || !state.activeSessionId) return;
    const messages = state.activeSession.messages || [];
    if (!messages.length) {
      try {
        await postAsync("agentSessionDelete", { id: state.activeSessionId });
      } catch (_) {}
      state.sessionMetas = state.sessionMetas.filter((m) => m.id !== state.activeSessionId);
      renderSessions();
      return;
    }

    const title = state.activeSession.title || "新对话";
    const updatedAt = Date.now();
    state.activeSession.updatedAt = updatedAt;
    try {
      await postAsync("agentSessionSave", { session: getActiveSessionForSave() });
      rememberLastSession(state.activeSessionId);
      await refreshSessionList();
    } catch (_) {
      const meta = state.sessionMetas.find((m) => m.id === state.activeSessionId);
      if (meta) {
        meta.title = title;
        meta.updatedAt = updatedAt;
      } else {
        state.sessionMetas.unshift({
          id: state.activeSessionId,
          title,
          updatedAt,
          createdAt: state.activeSession.createdAt || updatedAt,
          pinned: false,
        });
      }
      renderSessions();
    }
  }

  const ICON_MORE =
    '<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><circle cx="5" cy="12" r="1.6"/><circle cx="12" cy="12" r="1.6"/><circle cx="19" cy="12" r="1.6"/></svg>';
  const ICON_RENAME =
    '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4 12.5-12.5z"/></svg>';
  const ICON_PIN =
    '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M12 17v5"/><path d="M9 3h6l1 7h4l-5 6v-4H9v4L4 10h4z"/></svg>';
  const ICON_SHARE =
    '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M4 12v7a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-7"/><path d="M16 6l-4-4-4 4"/><path d="M12 2v14"/></svg>';
  const ICON_DELETE =
    '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M3 6h18"/><path d="M8 6V4h8v2"/><path d="M19 6l-1 14H6L5 6"/><path d="M10 11v6M14 11v6"/></svg>';

  let openSessionMenuId = null;
  let sessionMenuEl = null;

  function closeSessionMenu() {
    if (sessionMenuEl) {
      sessionMenuEl.remove();
      sessionMenuEl = null;
    }
    openSessionMenuId = null;
    document.querySelectorAll(".ds-session-row.ds-menu-open").forEach((el) => {
      el.classList.remove("ds-menu-open");
    });
  }

  function showToast(text) {
    const existing = document.querySelector(".ds-toast");
    if (existing) existing.remove();
    const toast = document.createElement("div");
    toast.className = "ds-toast";
    toast.textContent = text;
    document.body.appendChild(toast);
    setTimeout(() => toast.remove(), 1800);
  }

  function buildSessionShareText(session) {
    const lines = [(session.title || "新对话") + "\n"];
    (session.messages || []).forEach((m) => {
      if (m.role === "user" && m.text) lines.push("用户：\n" + m.text + "\n");
      if (m.role === "assistant") {
        const answer = (m.answer || "").trim();
        if (answer) lines.push("DeepSeek：\n" + answer + "\n");
      }
    });
    return lines.join("\n").trim();
  }

  async function shareSession(id) {
    closeSessionMenu();
    let session = id === state.activeSessionId ? getActiveSessionForSave() : null;
    if (!session) {
      try {
        const res = await postAsync("agentSessionLoad", { id });
        session = res.session;
      } catch (_) {
        session = null;
      }
    }
    if (!session) {
      alert("无法读取对话内容");
      return;
    }
    const text = buildSessionShareText(session);
    if (!text) {
      showToast("暂无可分享内容");
      return;
    }
    try {
      if (navigator.clipboard?.writeText) await navigator.clipboard.writeText(text);
      else throw new Error("clipboard unavailable");
      showToast("对话内容已复制");
    } catch (_) {
      alert("分享失败，请手动复制对话内容。");
    }
  }

  async function renameSession(id) {
    closeSessionMenu();
    const meta = state.sessionMetas.find((s) => s.id === id);
    const current = meta?.title || "新对话";
    const next = prompt("重命名对话", current);
    if (next === null) return;
    const title = next.trim() || "新对话";
    try {
      await postAsync("agentSessionRename", { id, title });
      if (state.activeSessionId === id && state.activeSession) state.activeSession.title = title;
      await refreshSessionList();
    } catch (_) {
      alert("重命名失败");
    }
  }

  async function togglePinSession(id) {
    closeSessionMenu();
    const meta = state.sessionMetas.find((s) => s.id === id);
    const pinned = !meta?.pinned;
    try {
      await postAsync("agentSessionPin", { id, pinned });
      if (state.activeSessionId === id && state.activeSession) state.activeSession.pinned = pinned;
      await refreshSessionList();
    } catch (_) {
      alert("置顶操作失败");
    }
  }

  async function deleteSession(id) {
    closeSessionMenu();
    const meta = state.sessionMetas.find((s) => s.id === id);
    const title = meta?.title || "该对话";
    if (!confirm("确定删除「" + title + "」？")) return;
    try {
      await postAsync("agentSessionDelete", { id });
    } catch (_) {
      alert("删除失败");
      return;
    }
    if (state.activeSessionId === id) {
      state.activeSessionId = null;
      state.activeSession = null;
      await refreshSessionList();
      const next = state.sessionMetas[0];
      if (next) await loadSession(next.id);
      else prepareEmptyChat();
      return;
    }
    await refreshSessionList();
  }

  function openSessionMenu(id, anchor) {
    if (openSessionMenuId === id) {
      closeSessionMenu();
      return;
    }
    closeSessionMenu();
    openSessionMenuId = id;
    const row = anchor.closest(".ds-session-row");
    row?.classList.add("ds-menu-open");

    const meta = state.sessionMetas.find((s) => s.id === id);
    const menu = document.createElement("div");
    menu.className = "ds-session-menu";
    menu.setAttribute("role", "menu");

    function addItem(label, icon, onClick, danger) {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "ds-session-menu-item" + (danger ? " ds-danger" : "");
      btn.setAttribute("role", "menuitem");
      btn.innerHTML = icon + "<span>" + label + "</span>";
      btn.onclick = (e) => {
        e.stopPropagation();
        onClick();
      };
      menu.appendChild(btn);
    }

    addItem("重命名", ICON_RENAME, () => renameSession(id));
    addItem(meta?.pinned ? "取消置顶" : "置顶", ICON_PIN, () => togglePinSession(id));
    addItem("分享", ICON_SHARE, () => shareSession(id));
    addItem("删除", ICON_DELETE, () => deleteSession(id), true);

    document.body.appendChild(menu);
    sessionMenuEl = menu;
    const rect = anchor.getBoundingClientRect();
    const width = menu.offsetWidth || 140;
    const left = Math.min(Math.max(8, rect.right - width + 8), window.innerWidth - width - 8);
    menu.style.top = rect.bottom + 6 + "px";
    menu.style.left = left + "px";
  }

  document.addEventListener("click", () => closeSessionMenu());
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") closeSessionMenu();
  });
  window.addEventListener("resize", () => closeSessionMenu());

  function renderSessions() {
    const list = $("session-list");
    const empty = $("session-empty");
    if (!list) return;
    list.innerHTML = "";

    const metas = [...state.sessionMetas].sort((a, b) => {
      if (!!a.pinned !== !!b.pinned) return a.pinned ? -1 : 1;
      return (b.updatedAt || 0) - (a.updatedAt || 0);
    });

    if (empty) empty.hidden = metas.length > 0;
    if (!metas.length) return;

    const groups = new Map();
    metas.forEach((s) => {
      const label = sessionGroupLabel(s);
      if (!groups.has(label)) groups.set(label, []);
      groups.get(label).push(s);
    });

    const order = ["置顶", "今天", "昨天", "7 天内", "30 天内", "更早"];
    order.forEach((label) => {
      const items = groups.get(label);
      if (!items?.length) return;

      const gl = document.createElement("div");
      gl.className = "ds-session-group-label";
      gl.textContent = label;
      list.appendChild(gl);

      items.forEach((s) => {
        const row = document.createElement("div");
        row.className =
          "ds-session-row" +
          (s.id === state.activeSessionId ? " ds-active-row" : "") +
          (state.selectMode ? " ds-select-mode" : "");

        if (state.selectMode) {
          const cb = document.createElement("input");
          cb.type = "checkbox";
          cb.className = "ds-session-check";
          cb.checked = state.selectedIds.has(s.id);
          cb.onchange = () => {
            if (cb.checked) state.selectedIds.add(s.id);
            else state.selectedIds.delete(s.id);
          };
          row.appendChild(cb);
        }

        const btn = document.createElement("button");
        btn.type = "button";
        btn.className =
          "ds-session-item" + (s.id === state.activeSessionId ? " ds-active" : "");
        btn.textContent = s.title || "新对话";
        btn.title = s.title || "";
        btn.onclick = () => {
          if (state.selectMode) {
            const cb = row.querySelector(".ds-session-check");
            if (cb) {
              cb.checked = !cb.checked;
              if (cb.checked) state.selectedIds.add(s.id);
              else state.selectedIds.delete(s.id);
            }
          } else {
            loadSession(s.id);
          }
        };

        const more = document.createElement("button");
        more.type = "button";
        more.className = "ds-session-more";
        more.setAttribute("aria-label", "对话操作");
        more.innerHTML = ICON_MORE;
        more.onclick = (e) => {
          e.stopPropagation();
          openSessionMenu(s.id, more);
        };

        row.append(btn, more);
        list.appendChild(row);
      });
    });
  }

  function setSelectMode(on) {
    state.selectMode = !!on;
    state.selectedIds.clear();
    $("storage-bar")?.classList.toggle("ds-hidden", state.selectMode);
    $("manage-bar")?.classList.toggle("ds-hidden", !state.selectMode);
    $("btn-manage-sessions")?.classList.toggle("ds-hidden", state.selectMode);
    renderSessions();
  }

  function scrollToBottom() {
    const el = $("chat-scroll");
    if (el) el.scrollTop = el.scrollHeight;
  }

  function renderAssistantAnswer(el, text) {
    if (!el) return;
    if (window.DsMessageRender) {
      window.DsMessageRender.apply(el, text || "");
    } else {
      el.textContent = text || "";
    }
  }

  function streamAssistantAnswer(el, chunk, append) {
    if (!el) return;
    el._dsStreamText = append ? (el._dsStreamText || "") + (chunk || "") : chunk || "";
    if (window.DsMessageRender) {
      window.DsMessageRender.scheduleApply(el, el._dsStreamText, append ? 150 : 0);
    } else {
      el.textContent = el._dsStreamText;
    }
  }

  function renderThinkProse(el, text) {
    if (!el || !text) return;
    if (window.DsMessageRender) {
      window.DsMessageRender.apply(el, text);
    } else {
      el.textContent = text;
    }
  }

  function hideEmptyState() {
    $("empty-state")?.classList.add("ds-hidden");
  }

  function showEmptyState() {
    $("empty-state")?.classList.remove("ds-hidden");
  }

  const ICON_SEND =
    '<svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M3.4 20.6l17.6-8.2c.8-.4.8-1.5 0-1.9L3.4 2.3c-.8-.4-1.6.3-1.4 1.2l2.1 7.3c.1.4.5.7.9.7h6.2c.6 0 1 .4 1 1s-.4 1-1 1H5c-.4 0-.8.3-.9.7l-2.1 7.3c-.2.9.6 1.6 1.4 1.2z"/></svg>';
  const ICON_STOP =
    '<svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><rect x="8" y="8" width="8" height="8" rx="1"/></svg>';

  function setSendButtonMode(mode) {
    const send = $("btn-send");
    if (!send) return;
    const stopping = mode === "stopping";
    const running = mode === "running" || stopping;
    send.classList.toggle("ds-stop-mode", running);
    send.innerHTML = running ? ICON_STOP : ICON_SEND;
    if (running) {
      send.disabled = !!stopping;
      send.setAttribute("aria-label", stopping ? "正在停止" : "停止");
      send.title = stopping ? "正在停止…" : "停止生成";
    } else {
      send.disabled = false;
      send.setAttribute("aria-label", "发送");
      send.title = "";
    }
  }

  function updateComposerState() {
    const input = $("chat-input");
    const send = $("btn-send");
    if (state.running) {
      setSendButtonMode(state.stopping ? "stopping" : "running");
      if (input) input.disabled = false;
      return;
    }
    setSendButtonMode("send");
    const hasText = input && input.value.trim().length > 0;
    const hasFiles = state.files.length > 0;
    const canSend = state.loggedIn && (hasText || hasFiles);
    if (send) send.disabled = !canSend;
    if (input) input.disabled = false;
  }

  function renderFileChips() {
    const box = $("file-chips");
    if (!box) return;
    if (!state.files.length) {
      box.hidden = true;
      box.innerHTML = "";
      return;
    }
    box.hidden = false;
    box.innerHTML = "";
    state.files.forEach((f, idx) => {
      const chip = document.createElement("span");
      chip.className = "ds-file-chip";
      const name = document.createElement("span");
      name.textContent = f.name;
      const rm = document.createElement("button");
      rm.type = "button";
      rm.setAttribute("aria-label", "移除附件");
      rm.textContent = "×";
      rm.onclick = () => {
        state.files.splice(idx, 1);
        renderFileChips();
        updateComposerState();
      };
      chip.append(name, rm);
      box.appendChild(chip);
    });
  }

  async function handleFiles(fileList) {
    if (!fileList?.length) return;
    if (!window.dsDesktopBridge?.uploadUserFile) {
      alert("文件上传不可用，请重启应用后重试。");
      return;
    }
    for (const file of fileList) {
      try {
        const id = await window.dsDesktopBridge.uploadUserFile(file);
        state.files.push({ id, name: file.name });
      } catch (e) {
        alert("上传失败: " + (e.message || e));
      }
    }
    renderFileChips();
    updateComposerState();
  }

  function setLoggedIn(online) {
    state.loggedIn = !!online;
    state.authResolved = true;
    const dot = $("login-dot");
    const label = $("login-label");
    const banner = $("login-banner");
    if (dot) dot.classList.toggle("ds-online", state.loggedIn);
    if (label) label.textContent = state.loggedIn ? "已登录" : "未登录";
    if (banner) {
      const hide = state.loggedIn;
      banner.hidden = hide;
      banner.classList.toggle("ds-hidden", hide);
      banner.style.display = hide ? "none" : "flex";
      banner.setAttribute("aria-hidden", hide ? "true" : "false");
    }
    updateComposerState();
  }

  window.dsAgentApplyAuth = function (online) {
    setLoggedIn(!!online);
  };

  function syncModeFloater(st) {
    const btn = $("mode-float");
    const label = $("mode-float-label");
    if (!btn || !label || !st) return;
    if (st.surface === "chat") return;
    label.textContent = st.label || "Agent";
    btn.classList.toggle("ds-on", !!st.highlight);
    btn.title = st.title || "";
  }

  function bindWorkModeClient() {
    if (!window.DsWorkMode || window.__dsWorkModeClientBound) return;
    window.__dsWorkModeClientBound = true;
    window.DsWorkMode.onChange(syncModeFloater);
    window.DsWorkMode.flushPending();
    post("requestWorkModeState", {});
  }

  function flushPendingNativeMessages() {
    const q = window.__dsPendingNativeMessages;
    if (!Array.isArray(q) || !q.length) return;
    window.__dsPendingNativeMessages = [];
    q.forEach((m) => {
      try {
        window.dsDesktopOnMessage && window.dsDesktopOnMessage(m);
      } catch (_) {}
    });
  }

  function truncate(s, max) {
    if (!s) return "";
    return s.length <= max ? s : s.slice(0, max) + "…";
  }

  function escapeHtml(s) {
    return (s || "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;");
  }

  function formatActivityHtml(target, detail) {
    let html = escapeHtml(target || "");
    html = html.replace(/`([^`]+)`/g, '<code class="ds-code">$1</code>');
    if (detail && detail !== "terminal") {
      html += ' <span class="ds-activity-meta">' + escapeHtml(detail) + "</span>";
    }
    return html;
  }

  function isInternalLog(text) {
    const t = (text || "").trim();
    if (!t) return true;
    return (
      /^DeepSeek-TUI/i.test(t) ||
      /^工作区:/.test(t) ||
      /^模式:/.test(t) ||
      /^线程:/.test(t) ||
      /^回合:/.test(t) ||
      /Runtime API/i.test(t) ||
      /Chat2API/i.test(t) ||
      /MCP \/ Skills/i.test(t) ||
      /^正努力工作/.test(t) ||
      /^正在整理回复/.test(t) ||
      /^已停止/.test(t) ||
      /^Log /i.test(t)
    );
  }

  function parseAgentLog(text) {
    const t = (text || "").trim();
    if (!t || isInternalLog(t)) return { kind: "skip" };
    let m;
    m = t.match(/^工具:\s*(.+)$/);
    if (m) return { kind: "activity", verb: "Ran", target: "`" + m[1].trim() + "`" };
    m = t.match(/^待审批:\s*(.+)$/);
    if (m) return { kind: "activity", verb: "Awaiting approval", target: m[1].trim() };
    if (/^(已允许|已拒绝)$/.test(t))
      return { kind: "activity", verb: t === "已允许" ? "Approved" : "Denied", target: "" };
    m = t.match(/^思考:\s*(.+)$/s);
    if (m) return { kind: "thinking", text: m[1].trim() };
    m = t.match(/^Thought:\s*(.+)$/s);
    if (m) return { kind: "thinking", text: m[1].trim() };
    if (/^Final Answer:/i.test(t)) return { kind: "skip" };
    if (/^错误:/.test(t)) return { kind: "activity", verb: "Error", target: t, error: true };
    return { kind: "skip" };
  }

  function setThinkPhase(run, phase) {
    if (!run?.phaseEl) return;
    run.phase = phase;
    const labels = {
      exploring: "Explore",
      explore: "Explore",
      orient: "Orient",
      blueprint: "Blueprint",
      execute: "Execute",
      verify: "Verify",
      writing: "Writing",
      thinking: "Thinking",
      planning: "Blueprint",
      stopping: "Stopping",
    };
    run.phaseEl.textContent = labels[phase] || phase || "Working";
  }

  function applyHarnessPhaseFromState(harnessStateRaw) {
    if (!state.currentRun || !harnessStateRaw) return;
    if (typeof harnessStateRaw !== "string" || !harnessStateRaw.startsWith("{")) return;
    try {
      const hs = JSON.parse(harnessStateRaw);
      const phase = hs.phase || hs.Phase;
      if (phase) setThinkPhase(state.currentRun, String(phase).toLowerCase());
    } catch (_) {}
  }

  function updateThinkTitle(run) {
    if (!run?.phaseEl && !run?.titleEl) return;
    const sec = Math.max(1, Math.floor((Date.now() - run.thinkStartTime) / 1000));
    if (run.subtitleEl) {
      if (run.thinkDone) {
        run.phaseEl.textContent = run.hasThinking ? "已思考" : "已处理";
        run.subtitleEl.textContent = `(用时 ${sec} 秒)`;
        run.subtitleEl.hidden = false;
      } else {
        run.phaseEl.textContent = run.hasThinking ? "思考中" : run.phaseEl?.textContent || "处理中";
        run.subtitleEl.hidden = true;
      }
    } else if (run.titleEl) {
      run.titleEl.textContent = run.thinkDone
        ? (run.hasThinking ? `已思考 (用时 ${sec} 秒)` : `已处理 (用时 ${sec} 秒)`)
        : run.phaseEl?.textContent || "Explore";
    }
  }

  function pushThinkRecord(run, entry) {
    if (!run.thinkRecords) run.thinkRecords = [];
    run.thinkRecords.push(entry);
    if (run.thinkRecords.length > 200) run.thinkRecords.shift();
  }

  function renderActivityLine(run, activity) {
    const list = run.activitiesEl || run.stepsEl;
    if (!list) return null;
    const row = document.createElement("div");
    row.className = "ds-activity" + (activity.error ? " ds-activity-error" : "");
    const verb = document.createElement("span");
    verb.className = "ds-activity-verb";
    verb.textContent = activity.verb;
    const detail = document.createElement("span");
    detail.className = "ds-activity-detail";
    detail.innerHTML = formatActivityHtml(activity.target, activity.detail);
    row.append(verb, detail);
    list.appendChild(row);
    const items = list.querySelectorAll(".ds-activity");
    items.forEach((el, i) => {
      el.classList.toggle("ds-activity-faded", i < items.length - 12);
    });
    return {
      kind: "activity",
      verb: activity.verb,
      target: activity.target,
      detail: activity.detail,
      error: !!activity.error,
    };
  }

  function applyAgentActivity(run, activity) {
    if (!run || !activity?.verb) return;
    if (/^(Read|Grepped|Listed|Searched|Ran|Edited|Fetched)/i.test(activity.verb)) {
      setThinkPhase(run, "exploring");
    }
    pushThinkRecord(run, renderActivityLine(run, activity));
    updateThinkTitle(run);
    scrollToBottom();
  }

  function sanitizeHarnessEcho(text) {
    if (!text || typeof text !== "string") return text;
    let t = text;
    if (t.includes("<｜User｜>")) {
      const tail = t.split("<｜User｜>").pop();
      if (tail && tail.trim()) return tail.trim();
    }
    if (t.includes("DSD Harness") && t.includes("工作区快照")) {
      const markers = ["<｜User｜>", "【Phase:", "【DSD Harness"];
      for (const m of markers) {
        const i = t.indexOf(m);
        if (i > 0) t = t.slice(i);
      }
      if (t.startsWith("<｜User｜>")) t = t.slice("<｜User｜>".length).trim();
    }
    return t;
  }

  function applyAgentThinking(run, text, append) {
    if (!run || !text) return;
    const cleaned = sanitizeHarnessEcho(text);
    if (!cleaned) return;
    run.hasThinking = true;
    setThinkPhase(run, "thinking");
    if (run.thinkingWrap) run.thinkingWrap.hidden = false;
    if (run.proseEl) {
      run.proseEl.textContent = append ? (run.proseEl.textContent || "") + cleaned : cleaned;
    }
    pushThinkRecord(run, { kind: "thinking", text: run.proseEl?.textContent || cleaned });
    updateThinkTitle(run);
    scrollToBottom();
  }

  function applyThinkLog(run, parsed) {
    if (!run || !parsed || parsed.kind === "skip") return;
    if (parsed.kind === "activity") applyAgentActivity(run, parsed);
    else if (parsed.kind === "thinking") applyAgentThinking(run, parsed.text, false);
    else if (parsed.kind === "prose") applyAgentThinking(run, parsed.text, false);
    else if (parsed.kind === "step") {
      applyAgentActivity(run, {
        verb: parsed.verb || "Log",
        target: parsed.target || "",
        error: parsed.error,
      });
    }
    updateThinkTitle(run);
  }

  function appendLogLine(text) {
    const run = state.currentRun;
    if (!run) return;
    applyThinkLog(run, parseAgentLog(text));
  }

  function createThinkBlock() {
    const think = document.createElement("div");
    think.className = "ds-think";
    const header = document.createElement("button");
    header.type = "button";
    header.className = "ds-think-header";
    header.setAttribute("aria-expanded", "true");
    const phase = document.createElement("span");
    phase.className = "ds-think-phase";
    phase.textContent = "Exploring";
    const chevron = document.createElement("span");
    chevron.className = "ds-think-chevron";
    chevron.innerHTML =
      '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 9l6 6 6-6"/></svg>';
    header.append(phase, chevron);

    const subtitle = document.createElement("div");
    subtitle.className = "ds-think-subtitle";
    subtitle.hidden = true;

    const body = document.createElement("div");
    body.className = "ds-think-body";
    const activities = document.createElement("div");
    activities.className = "ds-activity-list";

    const thinkingWrap = document.createElement("div");
    thinkingWrap.className = "ds-thinking-wrap";
    thinkingWrap.hidden = true;
    const thinkingToggle = document.createElement("button");
    thinkingToggle.type = "button";
    thinkingToggle.className = "ds-thinking-toggle";
    thinkingToggle.setAttribute("aria-expanded", "true");
    thinkingToggle.append(document.createTextNode("思考过程 "), (() => {
      const s = document.createElement("span");
      s.className = "ds-thinking-chevron";
      s.textContent = "⌄";
      return s;
    })());
    const prose = document.createElement("div");
    prose.className = "ds-think-prose";
    thinkingWrap.append(thinkingToggle, prose);

    body.append(activities, thinkingWrap);
    think.append(header, subtitle, body);

    header.addEventListener("click", () => {
      const collapsed = think.classList.toggle("ds-collapsed");
      header.setAttribute("aria-expanded", collapsed ? "false" : "true");
    });
    thinkingToggle.addEventListener("click", (e) => {
      e.stopPropagation();
      const collapsed = thinkingWrap.classList.toggle("ds-thinking-collapsed");
      thinkingToggle.setAttribute("aria-expanded", collapsed ? "false" : "true");
    });

    const tick = setInterval(() => {
      if (!think.isConnected || state.currentRun?.thinkEl !== think) {
        clearInterval(tick);
        return;
      }
      updateThinkTitle(state.currentRun);
    }, 1000);

    return {
      thinkEl: think,
      phaseEl: phase,
      subtitleEl: subtitle,
      activitiesEl: activities,
      thinkingWrap,
      thinkingToggle,
      proseEl: prose,
      phase: "exploring",
      hasThinking: false,
      thinkStartTime: Date.now(),
      thinkDone: false,
      thinkRecords: [],
    };
  }

  function finalizeThinkBlock(run) {
    if (!run) return;
    run.thinkDone = true;
    if (run.proseEl?.textContent?.trim()) {
      renderThinkProse(run.proseEl, run.proseEl.textContent);
    }
    updateThinkTitle(run);
    run.thinkEl?.classList.add("ds-think-done");
  }

  function restoreThinkBlock(container, data) {
    if (!data?.records?.length) return null;
    const think = createThinkBlock();
    think.thinkStartTime = Date.now() - (data.durationSec || 1) * 1000;
    think.thinkDone = true;
    container.appendChild(think.thinkEl);
    data.records.forEach((rec) => {
      if (rec.kind === "activity")
        renderActivityLine(think, {
          verb: rec.verb,
          target: rec.target,
          detail: rec.detail,
          error: rec.error,
        });
      else if (rec.kind === "thinking" || rec.kind === "prose") {
        think.hasThinking = true;
        if (think.thinkingWrap) think.thinkingWrap.hidden = false;
        renderThinkProse(think.proseEl, rec.text || "");
      } else if (rec.kind === "step") {
        renderActivityLine(think, { verb: rec.verb, target: rec.target, error: rec.error });
      }
    });
    updateThinkTitle(think);
    think.thinkRecords = data.records.slice();
    return think;
  }

  const ICON_COPY =
    '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><rect x="9" y="9" width="11" height="11" rx="2"/><path d="M5 15V5a2 2 0 0 1 2-2h10"/></svg>';
  const ICON_EDIT =
    '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4 12.5-12.5z"/></svg>';

  async function copyUserMessageText(text, btn) {
    const value = text || "";
    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(value);
      } else {
        const ta = document.createElement("textarea");
        ta.value = value;
        ta.style.cssText = "position:fixed;left:-9999px";
        document.body.appendChild(ta);
        ta.select();
        document.execCommand("copy");
        document.body.removeChild(ta);
      }
      if (btn) {
        const prev = btn.getAttribute("title") || "复制";
        btn.setAttribute("title", "已复制");
        btn.classList.add("ds-copied");
        setTimeout(() => {
          btn.setAttribute("title", prev);
          btn.classList.remove("ds-copied");
        }, 1200);
      }
    } catch (_) {
      alert("复制失败，请手动选择文本复制。");
    }
  }

  function removeMessagesFromDom(startRow) {
    const wrap = $("messages");
    if (!wrap || !startRow) return;
    let el = startRow;
    while (el && el.parentNode === wrap) {
      const next = el.nextElementSibling;
      wrap.removeChild(el);
      el = next;
    }
    if (!wrap.children.length) showEmptyState();
  }

  function beginEditUserMessage(msgIndex, text, row) {
    if (state.running) return;
    const session = ensureSession();
    session.messages = (session.messages || []).slice(0, msgIndex);
    removeMessagesFromDom(row);
    const input = $("chat-input");
    if (input) {
      input.value = text || "";
      resizeInput();
      input.focus();
    }
    if (session.messages.length) hideEmptyState();
    else showEmptyState();
    persistActiveSession().catch(() => {});
    updateComposerState();
  }

  function buildUserMessageRow(text, msgIndex) {
    const row = document.createElement("div");
    row.className = "ds-msg-row ds-user";
    row.dataset.msgIndex = String(msgIndex);

    const col = document.createElement("div");
    col.className = "ds-msg-user-col";

    const bubble = document.createElement("div");
    bubble.className = "ds-msg-bubble";
    bubble.textContent = text;

    const actions = document.createElement("div");
    actions.className = "ds-msg-actions";

    const copyBtn = document.createElement("button");
    copyBtn.type = "button";
    copyBtn.className = "ds-msg-action-btn";
    copyBtn.title = "复制";
    copyBtn.setAttribute("aria-label", "复制");
    copyBtn.innerHTML = ICON_COPY;
    copyBtn.onclick = (e) => {
      e.stopPropagation();
      copyUserMessageText(text, copyBtn);
    };

    const editBtn = document.createElement("button");
    editBtn.type = "button";
    editBtn.className = "ds-msg-action-btn";
    editBtn.title = "编辑";
    editBtn.setAttribute("aria-label", "编辑");
    editBtn.innerHTML = ICON_EDIT;
    editBtn.onclick = (e) => {
      e.stopPropagation();
      beginEditUserMessage(msgIndex, text, row);
    };

    const syncActionState = () => {
      const disabled = state.running;
      editBtn.disabled = disabled;
      editBtn.classList.toggle("ds-disabled", disabled);
    };
    syncActionState();
    row._syncUserActions = syncActionState;

    actions.append(copyBtn, editBtn);
    col.append(bubble, actions);
    row.appendChild(col);
    return row;
  }

  function appendUserMessage(text, msgIndex) {
    hideEmptyState();
    const wrap = $("messages");
    if (!wrap) return null;
    const row = buildUserMessageRow(text, msgIndex);
    wrap.appendChild(row);
    scrollToBottom();
    return { row, bubble: row.querySelector(".ds-msg-bubble") };
  }

  function syncAllUserMessageActions() {
    $("messages")
      ?.querySelectorAll(".ds-msg-row.ds-user")
      .forEach((row) => row._syncUserActions?.());
  }

  function appendMessage(role, text, extra) {
    hideEmptyState();
    const wrap = $("messages");
    if (!wrap) return null;

    if (role === "user") {
      const idx = state.activeSession?.messages?.length ?? 0;
      return appendUserMessage(text, idx);
    }

    const row = document.createElement("div");
    row.className = "ds-msg-row ds-" + role;

    const bubble = document.createElement("div");
    bubble.className = "ds-msg-bubble";

    const think = createThinkBlock();
    const answer = document.createElement("div");
    answer.className = "ds-msg-answer";
    answer._dsStreamText = "";

    bubble.append(think.thinkEl, answer);
    row.appendChild(bubble);
    wrap.appendChild(row);
    scrollToBottom();

    return { row, bubble, answerEl: answer, ...think };
  }

  function prepareEmptyChat() {
    state.files = [];
    renderFileChips();
    const now = Date.now();
    state.activeSessionId = uid();
    state.activeSession = {
      id: state.activeSessionId,
      title: "新对话",
      createdAt: now,
      updatedAt: now,
      messages: [],
    };
    $("messages").innerHTML = "";
    showEmptyState();
    $("chat-input").value = "";
    updateComposerState();
    renderSessions();
  }

  async function startNewChat() {
    if (state.running) return;
    await persistActiveSession().catch(() => {});
    prepareEmptyChat();
    rememberLastSession(state.activeSessionId);
    await refreshSessionList();
  }

  function normalizeThinkRecord(rec) {
    if (!rec) return null;
    return {
      kind: rec.kind || rec.Kind || "step",
      text: rec.text ?? rec.Text,
      label: rec.label ?? rec.Label,
      verb: rec.verb ?? rec.Verb,
      target: rec.target ?? rec.Target,
      error: !!(rec.error ?? rec.Error),
    };
  }

  function normalizeSession(raw) {
    if (!raw) return null;
    const harnessState =
      raw.harnessState ?? raw.HarnessState ?? raw.tuiThreadId ?? raw.TuiThreadId ?? null;
    const thinkOf = (t) => {
      if (!t) return null;
      const records = (t.records || t.Records || [])
        .map(normalizeThinkRecord)
        .filter(Boolean);
      return { records, durationSec: t.durationSec ?? t.DurationSec ?? 1 };
    };
    const messages = (raw.messages || raw.Messages || []).map((m) => ({
      role: m.role || m.Role || "user",
      text: m.text ?? m.Text ?? "",
      answer: m.answer ?? m.Answer ?? "",
      think: thinkOf(m.think || m.Think),
    }));
    return {
      id: raw.id || raw.Id,
      title: raw.title || raw.Title || "新对话",
      createdAt: raw.createdAt ?? raw.CreatedAt ?? Date.now(),
      updatedAt: raw.updatedAt ?? raw.UpdatedAt ?? Date.now(),
      pinned: !!(raw.pinned ?? raw.Pinned),
      messages,
      harnessState,
      tuiThreadId: harnessState,
      webChatSessionId: raw.webChatSessionId ?? raw.WebChatSessionId ?? null,
    };
  }

  function renderSessionMessages(messages) {
    $("messages").innerHTML = "";
    if (!messages?.length) {
      showEmptyState();
      return;
    }
    hideEmptyState();
    messages.forEach((m, i) => {
      if (m.role === "user") {
        if (m.text) appendUserMessage(m.text, i);
        return;
      }
      if (m.role !== "assistant") return;
      const wrap = $("messages");
      if (!wrap) return;
      const row = document.createElement("div");
      row.className = "ds-msg-row ds-assistant";
      const bubble = document.createElement("div");
      bubble.className = "ds-msg-bubble";
      if (m.think?.records?.length) restoreThinkBlock(bubble, m.think);
      const answerText = (m.answer || "").trim();
      if (answerText) {
        const answer = document.createElement("div");
        answer.className = "ds-msg-answer";
        renderAssistantAnswer(answer, answerText);
        bubble.appendChild(answer);
      } else if (!m.think?.records?.length) {
        const answer = document.createElement("div");
        answer.className = "ds-msg-answer ds-muted";
        answer.textContent = "（无回复内容）";
        bubble.appendChild(answer);
      }
      row.appendChild(bubble);
      wrap.appendChild(row);
    });
    scrollToBottom();
  }

  async function loadSession(id) {
    if (state.running || !id || id === state.activeSessionId) return;
    await persistActiveSession().catch(() => {});
    try {
      const res = await postAsync("agentSessionLoad", { id });
      const session = normalizeSession(res.session);
      if (!session) {
        alert("无法加载该对话");
        return;
      }
      state.activeSessionId = session.id;
      state.activeSession = session;
      rememberWebChatSessionId(session.webChatSessionId);
      rememberLastSession(session.id);
      renderSessionMessages(session.messages || []);
      renderSessions();
      updateComposerState();
    } catch (_) {
      alert("加载对话失败");
    }
  }

  function ensureSession() {
    if (!state.activeSessionId || !state.activeSession) {
      const now = Date.now();
      state.activeSessionId = uid();
      state.activeSession = {
        id: state.activeSessionId,
        title: "新对话",
        createdAt: now,
        updatedAt: now,
        messages: [],
      };
    }
    return state.activeSession;
  }

  function normalizeStrategy(value) {
    const s = (value || "execute").toLowerCase();
    if (s === "plan" || s === "blueprint" || s === "orient") return "blueprint";
    return "execute";
  }

  function agentHelpText() {
    return (
      "DeepSeek Edge Agent（DSD Harness）\n" +
      "/help  本帮助\n" +
      "/clear  清空对话\n" +
      "/blueprint  Blueprint 工作流（Explore→蓝图，只读调研）\n" +
      "/orient  Orient→Explore→Blueprint\n" +
      "/execute  Execute 阶段（读写 + 工具）\n" +
      "/phase  显示当前 strategy / playbook\n" +
      "/playbooks  列出 ~/.deepseek/playbooks/\n" +
      "/playbook <id>  启用 Playbook\n" +
      "/skills  列出市场标准 SKILL.md（Cursor/Claude/.agents）\n" +
      "/skill <id>  启用 Skill 执行下一任务\n" +
      "/checkpoint  查看会话检查点（断点续传）\n" +
      "/reload  热重载 Playbook / Skill 缓存\n" +
      "/react、/plan  兼容旧命令\n" +
      "/chat   返回普通对话\n\n" +
      "推理：本地 Chat2API（须先在网页登录）\n" +
      "互操作：MCP（含 ~/.cursor/mcp.json）· SKILL.md · OpenAI tools schema"
    );
  }

  async function listPlaybooksText() {
    try {
      const res = await postAsync("agentPlaybooksList", {});
      if (!res.ok) return "无法加载 Playbooks：" + (res.error || "未知错误");
      const items = Array.isArray(res.playbooks) ? res.playbooks : [];
      if (!items.length) return "暂无 Playbook。可在 ~/.deepseek/playbooks/ 添加 .yaml / .json。";
      return items
        .map((p) => {
          const tags = [
            p.strategy,
            p.hasVerify ? (p.verifyStepCount > 1 ? "verify×" + p.verifyStepCount : "verify") : null,
            p.source,
          ].filter(Boolean).join(" · ");
          return "• " + p.id + " — " + (p.name || p.id) + (tags ? " (" + tags + ")" : "") +
            (p.description ? "\n  " + p.description : "");
        })
        .join("\n");
    } catch (e) {
      return "Playbooks 加载失败：" + (e.message || e);
    }
  }

  async function listSkillsText() {
    try {
      const res = await postAsync("agentSkillsList", {});
      if (!res.ok) return "无法加载 Skills：" + (res.error || "未知错误");
      const items = Array.isArray(res.skills) ? res.skills : [];
      if (!items.length) {
        return (
          "未发现 SKILL.md。标准路径：\n" +
          "• ~/.cursor/skills/<name>/SKILL.md\n" +
          "• ~/.claude/skills/<name>/SKILL.md\n" +
          "• ~/.deepseek/skills/<name>/SKILL.md\n" +
          "• <workspace>/.cursor/skills/ · .agents/skills/"
        );
      }
      return items
        .map((s) => {
          const tag = s.source || "skill";
          return "• " + s.id + " — " + (s.name || s.id) + " (" + tag + ")" +
            (s.description ? "\n  " + s.description : "");
        })
        .join("\n");
    } catch (e) {
      return "Skills 加载失败：" + (e.message || e);
    }
  }

  const SLASH_SECTION_LIMIT = 6;

  const SLASH_ICONS = {
    skill:
      '<svg class="ds-slash-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M12 3l1.5 4.5L18 9l-4.5 1.5L12 15l-1.5-4.5L6 9l4.5-1.5L12 3z"/><path d="M5 19l1 3 1-3 3-1-3-1-1-3-1 3-3 1z"/></svg>',
    command:
      '<svg class="ds-slash-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M13 2L3 14h7l-1 8 10-12h-7l1-8z"/></svg>',
    mode:
      '<svg class="ds-slash-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><circle cx="12" cy="12" r="3"/><path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2"/></svg>',
    model:
      '<svg class="ds-slash-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><rect x="3" y="4" width="18" height="14" rx="2"/><path d="M8 20h8M12 18v2"/></svg>',
    playbook:
      '<svg class="ds-slash-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"/><path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"/></svg>',
  };

  const SLASH_COMMANDS = [
    { kind: "command", id: "help", label: "help", desc: "显示 Agent 帮助", insert: "/help", match: ["help"] },
    { kind: "command", id: "clear", label: "clear", desc: "清空当前对话", insert: "/clear", match: ["clear"] },
    { kind: "command", id: "execute", label: "execute", desc: "切换到 Execute 工作流", insert: "/execute", match: ["execute", "react"] },
    { kind: "command", id: "blueprint", label: "blueprint", desc: "切换到 Blueprint 工作流", insert: "/blueprint", match: ["blueprint", "plan"] },
    { kind: "command", id: "orient", label: "orient", desc: "Orient → Explore → Blueprint", insert: "/orient", match: ["orient"] },
    { kind: "command", id: "phase", label: "phase", desc: "显示当前 strategy / playbook / skill", insert: "/phase", match: ["phase"] },
    { kind: "command", id: "playbooks", label: "playbooks", desc: "列出可用 Playbook", insert: "/playbooks", match: ["playbooks", "playbook"] },
    { kind: "command", id: "skills", label: "skills", desc: "列出可用 Skill", insert: "/skills", match: ["skills", "skill"] },
    { kind: "command", id: "checkpoint", label: "checkpoint", desc: "查看会话检查点", insert: "/checkpoint", match: ["checkpoint"] },
    { kind: "command", id: "reload", label: "reload", desc: "热重载 Playbook / Skill 缓存", insert: "/reload", match: ["reload"] },
    { kind: "command", id: "chat", label: "chat", desc: "返回普通对话模式", insert: "/chat", match: ["chat"] },
    {
      kind: "command",
      id: "automations",
      label: "automations",
      desc: "打开 Automations（定时 / Webhook 常驻 Agent）",
      insert: "/automations",
      match: ["automations", "automation", "auto"],
    },
  ];

  const SLASH_MODELS = [
    { kind: "model", id: "deepseek-v4-pro", label: "deepseek-v4-pro", desc: "DeepSeek V4 Pro（默认 · 思考+联网）" },
    { kind: "model", id: "deepseek-reasoner", label: "deepseek-reasoner", desc: "深度推理（兼容）" },
  ];

  const slashPalette = {
    open: false,
    query: "",
    range: null,
    selected: 0,
    flat: [],
    expanded: { skills: false, playbooks: false },
    catalog: { skills: [], playbooks: [], loaded: false, loading: false },
  };

  function slashIcon(kind) {
    return SLASH_ICONS[kind] || SLASH_ICONS.command;
  }

  function getSlashContext(input) {
    if (!input) return null;
    const value = input.value;
    const pos = input.selectionStart ?? value.length;
    const before = value.slice(0, pos);
    const m = before.match(/(?:^|\s)\/([^\s]*)$/);
    if (!m) return null;
    const token = m[1];
    const start = before.length - token.length - 1;
    return { query: token.toLowerCase(), start, end: pos };
  }

  function slashMatches(item, query) {
    if (!query) return true;
    const hay = [
      item.label,
      item.id,
      item.desc,
      ...(item.match || []),
      ...(item.keywords || []),
    ]
      .filter(Boolean)
      .join(" ")
      .toLowerCase();
    return hay.includes(query);
  }

  async function ensureSlashCatalog() {
    if (slashPalette.catalog.loaded || slashPalette.catalog.loading) return;
    slashPalette.catalog.loading = true;
    try {
      const [sk, pb] = await Promise.all([
        postAsync("agentSkillsList", {}).catch(() => ({ ok: false })),
        postAsync("agentPlaybooksList", {}).catch(() => ({ ok: false })),
      ]);
      slashPalette.catalog.skills = sk.ok && Array.isArray(sk.skills) ? sk.skills : [];
      slashPalette.catalog.playbooks = pb.ok && Array.isArray(pb.playbooks) ? pb.playbooks : [];
      slashPalette.catalog.loaded = true;
    } catch (_) {
      slashPalette.catalog.skills = [];
      slashPalette.catalog.playbooks = [];
      slashPalette.catalog.loaded = true;
    } finally {
      slashPalette.catalog.loading = false;
    }
  }

  function buildSlashModeItems() {
    const items = [];
    STRATEGY_OPTIONS.forEach((opt) => {
      items.push({
        kind: "mode",
        section: "Modes",
        icon: "mode",
        id: "mode-" + opt.id,
        label: opt.label,
        desc: opt.desc,
        action: { type: "strategy", value: opt.id },
        active: displayStrategy(state.strategy) === opt.id,
      });
    });
    return items;
  }

  function buildSlashFlatItems() {
    const q = slashPalette.query;
    const flat = [];
    const skills = slashPalette.catalog.skills.filter((s) =>
      slashMatches({ label: s.id, id: s.id, desc: s.description || s.name, keywords: [s.name, s.source] }, q)
    );
    const skillLimit = slashPalette.expanded.skills ? skills.length : SLASH_SECTION_LIMIT;
    skills.slice(0, skillLimit).forEach((s) => {
      flat.push({
        kind: "skill",
        section: "Skills",
        icon: "skill",
        id: "skill-" + s.id,
        label: s.id,
        desc: s.description || s.name || s.source || "Skill",
        insert: "/skill " + s.id,
        action: { type: "skill", value: s.id },
      });
    });
    if (skills.length > skillLimit) {
      flat.push({
        kind: "more",
        section: "Skills",
        id: "more-skills",
        label: "Show " + (skills.length - skillLimit) + " more",
        action: { type: "expand", section: "skills" },
      });
    }

    const playbooks = slashPalette.catalog.playbooks.filter((p) =>
      slashMatches({ label: p.id, id: p.id, desc: p.description || p.name, keywords: [p.name, p.strategy] }, q)
    );
    const pbLimit = slashPalette.expanded.playbooks ? playbooks.length : SLASH_SECTION_LIMIT;
    playbooks.slice(0, pbLimit).forEach((p) => {
      flat.push({
        kind: "playbook",
        section: "Playbooks",
        icon: "playbook",
        id: "pb-" + p.id,
        label: p.id,
        desc: p.description || p.name || "Playbook",
        insert: "/playbook " + p.id,
        action: { type: "playbook", value: p.id },
      });
    });
    if (playbooks.length > pbLimit) {
      flat.push({
        kind: "more",
        section: "Playbooks",
        id: "more-playbooks",
        label: "Show " + (playbooks.length - pbLimit) + " more",
        action: { type: "expand", section: "playbooks" },
      });
    }

    SLASH_COMMANDS.filter((c) => slashMatches(c, q)).forEach((c) => {
      flat.push({ ...c, section: "Commands", icon: "command" });
    });

    buildSlashModeItems()
      .filter((m) => slashMatches(m, q))
      .forEach((m) => flat.push(m));

    SLASH_MODELS.filter((m) => slashMatches(m, q)).forEach((m) => {
      flat.push({ ...m, section: "Models", icon: "model" });
    });

    return flat;
  }

  function renderSlashPalette() {
    const root = $("slash-palette");
    if (!root) return;
    slashPalette.flat = buildSlashFlatItems();
    if (!slashPalette.open) {
      root.hidden = true;
      return;
    }
    root.hidden = false;
    root.replaceChildren();

    if (!slashPalette.flat.length) {
      const empty = document.createElement("div");
      empty.className = "ds-slash-empty";
      empty.textContent = slashPalette.catalog.loading ? "加载 Skills / Playbooks…" : "没有匹配的命令或工具";
      root.appendChild(empty);
      return;
    }

    let sectionEl = null;
    let sectionName = "";
    slashPalette.flat.forEach((item, idx) => {
      if (item.section !== sectionName) {
        sectionName = item.section;
        sectionEl = document.createElement("div");
        sectionEl.className = "ds-slash-section";
        const heading = document.createElement("div");
        heading.className = "ds-slash-heading";
        heading.textContent = sectionName;
        sectionEl.appendChild(heading);
        root.appendChild(sectionEl);
      }

      if (item.kind === "more") {
        const more = document.createElement("button");
        more.type = "button";
        more.className = "ds-slash-more";
        more.textContent = item.label;
        more.addEventListener("mousedown", (e) => {
          e.preventDefault();
          slashPalette.expanded[item.action.section] = true;
          renderSlashPalette();
        });
        sectionEl.appendChild(more);
        return;
      }

      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "ds-slash-item" + (idx === slashPalette.selected ? " is-active" : "");
      btn.setAttribute("role", "option");
      btn.setAttribute("aria-selected", idx === slashPalette.selected ? "true" : "false");
      btn.innerHTML =
        slashIcon(item.icon || item.kind) +
        '<span class="ds-slash-item-body">' +
        '<span class="ds-slash-label">' +
        escapeHtml(item.label) +
        (item.active ? " ✓" : "") +
        "</span>" +
        (item.desc ? '<span class="ds-slash-desc">' + escapeHtml(item.desc) + "</span>" : "") +
        "</span>";
      btn.addEventListener("mousedown", (e) => {
        e.preventDefault();
        applySlashItem(item);
      });
      sectionEl.appendChild(btn);
    });
    const active = root.querySelector(".ds-slash-item.is-active");
    active?.scrollIntoView({ block: "nearest" });
  }

  function closeSlashPalette() {
    slashPalette.open = false;
    slashPalette.query = "";
    slashPalette.range = null;
    slashPalette.selected = 0;
    slashPalette.expanded = { skills: false, playbooks: false };
    const root = $("slash-palette");
    if (root) root.hidden = true;
  }

  function replaceSlashToken(input, text) {
    if (!input || !slashPalette.range) return;
    const value = input.value;
    const before = value.slice(0, slashPalette.range.start);
    const after = value.slice(slashPalette.range.end);
    input.value = before + text + after;
    const pos = before.length + text.length;
    input.setSelectionRange(pos, pos);
  }

  async function applySlashItem(item) {
    const input = $("chat-input");
    if (!input) return;

    if (item.action?.type === "expand") {
      slashPalette.expanded[item.action.section] = true;
      renderSlashPalette();
      return;
    }

    if (item.action?.type === "strategy") {
      state.strategy = item.action.value;
      renderContextBar();
      patchWorkspace({ defaultAgentStrategy: item.action.value }).catch(() => {});
      replaceSlashToken(input, "");
      closeSlashPalette();
      resizeInput();
      updateComposerState();
      input.focus();
      return;
    }

    if (item.action?.type === "skill") {
      state.skillId = item.action.value;
    }
    if (item.action?.type === "playbook") {
      state.playbookId = item.action.value;
    }

    const insert = item.insert || (item.label ? "/" + item.label : "");
    replaceSlashToken(input, insert);
    closeSlashPalette();
    resizeInput();
    updateComposerState();
    input.focus();
  }

  function onSlashInput() {
    const input = $("chat-input");
    const ctx = getSlashContext(input);
    if (!ctx) {
      closeSlashPalette();
      return;
    }
    slashPalette.open = true;
    slashPalette.query = ctx.query;
    slashPalette.range = { start: ctx.start, end: ctx.end };
    slashPalette.selected = 0;
    ensureSlashCatalog().then(() => renderSlashPalette());
    renderSlashPalette();
  }

  function slashPaletteHandleKeydown(e) {
    if (!slashPalette.open) return false;
    const input = $("chat-input");
    if (e.key === "Escape") {
      e.preventDefault();
      closeSlashPalette();
      return true;
    }
    if (e.key === "ArrowDown") {
      e.preventDefault();
      if (slashPalette.flat.length) {
        slashPalette.selected = (slashPalette.selected + 1) % slashPalette.flat.length;
        renderSlashPalette();
      }
      return true;
    }
    if (e.key === "ArrowUp") {
      e.preventDefault();
      if (slashPalette.flat.length) {
        slashPalette.selected =
          (slashPalette.selected - 1 + slashPalette.flat.length) % slashPalette.flat.length;
        renderSlashPalette();
      }
      return true;
    }
    if (e.key === "Enter" && !e.shiftKey && !e.isComposing) {
      e.preventDefault();
      const item = slashPalette.flat[slashPalette.selected];
      if (item) applySlashItem(item);
      else closeSlashPalette();
      return true;
    }
    if (e.key === "Tab") {
      e.preventDefault();
      const item = slashPalette.flat[slashPalette.selected];
      if (item) applySlashItem(item);
      return true;
    }
    return false;
  }

  async function handleSlashCommand(text) {
    const t = text.trim();
    if (!t.startsWith("/")) return false;
    const cmd = t.split(/\s+/)[0].toLowerCase();
    if (cmd === "/help") {
      appendMessage("assistant", agentHelpText(), { status: "DeepSeek 帮助" });
      return true;
    }
    if (cmd === "/clear") {
      await startNewChat();
      return true;
    }
    if (cmd === "/execute" || cmd === "/react") {
      state.strategy = "execute";
      renderContextBar();
      appendMessage("assistant", "已切换为 Execute 工作流（多步工具执行）。", { status: "Phase" });
      return true;
    }
    if (cmd === "/blueprint" || cmd === "/plan") {
      state.strategy = "blueprint";
      renderContextBar();
      appendMessage("assistant", "已切换为 Blueprint 工作流（Explore → 蓝图，只读调研）。", { status: "Phase" });
      return true;
    }
    if (cmd === "/orient") {
      state.strategy = "orient";
      renderContextBar();
      appendMessage("assistant", "已切换为 Orient 入口（Orient → Explore → Blueprint）。", { status: "Phase" });
      return true;
    }
    if (cmd === "/phase") {
      const pb = state.playbookId ? "playbook=" + state.playbookId : "playbook=（无）";
      const sk = state.skillId ? "skill=" + state.skillId : "skill=（无）";
      appendMessage(
        "assistant",
        "当前 strategy=" + state.strategy + " · " + pb + " · " + sk,
        { status: "Phase" }
      );
      return true;
    }
    if (cmd === "/playbooks") {
      appendMessage("assistant", await listPlaybooksText(), { status: "Playbooks" });
      return true;
    }
    if (cmd === "/playbook") {
      const id = t.split(/\s+/)[1];
      if (!id) {
        appendMessage("assistant", "用法：/playbook <id>\n\n" + (await listPlaybooksText()), { status: "Playbooks" });
        return true;
      }
      state.playbookId = id.trim();
      appendMessage("assistant", "已启用 Playbook：" + state.playbookId, { status: "Playbook" });
      return true;
    }
    if (cmd === "/reload") {
      try {
        const res = await postAsync("agentHarnessReload", {});
        if (!res.ok) {
          appendMessage("assistant", "热重载失败：" + (res.error || ""), { status: "Reload" });
          return true;
        }
        appendMessage(
          "assistant",
          "已热重载 Playbook / Skill 缓存。" +
            (res.reloadedAtUtc ? "\n时间(UTC)：" + res.reloadedAtUtc : ""),
          { status: "Reload" }
        );
      } catch (e) {
        appendMessage("assistant", "热重载失败：" + (e.message || e), { status: "Reload" });
      }
      return true;
    }
    if (cmd === "/checkpoint") {
      try {
        const res = await postAsync("agentCheckpointGet", {});
        if (!res.ok) {
          appendMessage("assistant", "检查点读取失败：" + (res.error || ""), { status: "Checkpoint" });
          return true;
        }
        const cp = res.checkpoint || {};
        const lines = [
          cp.summary ? "摘要：" + cp.summary : null,
          cp.currentMilestone ? "里程碑：" + cp.currentMilestone : null,
          cp.lastPhase ? "上次阶段：" + cp.lastPhase : null,
          cp.domainId ? "领域：" + cp.domainId : null,
          Array.isArray(cp.pendingItems) && cp.pendingItems.length
            ? "待续：\n" + cp.pendingItems.map((x) => "• " + x).join("\n")
            : null,
          cp.nextContinuation ? "续接提示：" + cp.nextContinuation : null,
        ].filter(Boolean);
        appendMessage(
          "assistant",
          lines.length ? lines.join("\n") : "尚无检查点记录，完成一次 Agent 任务后会自动写入。",
          { status: "Checkpoint" }
        );
      } catch (e) {
        appendMessage("assistant", "检查点加载失败：" + (e.message || e), { status: "Checkpoint" });
      }
      return true;
    }
    if (cmd === "/skills") {
      const parts = t.split(/\s+/);
      if (parts.length === 1) {
        appendMessage("assistant", await listSkillsText(), { status: "Skills" });
        return true;
      }
      state.skillId = parts[1].trim();
      appendMessage("assistant", "已启用 Skill：" + state.skillId, { status: "Skill" });
      return true;
    }
    if (cmd === "/skill") {
      const id = t.split(/\s+/)[1];
      if (!id) {
        appendMessage("assistant", "用法：/skill <id>\n\n" + (await listSkillsText()), { status: "Skills" });
        return true;
      }
      state.skillId = id.trim();
      appendMessage("assistant", "已启用 Skill：" + state.skillId + "（下次发送任务时注入）", { status: "Skill" });
      return true;
    }
    if (cmd === "/chat") {
      if (window.DsWorkMode) window.DsWorkMode.requestSet("chat", chatNavigateExtra());
      else post("setWorkMode", { mode: "chat", ...chatNavigateExtra() });
      return true;
    }
    if (cmd === "/automations" || cmd === "/automation") {
      openEmbeddedPanel("automations");
      return true;
    }
    if (cmd === "/agents" || cmd === "/agent") {
      return false;
    }
    return false;
  }

  async function dispatchRun() {
    const input = $("chat-input");
    const text = (input?.value || "").trim();
    if (!text || state.running || !state.loggedIn) return;

    if (await handleSlashCommand(text)) {
      input.value = "";
      resizeInput();
      return;
    }

    const session = ensureSession();
    if (session && session.title === "新对话") {
      session.title = text.slice(0, 32) + (text.length > 32 ? "…" : "");
      renderSessions();
    }

    appendMessage("user", text);
    session.messages = session.messages || [];
    session.messages.push({ role: "user", text });
    await persistActiveSession().catch(() => {});

    state.currentRun = appendMessage("assistant", "", { status: "Agent 正在执行…" });
    state.running = true;
    state.stopping = false;
    syncAllUserMessageActions();
    updateComposerState();
    requestAnimationFrame(() => updateComposerState());
    input.value = "";
    resizeInput();

    const refFileIds = state.files.map((f) => f.id);
    state.files = [];
    renderFileChips();

    post("setWorkMode", { mode: "agent" });
    state.stopping = false;
    post("agentRun", {
      text: text || (refFileIds.length ? "请阅读附件并完成任务。" : text),
      mode: "专家",
      strategy: state.strategy,
      deepThink: !!state.deepThink,
      smartSearch: !!state.smartSearch,
      mcpOn: true,
      refFileIds,
      sessionId: state.activeSessionId,
      harnessState:
        state.activeSession?.harnessState || state.activeSession?.tuiThreadId || null,
      tuiThreadId:
        state.activeSession?.tuiThreadId || state.activeSession?.harnessState || null,
      playbookId: state.playbookId || null,
      skillId: state.skillId || null,
    });
  }

  function resizeInput() {
    const input = $("chat-input");
    if (!input) return;
    input.style.height = "auto";
    input.style.height = Math.min(200, Math.max(52, input.scrollHeight)) + "px";
  }

  function cancelRun() {
    if (!state.running || state.stopping) return;
    state.stopping = true;
    updateComposerState();
    if (state.currentRun?.phaseEl) {
      state.currentRun.phaseEl.textContent = "Stopping";
    }
    post("agentStop", {});
  }

  function finishRun(summary, answer) {
    state.running = false;
    state.stopping = false;
    syncAllUserMessageActions();
    updateComposerState();

    const text = (answer || summary || "任务已结束").trim();
    const failed = /^(失败|错误|已停止)/.test(text);

    if (state.currentRun) {
      if (failed) {
        applyThinkLog(state.currentRun, {
          kind: "step",
          verb: "Error",
          target: text.split("\n")[0],
          error: true,
        });
      }
      finalizeThinkBlock(state.currentRun);
      const durationSec = Math.max(
        1,
        Math.floor((Date.now() - state.currentRun.thinkStartTime) / 1000)
      );
      if (text && !/^任务已结束$/.test(text)) {
        renderAssistantAnswer(state.currentRun.answerEl, text);
      }

      const session = state.activeSession;
      if (session) {
        session.messages = session.messages || [];
        session.messages.push({
          role: "assistant",
          answer: failed ? "" : text,
          think: {
            records: state.currentRun.thinkRecords || [],
            durationSec,
          },
        });
        persistActiveSession().catch(() => {});
      }
    }

    state.currentRun = null;
    scrollToBottom();
  }

  window.dsDesktopOnMessage = function (msg) {
    if (!msg || !msg.type) return;
    if (handlePendingReply(msg)) return;

    if (msg.type === "showEmbeddedPanel" && msg.panel) {
      embeddedPanel?.show(msg.panel);
      return;
    }

    if (msg.type === "hideEmbeddedPanel") {
      embeddedPanel?.hide();
      return;
    }

    if (state.embeddedPanel && forwardEmbeddedHostMessage(msg)) return;

    if (msg.type === "embeddedPanelReady" && state.embeddedPanel) {
      const loading = $("embedded-panel-loading");
      if (loading) loading.hidden = true;
      return;
    }

    if (msg.type === "workModeState" && window.DsWorkMode) {
      window.DsWorkMode.applyState(msg);
    }

    if (msg.type === "apiInfo" || msg.type === "loginState") {
      setLoggedIn(!!msg.loggedIn);
      if (msg.type === "apiInfo") {
        if (msg.agentStrategy) {
          state.strategy = normalizeStrategy(msg.agentStrategy);
          renderContextBar();
        }
        if (typeof msg.agentDeepThinking === "boolean") state.deepThink = msg.agentDeepThinking;
        if (typeof msg.agentWebSearch === "boolean") state.smartSearch = msg.agentWebSearch;
        syncFeaturePills();
      }
    }

    if (msg.type === "agentWorkspaceState" && msg.workspace) {
      applyWorkspace(msg.workspace);
    }

    if (msg.type === "agentActivity" && msg.verb) {
      if (state.currentRun && !state.running) {
        state.running = true;
        updateComposerState();
      }
      applyAgentActivity(state.currentRun, {
        verb: msg.verb,
        target: msg.target || "",
        detail: msg.detail || null,
      });
    }

    if (msg.type === "agentThinking" && msg.text) {
      if (state.currentRun) applyAgentThinking(state.currentRun, msg.text, !!msg.append);
    }

    if (msg.type === "agentLog" && msg.text) {
      if (state.currentRun && !state.running) {
        state.running = true;
        updateComposerState();
      }
      appendLogLine(msg.text);
    }

    if (msg.type === "agentAnswer" && msg.text) {
      if (state.currentRun) setThinkPhase(state.currentRun, "writing");
      if (state.currentRun?.answerEl) {
        streamAssistantAnswer(state.currentRun.answerEl, msg.text, !!msg.append);
      }
    }

    if ((msg.type === "agentHarnessState" || msg.type === "agentTuiThread") && state.activeSession) {
      const id = msg.harnessState || msg.tuiThreadId;
      if (id) {
        state.activeSession.harnessState = id;
        state.activeSession.tuiThreadId = id;
        applyHarnessPhaseFromState(id);
      }
      if (msg.phase) setThinkPhase(state.currentRun, String(msg.phase).toLowerCase());
      if (msg.webChatSessionId) rememberWebChatSessionId(msg.webChatSessionId);
      persistActiveSession().catch(() => {});
    }

    if (msg.type === "agentPhase" && state.currentRun && msg.phase) {
      setThinkPhase(state.currentRun, String(msg.phase).toLowerCase());
    }

    if (msg.type === "agentStarted") {
      state.running = true;
      state.stopping = false;
      updateComposerState();
      if (state.currentRun) {
        state.currentRun._waitTimer = setTimeout(() => {
          if (state.running && state.currentRun && !state.currentRun.thinkDone) {
            setThinkPhase(state.currentRun, "thinking");
            updateThinkTitle(state.currentRun);
          }
        }, 1500);
      }
      updateThinkTitle(state.currentRun);
    }

    if (msg.type === "agentDone") {
      if (state.currentRun?._waitTimer) clearTimeout(state.currentRun._waitTimer);
      finishRun(msg.summary, msg.answer);
    }
  };

  function syncFeaturePills() {
    /* 深度思考/联网由 UI 状态决定，发消息时传给宿主；不强制全开 */
  }

  function persistAgentFeatures() {
    post("setAgentFeatures", {
      deepThink: !!state.deepThink,
      smartSearch: !!state.smartSearch,
    });
  }

  function forwardEmbeddedHostMessage(msg) {
    const iframe = $("embedded-frame");
    if (!iframe?.contentWindow || !state.embeddedPanel) return false;

    if (state.embeddedPanel === "settings") {
      if (msg.reqId && String(msg.reqId).startsWith("s")) {
        try {
          iframe.contentWindow.postMessage(JSON.stringify(msg), "*");
        } catch (_) {}
        return true;
      }
      return false;
    }

    if (state.embeddedPanel === "chat2api") {
      if (
        msg.type === "ipcResult" ||
        msg.type === "ipcEvent" ||
        msg.type === "desktopStackSynced"
      ) {
        try {
          iframe.contentWindow.postMessage(JSON.stringify(msg), "*");
        } catch (_) {}
        return true;
      }
      return false;
    }

    return false;
  }

  function bindEmbeddedPanel() {
    const root = $("embedded-panel");
    const iframe = $("embedded-frame");
    const loading = $("embedded-panel-loading");
    const titleEl = $("embedded-panel-title");
    const backBtn = $("embedded-back");
    if (!root || !iframe) return { show: () => {}, hide: () => {} };

    function hideEmbeddedPanel() {
      const wasPanel = state.embeddedPanel;
      state.embeddedPanel = null;
      root.hidden = true;
      document.body.classList.remove("ds-embedded-open");
      // 保留 Chat2API iframe 文档，返回 Agent 时无需整页重载
      if (wasPanel !== "chat2api") iframe.src = "about:blank";
      if (loading) loading.hidden = false;
    }

    function showEmbedLoadError(message) {
      if (!loading) return;
      loading.hidden = false;
      loading.innerHTML =
        '<span class="ds-embedded-spinner" aria-hidden="true"></span>' +
        "<span>" +
        (message || "内嵌页面加载失败") +
        "</span>";
    }

    function isChat2ApiIframeReady() {
      try {
        const doc = iframe.contentDocument;
        const rootEl = doc?.getElementById("root");
        return !!(doc && rootEl && rootEl.childElementCount > 0);
      } catch (_) {
        return false;
      }
    }

    function showEmbeddedPanel(panel) {
      if (!EMBED_URLS[panel]) return;
      const url = EMBED_URLS[panel];
      const reuseChat2Api =
        panel === "chat2api" &&
        (iframe.src || "").indexOf("/chat2api/") >= 0 &&
        isChat2ApiIframeReady();

      state.embeddedPanel = panel;
      if (titleEl) titleEl.textContent = EMBED_TITLES[panel] || panel;
      root.hidden = false;
      document.body.classList.add("ds-embedded-open");
      iframe.removeAttribute("srcdoc");

      if (reuseChat2Api) {
        if (loading) loading.hidden = true;
        return;
      }

      if (loading) {
        loading.hidden = false;
        loading.innerHTML =
          '<span class="ds-embedded-spinner" aria-hidden="true"></span><span>加载中…</span>';
      }
      iframe.src = url;
    }

    backBtn?.addEventListener("click", hideEmbeddedPanel);
    iframe?.addEventListener("error", () => {
      if (state.embeddedPanel === "chat2api") {
        showEmbedLoadError("API 管理页加载失败，请重新运行 build.ps1 部署");
      }
    });
    iframe?.addEventListener("load", () => {
      if (!state.embeddedPanel) return;
      if (state.embeddedPanel === "chat2api") {
        try {
          const doc = iframe.contentDocument;
          const root = doc?.getElementById("root");
          if (!doc || !root) {
            showEmbedLoadError("API 管理资源未就绪，请重新运行 build.ps1");
            return;
          }
          if (root.childElementCount === 0) return;
        } catch (_) {
          showEmbedLoadError("无法读取 API 管理页，请重新部署应用");
          return;
        }
      }
      if (loading) loading.hidden = true;
      if (state.embeddedPanel === "settings" && iframe.contentWindow && state.authResolved) {
        try {
          iframe.contentWindow.postMessage(
            JSON.stringify({
              type: "settingsBootstrap",
              __dsEmbed: true,
              loggedIn: state.loggedIn,
            }),
            "*"
          );
        } catch (_) {}
      }
    });

    window.addEventListener("message", (e) => {
      if (e.source !== iframe.contentWindow) return;
      let msg;
      try {
        msg = typeof e.data === "string" ? JSON.parse(e.data) : e.data;
      } catch (_) {
        return;
      }
      if (!msg?.type) return;
      if (!msg.__dsEmbed) return;
      if (msg.type === "settingsEmbedClose" || msg.type === "automationsEmbedClose") {
        hideEmbeddedPanel();
        return;
      }
      const payload = { ...msg };
      delete payload.__dsEmbed;
      if (msg.type === "consoleUiReady") {
        if (loading) loading.hidden = true;
      }
      post(msg.type, payload);
    });

    document.addEventListener("keydown", (e) => {
      if (e.key === "Escape" && state.embeddedPanel) hideEmbeddedPanel();
    });

    return { show: showEmbeddedPanel, hide: hideEmbeddedPanel };
  }

  function openEmbeddedPanel(panel) {
    embeddedPanel?.show(panel);
    post("prepareEmbeddedPanel", { panel });
  }

  function dismissAutomationsIntro() {
    try {
      localStorage.setItem(AUTOMATIONS_INTRO_KEY, "1");
    } catch (_) {}
    const backdrop = $("auto-intro-backdrop");
    const modal = $("auto-intro");
    if (backdrop) {
      backdrop.hidden = true;
      backdrop.setAttribute("aria-hidden", "true");
    }
    if (modal) modal.hidden = true;
  }

  function showAutomationsIntroIfNeeded() {
    try {
      if (localStorage.getItem(AUTOMATIONS_INTRO_KEY)) return;
    } catch (_) {}
    const backdrop = $("auto-intro-backdrop");
    const modal = $("auto-intro");
    if (!backdrop || !modal) return;
    backdrop.hidden = false;
    backdrop.setAttribute("aria-hidden", "false");
    modal.hidden = false;
  }

  function bindAutomationsIntro() {
    $("auto-intro-try")?.addEventListener("click", () => {
      dismissAutomationsIntro();
      openEmbeddedPanel("automations");
    });
    $("auto-intro-later")?.addEventListener("click", dismissAutomationsIntro);
    $("auto-intro-backdrop")?.addEventListener("click", dismissAutomationsIntro);
    $("auto-intro-learn")?.addEventListener("click", () => dismissAutomationsIntro());
    showAutomationsIntroIfNeeded();
  }

  function bindSettingsMenu() {
    const btn = $("btn-sidebar-settings");
    const backdrop = $("settings-backdrop");
    const popover = $("settings-popover");
    if (!btn || !backdrop || !popover) return;

    function positionPopover() {
      const rect = btn.getBoundingClientRect();
      popover.style.visibility = "hidden";
      popover.hidden = false;
      const width = popover.offsetWidth;
      popover.hidden = true;
      popover.style.visibility = "";
      const left = Math.min(
        Math.max(8, rect.right - width),
        window.innerWidth - width - 8
      );
      popover.style.top = `${rect.bottom + 6}px`;
      popover.style.left = `${left}px`;
    }

    function closeSettingsMenu() {
      backdrop.hidden = true;
      popover.hidden = true;
      btn.setAttribute("aria-expanded", "false");
      btn.classList.remove("ds-active");
    }

    function openSettingsMenu() {
      positionPopover();
      backdrop.hidden = false;
      popover.hidden = false;
      btn.setAttribute("aria-expanded", "true");
      btn.classList.add("ds-active");
    }

    btn.addEventListener("click", (e) => {
      e.stopPropagation();
      if (popover.hidden) openSettingsMenu();
      else closeSettingsMenu();
    });

    backdrop.addEventListener("click", closeSettingsMenu);
    popover.addEventListener("click", (e) => e.stopPropagation());

    document.addEventListener("keydown", (e) => {
      if (e.key === "Escape" && !popover.hidden) closeSettingsMenu();
    });

    window.addEventListener("resize", () => {
      if (!popover.hidden) positionPopover();
    });

    return closeSettingsMenu;
  }

  let embeddedPanel = null;

  function bindUi() {
    embeddedPanel = bindEmbeddedPanel();
    // 后台预热 API 管理（同源 iframe），首次打开无需整页导航
    setTimeout(() => {
      const frame = $("embedded-frame");
      if (!frame || state.embeddedPanel) return;
      if ((frame.src || "").indexOf("/chat2api/") >= 0) return;
      frame.src = EMBED_URLS.chat2api;
    }, 1200);
    $("btn-attach")?.addEventListener("click", () => fileInput?.click());

    $("btn-send")?.addEventListener("click", () => {
      if (state.running) cancelRun();
      else dispatchRun();
    });
    $("btn-new-chat")?.addEventListener("click", () => startNewChat());
    const closeSettingsMenu = bindSettingsMenu();
    $("btn-chat2api")?.addEventListener("click", () => {
      closeSettingsMenu?.();
      openEmbeddedPanel("chat2api");
    });
    $("btn-automations")?.addEventListener("click", () => {
      closeSettingsMenu?.();
      dismissAutomationsIntro();
      openEmbeddedPanel("automations");
    });
    $("btn-mcp")?.addEventListener("click", () => {
      closeSettingsMenu?.();
      openEmbeddedPanel("settings");
    });
    $("btn-storage-settings")?.addEventListener("click", () => openEmbeddedPanel("settings"));
    bindAutomationsIntro();
    const modeFloat = $("mode-float");
    if (modeFloat && window.DsWorkMode && window.DsWorkMode.markFloater) {
      window.DsWorkMode.markFloater(modeFloat);
    }
    $("brand-home")?.addEventListener("click", (e) => {
      e.preventDefault();
    });
    $("btn-login-goto")?.addEventListener("click", () => {
      if (window.DsWorkMode) window.DsWorkMode.requestSet("chat", chatNavigateExtra());
      else post("setWorkMode", { mode: "chat", ...chatNavigateExtra() });
    });

    const fileInput = $("file-input");
    const attachBtn = $("btn-attach");
    attachBtn?.addEventListener("click", () => fileInput?.click());
    fileInput?.addEventListener("change", () => {
      if (fileInput.files?.length) handleFiles([...fileInput.files]);
      fileInput.value = "";
    });

    const input = $("chat-input");
    input?.addEventListener("input", () => {
      onSlashInput();
      resizeInput();
      updateComposerState();
    });
    input?.addEventListener("blur", () => {
      setTimeout(() => closeSlashPalette(), 120);
    });
    input?.addEventListener("keydown", (e) => {
      if (slashPaletteHandleKeydown(e)) return;
      if (e.key === "Enter" && !e.shiftKey && !e.isComposing) {
        e.preventDefault();
        if (state.running) cancelRun();
        else dispatchRun();
      }
    });

    $("btn-manage-sessions")?.addEventListener("click", () => setSelectMode(true));
    $("btn-manage-done")?.addEventListener("click", () => setSelectMode(false));
    $("btn-select-all")?.addEventListener("click", () => {
      state.sessionMetas.forEach((s) => state.selectedIds.add(s.id));
      renderSessions();
    });
    $("btn-delete-selected")?.addEventListener("click", async () => {
      if (!confirm("确定清空当前对话？")) return;
      setSelectMode(false);
      await startNewChat();
    });
  }

  async function bootstrapStorage() {
    await migrateLegacyLocalStorage();
    await refreshSessionList();
    const lastId = readLastSessionId();
    const target =
      (lastId && state.sessionMetas.some((s) => s.id === lastId) && lastId) ||
      state.sessionMetas[0]?.id ||
      null;
    if (target) await loadSession(target);
    else prepareEmptyChat();
  }

  const workspaceUi = {
    currentPath: "",
    currentName: "",
    homePath: "",
    recents: [],
    filterOpen: false,
    filterQuery: "",
    menuQuery: "",
    openMenu: null,
  };

  const STRATEGY_OPTIONS = [
    { id: "execute", label: "Execute", desc: "直接执行与工具调用" },
    { id: "orient", label: "Orient", desc: "Orient → Explore → Blueprint" },
    { id: "blueprint", label: "Blueprint", desc: "先探索再生成蓝图" },
  ];

  function displayStrategy(value) {
    const s = (value || "execute").toLowerCase();
    if (s === "plan" || s === "blueprint") return "blueprint";
    if (s === "orient") return "orient";
    return "execute";
  }

  function strategyLabel(id) {
    const s = displayStrategy(id);
    if (s === "blueprint") return "Blueprint";
    if (s === "orient") return "Orient";
    return "Execute";
  }

  function truncatePath(path, max) {
    if (!path) return "";
    const m = max || 48;
    if (path.length <= m) return path;
    const head = Math.floor(m * 0.35);
    const tail = m - head - 1;
    return path.slice(0, head) + "…" + path.slice(-tail);
  }

  function pathsEqual(a, b) {
    if (!a || !b) return false;
    return a.replace(/\\/g, "/").toLowerCase() === b.replace(/\\/g, "/").toLowerCase();
  }

  function applyWorkspace(ws) {
    if (!ws) return;
    workspaceUi.currentPath = ws.currentPath || "";
    workspaceUi.currentName = ws.currentName || "";
    workspaceUi.homePath = ws.homePath || "";
    workspaceUi.recents = Array.isArray(ws.recents) ? ws.recents : [];
    state.strategy = displayStrategy(ws.defaultAgentStrategy);
    renderWorkspaceSidebar();
    renderContextBar();
  }

  async function loadWorkspaceFromHost() {
    try {
      const msg = await postAsync("agentWorkspaceGet", {});
      applyWorkspaceFromReply(msg);
    } catch (_) {
      // 宿主未实现时静默
    }
  }

  function applyWorkspaceFromReply(msg) {
    if (!msg?.ok || !msg.workspace) return;
    applyWorkspace(msg.workspace);
  }

  async function setWorkspacePath(path) {
    const msg = await postAsync("agentWorkspaceSet", { path });
    applyWorkspaceFromReply(msg);
  }

  async function pickWorkspaceFolder() {
    const msg = await postAsync("agentWorkspacePickFolder", {});
    applyWorkspaceFromReply(msg);
  }

  async function patchWorkspace(patch) {
    const msg = await postAsync("agentWorkspacePatch", patch || {});
    applyWorkspaceFromReply(msg);
  }

  function workspaceSidebarEntries() {
    const q = (workspaceUi.filterQuery || "").trim().toLowerCase();
    const items = [];
    const seen = new Set();

    function add(path, name, kind) {
      if (!path || seen.has(path.toLowerCase())) return;
      const label = name || path.split(/[/\\]/).pop() || path;
      if (q && !label.toLowerCase().includes(q) && !path.toLowerCase().includes(q)) return;
      seen.add(path.toLowerCase());
      items.push({ path, name: label, kind });
    }

    if (workspaceUi.currentPath) {
      add(workspaceUi.currentPath, workspaceUi.currentName, "current");
    }
    (workspaceUi.recents || []).forEach((r) => {
      const p = typeof r === "string" ? r : r.path;
      const n = typeof r === "string" ? null : r.name;
      if (p && !pathsEqual(p, workspaceUi.currentPath)) add(p, n, "recent");
    });
    if (workspaceUi.homePath) add(workspaceUi.homePath, "Home", "home");
    return items;
  }

  function renderWorkspaceSidebar() {
    const list = $("workspace-list");
    if (!list) return;
    list.replaceChildren();

    workspaceSidebarEntries().forEach((item) => {
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = "ds-workspace-item" + (pathsEqual(item.path, workspaceUi.currentPath) ? " is-active" : "");
      btn.setAttribute("role", "listitem");
      const active = pathsEqual(item.path, workspaceUi.currentPath);
      const icon =
        item.kind === "home"
          ? '<svg class="ds-workspace-item-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M3 11l9-8 9 8"/><path d="M5 10v10h14V10"/></svg>'
          : active
            ? '<span class="ds-workspace-dot" aria-hidden="true"></span>'
            : '<svg class="ds-workspace-item-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7z"/></svg>';
      const sub =
        workspaceUi.filterOpen || item.kind === "recent"
          ? '<span class="ds-workspace-item-path">' + escapeHtml(truncatePath(item.path, 36)) + "</span>"
          : "";
      btn.classList.toggle("has-sub", !!sub);
      btn.innerHTML =
        icon +
        '<span class="ds-workspace-item-label">' +
        escapeHtml(item.name) +
        "</span>" +
        sub;
      btn.title = item.path;
      btn.addEventListener("click", () => {
        if (!pathsEqual(item.path, workspaceUi.currentPath)) setWorkspacePath(item.path).catch(() => {});
      });
      list.appendChild(btn);
    });

    const openBtn = document.createElement("button");
    openBtn.type = "button";
    openBtn.className = "ds-workspace-item";
    openBtn.innerHTML =
      '<svg class="ds-workspace-item-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7z"/><path d="M12 11v6M9 14h6"/></svg>' +
      '<span class="ds-workspace-item-label">打开工作区…</span>';
    openBtn.addEventListener("click", () => pickWorkspaceFolder().catch(() => {}));
    list.appendChild(openBtn);
  }

  function renderContextBar() {
    const wLabel = $("ctx-workspace-label");
    const mLabel = $("ctx-mode-label");
    if (wLabel) {
      wLabel.textContent =
        truncatePath(workspaceUi.currentPath, 42) ||
        workspaceUi.currentName ||
        "工作区";
    }
    if (mLabel) mLabel.textContent = strategyLabel(state.strategy);
    const wChip = $("ctx-workspace");
    if (wChip) wChip.title = workspaceUi.currentPath || "";
  }

  function closeCtxMenu() {
    workspaceUi.openMenu = null;
    workspaceUi.menuQuery = "";
    const menu = $("ctx-menu");
    const backdrop = $("ctx-backdrop");
    if (menu) menu.hidden = true;
    if (backdrop) backdrop.hidden = true;
    ["ctx-workspace", "ctx-mode"].forEach((id) => {
      const el = $(id);
      if (el) el.setAttribute("aria-expanded", "false");
    });
  }

  function openCtxMenu(kind, anchorEl) {
    const menu = $("ctx-menu");
    const backdrop = $("ctx-backdrop");
    if (!menu || !anchorEl) return;
    workspaceUi.openMenu = kind;
    anchorEl.setAttribute("aria-expanded", "true");
    menu.replaceChildren();
    menu.hidden = false;
    if (backdrop) backdrop.hidden = false;

    const rect = anchorEl.getBoundingClientRect();
    menu.style.left = Math.max(8, rect.left) + "px";
    menu.style.top = rect.bottom + 6 + "px";

    if (kind === "workspace") buildWorkspaceMenu(menu);
    else if (kind === "mode") buildModeMenu(menu);
  }

  function menuSection(menu, heading) {
    const sec = document.createElement("div");
    sec.className = "ds-ctx-menu-section";
    if (heading) {
      const h = document.createElement("div");
      h.className = "ds-ctx-menu-heading";
      h.textContent = heading;
      sec.appendChild(h);
    }
    menu.appendChild(sec);
    return sec;
  }

  function menuItem(sec, opts) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "ds-ctx-menu-item" + (opts.active ? " is-active" : "");
    btn.setAttribute("role", "menuitem");
    btn.innerHTML =
      '<span class="ds-ctx-menu-item-body">' +
      '<span class="ds-ctx-menu-item-title">' +
      escapeHtml(opts.title) +
      "</span>" +
      (opts.desc ? '<span class="ds-ctx-menu-item-desc">' + escapeHtml(opts.desc) + "</span>" : "") +
      "</span>" +
      '<svg class="ds-ctx-menu-check" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2"><path d="M5 12l4 4L19 7"/></svg>';
    btn.addEventListener("click", () => {
      closeCtxMenu();
      opts.onClick();
    });
    sec.appendChild(btn);
  }

  function buildWorkspaceMenu(menu) {
    const searchWrap = document.createElement("div");
    searchWrap.className = "ds-ctx-menu-search-wrap";
    const search = document.createElement("input");
    search.type = "search";
    search.className = "ds-ctx-menu-search";
    search.placeholder = "搜索或切换工作区…";
    search.autocomplete = "off";
    search.value = workspaceUi.menuQuery || "";
    search.addEventListener("input", (e) => {
      workspaceUi.menuQuery = e.target.value || "";
      menu.replaceChildren();
      buildWorkspaceMenu(menu);
      const next = menu.querySelector(".ds-ctx-menu-search");
      if (next) {
        next.focus();
        next.selectionStart = next.selectionEnd = next.value.length;
      }
    });
    searchWrap.appendChild(search);
    menu.appendChild(searchWrap);

    const q = (workspaceUi.menuQuery || "").trim().toLowerCase();
    function matchesQuery(path, name) {
      if (!q) return true;
      const label = (name || path.split(/[/\\]/).pop() || path).toLowerCase();
      return label.includes(q) || path.toLowerCase().includes(q);
    }

    const sec = menuSection(menu, "最近");

    if (workspaceUi.currentPath && matchesQuery(workspaceUi.currentPath, workspaceUi.currentName)) {
      menuItem(sec, {
        title: workspaceUi.currentName || workspaceUi.currentPath,
        desc: truncatePath(workspaceUi.currentPath, 52),
        active: true,
        onClick: () => {},
      });
    }
    (workspaceUi.recents || []).forEach((r) => {
      const p = typeof r === "string" ? r : r.path;
      const n = typeof r === "string" ? null : r.name;
      if (!p || pathsEqual(p, workspaceUi.currentPath) || !matchesQuery(p, n)) return;
      menuItem(sec, {
        title: n || p.split(/[/\\]/).pop() || p,
        desc: truncatePath(p, 52),
        active: false,
        onClick: () => setWorkspacePath(p).catch(() => {}),
      });
    });
    if (
      workspaceUi.homePath &&
      !pathsEqual(workspaceUi.homePath, workspaceUi.currentPath) &&
      matchesQuery(workspaceUi.homePath, "Home")
    ) {
      menuItem(sec, {
        title: "Home",
        desc: truncatePath(workspaceUi.homePath, 52),
        active: false,
        onClick: () => setWorkspacePath(workspaceUi.homePath).catch(() => {}),
      });
    }
    const actions = menuSection(menu, "");
    menuItem(actions, {
      title: "打开文件夹…",
      desc: "选择本地工作区目录",
      active: false,
      onClick: () => pickWorkspaceFolder().catch(() => {}),
    });
  }

  function buildModeMenu(menu) {
    const sec = menuSection(menu, "工作流");
    STRATEGY_OPTIONS.forEach((opt) => {
      menuItem(sec, {
        title: opt.label,
        desc: opt.desc,
        active: displayStrategy(state.strategy) === opt.id,
        onClick: () => {
          state.strategy = opt.id;
          renderContextBar();
          patchWorkspace({ defaultAgentStrategy: opt.id }).catch(() => {});
        },
      });
    });
  }

  function initWorkspaceUi() {
    $("btn-workspace-add")?.addEventListener("click", () => pickWorkspaceFolder().catch(() => {}));
    $("btn-workspace-filter")?.addEventListener("click", () => {
      workspaceUi.filterOpen = !workspaceUi.filterOpen;
      const box = $("workspace-filter");
      if (box) box.hidden = !workspaceUi.filterOpen;
      if (workspaceUi.filterOpen) $("workspace-search")?.focus();
      else {
        workspaceUi.filterQuery = "";
        const inp = $("workspace-search");
        if (inp) inp.value = "";
      }
      renderWorkspaceSidebar();
    });
    $("workspace-search")?.addEventListener("input", (e) => {
      workspaceUi.filterQuery = e.target.value || "";
      renderWorkspaceSidebar();
    });
    $("ctx-workspace")?.addEventListener("click", (e) => {
      e.stopPropagation();
      if (workspaceUi.openMenu === "workspace") closeCtxMenu();
      else {
        closeCtxMenu();
        openCtxMenu("workspace", $("ctx-workspace"));
      }
    });
    $("ctx-mode")?.addEventListener("click", (e) => {
      e.stopPropagation();
      if (workspaceUi.openMenu === "mode") closeCtxMenu();
      else {
        closeCtxMenu();
        openCtxMenu("mode", $("ctx-mode"));
      }
    });
    $("ctx-backdrop")?.addEventListener("click", closeCtxMenu);
    document.addEventListener("keydown", (e) => {
      if (e.key === "Escape") closeCtxMenu();
    });
  }

  async function init() {
    bindUi();
    initWorkspaceUi();
    resizeInput();
    bindWorkModeClient();

    const banner = $("login-banner");
    if (banner) {
      banner.hidden = true;
      banner.style.display = "none";
    }
    if (window.__dsLoggedIn === true) setLoggedIn(true);
    else if (window.__dsLoggedIn === false) setLoggedIn(false);

    await bootstrapStorage();
    loadWorkspaceFromHost().catch(() => {});
    ensureSlashCatalog().catch(() => {});
    post("nativeReady", {});
    post("refreshLoginState", {});
    flushPendingNativeMessages();

    const pollLogin = () => post("refreshLoginState", {});
    setInterval(pollLogin, 3000);
    [400, 1200, 2500].forEach((ms) => setTimeout(pollLogin, ms));

    setTimeout(() => {
      if (!state.authResolved) {
        pollLogin();
        setTimeout(() => {
          if (!state.authResolved) setLoggedIn(false);
        }, 2500);
      }
    }, 1200);

    document.addEventListener("visibilitychange", () => {
      if (document.visibilityState === "visible") pollLogin();
    });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", () => init());
  } else {
    init();
  }

  flushPendingNativeMessages();
})();
