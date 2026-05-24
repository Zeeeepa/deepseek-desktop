/**
 * Agent 回复 Markdown + LaTeX + 代码块（对齐 DeepSeek 网页版）。
 * 公式须在 marked 之前抽出：否则 GFM 会把 $$ 拆成多个 <p>，KaTeX 无法配对渲染。
 */
(function () {
  "use strict";

  const MATH_SLOT = "ds-math-slot";

  function escapeHtml(s) {
    return String(s || "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  /** DeepSeek API 常用 \[ \] \( \) → KaTeX 可识别的 $$ $ */
  function normalizeMathDelimiters(text) {
    let s = String(text || "");
    s = s.replace(/\\\[([\s\S]*?)\\\]/g, (_, body) => "\n$$\n" + body.trim() + "\n$$\n");
    s = s.replace(/\\\(([\s\S]*?)\\\)/g, (_, body) => "$" + body.trim() + "$");
    return s;
  }

  function splitOutsideFences(text) {
    const parts = [];
    const re = /```[\w-]*\n?[\s\S]*?```/g;
    let last = 0;
    let m;
    while ((m = re.exec(text))) {
      if (m.index > last) parts.push({ kind: "text", value: text.slice(last, m.index) });
      parts.push({ kind: "fence", value: m[0] });
      last = m.index + m[0].length;
    }
    if (last < text.length) parts.push({ kind: "text", value: text.slice(last) });
    if (!parts.length) parts.push({ kind: "text", value: text });
    return parts;
  }

  /** 在纯文本段内把 $/$$ 换成占位元素，返回拼接后的文段与公式表 */
  function extractMathInText(segment, store) {
    let s = segment;

    s = s.replace(/\$\$([\s\S]*?)\$\$/g, (_, body) => {
      const id = store.length;
      store.push({ tex: body.trim(), display: true });
      return (
        '\n\n<div class="' +
        MATH_SLOT +
        '" data-ds-math-id="' +
        id +
        '" data-ds-display="1"></div>\n\n'
      );
    });

    s = s.replace(/(?<!\$)\$(?!\$)((?:\\.|[^$\n\\])+?)\$(?!\$)/g, (_, body) => {
      const id = store.length;
      store.push({ tex: body.trim(), display: false });
      return (
        '<span class="' +
        MATH_SLOT +
        '" data-ds-math-id="' +
        id +
        '" data-ds-display="0"></span>'
      );
    });

    return s;
  }

  function extractMathBlocks(text) {
    const store = [];
    const chunks = splitOutsideFences(text).map((part) => {
      if (part.kind === "fence") return part.value;
      return extractMathInText(part.value, store);
    });
    return { text: chunks.join(""), store };
  }

  function configureMarked() {
    if (!window.marked || window.__dsMarkedConfigured) return;
    window.__dsMarkedConfigured = true;

    const renderer = new marked.Renderer();
    const baseCode = renderer.code.bind(renderer);
    renderer.code = function (token) {
      const code = typeof token === "object" ? token.text : arguments[0];
      const lang = typeof token === "object" ? token.lang : arguments[1];
      const language = (lang || "").trim().split(/\s+/)[0] || "text";
      let highlighted = escapeHtml(code);
      if (window.hljs) {
        try {
          if (lang && hljs.getLanguage(language)) {
            highlighted = hljs.highlight(code, { language }).value;
          } else {
            highlighted = hljs.highlightAuto(code).value;
          }
        } catch (_) {
          /* keep escaped */
        }
      }
      return (
        '<pre class="ds-md-pre"><code class="language-' +
        escapeHtml(language) +
        '">' +
        highlighted +
        "</code></pre>"
      );
    };

    marked.setOptions({
      gfm: true,
      breaks: false,
      renderer,
    });
  }

  function renderMathSlots(root, store) {
    if (!root || !store?.length) return;
    root.querySelectorAll("." + MATH_SLOT).forEach((slot) => {
      const id = parseInt(slot.getAttribute("data-ds-math-id"), 10);
      const item = store[id];
      if (!item || !window.katex) return;
      try {
        const html = katex.renderToString(item.tex, {
          displayMode: !!item.display,
          throwOnError: false,
          strict: "ignore",
          trust: false,
        });
        slot.outerHTML = html;
      } catch (_) {
        slot.textContent = item.display ? "$$" + item.tex + "$$" : "$" + item.tex + "$";
      }
    });
  }

  function renderMathFallback(root) {
    if (!root || !window.renderMathInElement) return;
    try {
      renderMathInElement(root, {
        delimiters: [
          { left: "$$", right: "$$", display: true },
          { left: "$", right: "$", display: false },
        ],
        throwOnError: false,
        strict: "ignore",
      });
    } catch (_) {
      /* streaming partial */
    }
  }

  function renderMarkdownHtml(text) {
    const normalized = normalizeMathDelimiters(text);
    const { text: mdSrc, store } = extractMathBlocks(normalized);

    if (window.marked) {
      configureMarked();
      const html = marked.parse(mdSrc);
      return { html: '<div class="ds-md">' + html + "</div>", store };
    }
    return {
      html:
        '<div class="ds-md"><p>' +
        escapeHtml(mdSrc).replace(/\n\n/g, "</p><p>").replace(/\n/g, "<br>") +
        "</p></div>",
      store,
    };
  }

  function enhanceCodeBlocks(root) {
    if (!root) return;
    root.querySelectorAll("pre.ds-md-pre").forEach((pre) => {
      if (pre.dataset.dsEnhanced === "1") return;
      pre.dataset.dsEnhanced = "1";
      const code = pre.querySelector("code");
      const raw = code?.textContent || "";
      const langClass = [...(code?.classList || [])].find((c) => c.startsWith("language-"));
      const lang = langClass ? langClass.slice(9) : "text";

      const wrap = document.createElement("div");
      wrap.className = "ds-code-block";

      const head = document.createElement("div");
      head.className = "ds-code-head";
      head.innerHTML =
        '<span class="ds-code-lang">' +
        escapeHtml(lang) +
        '</span><div class="ds-code-actions">' +
        '<button type="button" class="ds-code-btn" data-action="copy">复制</button>' +
        '<button type="button" class="ds-code-btn" data-action="download">下载</button>' +
        "</div>";

      const body = document.createElement("div");
      body.className = "ds-code-body";
      body.appendChild(pre);

      wrap.append(head, body);
      pre.parentNode?.replaceChild(wrap, pre);

      head.querySelector('[data-action="copy"]')?.addEventListener("click", () => {
        if (navigator.clipboard?.writeText) {
          navigator.clipboard.writeText(raw).catch(() => {});
        }
      });
      head.querySelector('[data-action="download"]')?.addEventListener("click", () => {
        const ext =
          { python: "py", javascript: "js", typescript: "ts", csharp: "cs", shell: "sh", bash: "sh" }[
            lang
          ] || "txt";
        const blob = new Blob([raw], { type: "text/plain;charset=utf-8" });
        const a = document.createElement("a");
        a.href = URL.createObjectURL(blob);
        a.download = "snippet." + ext;
        a.click();
        URL.revokeObjectURL(a.href);
      });
    });
  }

  function apply(el, text) {
    if (!el) return;
    const src = text ?? el._dsRawText ?? "";
    el._dsRawText = src;
    el.classList.add("ds-msg-answer", "ds-md-root");
    const { html, store } = renderMarkdownHtml(src);
    el.innerHTML = html;
    renderMathSlots(el, store);
    enhanceCodeBlocks(el);
    renderMathFallback(el);
  }

  function scheduleApply(el, text, delayMs) {
    if (!el) return;
    el._dsRawText = text;
    clearTimeout(el._dsRenderTimer);
    el._dsRenderTimer = setTimeout(() => apply(el, text), delayMs == null ? 120 : delayMs);
  }

  window.DsMessageRender = {
    apply,
    scheduleApply,
    normalizeMathDelimiters,
    extractMathBlocks,
    renderMarkdownHtml,
  };
})();
