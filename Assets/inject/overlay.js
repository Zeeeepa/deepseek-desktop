(function () {
  if (/^ds-agent\.local$/i.test(location.hostname)) return;

  function post(type, payload) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(JSON.stringify({ type, ...(payload || {}) }));
    }
  }

  function domRoot() {
    return document.documentElement || null;
  }

  function domBody() {
    return document.body || null;
  }

  function appendToHead(el) {
    const parent = document.head || domRoot();
    if (!parent || !el) return false;
    parent.appendChild(el);
    return true;
  }

  function appendToBody(el) {
    const parent = domBody() || domRoot();
    if (!parent || !el) return false;
    parent.appendChild(el);
    return true;
  }

  let workMode = "chat";
  let workModeUi = null;
  let modeMenuOpen = false;
  let toolbarInjected = false;
  let dockInjected = false;
  let apiOnline = null;
  let burstTimer = null;

  function norm(s) {
    return (s || "").replace(/\s+/g, "");
  }

  function elText(el) {
    return norm(el.textContent || el.innerText || el.getAttribute("aria-label") || "");
  }

  function findDeepThinkBtn() {
    const all = [...document.querySelectorAll("button,[role='button']")];
    return (
      document.querySelector('[aria-label="深度思考"]') ||
      all.find((el) => elText(el).includes("深度思考")) ||
      null
    );
  }

  function findSmartSearchBtn() {
    const all = [...document.querySelectorAll("button,[role='button']")];
    return (
      document.querySelector('[aria-label="智能搜索"]') ||
      all.find((el) => elText(el).includes("智能搜索")) ||
      null
    );
  }

  function findInput() {
    return (
      document.querySelector('[role="textbox"][placeholder*="DeepSeek"]') ||
      document.querySelector('textarea[placeholder*="DeepSeek"]') ||
      document.querySelector('[contenteditable="true"][placeholder*="DeepSeek"]') ||
      document.querySelector('[role="textbox"]') ||
      document.querySelector("textarea") ||
      null
    );
  }

  function getInputText(el) {
    if (!el) return "";
    if ("value" in el && typeof el.value === "string") return el.value.trim();
    return (el.textContent || el.innerText || "").trim();
  }

  function setInputText(el, text) {
    if (!el) return;
    if ("value" in el) {
      el.value = text;
      el.dispatchEvent(new Event("input", { bubbles: true }));
      return;
    }
    el.textContent = text;
    el.dispatchEvent(new Event("input", { bubbles: true }));
  }

  function findSendButton() {
    const input = findInput();
    if (!input) return null;
    let root = input.closest("form") || input.parentElement;
    for (let i = 0; i < 10 && root; i++) {
      const buttons = [...root.querySelectorAll("button,[role='button']")];
      const send = buttons.find((b) => {
        const t = elText(b);
        if (t.includes("深度思考") || t.includes("智能搜索") || t.includes("本地") || t.includes("MCP")) return false;
        return b.querySelector("svg") || b.className.includes("send");
      });
      if (send) return send;
      root = root.parentElement;
    }
    return null;
  }

  function readNativeMode() {
    const radios = [...document.querySelectorAll('[role="radio"],button')];
    for (const name of ["专家", "识图", "快速"]) {
      const el = radios.find((r) => elText(r).includes(name));
      if (!el) continue;
      if (el.getAttribute("aria-checked") === "true" || el.getAttribute("aria-pressed") === "true") return name;
      if (el.className && /active|selected|checked/i.test(el.className)) return name;
    }
    return "快速";
  }

  function readToggle(label) {
    const btn = findDeepThinkBtn();
    const search = findSmartSearchBtn();
    if (label === "深度思考" && btn) {
      return btn.getAttribute("aria-pressed") === "true" || btn.className.includes("active");
    }
    if (label === "智能搜索" && search) {
      return search.getAttribute("aria-pressed") === "true" || search.className.includes("active");
    }
    return false;
  }

  const ICONS = {
    api: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M12 3v3M8 6h8M6 9h12v9a3 3 0 0 1-3 3H9a3 3 0 0 1-3-3V9z"/><path d="M9 14h6"/></svg>',
    agent: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M12 3l1.5 4.5L18 9l-4.5 1.5L12 15l-1.5-4.5L6 9l4.5-1.5L12 3z"/><path d="M5 18h14M8 21h8"/></svg>',
    settings: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="12" cy="12" r="3"/><path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.41-1.41M17.66 6.34l1.41-1.41"/></svg>',
  };

  const BTN_OFF = {
    display: "inline-flex",
    alignItems: "center",
    gap: "6px",
    height: "32px",
    padding: "0 14px",
    borderRadius: "9999px",
    border: "1px solid rgb(229, 231, 235)",
    background: "rgb(255, 255, 255)",
    color: "rgb(55, 65, 81)",
    fontSize: "14px",
    lineHeight: "20px",
    fontFamily: "inherit",
    cursor: "pointer",
    whiteSpace: "nowrap",
    boxSizing: "border-box",
    verticalAlign: "middle",
  };

  const BTN_ON = {
    background: "rgb(238, 242, 255)",
    border: "1px solid rgb(77, 107, 254)",
    color: "rgb(77, 107, 254)",
  };

  function ensureStyles() {
    if (!domRoot() && !document.head) return;
    if (!document.getElementById("ds-inject-css-link")) {
      const link = document.createElement("link");
      link.id = "ds-inject-css-link";
      link.rel = "stylesheet";
      link.href = "https://ds-inject.local/overlay.css";
      appendToHead(link);
    }
    if (!document.getElementById("ds-native-style-fallback")) {
      const style = document.createElement("style");
      style.id = "ds-native-style-fallback";
      style.textContent =
        "#ds-toast-wrap{position:fixed!important;right:20px!important;bottom:24px!important;left:auto!important;top:auto!important;z-index:2147483647!important;max-width:360px!important;pointer-events:none!important}#ds-provider-mask{position:fixed!important;inset:0!important;z-index:2147483646!important;display:flex!important;align-items:center!important;justify-content:center!important;background:rgba(0,0,0,.4)!important}.ds-mode-menu-portal{position:fixed!important;z-index:2147483647!important;pointer-events:auto!important}#ds-agent-mode-float,.ds-agent-mode-float-btn{position:fixed!important;top:14px!important;right:16px!important;left:auto!important;bottom:auto!important;z-index:2147483647!important;display:inline-flex!important;align-items:center!important;justify-content:center!important;visibility:visible!important;opacity:1!important;pointer-events:auto!important}.ds-mode-wrap{margin:0!important;padding:0!important}";
      appendToHead(style);
    }
  }

  function isModeFloater(btn) {
    return !!(
      btn &&
      (btn.id === "ds-agent-mode-float" ||
        btn.classList.contains("ds-agent-mode-float") ||
        btn.classList.contains("ds-agent-mode-float-btn"))
    );
  }

  function applyBtnStyle(btn, on) {
    if (isModeFloater(btn)) return;
    Object.assign(btn.style, BTN_OFF);
    if (on) Object.assign(btn.style, BTN_ON);
  }

  function findToolbarHost() {
    const think = findDeepThinkBtn();
    const search = findSmartSearchBtn();
    if (think && search && think.parentElement === search.parentElement) {
      return think.parentElement;
    }
    const ref = think || search;
    if (!ref) return null;
    let node = ref.parentElement;
    for (let i = 0; i < 8 && node; i++) {
      const t = findDeepThinkBtn();
      const s = findSmartSearchBtn();
      if (t && s && node.contains(t) && node.contains(s) && t.parentElement === s.parentElement) {
        return t.parentElement;
      }
      node = node.parentElement;
    }
    return ref.parentElement;
  }

  function getToolbarParts() {
    const settings = document.getElementById("ds-settings");
    return { settings };
  }

  function removeToolbarParts() {
    document.querySelectorAll("[data-ds-toolbar='1']").forEach((el) => el.remove());
    document.getElementById("ds-native-toolbar")?.remove();
    toolbarInjected = false;
  }

  function measureGap(leftEl, rightEl) {
    if (!leftEl || !rightEl) return 0;
    return Math.max(0, rightEl.getBoundingClientRect().left - leftEl.getBoundingClientRect().right);
  }

  function syncToolbarSpacing() {
    const think = findDeepThinkBtn();
    const search = findSmartSearchBtn();
    const { settings } = getToolbarParts();
    if (!think || !settings) return;

    settings.style.marginLeft = "0";
    settings.style.marginRight = "0";

    const refGap = measureGap(think, settings);
    if (refGap <= 0) return;

    if (search) {
      const gapMcpSearch = measureGap(settings, search);
      const deltaSearch = refGap - gapMcpSearch;
      if (Math.abs(deltaSearch) > 0.5) {
        settings.style.marginRight = deltaSearch + "px";
      }
    }
  }

  function isToolbarOrderCorrect() {
    const think = findDeepThinkBtn();
    const search = findSmartSearchBtn();
    const { settings } = getToolbarParts();
    const host = findToolbarHost();
    if (!host || !settings) return false;
    if (settings.parentElement !== host) return false;
    const nodes = [...host.children];
    const ti = think ? nodes.indexOf(think) : -1;
    const si = nodes.indexOf(settings);
    const ss = search ? nodes.indexOf(search) : -1;
    if (si < 0) return false;
    if (think && ti >= 0 && ti >= si) return false;
    if (search && ss >= 0 && !(si < ss)) return false;
    return true;
  }

  function cloneNativeClasses(btn) {
    btn.className = "ds-native-btn ds-ext-btn";
  }

  function createBtn(id, label, iconKey, withDot) {
    ensureStyles();
    const btn = document.createElement("button");
    btn.type = "button";
    cloneNativeClasses(btn);
    btn.id = id;
    btn.setAttribute("aria-label", label);
    applyBtnStyle(btn, false);

    if (iconKey && ICONS[iconKey]) {
      const icon = document.createElement("span");
      icon.className = "ds-native-icon";
      icon.style.cssText = "display:inline-flex;width:16px;height:16px;color:inherit";
      icon.innerHTML = ICONS[iconKey];
      btn.appendChild(icon);
    }
    if (withDot) {
      const dot = document.createElement("span");
      dot.className = "ds-native-dot";
      dot.id = id + "-dot";
      dot.style.cssText = "width:8px;height:8px;border-radius:50%;background:#9ca3af;flex-shrink:0";
      btn.appendChild(dot);
    }

    const span = document.createElement("span");
    span.className = "ds-native-label";
    span.textContent = label;
    btn.appendChild(span);
    return btn;
  }


  function isAgentLikeMode() {
    if (window.DsWorkMode && window.DsWorkMode.getState()) return window.DsWorkMode.isAgentLike();
    return workMode === "agent" || workMode === "plan";
  }

  function bindWorkModeClient() {
    if (window.__dsWorkModeClientBound) return;
    if (!window.DsWorkMode) {
      window.__dsWorkModeBindAttempts = (window.__dsWorkModeBindAttempts || 0) + 1;
      if (window.__dsWorkModeBindAttempts < 60) setTimeout(bindWorkModeClient, 100);
      return;
    }
    window.__dsWorkModeClientBound = true;
    window.DsWorkMode.onChange(function (st) {
      workModeUi = st;
      workMode = st.mode;
      syncWorkModeUi(st);
      if (st.mode === "chat" && isDeepSeekOfficialPage()) teardownChatPageOverrides();
    });
    window.DsWorkMode.flushPending();
    post("requestWorkModeState", {});
  }

  /** 官网 chat.deepseek.com：普通模式保持原生 UI，不做工具栏/发送拦截等注入 */
  function isDeepSeekOfficialPage() {
    return /chat\.deepseek\.com/i.test(location.hostname);
  }

  function shouldKeepNativeDeepSeekUi() {
    return isDeepSeekOfficialPage();
  }

  function teardownChatPageOverrides() {
    removeToolbarParts();
    document.getElementById("ds-dock-bar")?.remove();
    dockInjected = false;
    toolbarInjected = false;
    document.getElementById("ds-agent-mode-hint")?.remove();
    hideAgentLogPanel();
    removeAgentRunBlock();
    closeModeMenus();
    document.querySelectorAll(".ds-mode-menu").forEach((m) => m.remove());
  }

  function syncWorkModeUi(st) {
    if (st && isDeepSeekOfficialPage() && st.surface === "agent") return;
    const ui = st || workModeUi || {
      label: isAgentLikeMode() ? "Agent" : "普通",
      title: isAgentLikeMode()
        ? "当前为 Agent 模式，点击切换到普通对话"
        : "当前为普通对话，点击切换到 Agent",
      highlight: isAgentLikeMode(),
    };
    const btn = document.getElementById("ds-agent-mode-float");
    if (btn) {
      let label = getFloaterLabel(btn);
      if (!label) {
        label = document.createElement("span");
        label.className = "ds-native-label";
        label.id = "ds-agent-mode-float-label";
        btn.appendChild(label);
      } else if (!label.id) {
        label.id = "ds-agent-mode-float-label";
      }
      label.textContent = ui.label;
      btn.dataset.dsOn = ui.highlight ? "1" : "0";
      btn.classList.toggle("ds-on", !!ui.highlight);
      btn.style.cssText = "";
      btn.setAttribute("aria-label", ui.title);
      btn.title = ui.title;
      bindFloaterClick(btn);
    }
    if (!shouldKeepNativeDeepSeekUi()) {
      updateAgentModeHint();
      scheduleToolbarSpacing();
    } else {
      document.getElementById("ds-agent-mode-hint")?.remove();
    }
  }

  function onFloaterClick(e) {
    if (e) {
      e.preventDefault();
      e.stopPropagation();
    }
    if (isDeepSeekOfficialPage()) syncTokenFromPage();
    if (window.DsWorkMode && window.DsWorkMode.activateFloater) {
      window.DsWorkMode.activateFloater();
      return;
    }
    post("toggleWorkMode", {});
  }

  function bindFloaterClick(btn) {
    if (!btn) return;
    if (window.DsWorkMode && window.DsWorkMode.markFloater) window.DsWorkMode.markFloater(btn);
  }

  const DS_AGENT_URL = "https://ds-agent.local/index.html";
  const DS_CHAT_URL = "https://chat.deepseek.com/";

  function isAgentHostPage() {
    return /ds-agent\.local/i.test(location.hostname);
  }

  function readChatUserToken() {
    try {
      const raw = localStorage.getItem("userToken");
      if (!raw) return null;
      let token = raw;
      try {
        const parsed = JSON.parse(raw);
        if (typeof parsed === "string") token = parsed;
        else if (parsed && typeof parsed === "object" && typeof parsed.value === "string")
          token = parsed.value;
      } catch (_) {}
      return token || null;
    } catch (_) {
      return null;
    }
  }

  function syncTokenFromPage() {
    const token = readChatUserToken();
    if (token) post("syncToken", { token });
  }

  function watchChatUserToken() {
    if (!isDeepSeekOfficialPage() || window.__dsTokenWatch) return;
    window.__dsTokenWatch = true;
    let last = "";
    const tick = () => {
      try {
        const raw = localStorage.getItem("userToken") || "";
        if (!raw || raw === last) return;
        last = raw;
        syncTokenFromPage();
      } catch (_) {}
    };
    tick();
    setInterval(tick, 2500);
  }

  function navigateForWorkMode(mode) {
    const wantAgent = mode === "agent" || mode === "plan";
    if (wantAgent) {
      if (!isAgentHostPage()) location.assign(DS_AGENT_URL);
      return;
    }
    if (isAgentHostPage()) location.assign(DS_CHAT_URL);
  }

  function requestWorkMode(mode, notify) {
    const goingAgent = mode === "agent" || mode === "plan";
    const extra = {};
    if (goingAgent && isDeepSeekOfficialPage()) {
      syncTokenFromPage();
      const token = readChatUserToken();
      if (token) extra.token = token;
    }
    if (window.DsWorkMode) window.DsWorkMode.requestSet(mode, extra);
    else post("setWorkMode", Object.assign({ mode: mode }, extra));
    if (notify) {
      if (goingAgent) showToast("Agent", ["已打开 Agent 工作台"]);
      else showToast("普通对话", ["已打开官网对话页"]);
    }
  }

  function setWorkMode(mode, notify) {
    requestWorkMode(mode, notify);
  }

  function getFloaterLabel(btn) {
    return (
      btn.querySelector("#ds-agent-mode-float-label") || btn.querySelector(".ds-native-label")
    );
  }

  let activeModeMenu = null;
  let activeModeAnchor = null;

  const MODE_MENU_STYLE =
    "display:none;position:fixed;min-width:168px;background:#fff;border:1px solid #e5e7eb;border-radius:12px;box-shadow:0 8px 24px rgba(0,0,0,.12);padding:6px;z-index:2147483647;pointer-events:auto";

  function positionModeMenu(anchorBtn, menu) {
    const r = anchorBtn.getBoundingClientRect();
    menu.style.display = "block";
    const menuH = menu.offsetHeight || 200;
    let top = r.bottom + 6;
    if (top + menuH > window.innerHeight - 8 && r.top - menuH - 6 > 0) {
      top = r.top - menuH - 6;
    }
    const left = Math.min(Math.max(8, r.left), window.innerWidth - (menu.offsetWidth || 168) - 8);
    menu.style.position = "fixed";
    menu.style.left = left + "px";
    menu.style.top = top + "px";
    menu.style.minWidth = Math.max(168, r.width) + "px";
  }

  function openModeMenu(anchorBtn, menu) {
    if (!domBody()) return;
    if (menu.parentElement !== document.body) {
      appendToBody(menu);
    }
    menu.classList.add("ds-mode-menu-portal");
    positionModeMenu(anchorBtn, menu);
    activeModeMenu = menu;
    activeModeAnchor = anchorBtn;
    modeMenuOpen = true;
  }

  function closeModeMenus() {
    modeMenuOpen = false;
    activeModeMenu = null;
    activeModeAnchor = null;
    document.querySelectorAll(".ds-mode-menu").forEach((m) => {
      m.style.display = "none";
    });
  }

  function createModeSelector(btnId, menuId) {
    const btn = createBtn(btnId, isAgentLikeMode() ? "Agent" : "普通", "agent", false);
    btn.setAttribute("data-ds-toolbar", "1");
    applyBtnStyle(btn, isAgentLikeMode());
    if (isAgentLikeMode()) btn.classList.add("ds-on");
    const chev = document.createElement("span");
    chev.textContent = "▾";
    chev.style.cssText = "font-size:10px;margin-left:2px;opacity:.7";
    btn.querySelector(".ds-native-label")?.after(chev);

    let menu = document.getElementById(menuId);
    if (!menu) {
      menu = document.createElement("div");
      menu.id = menuId;
      menu.className = "ds-mode-menu ds-mode-menu-portal";
      menu.style.cssText = MODE_MENU_STYLE;
      function addItem(label, handler, modeAttr) {
        const item = document.createElement("button");
        item.type = "button";
        item.className = "ds-mode-item";
        if (modeAttr) item.setAttribute("data-mode", modeAttr);
        item.textContent = label;
        item.style.cssText =
          "display:block;width:100%;text-align:left;padding:8px 12px;border:none;background:transparent;border-radius:8px;font-size:13px;color:#374151;cursor:pointer";
        item.onclick = (e) => {
          e.stopPropagation();
          handler();
          closeModeMenus();
        };
        menu.appendChild(item);
      }
      addItem("对话", () => setWorkMode("chat", true), "chat");
      addItem("Agent · ReAct", () => setWorkMode("agent", true), "agent");
      addItem("计划 · 子 Agent", () => setWorkMode("plan", true), "plan");
      addItem("打开 Agent 工作台…", () => post("openAgentWorkspace", {}), null);
      appendToBody(menu);
    }

    btn.addEventListener("click", (e) => {
      e.stopPropagation();
      const opening = !(modeMenuOpen && activeModeMenu === menu);
      closeModeMenus();
      if (opening) {
        openModeMenu(btn, menu);
      }
    });
    return btn;
  }

  const agentChat = {
    active: false,
    blockEl: null,
    contentEl: null,
    statusEl: null,
    answerEl: null,
    logEl: null,
    listEl: null,
  };

  function scrollChatToBottom(container) {
    const root = container || agentChat.listEl || findChatScrollRoot();
    if (!root) return;
    try {
      root.scrollTop = root.scrollHeight;
    } catch (_) {}
    const scrollRoot = findChatScrollRoot();
    if (scrollRoot && scrollRoot !== root) {
      try {
        scrollRoot.scrollTop = scrollRoot.scrollHeight;
      } catch (_) {}
    }
    requestAnimationFrame(() => {
      try {
        if (scrollRoot) scrollRoot.scrollTop = scrollRoot.scrollHeight;
      } catch (_) {}
    });
  }

  function findChatScrollRoot() {
    const input = findInput();
    if (!input) return document.querySelector("main") || document.body;
    let best = null;
    let bestScore = 0;
    let node = input.parentElement;
    for (let i = 0; i < 32 && node; i++) {
      const style = getComputedStyle(node);
      const r = node.getBoundingClientRect();
      const scrollable =
        style.overflowY === "auto" ||
        style.overflowY === "scroll" ||
        style.overflowY === "overlay";
      const score =
        (scrollable ? 4 : 0) +
        (r.height > 200 ? 2 : 0) +
        (r.width > window.innerWidth * 0.35 ? 2 : 0);
      if (score > bestScore) {
        bestScore = score;
        best = node;
      }
      node = node.parentElement;
    }
    return best || document.querySelector("main") || document.body;
  }

  function findAgentChatMount() {
    const scroll = findChatScrollRoot();
    const scrollStyle = getComputedStyle(scroll);
    if (
      scrollStyle.flexDirection.includes("column") ||
      scroll.getBoundingClientRect().width > window.innerWidth * 0.4
    ) {
      return scroll;
    }
    let best = scroll;
    let bestH = 0;
    for (const child of scroll.children) {
      if (!child || child.id?.startsWith("ds-")) continue;
      const r = child.getBoundingClientRect();
      const cs = getComputedStyle(child);
      if (r.height > bestH && r.width > window.innerWidth * 0.3) {
        if (cs.flexDirection.includes("column") || cs.display === "block") {
          bestH = r.height;
          best = child;
        }
      }
    }
    return best;
  }

  function removeAgentRunBlock() {
    document.querySelectorAll("#ds-agent-run-block").forEach((n) => n.remove());
    document
      .querySelectorAll("[data-ds-agent-injected]:not(#ds-agent-run-block)")
      .forEach((n) => n.remove());
  }

  function beginAgentChatInThread(userText) {
    removeAgentRunBlock();
    resetAgentChat(false);
    agentChat.active = true;

    const mount = findAgentChatMount();
    agentChat.listEl = mount;

    const block = document.createElement("div");
    block.id = "ds-agent-run-block";
    block.setAttribute("data-ds-agent-injected", "1");
    block.style.cssText =
      "flex:0 0 100%;width:100%;max-width:48rem;min-width:min(100%,320px);margin:0 auto;padding:20px 24px 28px;box-sizing:border-box;display:flex;flex-direction:column;gap:16px;align-self:stretch;";

    const userRow = document.createElement("div");
    userRow.style.cssText = "display:flex;justify-content:flex-end;width:100%;";
    const userBubble = document.createElement("div");
    userBubble.style.cssText =
      "max-width:85%;padding:12px 16px;border-radius:16px;border-bottom-right-radius:4px;background:#4d6bfe;color:#fff;font-size:15px;line-height:1.6;white-space:pre-wrap;word-break:break-word;";
    userBubble.textContent = userText;
    userRow.appendChild(userBubble);

    const asstRow = document.createElement("div");
    asstRow.style.cssText = "display:flex;justify-content:flex-start;width:100%;";
    const asstBubble = document.createElement("div");
    asstBubble.style.cssText =
      "max-width:92%;min-width:200px;padding:12px 16px;border-radius:16px;border-bottom-left-radius:4px;background:#f3f4f6;color:#111827;font-size:15px;line-height:1.6;";
    agentChat.contentEl = asstBubble;

    const status = document.createElement("div");
    status.style.cssText = "font-size:13px;color:#6b7280;margin-bottom:8px;";
    status.textContent = "Agent 正在执行（Chat2API + MCP）…";
    agentChat.statusEl = status;

    const answer = document.createElement("div");
    answer.setAttribute("data-ds-agent-answer", "1");
    answer.style.cssText =
      "font-size:15px;line-height:1.65;white-space:pre-wrap;word-break:break-word;color:#111827;display:none;";
    agentChat.answerEl = answer;

    const details = document.createElement("details");
    details.style.cssText = "margin-top:10px;font-size:12px;color:#6b7280;";
    const summary = document.createElement("summary");
    summary.textContent = "查看执行日志";
    summary.style.cursor = "pointer";
    const logBody = document.createElement("div");
    logBody.style.cssText =
      "margin-top:6px;max-height:200px;overflow-y:auto;font-family:Consolas,monospace;line-height:1.45;white-space:pre-wrap;";
    details.append(summary, logBody);
    agentChat.logEl = logBody;

    asstBubble.append(status, answer, details);
    asstRow.appendChild(asstBubble);
    block.append(userRow, asstRow);
    mount.appendChild(block);
    agentChat.blockEl = block;
    scrollChatToBottom(mount);
  }

  function updateAgentChatStatus(text) {
    if (agentChat.statusEl) agentChat.statusEl.textContent = text;
    scrollChatToBottom(agentChat.listEl);
  }

  function updateAgentChatAnswer(text) {
    if (!agentChat.answerEl) return;
    agentChat.answerEl.style.display = text ? "block" : "none";
    agentChat.answerEl.textContent = text;
    scrollChatToBottom(agentChat.listEl);
  }

  function finalizeAgentChat(summary, answer) {
    const text = (answer || summary || "任务已结束").trim();
    const failed = /^(失败|错误|已停止)/.test(text);
    if (text && !/^任务已结束$/.test(text)) updateAgentChatAnswer(text);
    if (agentChat.statusEl) {
      agentChat.statusEl.textContent = failed ? text.split("\n")[0] : "Agent 已完成";
      agentChat.statusEl.style.color = failed ? "#dc2626" : "#059669";
    }
    agentChat.active = false;
    scrollChatToBottom(agentChat.listEl);
  }

  function resetAgentChat(removeDom) {
    if (removeDom !== false) removeAgentRunBlock();
    agentChat.active = false;
    agentChat.blockEl = null;
    agentChat.contentEl = null;
    agentChat.statusEl = null;
    agentChat.answerEl = null;
    agentChat.logEl = null;
    agentChat.listEl = null;
  }

  function ensureAgentLogPanel() {
    let panel = document.getElementById("ds-agent-log");
    if (panel) return panel;
    panel = document.createElement("div");
    panel.id = "ds-agent-log";
    panel.style.cssText = "position:fixed;right:20px;bottom:88px;width:340px;max-height:280px;background:#fff;border:1px solid #e5e7eb;border-radius:12px;box-shadow:0 8px 24px rgba(0,0,0,.12);z-index:2147483646;display:none;flex-direction:column;font-size:12px";
    const head = document.createElement("div");
    head.style.cssText = "padding:10px 12px;font-weight:600;color:#111827;border-bottom:1px solid #f3f4f6";
    head.textContent = "Agent 执行中";
    const body = document.createElement("div");
    body.id = "ds-agent-log-body";
    body.style.cssText = "padding:10px 12px;overflow-y:auto;max-height:220px;color:#374151;line-height:1.5;font-family:Consolas,monospace";
    panel.append(head, body);
    appendToBody(panel);
    return panel;
  }

  function appendAgentLog(text) {
    if (agentChat.logEl) {
      const line = document.createElement("div");
      line.style.marginBottom = "4px";
      line.textContent = text;
      agentChat.logEl.appendChild(line);
      while (agentChat.logEl.children.length > 120) agentChat.logEl.removeChild(agentChat.logEl.firstChild);
      agentChat.logEl.scrollTop = agentChat.logEl.scrollHeight;
      if (/^--- ReAct|^Thought:|^Action:|^Observation:|^Final Answer:|^错误:|^MCP |^策略:/.test(text)) {
        updateAgentChatStatus(text.replace(/^--- ReAct 第 \d+ 步 ---$/, "Agent 推理中…").slice(0, 80));
      }
      scrollChatToBottom(agentChat.listEl);
      return;
    }
    const body = document.getElementById("ds-agent-log-body");
    if (!body) return;
    ensureAgentLogPanel().style.display = "flex";
    const line = document.createElement("div");
    line.style.marginBottom = "4px";
    line.textContent = text;
    body.appendChild(line);
    while (body.children.length > 80) body.removeChild(body.firstChild);
    body.scrollTop = body.scrollHeight;
  }

  function hideAgentLogPanel() {
    const panel = document.getElementById("ds-agent-log");
    if (panel) panel.style.display = "none";
    const body = document.getElementById("ds-agent-log-body");
    if (body) body.textContent = "";
  }

  function ensureAgentModeFloater() {
    if (!document.body) return null;
    document.getElementById("ds-api-status-float")?.remove();
    ensureStyles();
    let btn = document.getElementById("ds-agent-mode-float");
    if (btn && !document.body.contains(btn)) {
      btn.remove();
      btn = null;
    }
    if (btn) {
      syncWorkModeUi();
      return btn;
    }

    btn = document.createElement("button");
    btn.id = "ds-agent-mode-float";
    btn.type = "button";
    btn.className = "ds-agent-mode-float ds-agent-mode-float-btn";
    btn.setAttribute("data-ds-floater", "1");

    const icon = document.createElement("span");
    icon.className = "ds-native-icon";
    icon.innerHTML = ICONS.agent;

    const label = document.createElement("span");
    label.className = "ds-native-label";
    label.id = "ds-agent-mode-float-label";
    label.textContent = "普通";

    btn.append(icon, label);
    bindFloaterClick(btn);

    appendToBody(btn);
    syncWorkModeUi();
    return btn;
  }

  function isFloaterMounted() {
    const btn = document.getElementById("ds-agent-mode-float");
    return !!(btn && document.body && document.body.contains(btn));
  }

  function isLoginOrAuthPage() {
    if (!isDeepSeekOfficialPage()) return false;
    const path = (location.pathname || "").toLowerCase();
    if (/\/sign[-_]?in/.test(path) || /\/login/.test(path) || /\/register/.test(path) || /\/auth/.test(path)) {
      return true;
    }
    return !readChatUserToken();
  }

  function shouldShowModeFloater() {
    return isDeepSeekOfficialPage() && !isLoginOrAuthPage();
  }

  function mountAgentModeFloater() {
    try {
      if (!document.body) {
        if (document.readyState === "loading") {
          document.addEventListener("DOMContentLoaded", mountAgentModeFloater, { once: true });
        } else {
          setTimeout(mountAgentModeFloater, 50);
        }
        return;
      }
      if (!shouldShowModeFloater()) {
        document.getElementById("ds-agent-mode-float")?.remove();
        return;
      }
      if (isFloaterMounted()) {
        syncWorkModeUi();
        return;
      }
      ensureAgentModeFloater();
    } catch (err) {
      console.warn("[DeepSeek Desktop] mode floater:", err);
    }
  }

  function buildToolbarParts() {
    const settingsBtn = createBtn("ds-settings", "MCP 设置", "settings", false);
    settingsBtn.setAttribute("data-ds-toolbar", "1");
    settingsBtn.addEventListener("click", (e) => {
      e.stopPropagation();
      post("openSettings", {});
    });

    return { settingsBtn };
  }

  function scheduleToolbarSpacing() {
    requestAnimationFrame(() => {
      syncToolbarSpacing();
      requestAnimationFrame(() => {
        syncToolbarSpacing();
        setTimeout(syncToolbarSpacing, 120);
      });
    });
  }

  function showToast(title, lines) {
    ensureStyles();
    let wrap = document.getElementById("ds-toast-wrap");
    if (!wrap) {
      wrap = document.createElement("div");
      wrap.id = "ds-toast-wrap";
      wrap.style.cssText =
        "position:fixed;right:20px;bottom:24px;z-index:2147483647;max-width:320px;pointer-events:none";
      appendToBody(wrap);
    }
    const toast = document.createElement("div");
    toast.style.cssText =
      "pointer-events:auto;margin-top:8px;padding:10px 14px;background:#fff;border:1px solid #e5e7eb;border-radius:12px;box-shadow:0 8px 24px rgba(0,0,0,.12);font-size:13px;color:#374151";
    const headEl = document.createElement("div");
    headEl.style.cssText = "font-weight:600;color:#111827;margin-bottom:4px";
    headEl.textContent = title;
    toast.appendChild(headEl);
    (lines || []).filter(Boolean).forEach(function (line) {
      const row = document.createElement("div");
      row.style.marginTop = "2px";
      row.textContent = line;
      toast.appendChild(row);
    });
    wrap.appendChild(toast);
    while (wrap.children.length > 3) wrap.removeChild(wrap.firstChild);
    setTimeout(function () { toast.remove(); }, 5000);
  }

  function copyText(text) {
    if (!text) return;
    try {
      navigator.clipboard.writeText(text);
    } catch (_) {
      const ta = document.createElement("textarea");
      ta.value = text;
      document.body.appendChild(ta);
      ta.select();
      document.execCommand("copy");
      ta.remove();
    }
  }

  function showProviderCard(info) {
    ensureStyles();
    document.getElementById("ds-provider-mask")?.remove();
    const mask = document.createElement("div");
    mask.id = "ds-provider-mask";
    mask.style.cssText =
      "position:fixed;inset:0;z-index:2147483646;display:flex;align-items:center;justify-content:center;background:rgba(0,0,0,.4)";
    const card = document.createElement("div");
    card.style.cssText =
      "width:420px;max-width:calc(100vw - 32px);background:#fff;border-radius:16px;border:1px solid #e5e7eb;box-shadow:0 20px 50px rgba(0,0,0,.15);padding:20px;font-family:inherit";
    const titleRow = document.createElement("div");
    titleRow.style.cssText = "display:flex;justify-content:space-between;align-items:center;margin-bottom:12px";
    const h3 = document.createElement("div");
    h3.style.cssText = "font-size:16px;font-weight:600;color:#111827";
    h3.textContent = "DeepSeek";
    const dot = document.createElement("span");
    dot.style.cssText =
      "width:10px;height:10px;border-radius:50%;background:" + (info.loggedIn ? "#22c55e" : "#ef4444");
    titleRow.append(h3, dot);
    const desc = document.createElement("div");
    desc.style.cssText = "font-size:13px;color:#6b7280;line-height:1.5;margin-bottom:12px";
    desc.textContent =
      "经本地 Chat2API 使用网页会话；Agent 由进程内 DSD Harness 驱动。";
    const stats = document.createElement("div");
    stats.style.cssText = "font-size:12px;color:#374151;margin-bottom:12px;line-height:1.8";
    stats.textContent =
      "账户: " +
      (info.loggedIn ? "1/1 在线" : "未登录") +
      "  |  模型: " +
      (info.modelCount != null ? info.modelCount : "—") +
      "  |  认证: User Token";
    const baseUrl = info.url || "";
    const apiKey = info.apiKey || "";
    const masked = info.apiKeyMasked || (apiKey ? apiKey.slice(0, 8) + "…" : "—");
    function fieldRow(label, value, copyVal) {
      const row = document.createElement("div");
      row.style.cssText = "margin-bottom:10px";
      const lab = document.createElement("div");
      lab.style.cssText = "font-size:11px;color:#9ca3af;margin-bottom:4px";
      lab.textContent = label;
      const box = document.createElement("div");
      box.style.cssText = "display:flex;gap:8px;align-items:center";
      const val = document.createElement("div");
      val.style.cssText =
        "flex:1;background:#f7f8fa;border-radius:10px;padding:8px 10px;font-size:12px;color:#4d6bfe;word-break:break-all";
      val.textContent = value;
      const btn = document.createElement("button");
      btn.type = "button";
      btn.textContent = "复制";
      btn.style.cssText =
        "padding:6px 12px;border-radius:8px;border:1px solid #e5e7eb;background:#fff;cursor:pointer;font-size:12px";
      btn.onclick = function () {
        copyText(copyVal || value);
      };
      box.append(val, btn);
      row.append(lab, box);
      return row;
    }
    const actions = document.createElement("div");
    actions.style.cssText = "display:flex;justify-content:flex-end;gap:8px;margin-top:12px";
    const btnSettings = document.createElement("button");
    btnSettings.type = "button";
    btnSettings.textContent = "MCP 设置";
    btnSettings.style.cssText = "padding:8px 14px;border-radius:10px;border:1px solid #e5e7eb;background:#fff;cursor:pointer;font-size:13px";
    const btnClose = document.createElement("button");
    btnClose.type = "button";
    btnClose.textContent = "确定";
    btnClose.style.cssText =
      "padding:8px 14px;border-radius:10px;border:none;background:#4d6bfe;color:#fff;cursor:pointer;font-size:13px";
    actions.append(btnSettings, btnClose);
    card.append(
      titleRow,
      desc,
      stats,
      fieldRow("Chat2API Base URL", baseUrl, baseUrl),
      fieldRow("API Key", masked, apiKey)
    );
    if (info.harnessRuntimeUrl) card.append(fieldRow("DSD Harness", info.harnessRuntimeUrl, info.harnessRuntimeUrl));
    card.append(actions);
    mask.appendChild(card);
    appendToBody(mask);
    mask.addEventListener("click", function (e) { if (e.target === mask) mask.remove(); });
    btnClose.onclick = function () { mask.remove(); };
    btnSettings.onclick = function () { mask.remove(); post("openSettings", {}); };
  }

  function updateApiDot() {
    const ping = window.dsDesktopBridge && window.dsDesktopBridge.ping();
    const online = !!(ping && ping.token);
    const changed = apiOnline !== online;
    apiOnline = online;

    if (online && changed) {
      try {
        post("syncToken", { token: window.dsDesktopBridge.getToken() });
      } catch (_) {}
    }
    return online;
  }

  function isToolbarHealthy() {
    if (shouldKeepNativeDeepSeekUi()) return true;
    return isToolbarOrderCorrect();
  }

  function injectIntoNativeToolbar() {
    const host = findToolbarHost();
    const searchBtn = findSmartSearchBtn();
    const thinkBtn = findDeepThinkBtn();

    if (isToolbarOrderCorrect()) {
      toolbarInjected = true;
      scheduleToolbarSpacing();
      return true;
    }

    removeToolbarParts();
    if (!host || (!searchBtn && !thinkBtn)) return false;

    const { settingsBtn } = buildToolbarParts();
    if (searchBtn && host.contains(searchBtn)) {
      host.insertBefore(settingsBtn, searchBtn);
    } else if (thinkBtn && thinkBtn.nextSibling) {
      host.insertBefore(settingsBtn, thinkBtn.nextSibling);
    } else {
      host.appendChild(settingsBtn);
    }

    toolbarInjected = true;
    scheduleToolbarSpacing();
    post("nativeReady", {});
    return true;
  }

  function injectDockFallback() {
    if (dockInjected || document.getElementById("ds-dock-bar")) return;

    const input = findInput();
    if (!input) return;

    const rect = input.getBoundingClientRect();
    if (rect.width < 100) return;

    const dock = document.createElement("div");
    dock.id = "ds-dock-bar";
    dock.className = "ds-dock-bar";
    dock.style.left = rect.left + "px";
    dock.style.width = rect.width + "px";
    dock.style.bottom = window.innerHeight - rect.top + 8 + "px";

    const settingsBtn = createBtn("ds-settings-dock", "MCP 设置", "settings", false);

    settingsBtn.onclick = () => post("openSettings", {});

    dock.append(settingsBtn);
    appendToBody(dock);
    dockInjected = true;

    if (!toolbarInjected) post("nativeReady", {});
  }

  function dispatchAgentRun() {
    const input = findInput();
    const text = getInputText(input);
    if (!text) return false;

    hideAgentLogPanel();
    beginAgentChatInThread(text);
    post("agentRun", {
      text,
      mode: readNativeMode(),
      strategy: workMode === "plan" ? "blueprint" : "execute",
      deepThink: readToggle("深度思考"),
      smartSearch: readToggle("智能搜索"),
      mcpOn: true,
    });
    setInputText(input, "");
    return true;
  }

  function isInputActive(input) {
    if (!input) return false;
    const active = document.activeElement;
    return active === input || input.contains(active);
  }

  function hookAgentSend() {
    const sendBtn = findSendButton();
    const input = findInput();

    if (sendBtn && sendBtn.dataset.dsSendHook !== "1") {
      sendBtn.dataset.dsSendHook = "1";
      const onSend = (e) => {
        if (!isAgentLikeMode()) return;
        if (!getInputText(findInput())) return;
        e.preventDefault();
        e.stopPropagation();
        e.stopImmediatePropagation();
        dispatchAgentRun();
      };
      sendBtn.addEventListener("click", onSend, true);
      sendBtn.addEventListener("pointerdown", onSend, true);
    }

    if (input && input.dataset.dsKeyHook !== "1") {
      input.dataset.dsKeyHook = "1";
      input.addEventListener(
        "keydown",
        (e) => {
          if (!isAgentLikeMode()) return;
          if (e.key !== "Enter" || e.shiftKey || e.ctrlKey || e.altKey || e.metaKey) return;
          if (e.isComposing) return;
          if (!getInputText(input).trim()) return;
          e.preventDefault();
          e.stopPropagation();
          e.stopImmediatePropagation();
          dispatchAgentRun();
        },
        true
      );
    }

    if (!window.__dsEnterHooked) {
      window.__dsEnterHooked = true;
      document.addEventListener(
        "keydown",
        (e) => {
          if (!isAgentLikeMode()) return;
          if (e.key !== "Enter" || e.shiftKey || e.ctrlKey || e.altKey || e.metaKey) return;
          if (e.isComposing) return;
          const inp = findInput();
          if (!inp || !isInputActive(inp)) return;
          if (!getInputText(inp).trim()) return;
          e.preventDefault();
          e.stopPropagation();
          e.stopImmediatePropagation();
          dispatchAgentRun();
        },
        true
      );
    }
  }

  function updateAgentModeHint() {
    let hint = document.getElementById("ds-agent-mode-hint");
    if (!isAgentLikeMode()) {
      hint?.remove();
      return;
    }
    if (!hint) {
      hint = document.createElement("div");
      hint.id = "ds-agent-mode-hint";
      hint.style.cssText =
        "position:fixed;left:50%;transform:translateX(-50%);bottom:108px;z-index:2147483644;padding:6px 14px;background:#eef2ff;border:1px solid #4d6bfe;border-radius:9999px;font-size:12px;color:#4d6bfe;pointer-events:none;white-space:nowrap";
      appendToBody(hint);
    }
    hint.textContent = isAgentLikeMode()
      ? "右上角为 Agent 模式，发送走 Chat2API + MCP"
      : "右上角为普通模式，发送走网页对话";
  }

  function repositionDock() {
    const dock = document.getElementById("ds-dock-bar");
    const input = findInput();
    if (!dock || !input) return;
    const rect = input.getBoundingClientRect();
    dock.style.left = rect.left + "px";
    dock.style.width = rect.width + "px";
    dock.style.bottom = window.innerHeight - rect.top + 8 + "px";
    if (toolbarInjected) dock.style.display = "none";
    else dock.style.display = "flex";
  }

  function resetInjectState() {
    toolbarInjected = false;
    dockInjected = false;
    closeModeMenus();
    document.querySelectorAll(".ds-mode-menu").forEach((m) => m.remove());
    document.getElementById("ds-work-mode")?.remove();
    document.getElementById("ds-work-mode-dock")?.remove();
    document.getElementById("ds-api-status-float")?.remove();
    removeToolbarParts();
    document.getElementById("ds-dock-bar")?.remove();
  }

  function tryInject() {
    updateApiDot();

    if (shouldKeepNativeDeepSeekUi()) {
      teardownChatPageOverrides();
      mountAgentModeFloater();
      bindWorkModeClient();
      if (!window.__dsNativeReadyPosted) {
        window.__dsNativeReadyPosted = true;
        post("nativeReady", {});
      }
      return;
    }

    mountAgentModeFloater();

    const { settings } = getToolbarParts();
    if (settings && !(findSmartSearchBtn() || findDeepThinkBtn())) {
      removeToolbarParts();
    }
    if (!document.getElementById("ds-settings")) toolbarInjected = false;
    if (!document.getElementById("ds-dock-bar")) dockInjected = false;

    const ok = injectIntoNativeToolbar();
    if (!ok) injectDockFallback();
    repositionDock();
    hookAgentSend();
    updateAgentModeHint();
    scheduleToolbarSpacing();
  }

  function runBurstTryInject(delays, hardReset) {
    if (hardReset) resetInjectState();
    delays.forEach((ms) => setTimeout(tryInject, ms));
  }

  function scheduleBurstInject(forceReset) {
    if (burstTimer) clearTimeout(burstTimer);
    burstTimer = setTimeout(() => {
      burstTimer = null;
      const healthy = isToolbarHealthy();
      const hard = !!forceReset || !healthy;
      const delays = hard
        ? [0, 120, 300, 600, 1000, 1600, 2500, 4000, 6500, 10000]
        : [0, 400, 1200, 2500];
      runBurstTryInject(delays, hard);
    }, 280);
  }

  function hookSpaNavigation() {
    if (window.__dsSpaHooked) return;
    window.__dsSpaHooked = true;
    const fire = () => scheduleBurstInject(false);
    const wrap = (fn) =>
      function () {
        const r = fn.apply(this, arguments);
        queueMicrotask(fire);
        return r;
      };
    history.pushState = wrap(history.pushState);
    history.replaceState = wrap(history.replaceState);
    window.addEventListener("popstate", fire);
    let lastHref = location.href;
    setInterval(() => {
      if (location.href !== lastHref) {
        lastHref = location.href;
        fire();
      }
    }, 400);
  }

  window.dsDesktopOnMessage = function (msg) {
    try {
      handleDesktopMessage(msg);
    } finally {
      flushPendingNativeMessages();
    }
  };

  function handleDesktopMessage(msg) {
    const nativeOnly = shouldKeepNativeDeepSeekUi();

    if (!nativeOnly) {
      if (msg.type === "log" || msg.type === "agentLog") {
        if (msg.text) appendAgentLog(msg.text);
      }
      if (msg.type === "agentStarted") {
        hideAgentLogPanel();
        if (!document.getElementById("ds-agent-run-block") && msg.task) beginAgentChatInThread(msg.task);
        else updateAgentChatStatus("Agent 正在执行…");
      }
      if (msg.type === "agentStatus" && msg.text) {
        updateAgentChatStatus(msg.text);
      }
      if (msg.type === "agentAnswer" && msg.text) {
        updateAgentChatAnswer(msg.text);
      }
      if (msg.type === "agentDone") {
        appendAgentLog(msg.summary || "任务已结束");
        finalizeAgentChat(msg.summary, msg.answer);
      }
    }

    if (msg.type === "workModeState" && window.DsWorkMode) {
      window.DsWorkMode.applyState(msg);
    }
    if (msg.type === "loginState") {
      if (typeof msg.loggedIn === "boolean") apiOnline = msg.loggedIn;
      updateApiDot();
    }
    if (msg.type === "apiInfo") {
      updateApiDot();
    }
    if (msg.type === "reinject") scheduleBurstInject(false);
  }

  window.__dsNativeTryInject = function dsNativeTryInject() {
    try {
      tryInject();
      return true;
    } catch (err) {
      console.warn("[DeepSeek Desktop] inject:", err);
      return false;
    }
  };
  window.__dsNativeOnRouteChange = function (forceReset) {
    scheduleBurstInject(!!forceReset);
  };

  let mutTimer = null;
  let domObserver = null;
  function hookNativeModePills() {
    if (window.__dsModePillHooked) return;
    window.__dsModePillHooked = true;
    document.addEventListener(
      "click",
      (e) => {
        const el = e.target.closest("button,[role='button'],[role='radio']");
        if (!el) return;
        const text = elText(el);
        if (!text.includes("快速") && !text.includes("专家") && !text.includes("识图")) return;
        closeModeMenus();
        setTimeout(() => scheduleBurstInject(true), 80);
        setTimeout(() => scheduleBurstInject(true), 350);
        setTimeout(() => scheduleBurstInject(true), 900);
      },
      true
    );
  }

  function onDomMutated() {
    if (mutTimer) return;
    mutTimer = setTimeout(() => {
      mutTimer = null;
      if (!isFloaterMounted()) mountAgentModeFloater();
      if (!shouldKeepNativeDeepSeekUi() && !isToolbarHealthy()) tryInject();
      else if (shouldKeepNativeDeepSeekUi() && !isFloaterMounted()) tryInject();
    }, 280);
  }

  function startDomObserver() {
    const root = domRoot();
    if (!root || domObserver) return;
    try {
      domObserver = new MutationObserver(onDomMutated);
      domObserver.observe(root, { childList: true, subtree: true });
    } catch (err) {
      console.warn("[DeepSeek Desktop] MutationObserver:", err);
    }
  }

  function flushPendingNativeMessages() {
    const q = window.__dsPendingNativeMessages;
    if (!q || !q.length || typeof window.dsDesktopOnMessage !== "function") return;
    window.__dsPendingNativeMessages = [];
    q.forEach((m) => {
      try {
        window.dsDesktopOnMessage(m);
      } catch (err) {
        console.warn("[DeepSeek Desktop] pending native msg:", err);
      }
    });
  }

  function bootstrapNativeInject() {
    if (!domRoot()) return false;
    if (!window.__dsNativeInject) {
      window.__dsNativeInject = true;
      try {
        document
          .querySelectorAll("[data-ds-agent-injected]:not(#ds-agent-run-block)")
          .forEach((n) => n.remove());
        document.getElementById("ds-api-status-float")?.remove();
      } catch (_) {}
      document.addEventListener("click", (e) => {
        if (e.target.closest("#ds-agent-mode-float")) return;
        closeModeMenus();
      });
      hookSpaNavigation();
      hookNativeModePills();
      startDomObserver();
      bindWorkModeClient();
      if (!window.__dsResizeHooked) {
        window.__dsResizeHooked = true;
        window.addEventListener("resize", () => {
          if (shouldKeepNativeDeepSeekUi()) return;
          repositionDock();
          scheduleToolbarSpacing();
          if (modeMenuOpen && activeModeMenu && activeModeAnchor) {
            positionModeMenu(activeModeAnchor, activeModeMenu);
          }
        });
      }
      if (!window.__dsFloaterInterval) {
        window.__dsFloaterInterval = setInterval(() => {
          if (!isFloaterMounted()) mountAgentModeFloater();
          if (!shouldKeepNativeDeepSeekUi() && !isToolbarHealthy()) tryInject();
          updateApiDot();
        }, 5000);
      }
    }
    mountAgentModeFloater();
    watchChatUserToken();
    flushPendingNativeMessages();
    scheduleBurstInject(!window.__dsNativeBootDone);
    window.__dsNativeBootDone = true;
    return true;
  }

  function scheduleBootstrap() {
    if (bootstrapNativeInject()) return;
    const run = () => bootstrapNativeInject();
    if (document.readyState === "loading") {
      document.addEventListener("DOMContentLoaded", run, { once: true });
    }
    setTimeout(run, 0);
    setTimeout(run, 80);
    setTimeout(run, 300);
    setTimeout(run, 800);
    setTimeout(run, 2000);
  }

  scheduleBootstrap();
})();
