(function (global) {
  let term = null;
  let fitAddon = null;
  let panelEl = null;
  let visible = false;

  function ensurePanel() {
    if (panelEl) return panelEl;
    panelEl = document.getElementById("agent-terminal-panel");
    if (!panelEl) {
      panelEl = document.createElement("section");
      panelEl.id = "agent-terminal-panel";
      panelEl.className = "ds-terminal-panel";
      panelEl.hidden = true;
      panelEl.innerHTML =
        '<div class="ds-terminal-head">' +
        '<span>Terminal</span>' +
        '<button type="button" class="ds-icon-btn" id="agent-terminal-close" aria-label="关闭">×</button>' +
        "</div>" +
        '<div id="agent-terminal-mount" class="ds-terminal-mount"></div>';
      const main = document.querySelector(".ds-main") || document.body;
      main.appendChild(panelEl);
      panelEl.querySelector("#agent-terminal-close")?.addEventListener("click", hide);
    }
    return panelEl;
  }

  function initTerminal() {
    if (term || typeof Terminal === "undefined") return;
    const mount = document.getElementById("agent-terminal-mount");
    if (!mount) return;
    term = new Terminal({
      convertEol: true,
      fontFamily: "Consolas, monospace",
      fontSize: 13,
      theme: { background: "#1e1e1e", foreground: "#d4d4d4" },
    });
    if (typeof FitAddon !== "undefined") {
      const FitCtor = FitAddon.FitAddon || FitAddon;
      fitAddon = new FitCtor();
      term.loadAddon(fitAddon);
    }
    term.open(mount);
    fitAddon?.fit();
  }

  function show() {
    ensurePanel();
    initTerminal();
    panelEl.hidden = false;
    visible = true;
    fitAddon?.fit();
  }

  function hide() {
    if (panelEl) panelEl.hidden = true;
    visible = false;
  }

  function writeln(text) {
    if (!term) {
      show();
      initTerminal();
    }
    if (!term) return;
    const lines = String(text || "").split(/\r?\n/);
    lines.forEach((l) => {
      if (l.length) term.writeln(l);
    });
  }

  function onShellEvent(ev) {
    if (ev.started) {
      show();
      writeln("$ " + (ev.command || ""));
    }
    if (ev.chunk) writeln(ev.chunk);
    if (ev.completed) {
      const tail =
        ev.timedOut ? "[timed out]" : typeof ev.exitCode === "number" ? "[exit " + ev.exitCode + "]" : "[done]";
      writeln(tail);
    }
  }

  global.DsTerminalPanel = { show, hide, writeln, onShellEvent };
})(typeof window !== "undefined" ? window : globalThis);
