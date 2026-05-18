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
    strategy: "react",
    storageBytes: 0,
    storageCount: 0,
    selectMode: false,
    selectedIds: new Set(),
  };

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

  function handlePendingReply(msg) {
    if (!msg?.reqId || !pendingRequests.has(msg.reqId)) return false;
    pendingRequests.get(msg.reqId)(msg);
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
        state.storageCount + " 条 · " + formatStorageSize(state.storageBytes);
    }
  }

  function sessionGroupLabel(updatedAt) {
    const ts = updatedAt || Date.now();
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

  async function migrateLegacyLocalStorage() {
    try {
      const raw = localStorage.getItem("ds-agent-sessions");
      if (!raw) return;
      const sessions = JSON.parse(raw);
      if (!Array.isArray(sessions) || !sessions.length) return;
      const now = Date.now();
      const normalized = sessions.map((s) => ({
        id: s.id || uid(),
        title: s.title || "新对话",
        createdAt: s.createdAt || now,
        updatedAt: s.updatedAt || now,
        messages: s.messages || [],
      }));
      await postAsync("agentStorageMigrate", { sessions: normalized });
      localStorage.removeItem("ds-agent-sessions");
    } catch (_) {}
  }

  async function refreshSessionList() {
    const res = await postAsync("agentStorageList", {});
    state.sessionMetas = Array.isArray(res.sessions) ? res.sessions : [];
    state.storageBytes = res.totalBytes || 0;
    state.storageCount = res.count || state.sessionMetas.length;
    updateStorageStats();
    renderSessions();
  }

  function getActiveSessionForSave() {
    if (!state.activeSessionId || !state.activeSession) return null;
    return {
      id: state.activeSessionId,
      title: state.activeSession.title || "新对话",
      createdAt: state.activeSession.createdAt || Date.now(),
      updatedAt: Date.now(),
      messages: state.activeSession.messages || [],
      tuiThreadId: state.activeSession.tuiThreadId || null,
    };
  }

  async function persistActiveSession() {
    const payload = getActiveSessionForSave();
    if (!payload) return;
    const res = await postAsync("agentStorageSave", { session: payload });
    state.storageBytes = res.totalBytes ?? state.storageBytes;
    state.storageCount = res.count ?? state.storageCount;
    const meta = state.sessionMetas.find((m) => m.id === payload.id);
    if (meta) {
      meta.title = payload.title;
      meta.updatedAt = payload.updatedAt;
    } else {
      state.sessionMetas.unshift({
        id: payload.id,
        title: payload.title,
        updatedAt: payload.updatedAt,
        createdAt: payload.createdAt,
      });
    }
    updateStorageStats();
    renderSessions();
  }

  function renderSessions() {
    const list = $("session-list");
    if (!list) return;
    list.innerHTML = "";

    const groups = new Map();
    state.sessionMetas.forEach((s) => {
      const label = sessionGroupLabel(s.updatedAt || s.createdAt);
      if (!groups.has(label)) groups.set(label, []);
      groups.get(label).push(s);
    });

    const order = ["今天", "昨天", "7 天内", "30 天内", "更早"];
    order.forEach((label) => {
      const items = groups.get(label);
      if (!items?.length) return;

      const gl = document.createElement("div");
      gl.className = "ds-session-group-label";
      gl.textContent = label;
      list.appendChild(gl);

      items.forEach((s) => {
        const row = document.createElement("div");
        row.className = "ds-session-row";

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
        row.appendChild(btn);
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

  function hideEmptyState() {
    $("empty-state")?.classList.add("ds-hidden");
  }

  function showEmptyState() {
    $("empty-state")?.classList.remove("ds-hidden");
  }

  const ICON_SEND =
    '<svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M3.4 20.6l17.6-8.2c.8-.4.8-1.5 0-1.9L3.4 2.3c-.8-.4-1.6.3-1.4 1.2l2.1 7.3c.1.4.5.7.9.7h6.2c.6 0 1 .4 1 1s-.4 1-1 1H5c-.4 0-.8.3-.9.7l-2.1 7.3c-.2.9.6 1.6 1.4 1.2z"/></svg>';
  const ICON_STOP =
    '<svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><rect x="7" y="7" width="10" height="10" rx="1.5"/></svg>';

  function setSendButtonMode(mode) {
    const send = $("btn-send");
    if (!send) return;
    const stopping = mode === "stopping";
    const running = mode === "running" || stopping;
    send.classList.toggle("ds-stop-mode", running);
    send.innerHTML = running ? ICON_STOP : ICON_SEND;
    if (running) {
      send.disabled = stopping;
      send.setAttribute("aria-label", stopping ? "正在停止" : "停止");
      send.title = stopping ? "正在停止…" : "停止生成";
    } else {
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

  const THINK_ICON_SVG =
    '<svg viewBox="0 0 24 24" fill="none"><circle cx="12" cy="12" r="10" fill="#EEF2FF"/><path d="M8 12h8M12 8v8" stroke="#4D6BFE" stroke-width="1.6" stroke-linecap="round"/></svg>';

  function truncate(s, max) {
    if (!s) return "";
    return s.length <= max ? s : s.slice(0, max) + "…";
  }

  function parseAgentLog(text) {
    const t = (text || "").trim();
    if (!t) return null;
    let m;
    if (/^DeepSeek-TUI ·/.test(t)) {
      return { kind: "step", verb: "DeepSeek-TUI", target: t.replace(/^DeepSeek-TUI ·\s*/, "").trim() || "运行中" };
    }
    m = t.match(/^工作区:\s*(.+)$/);
    if (m) return { kind: "step", verb: "工作区", target: m[1].trim(), muted: true };
    m = t.match(/^模式:\s*(.+)$/);
    if (m) return { kind: "step", verb: "模式", target: m[1].trim(), muted: true };
    m = t.match(/^线程:\s*(.+)$/);
    if (m) return { kind: "step", verb: "线程", target: m[1].trim(), muted: true };
    m = t.match(/^回合:\s*(.+)$/);
    if (m) return { kind: "step", verb: "回合", target: m[1].trim(), muted: true };
    m = t.match(/^待审批:\s*(.+)$/);
    if (m) return { kind: "step", verb: "审批", target: m[1].trim() };
    m = t.match(/^工具:\s*(.+)$/);
    if (m) return { kind: "step", verb: "工具", target: m[1].trim() };
    if (/^(已允许|已拒绝)$/.test(t)) return { kind: "step", verb: t, target: "", muted: true };
    m = t.match(/^--- ReAct 第 (\d+) 步 ---$/);
    if (m) return { kind: "section", label: "ReAct 第 " + m[1] + " 步" };
    m = t.match(/^══\s*子 Agent · 步骤 (.+?) ══$/);
    if (m) return { kind: "section", label: "子 Agent · " + m[1] };
    m = t.match(/^思考:\s*(.+)$/s);
    if (m) return { kind: "prose", text: m[1].trim() };
    m = t.match(/^Thought:\s*(.+)$/s);
    if (m) return { kind: "prose", text: m[1].trim() };
    m = t.match(/^Action:\s*调用\s*(\d+)\s*个工具/);
    if (m) return { kind: "step", verb: "Action", target: m[1] + " 个 MCP 工具" };
    m = t.match(/^Calling\s+(.+)$/i);
    if (m) return { kind: "step", verb: "Calling", target: m[1].trim() };
    m = t.match(/^→\s*(.+)$/);
    if (m) return { kind: "step", verb: "Calling", target: m[1].trim() };
    m = t.match(/^Observation:\s*(.+)$/s);
    if (m) return { kind: "step", verb: "Result", target: m[1].trim(), muted: true };
    if (/^Final Answer:/i.test(t)) return { kind: "skip" };
    m = t.match(/^策略:\s*(.+)$/);
    if (m) return { kind: "step", verb: "Strategy", target: m[1].trim() };
    m = t.match(/^\[计划模式\]\s*(.+)$/);
    if (m) return { kind: "step", verb: "Plan", target: m[1].trim() };
    m = t.match(/^  ·\s*(.+)$/);
    if (m) return { kind: "step", verb: "Step", target: m[1].trim(), muted: true };
    m = t.match(/^MCP 工具注册表:\s*(.+)$/);
    if (m) return { kind: "step", verb: "MCP", target: m[1].trim() };
    m = t.match(/^工具范围:\s*(.+)$/);
    if (m) return { kind: "step", verb: "Tools", target: m[1].trim(), muted: true };
    if (/^已连接/.test(t)) return { kind: "step", verb: "MCP", target: t };
    if (/^连接失败:/.test(t)) return { kind: "step", verb: "Error", target: t, error: true };
    if (/^错误:/.test(t)) return { kind: "step", verb: "Error", target: t, error: true };
    return { kind: "step", verb: "Log", target: t, muted: true };
  }

  function updateThinkTitle(run) {
    if (!run?.titleEl) return;
    const sec = Math.max(1, Math.floor((Date.now() - run.thinkStartTime) / 1000));
    run.titleEl.textContent = run.thinkDone
      ? "已完成（用时 " + sec + " 秒）"
      : "正努力工作（" + sec + " 秒）";
  }

  function fadeThinkSteps(stepsEl) {
    if (!stepsEl) return;
    const items = [...stepsEl.querySelectorAll(".ds-step")];
    items.forEach((el, i) => {
      el.classList.toggle("ds-step-faded", i < items.length - 10);
    });
  }

  function pushThinkRecord(run, entry) {
    if (!run.thinkRecords) run.thinkRecords = [];
    run.thinkRecords.push(entry);
    if (run.thinkRecords.length > 200) run.thinkRecords.shift();
  }

  function renderThinkSection(stepsEl, label) {
    const h = document.createElement("div");
    h.className = "ds-step-section";
    h.textContent = label;
    stepsEl.appendChild(h);
    return { kind: "section", label };
  }

  function renderThinkStep(stepsEl, step) {
    const row = document.createElement("div");
    row.className = "ds-step" + (step.error ? " ds-step-error" : "");
    const verb = document.createElement("span");
    verb.className = "ds-step-verb";
    verb.textContent = step.verb;
    const target = document.createElement("span");
    target.className = "ds-step-target";
    target.textContent = truncate(step.target, 220);
    row.append(verb, target);
    stepsEl.appendChild(row);
    fadeThinkSteps(stepsEl);
    return { kind: "step", verb: step.verb, target: step.target, error: !!step.error };
  }

  function applyThinkLog(run, parsed) {
    if (!run || !parsed || parsed.kind === "skip") return;
    if (parsed.kind === "prose") {
      run.proseEl.textContent = parsed.text;
      pushThinkRecord(run, { kind: "prose", text: parsed.text });
    } else if (parsed.kind === "section") {
      renderThinkSection(run.stepsEl, parsed.label);
      pushThinkRecord(run, { kind: "section", label: parsed.label });
    } else if (parsed.kind === "step") {
      pushThinkRecord(run, renderThinkStep(run.stepsEl, parsed));
    }
    updateThinkTitle(run);
    scrollToBottom();
  }

  function appendLogLine(text) {
    const run = state.currentRun;
    if (!run?.stepsEl) return;
    applyThinkLog(run, parseAgentLog(text));
  }

  function createThinkBlock() {
    const think = document.createElement("div");
    think.className = "ds-think";
    const header = document.createElement("button");
    header.type = "button";
    header.className = "ds-think-header";
    header.setAttribute("aria-expanded", "true");
    const icon = document.createElement("span");
    icon.className = "ds-think-icon";
    icon.innerHTML = THINK_ICON_SVG;
    const title = document.createElement("span");
    title.className = "ds-think-title";
    title.textContent = "正努力工作…";
    const chevron = document.createElement("span");
    chevron.className = "ds-think-chevron";
    chevron.innerHTML =
      '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M6 9l6 6 6-6"/></svg>';
    header.append(icon, title, chevron);
    const body = document.createElement("div");
    body.className = "ds-think-body";
    const prose = document.createElement("div");
    prose.className = "ds-think-prose";
    const steps = document.createElement("div");
    steps.className = "ds-think-steps";
    body.append(prose, steps);
    think.append(header, body);
    header.addEventListener("click", () => {
      const collapsed = think.classList.toggle("ds-collapsed");
      header.setAttribute("aria-expanded", collapsed ? "false" : "true");
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
      titleEl: title,
      proseEl: prose,
      stepsEl: steps,
      thinkStartTime: Date.now(),
      thinkDone: false,
      thinkRecords: [],
    };
  }

  function finalizeThinkBlock(run) {
    if (!run) return;
    run.thinkDone = true;
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
      if (rec.kind === "prose") think.proseEl.textContent = rec.text;
      else if (rec.kind === "section") renderThinkSection(think.stepsEl, rec.label);
      else if (rec.kind === "step") {
        const row = document.createElement("div");
        row.className = "ds-step" + (rec.error ? " ds-step-error" : "");
        const verb = document.createElement("span");
        verb.className = "ds-step-verb";
        verb.textContent = rec.verb;
        const target = document.createElement("span");
        target.className = "ds-step-target";
        target.textContent = truncate(rec.target, 220);
        row.append(verb, target);
        think.stepsEl.appendChild(row);
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
    const tuiThreadId = raw.tuiThreadId ?? raw.TuiThreadId ?? null;
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
      messages,
      tuiThreadId,
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
        answer.textContent = answerText;
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
    if (state.running) return;
    if (!id) return;
    await persistActiveSession().catch(() => {});
    const res = await postAsync("agentStorageLoad", { id });
    const raw = res.session;
    if (!raw) return;
    const s = normalizeSession(raw);
    if (!s) return;
    state.activeSessionId = id;
    state.activeSession = s;
    renderSessions();
    renderSessionMessages(s.messages);
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

  function agentHelpText() {
    return (
      "DeepSeek 桌面 Agent（DeepSeek-TUI）\n" +
      "/help  本帮助\n" +
      "/clear  清空对话\n" +
      "/react  Agent 模式（多步工具，Tab 对应官网 Agent）\n" +
      "/plan   Plan 模式（只读调研，官网 Plan）\n" +
      "/chat   返回普通对话\n\n" +
      "推理：本地 Chat2API（须先在网页登录）\n" +
      "工具 / MCP / Skills：~/.deepseek/"
    );
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
    if (cmd === "/react") {
      state.strategy = "react";
      appendMessage("assistant", "已切换为 Agent 模式（多步工具，DeepSeek-TUI）。", { status: "模式" });
      return true;
    }
    if (cmd === "/plan") {
      state.strategy = "plan";
      appendMessage("assistant", "已切换为 Plan 模式（只读调研，DeepSeek-TUI）。", { status: "模式" });
      return true;
    }
    if (cmd === "/chat") {
      post("setWorkMode", { mode: "chat" });
      return true;
    }
    if (cmd === "/skills" || cmd === "/skill" || cmd === "/agents" || cmd === "/agent") {
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

    state.currentRun = appendMessage("assistant", "", { status: "DeepSeek-TUI 正在执行…" });
    state.running = true;
    syncAllUserMessageActions();
    updateComposerState();
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
      deepThink: true,
      smartSearch: true,
      mcpOn: true,
      refFileIds,
      sessionId: state.activeSessionId,
      tuiThreadId: state.activeSession?.tuiThreadId || null,
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
    if (state.currentRun?.titleEl) {
      state.currentRun.titleEl.textContent = "正在停止…";
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
        state.currentRun.answerEl.textContent = text;
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

    if (msg.type === "apiInfo" || msg.type === "loginState") {
      setLoggedIn(!!msg.loggedIn);
      if (msg.type === "apiInfo" && msg.agentStrategy) {
        state.strategy = msg.agentStrategy === "plan" ? "plan" : "react";
      }
    }

    if (msg.type === "agentLog" && msg.text) {
      appendLogLine(msg.text);
    }

    if (msg.type === "agentAnswer" && msg.text) {
      if (state.currentRun?.answerEl) {
        if (msg.append) {
          state.currentRun.answerEl.textContent += msg.text;
        } else {
          state.currentRun.answerEl.textContent = msg.text;
        }
      }
    }

    if (msg.type === "agentTuiThread" && msg.tuiThreadId && state.activeSession) {
      state.activeSession.tuiThreadId = msg.tuiThreadId;
      persistActiveSession().catch(() => {});
    }

    if (msg.type === "agentStarted") {
      updateThinkTitle(state.currentRun);
    }

    if (msg.type === "agentDone") {
      finishRun(msg.summary, msg.answer);
    }
  };

  function bindUi() {
    $("btn-send")?.addEventListener("click", () => {
      if (state.running) cancelRun();
      else dispatchRun();
    });
    $("btn-new-chat")?.addEventListener("click", () => startNewChat());
    $("btn-mcp")?.addEventListener("click", () => post("openSettings", {}));
    $("btn-storage-settings")?.addEventListener("click", () => post("openSettings", {}));
    $("mode-float")?.addEventListener("click", () => {
      post("setWorkMode", { mode: "chat" });
    });
    $("brand-home")?.addEventListener("click", (e) => {
      e.preventDefault();
    });
    $("btn-login-goto")?.addEventListener("click", () => post("setWorkMode", { mode: "chat" }));

    const fileInput = $("file-input");
    const attachBtn = $("btn-attach");
    attachBtn?.addEventListener("click", () => fileInput?.click());
    fileInput?.addEventListener("change", () => {
      if (fileInput.files?.length) handleFiles([...fileInput.files]);
      fileInput.value = "";
    });

    const input = $("chat-input");
    input?.addEventListener("input", () => {
      resizeInput();
      updateComposerState();
    });
    input?.addEventListener("keydown", (e) => {
      if (e.key === "Enter" && !e.shiftKey && !e.isComposing) {
        e.preventDefault();
        dispatchRun();
      }
    });

    $("btn-manage-sessions")?.addEventListener("click", () => setSelectMode(true));
    $("btn-manage-done")?.addEventListener("click", () => setSelectMode(false));
    $("btn-select-all")?.addEventListener("click", () => {
      state.sessionMetas.forEach((s) => state.selectedIds.add(s.id));
      renderSessions();
    });
    $("btn-delete-selected")?.addEventListener("click", async () => {
      const ids = [...state.selectedIds];
      if (!ids.length) {
        alert("请先勾选要删除的对话。");
        return;
      }
      if (!confirm("确定删除所选 " + ids.length + " 条对话？此操作不可恢复。")) return;
      const res = await postAsync("agentStorageDelete", { ids });
      state.sessionMetas = res.sessions || [];
      state.storageBytes = res.totalBytes || 0;
      state.storageCount = res.count || 0;
      if (ids.includes(state.activeSessionId)) {
        state.activeSessionId = null;
        state.activeSession = null;
        if (state.sessionMetas.length) {
          await loadSession(state.sessionMetas[0].id);
        } else {
          await startNewChat();
        }
      }
      setSelectMode(false);
      updateStorageStats();
      renderSessions();
    });
  }

  async function bootstrapStorage() {
    try {
      await migrateLegacyLocalStorage();
      await refreshSessionList();
      if (state.sessionMetas.length) {
        await loadSession(state.sessionMetas[0].id);
      } else {
        prepareEmptyChat();
      }
    } catch (e) {
      console.warn("Agent storage init:", e);
      prepareEmptyChat();
    }
  }

  async function init() {
    bindUi();
    resizeInput();

    const banner = $("login-banner");
    if (banner) {
      banner.hidden = true;
      banner.style.display = "none";
    }
    if (window.__dsLoggedIn === true) setLoggedIn(true);
    else if (window.__dsLoggedIn === false) setLoggedIn(false);

    post("nativeReady", {});
    post("refreshLoginState", {});
    flushPendingNativeMessages();

    await bootstrapStorage();

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
