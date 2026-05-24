(function () {
  "use strict";

  const pending = new Map();
  let seq = 0;
  let automations = [];
  let webhookBaseUrl = "";
  let editingId = null;

  function $(id) {
    return document.getElementById(id);
  }

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
    }
  }

  function postAsync(type, payload) {
    return new Promise((resolve, reject) => {
      const reqId = "a" + ++seq;
      const timer = setTimeout(() => {
        pending.delete(reqId);
        reject(new Error("请求超时"));
      }, 120000);
      pending.set(reqId, (msg) => {
        clearTimeout(timer);
        const p = msg.payload || msg;
        if (p.ok === false) reject(new Error(p.error || "操作失败"));
        else resolve(p);
      });
      post(type, { ...(payload || {}), reqId });
    });
  }

  function onHostMessage(raw) {
    let msg = raw;
    if (typeof raw === "string") {
      try {
        msg = JSON.parse(raw);
      } catch (_) {
        return;
      }
    }
    if (!msg?.type) return;
    if (msg.type === "agentAutomation" && msg.reqId && pending.has(msg.reqId)) {
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

  function triggerLabel(a) {
    const t = a.trigger?.type || "schedule";
    if (t === "schedule") {
      const p = a.trigger?.schedulePreset || "daily";
      const time = a.trigger?.scheduleTimeUtc || "09:00";
      return `定时 · ${p} @ ${time} UTC`;
    }
    if (t === "github") return `GitHub · ${a.trigger?.githubEvent || "任意"}`;
    if (t === "slack") return `Slack · ${a.trigger?.slackEvent || "任意"}`;
    if (t === "webhook") return "Webhook";
    return "手动";
  }

  function hookUrl(id) {
    if (!webhookBaseUrl || !id) return "";
    return `${webhookBaseUrl.replace(/\/$/, "")}/hooks/${id}`;
  }

  function renderList() {
    const list = $("automation-list");
    const empty = $("list-empty");
    if (!list) return;
    list.innerHTML = "";
    if (!automations.length) {
      if (empty) empty.hidden = false;
      return;
    }
    if (empty) empty.hidden = true;

    automations.forEach((a) => {
      const card = document.createElement("article");
      card.className = "au-card";
      const next = a.nextRunAt
        ? new Date(a.nextRunAt).toLocaleString()
        : "—";
      const last = a.lastRunAt
        ? new Date(a.lastRunAt).toLocaleString()
        : "从未";
      card.innerHTML = `
        <div class="au-card-head">
          <div>
            <h3>${escapeHtml(a.name)}</h3>
            <p class="au-card-meta">${escapeHtml(triggerLabel(a))} · 上次 ${last} · 下次 ${next}</p>
          </div>
          <label class="au-toggle">
            <input type="checkbox" data-toggle="${a.id}" ${a.enabled ? "checked" : ""} />
            <span class="${a.enabled ? "au-status-on" : "au-status-off"}">${a.enabled ? "开启" : "关闭"}</span>
          </label>
        </div>
        <div class="au-card-actions">
          <button type="button" class="au-btn" data-edit="${a.id}">编辑</button>
          <button type="button" class="au-btn" data-test="${a.id}">测试</button>
          <button type="button" class="au-btn" data-runs="${a.id}">运行历史</button>
          <button type="button" class="au-btn" data-del="${a.id}">删除</button>
        </div>`;
      list.appendChild(card);
    });

    list.querySelectorAll("[data-toggle]").forEach((el) => {
      el.addEventListener("change", async () => {
        const id = el.getAttribute("data-toggle");
        try {
          await postAsync("agentAutomationsToggle", { id, enabled: el.checked });
          await loadList();
        } catch (e) {
          alert(e.message);
          el.checked = !el.checked;
        }
      });
    });
    list.querySelectorAll("[data-edit]").forEach((btn) => {
      btn.addEventListener("click", () => openEditor(btn.getAttribute("data-edit")));
    });
    list.querySelectorAll("[data-test]").forEach((btn) => {
      btn.addEventListener("click", () => testRun(btn.getAttribute("data-test")));
    });
    list.querySelectorAll("[data-runs]").forEach((btn) => {
      btn.addEventListener("click", () => showRuns(btn.getAttribute("data-runs")));
    });
    list.querySelectorAll("[data-del]").forEach((btn) => {
      btn.addEventListener("click", () => deleteAuto(btn.getAttribute("data-del")));
    });
  }

  function escapeHtml(s) {
    return String(s || "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  async function loadList() {
    const res = await postAsync("agentAutomationsList", {});
    automations = res.automations || [];
    webhookBaseUrl = res.webhookBaseUrl || "";
    const hint = $("webhook-base");
    if (hint && webhookBaseUrl) {
      hint.hidden = false;
      hint.textContent = `Webhook 基址：${webhookBaseUrl}（路径 /hooks/{自动化ID}）`;
    }
    renderList();
  }

  function syncTriggerUi() {
    const type = $("f-trigger")?.value || "schedule";
    $("wrap-schedule").hidden = type !== "schedule";
    $("wrap-time").hidden = type !== "schedule";
    $("wrap-dow").hidden = type !== "schedule" || $("f-schedule")?.value !== "weekly";
    $("wrap-github").hidden = type !== "github";
    $("wrap-slack").hidden = type !== "slack";
  }

  function openEditor(id) {
    editingId = id || null;
    const editor = $("editor");
    if (editor) editor.hidden = false;
    $("editor-title").textContent = id ? "编辑自动化" : "新建自动化";
    const a = id ? automations.find((x) => x.id === id) : null;
    $("f-name").value = a?.name || "";
    $("f-desc").value = a?.description || "";
    $("f-trigger").value = a?.trigger?.type || "schedule";
    $("f-schedule").value = a?.trigger?.schedulePreset || "daily";
    $("f-time").value = (a?.trigger?.scheduleTimeUtc || "09:00").slice(0, 5);
    $("f-dow").value = String(a?.trigger?.scheduleDayOfWeek ?? 1);
    $("f-github").value = a?.trigger?.githubEvent || "";
    $("f-slack").value = a?.trigger?.slackEvent || "";
    $("f-strategy").value = a?.strategy || "execute";
    $("f-instructions").value = a?.instructions || "";
    $("f-webhook-secret").value = a?.webhookSecret || "";
    syncTriggerUi();
    const hook = $("hook-url");
    if (hook && id) {
      hook.hidden = false;
      hook.textContent = "Webhook URL: " + hookUrl(id);
    } else if (hook) hook.hidden = true;
  }

  function readEditor() {
    const id = editingId || "auto_" + Date.now().toString(36);
    const triggerType = $("f-trigger").value;
    return {
      id,
      name: ($("f-name").value || "").trim(),
      description: ($("f-desc").value || "").trim() || null,
      enabled: true,
      trigger: {
        type: triggerType,
        schedulePreset: $("f-schedule").value,
        scheduleTimeUtc: $("f-time").value || "09:00",
        scheduleDayOfWeek: parseInt($("f-dow").value, 10) || 1,
        githubEvent: $("f-github").value.trim() || null,
        slackEvent: $("f-slack").value.trim() || null,
      },
      actions: [{ type: "agent" }],
      instructions: $("f-instructions").value.trim(),
      strategy: $("f-strategy").value,
      webhookSecret: $("f-webhook-secret").value.trim() || null,
    };
  }

  async function saveEditor() {
    const automation = readEditor();
    if (!automation.name) {
      alert("请填写名称");
      return;
    }
    if (!automation.instructions) {
      alert("请填写 Agent 指令");
      return;
    }
    const res = await postAsync("agentAutomationsSave", { automation });
    editingId = res.automation?.id || automation.id;
    $("editor").hidden = true;
    await loadList();
  }

  async function testRun(id) {
    try {
      const res = await postAsync("agentAutomationsTest", {
        id,
        payload: JSON.stringify({ type: "manual", note: "test automation" }),
      });
      alert(res.run?.status === "completed" ? "测试完成" : "测试结束: " + (res.run?.summary || res.run?.error || res.run?.status));
      await showRuns(id);
    } catch (e) {
      alert(e.message);
    }
  }

  async function deleteAuto(id) {
    if (!confirm("确定删除该自动化？")) return;
    await postAsync("agentAutomationsDelete", { id });
    await loadList();
  }

  async function showRuns(id) {
    const a = automations.find((x) => x.id === id);
    const panel = $("runs-panel");
    const list = $("runs-list");
    if (!panel || !list) return;
    panel.hidden = false;
    $("runs-for-name").textContent = a ? `· ${a.name}` : "";
    const res = await postAsync("agentAutomationsRuns", { automationId: id });
    const runs = res.runs || [];
    list.innerHTML = runs.length
      ? runs
          .map(
            (r) =>
              `<div class="au-run ${r.status === "completed" ? "ok" : "fail"}">` +
              `${new Date(r.startedAt).toLocaleString()} · ${r.triggerType} · ${r.status}` +
              (r.summary ? ` — ${escapeHtml(r.summary.slice(0, 120))}` : "") +
              `</div>`
          )
          .join("")
      : "<p class=\"au-empty\">暂无运行记录</p>";
  }

  function bindUi() {
    $("btn-new")?.addEventListener("click", () => openEditor(null));
    $("btn-refresh")?.addEventListener("click", () => loadList().catch((e) => alert(e.message)));
    $("btn-save")?.addEventListener("click", () => saveEditor().catch((e) => alert(e.message)));
    $("btn-cancel-edit")?.addEventListener("click", () => {
      $("editor").hidden = true;
      editingId = null;
    });
    $("btn-test")?.addEventListener("click", () => {
      if (!editingId) {
        alert("请先保存自动化");
        return;
      }
      testRun(editingId);
    });
    $("f-trigger")?.addEventListener("change", syncTriggerUi);
    $("f-schedule")?.addEventListener("change", syncTriggerUi);
  }

  bindHostMessages();
  bindUi();
  loadList().catch((e) => {
    const list = $("automation-list");
    if (list) list.innerHTML = `<p class="au-empty">${escapeHtml(e.message)}</p>`;
  });
})();
