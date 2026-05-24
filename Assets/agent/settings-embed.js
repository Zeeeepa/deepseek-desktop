(function () {
  "use strict";

  const pending = new Map();
  let seq = 0;

  function isInAgentIframe() {
    try {
      return window.parent !== window;
    } catch (_) {
      return false;
    }
  }

  function post(type, payload) {
    const body = { type, __dsEmbed: true, ...(payload || {}) };
    const json = JSON.stringify(body);
    if (isInAgentIframe()) {
      window.parent.postMessage(json, "*");
      return;
    }
    if (window.chrome?.webview) {
      window.chrome.webview.postMessage(json);
      return;
    }
  }

  function postAsync(type, payload) {
    return new Promise((resolve, reject) => {
      const reqId = "s" + ++seq;
      const timer = setTimeout(() => {
        pending.delete(reqId);
        reject(new Error("请求超时"));
      }, 30000);
      pending.set(reqId, (msg) => {
        clearTimeout(timer);
        if (msg.ok === false) reject(new Error(msg.error || "操作失败"));
        else resolve(msg);
      });
      post(type, { ...(payload || {}), reqId });
    });
  }

    function onHostMessage(raw) {
    let msg = raw;
    if (typeof raw === "string") {
      try { msg = JSON.parse(raw); } catch (_) { return; }
    }
    if (!msg?.type) return;
    if (msg.type === "settingsBootstrap") {
      applyPayload({ loggedIn: !!msg.loggedIn });
      return;
    }
    if (msg.reqId && pending.has(msg.reqId)) {
      pending.get(msg.reqId)(msg);
      pending.delete(msg.reqId);
    }
  }

  function bindHostMessages() {
    if (isInAgentIframe()) {
      window.addEventListener("message", (e) => {
        if (e.source !== window.parent) return;
        onHostMessage(e.data);
      });
      return;
    }
    if (window.chrome?.webview) {
      window.chrome.webview.addEventListener("message", (e) => onHostMessage(e.data));
      return;
    }
    setTimeout(bindHostMessages, 20);
  }

  function $(id) { return document.getElementById(id); }

  function renderMcpList(servers) {
    const list = $("mcp-list");
    if (!list) return;
    list.innerHTML = "";
    if (!servers?.length) {
      list.innerHTML = "<p class=\"se-hint\">尚未添加 MCP 服务器。点击下方「添加 MCP 服务器」，连接成功后 Agent 可动态发现并调用工具。</p>";
      return;
    }
    servers.forEach((s) => {
      const item = document.createElement("div");
      item.className = "se-mcp-item";
      const status = !s.enabled
        ? "已禁用"
        : s.connected
          ? `已连接 · ${s.toolCount || 0} 个工具可用`
          : "未连接 — 请启用或点击「连接全部」";
      item.innerHTML =
        `<h4>${escapeHtml(s.name || "MCP Server")}</h4>` +
        `<div class="se-mcp-meta">${escapeHtml(s.transport || "")} · ${escapeHtml(s.endpoint || "")}</div>` +
        `<div class="se-mcp-meta">${escapeHtml(status)}</div>` +
        `<div class="se-mcp-actions">` +
        `<button type="button" class="se-btn" data-action="toggle" data-id="${escapeAttr(s.id)}">${s.enabled ? "禁用" : "启用"}</button>` +
        `<button type="button" class="se-btn" data-action="edit" data-id="${escapeAttr(s.id)}">编辑</button>` +
        `<button type="button" class="se-btn" data-action="remove" data-id="${escapeAttr(s.id)}">删除</button>` +
        `</div>`;
      list.appendChild(item);
    });
    list.querySelectorAll("button[data-action]").forEach((btn) => {
      btn.addEventListener("click", () => handleMcpAction(btn.dataset.action, btn.dataset.id));
    });
  }

  function escapeHtml(text) {
    return String(text || "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function escapeAttr(text) {
    return escapeHtml(text).replace(/'/g, "&#39;");
  }

  function applyPayload(data) {
    const tokenEl = $("web-token-status");
    if (tokenEl) {
      tokenEl.textContent = data.loggedIn ? "已登录" : "未登录";
      tokenEl.className = data.loggedIn ? "se-status-ok" : "se-status-bad";
    }
    const engineEl = $("engine-status");
    if (engineEl) {
      engineEl.textContent = data.loggedIn ? "DSD Harness · 就绪" : "需先登录网页会话";
      engineEl.className = data.loggedIn ? "se-status-ok" : "se-status-bad";
    }
    const summaryEl = $("service-summary");
    if (summaryEl) {
      summaryEl.textContent =
        data.serviceSummary || "对话桥接与进程内 Harness 在后台运行。";
    }
    $("max-steps").value = data.maxAgentSteps ?? 30;
    $("max-sub-steps").value = data.maxSubAgentSteps ?? 10;
    $("strategy").value =
      (data.defaultAgentStrategy || "execute").toLowerCase() === "plan" ||
      (data.defaultAgentStrategy || "").toLowerCase() === "blueprint"
        ? "blueprint"
        : "execute";
    $("harness-info").textContent = data.harnessInfo || data.tuiInfo || "";
    $("sandbox-lazy").checked = data.agentSandboxLazyInit !== false;
    $("workspace").value = data.agentWorkspaceRoot || "";
    $("approval").value = data.agentApprovalMode || "smart";
    $("allow-shell").checked = !!data.agentAllowShell;
    $("adaptive-output").checked = !!data.enableAdaptiveOutputEscalation;
    $("config-path").textContent = data.configPath || "";
    renderMcpList(data.mcpServers || []);
  }

  function collectSavePayload() {
    return {
      maxAgentSteps: parseInt($("max-steps").value, 10) || 30,
      maxSubAgentSteps: parseInt($("max-sub-steps").value, 10) || 10,
      defaultAgentStrategy: $("strategy").value || "execute",
      agentSandboxLazyInit: $("sandbox-lazy").checked,
      agentWorkspaceRoot: $("workspace").value.trim(),
      agentApprovalMode: $("approval").value,
      agentAllowShell: $("allow-shell").checked,
      enableAdaptiveOutputEscalation: $("adaptive-output").checked,
    };
  }

  async function reload() {
    const res = await postAsync("settingsLoad", {});
    applyPayload(res);
  }

  async function handleMcpAction(action, id) {
    if (action === "toggle") {
      await postAsync("settingsMcpToggle", { serverId: id });
      await reload();
      return;
    }
    if (action === "edit") {
      await postAsync("settingsMcpEdit", { serverId: id });
      await reload();
      return;
    }
    if (action === "remove") {
      if (!confirm("确定删除此 MCP 服务器？")) return;
      await postAsync("settingsMcpRemove", { serverId: id });
      await reload();
    }
  }

  async function init() {
    bindHostMessages();
    $("btn-save")?.addEventListener("click", async () => {
      try {
        await postAsync("settingsSave", collectSavePayload());
      } catch (e) {
        alert(e.message || "保存失败");
      }
    });
    $("btn-cancel")?.addEventListener("click", () => {
      if (window.parent !== window) {
        window.parent.postMessage(JSON.stringify({ type: "settingsEmbedClose", __dsEmbed: true }), "*");
      } else {
        reload();
      }
    });
    $("btn-connect-all")?.addEventListener("click", async () => {
      try {
        const res = await postAsync("settingsConnectAllMcp", {});
        if (res.summary) alert(res.summary);
        await reload();
      } catch (e) {
        alert(e.message || "连接失败");
      }
    });
    $("btn-add-mcp")?.addEventListener("click", async () => {
      await postAsync("settingsMcpAdd", {});
      await reload();
    });
    $("btn-open-home")?.addEventListener("click", () => post("settingsOpenDeepSeekHome", {}));
    $("btn-docs")?.addEventListener("click", () => post("settingsOpenDocs", {}));
    $("btn-copy-config")?.addEventListener("click", () => post("settingsCopyConfigPath", {}));
    $("btn-open-config")?.addEventListener("click", () => post("settingsOpenConfig", {}));
    $("btn-doctor")?.addEventListener("click", async () => {
      try {
        const res = await postAsync("settingsRunDoctor", {});
        if (res.text) alert(res.text);
      } catch (e) {
        alert(e.message || "doctor 失败");
      }
    });
    try {
      await reload();
    } catch (e) {
      const el = $("harness-info");
      if (el) el.textContent = "加载设置失败：" + (e.message || e);
    }
  }

  init();
})();
