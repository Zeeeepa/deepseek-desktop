/**
 * Monaco Editor 宿主：只读代码框 + 流式 setValue（对齐 Cursor / VS Code 体验）。
 */
(function () {
  "use strict";

  let loadPromise = null;

  const LANG_MAP = {
    js: "javascript",
    ts: "typescript",
    py: "python",
    sh: "shell",
    bash: "shell",
    yml: "yaml",
    md: "markdown",
    cs: "csharp",
    "c#": "csharp",
    htm: "html",
    html: "html",
    json: "json",
    css: "css",
    txt: "plaintext",
    text: "plaintext",
  };

  function mapLang(lang) {
    const k = String(lang || "text")
      .trim()
      .toLowerCase()
      .split(/\s+/)[0];
    return LANG_MAP[k] || k || "plaintext";
  }

  function ensureLoaded() {
    if (window.monaco) return Promise.resolve();
    if (loadPromise) return loadPromise;
    loadPromise = new Promise((resolve, reject) => {
      const boot = () => {
        if (typeof require === "undefined") {
          reject(new Error("Monaco loader missing"));
          return;
        }
        require.config({ paths: { vs: "monaco/vs" } });
        require(
          ["vs/editor/editor.main"],
          () => resolve(),
          (err) => reject(err)
        );
      };
      if (typeof require !== "undefined") {
        boot();
        return;
      }
      const s = document.createElement("script");
      s.src = "monaco/vs/loader.js";
      s.onload = boot;
      s.onerror = () => reject(new Error("Failed to load monaco/vs/loader.js"));
      document.head.appendChild(s);
    });
    return loadPromise;
  }

  function trimLeadingBlankLines(text) {
    return String(text || "").replace(/^(?:[ \t\r]*\n)+/, "");
  }

  function hasSubstantialCode(text) {
    return /\S/.test(String(text || ""));
  }

  function layoutEditor(editor, host) {
    if (!editor || !host) return;
    const model = editor.getModel();
    const value = model?.getValue() || "";
    if (!hasSubstantialCode(value)) {
      host.style.height = "0px";
      host.style.minHeight = "0";
      editor.layout({ width: host.clientWidth, height: 0 });
      return;
    }
    const lines = value.split("\n");
    const nonEmpty = lines.filter((l) => l.trim().length > 0).length;
    const contentH = editor.getContentHeight();
    const lineH = 20;
    const pad = 12;
    const byLines = Math.max(nonEmpty, 1) * lineH + pad;
    const h = Math.min(Math.max(contentH + 4, byLines, 44), 560);
    host.style.minHeight = "";
    host.style.height = h + "px";
    editor.layout({ width: host.clientWidth, height: h });
  }

  function mount(host, options) {
    const lang = mapLang(options.lang);
    const value = trimLeadingBlankLines(options.value || "");
    const streaming = !!options.streaming;

    function escapeHtml(s) {
      return String(s || "")
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;");
    }

    const mountPlainFallback = () => {
      host.innerHTML =
        '<pre class="ds-code-fallback"><code>' + escapeHtml(value) + "</code></pre>";
      host.closest(".ds-code-block")?.classList.remove("ds-code-streaming");
      host.style.minHeight = "";
      host.style.height = "auto";
      return null;
    };

    if (!hasSubstantialCode(value)) {
      dispose(host);
      return null;
    }

    if (!window.monaco) {
      return mountPlainFallback();
    }

    if (host._dsMonacoEditor) {
      const ed = host._dsMonacoEditor;
      const model = ed.getModel();
      if (model && model.getValue() !== value) {
        const pos = ed.getPosition();
        model.setValue(value);
        if (streaming && pos) {
          const lc = model.getLineCount();
          ed.revealLine(lc);
        }
      }
      host.closest(".ds-code-block")?.classList.toggle("ds-code-streaming", streaming);
      layoutEditor(ed, host);
      return ed;
    }

    const editor = monaco.editor.create(host, {
      value,
      language: lang,
      readOnly: true,
      domReadOnly: true,
      minimap: { enabled: false },
      scrollBeyondLastLine: false,
      fontSize: 13,
      lineHeight: 20,
      fontFamily: 'ui-monospace, "Cascadia Code", Consolas, monospace',
      lineNumbers: "on",
      wordWrap: "on",
      wrappingStrategy: "advanced",
      automaticLayout: false,
      padding: { top: 10, bottom: 10 },
      renderLineHighlight: "none",
      overviewRulerLanes: 0,
      hideCursorInOverviewRuler: true,
      scrollbar: {
        vertical: "auto",
        horizontal: "auto",
        verticalScrollbarSize: 8,
        horizontalScrollbarSize: 8,
      },
      contextmenu: false,
      links: false,
      folding: true,
      glyphMargin: false,
      theme: document.documentElement.classList.contains("ds-dark")
        ? "vs-dark"
        : "vs",
    });

    host._dsMonacoEditor = editor;
    host.closest(".ds-code-block")?.classList.toggle("ds-code-streaming", streaming);

    const ro =
      typeof ResizeObserver !== "undefined"
        ? new ResizeObserver(() => layoutEditor(editor, host))
        : null;
    if (ro) {
      ro.observe(host);
      host._dsMonacoRo = ro;
    }

    layoutEditor(editor, host);
    return editor;
  }

  function dispose(host) {
    if (!host) return;
    host._dsMonacoRo?.disconnect();
    host._dsMonacoRo = null;
    if (host._dsMonacoEditor) {
      host._dsMonacoEditor.dispose();
      host._dsMonacoEditor = null;
    }
  }

  function disposeAll(root) {
    if (!root) return;
    root.querySelectorAll(".ds-monaco-host").forEach(dispose);
  }

  window.DsMonacoHost = {
    ensureLoaded,
    mapLang,
    mount,
    layoutEditor,
    dispose,
    disposeAll,
    trimLeadingBlankLines,
    hasSubstantialCode,
  };
})();
