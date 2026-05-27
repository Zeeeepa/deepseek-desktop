/**
 * Agent 回复：Markdown + LaTeX + Monaco 代码框（流式增量更新）。
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

  function hasSubstantialCode(text) {
    return window.DsMonacoHost?.hasSubstantialCode
      ? window.DsMonacoHost.hasSubstantialCode(text)
      : /\S/.test(String(text || ""));
  }

  function trimLeadingBlankLines(text) {
    return window.DsMonacoHost?.trimLeadingBlankLines
      ? window.DsMonacoHost.trimLeadingBlankLines(text)
      : String(text || "").replace(/^(?:[ \t\r]*\n)+/, "");
  }

  /** 剥离 tool_calling / 破损 JSON / 单行工具 JSON，避免正文里出现空 Monaco 框 */
  function prepareAnswerForDisplay(raw) {
    let t = String(raw || "");
    t = t.replace(/<tool_calling>[\s\S]*?<\/tool_calling>/gi, "");
    t = t.replace(/<tool_calling>[\s\S]*$/i, "");
    t = t.replace(/\[TOOL_CALL\][\s\S]*?\[\/TOOL_CALL\]/gi, "");
    t = t.replace(/\[TOOL_CALL\][\s\S]*$/i, "");
    t = t.replace(/<(?:deepseek:)?tool_call[\s\S]*?<\/(?:deepseek:)?tool_call>/gi, "");
    t = t.replace(/<invoke\s+name[^>]*>[\s\S]*?<\/invoke>/gi, "");
    t = t.replace(/"[\s,]*"content"\s*:\s*"[\s\S]*$/i, "");
    t = t.replace(/\{\s*"(?:path|file_path)"[\s\S]*$/i, "");
    t = t.replace(/"?\}\s*<\/arguments>\s*<\/tool_calling>/gi, "");
    t = t.replace(/<\/arguments>\s*<\/tool_calling>/gi, "");
    t = t.replace(/```(?:json)?\s*\{[\s\S]*?"name"[\s\S]*?"arguments"[\s\S]*?\}\s*```/gi, "");
    t = t.replace(/```json\s*```/gi, "");
    t = t.replace(/```json\s*$/gim, "");
    t = stripLooseJsonToolObjects(t);
    t = t.replace(/\n{3,}/g, "\n\n");
    return t.trim();
  }

  function looksLikeToolJsonObject(slice) {
    const s = String(slice || "").trim();
    if (!/"name"\s*:/i.test(s)) return false;
    if (!/"arguments"\s*:/i.test(s) && !/"args"\s*:/i.test(s)) return false;
    try {
      const obj = JSON.parse(s);
      return obj && typeof obj === "object" && (obj.name || obj.tool || obj.function);
    } catch (_) {
      return false;
    }
  }

  function findBalancedJsonEnd(text, start) {
    if (start >= text.length || text[start] !== "{") return -1;
    let depth = 0;
    let inStr = false;
    let esc = false;
    for (let i = start; i < text.length; i++) {
      const c = text[i];
      if (inStr) {
        if (esc) esc = false;
        else if (c === "\\") esc = true;
        else if (c === '"') inStr = false;
        continue;
      }
      if (c === '"') {
        inStr = true;
        continue;
      }
      if (c === "{") depth++;
      else if (c === "}") {
        depth--;
        if (depth === 0) return i;
      }
    }
    return -1;
  }

  function stripLooseJsonToolObjects(text) {
    let src = String(text || "");
    let out = "";
    let last = 0;
    for (let i = 0; i < src.length; i++) {
      if (src[i] !== "{") continue;
      const end = findBalancedJsonEnd(src, i);
      if (end < 0) continue;
      const slice = src.slice(i, end + 1);
      if (!looksLikeToolJsonObject(slice)) continue;
      out += src.slice(last, i);
      last = end + 1;
      i = end;
    }
    if (last === 0) return src;
    return out + src.slice(last);
  }

  function isToolCallPayload(content, lang) {
    const body = String(content || "").trim();
    if (!body) return false;
    if (looksLikeToolJsonObject(body)) return true;
    if ((lang || "").toLowerCase() === "json" && /"name"\s*:/i.test(body) && /"arguments"\s*:/i.test(body))
      return true;
    return false;
  }

  /** 未闭合且仅有空行的围栏留在 Markdown 段，避免提前出现大段空代码框 */
  function guardOpenEmptyFenceInText(text) {
    const m = String(text || "").match(/^(.*?)(```[\w-]*\n?)([\s\S]*)$/s);
    if (!m || hasSubstantialCode(m[3])) return text;
    const fence = m[2];
    const escaped = fence.replace(/`/g, "\u200b`");
    return m[1] + escaped + m[3];
  }

  /** 将全文拆成 markdown 段与代码段（有实质内容才拆出代码段） */
  function parseSegments(text) {
    const segments = [];
    const src = String(text || "");
    const completeRe = /```([\w-]*)\n?([\s\S]*?)```/g;
    let last = 0;
    let m;
    while ((m = completeRe.exec(src))) {
      if (m.index > last) {
        segments.push({ type: "text", content: src.slice(last, m.index) });
      }
      const body = trimLeadingBlankLines(m[2].replace(/\n$/, ""));
      const lang = (m[1] || "").trim() || "text";
      if (hasSubstantialCode(body) && !isToolCallPayload(body, lang)) {
        segments.push({
          type: "code",
          lang,
          content: body,
          complete: true,
        });
      }
      last = m.index + m[0].length;
    }
    const rest = src.slice(last);
    const open = rest.match(/```([\w-]*)\n?([\s\S]*)$/);
    if (open) {
      const body = trimLeadingBlankLines(open[2]);
      const lang = (open[1] || "").trim() || "text";
      if (hasSubstantialCode(body) && !isToolCallPayload(body, lang)) {
        const before = rest.slice(0, open.index);
        if (before) segments.push({ type: "text", content: before });
        segments.push({
          type: "code",
          lang,
          content: body,
          complete: false,
        });
      } else if (rest) {
        segments.push({ type: "text", content: rest });
      }
    } else if (rest) {
      segments.push({ type: "text", content: rest });
    }
    if (!segments.length) segments.push({ type: "text", content: src });
    return promoteBareHtmlSegments(segments);
  }

  /** 模型未写 ``` 时，把裸露的 HTML 文档提成 Monaco 代码段 */
  function promoteBareHtmlSegments(segments) {
    const out = [];
    for (const seg of segments) {
      if (seg.type !== "text") {
        out.push(seg);
        continue;
      }
      let content = seg.content;
      const docRe = /<!DOCTYPE\s+html[\s\S]*?<\/html>/gi;
      let hit = docRe.exec(content);
      if (!hit) {
        const open = /<!DOCTYPE\s+html[\s\S]*$/i.exec(content);
        if (open) {
          const before = content.slice(0, open.index);
          const body = trimLeadingBlankLines(open[0]);
          if (hasSubstantialCode(body)) {
            if (before.trim()) out.push({ type: "text", content: before });
            out.push({
              type: "code",
              lang: "html",
              content: body,
              complete: false,
            });
          } else {
            out.push(seg);
          }
          continue;
        }
        out.push(seg);
        continue;
      }
      docRe.lastIndex = 0;
      let cursor = 0;
      while ((hit = docRe.exec(content))) {
        const before = content.slice(cursor, hit.index);
        if (before.trim()) out.push({ type: "text", content: before });
        out.push({
          type: "code",
          lang: "html",
          content: hit[0],
          complete: true,
        });
        cursor = hit.index + hit[0].length;
      }
      const tail = content.slice(cursor);
      if (tail.trim()) out.push({ type: "text", content: tail });
    }
    return out.length ? out : segments;
  }

  function segmentSignature(segments) {
    return segments
      .map((s) => (s.type === "code" ? "c:" + s.lang : "t"))
      .join("|");
  }

  function configureMarked() {
    if (!window.marked || window.__dsMarkedConfigured) return;
    window.__dsMarkedConfigured = true;

    const renderer = new marked.Renderer();
    renderer.html = function (token) {
      const text = typeof token === "object" ? token.text : arguments[0];
      return escapeHtml(text);
    };
    renderer.code = function (token) {
      const code = typeof token === "object" ? token.text : arguments[0];
      const lang = typeof token === "object" ? token.lang : arguments[1];
      const language = (lang || "").trim().split(/\s+/)[0] || "text";
      if (!hasSubstantialCode(code)) return "";
      return (
        '<pre class="ds-md-pre" data-ds-fallback-lang="' +
        escapeHtml(language) +
        '"><code>' +
        escapeHtml(code) +
        "</code></pre>"
      );
    };

    const options = { gfm: true, breaks: false, renderer };
    if (typeof marked.use === "function") marked.use(options);
    else marked.setOptions(options);
  }

  function renderMarkdownPart(text) {
    const normalized = normalizeMathDelimiters(text);
    const { text: mdSrc, store } = extractMathBlocks(normalized);
    if (window.marked) {
      configureMarked();
      return {
        html: '<div class="ds-md">' + marked.parse(mdSrc) + "</div>",
        store,
      };
    }
    return {
      html:
        '<div class="ds-md"><p>' +
        escapeHtml(mdSrc).replace(/\n\n/g, "</p><p>").replace(/\n/g, "<br>") +
        "</p></div>",
      store: [],
    };
  }

  function renderMathSlots(root, store) {
    if (!root || !store?.length) return;
    root.querySelectorAll("." + MATH_SLOT).forEach((slot) => {
      const id = parseInt(slot.getAttribute("data-ds-math-id"), 10);
      const item = store[id];
      if (!item || !window.katex) return;
      try {
        slot.outerHTML = katex.renderToString(item.tex, {
          displayMode: !!item.display,
          throwOnError: false,
          strict: "ignore",
          trust: false,
        });
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
      /* partial stream */
    }
  }

  function createCodeBlockShell(lang, complete, filePath) {
    const wrap = document.createElement("div");
    wrap.className = "ds-code-block" + (complete ? "" : " ds-code-streaming");
    if (filePath) wrap.dataset.dsFilePath = filePath;

    const head = document.createElement("div");
    head.className = "ds-code-head";
    const label = filePath
      ? '<span class="ds-code-path" title="' + escapeHtml(filePath) + '">' + escapeHtml(filePath) + "</span>"
      : '<span class="ds-code-lang">' + escapeHtml(lang || "text") + "</span>";
    head.innerHTML =
      label +
      '<div class="ds-code-actions">' +
      '<button type="button" class="ds-code-btn" data-action="copy">复制</button>' +
      '<button type="button" class="ds-code-btn" data-action="download">下载</button>' +
      "</div>";

    const body = document.createElement("div");
    body.className = "ds-code-body";
    const host = document.createElement("div");
    host.className = "ds-monaco-host";
    host.setAttribute("data-lang", lang || "text");
    body.appendChild(host);

    wrap.append(head, body);
    return { wrap, host, head };
  }

  function wireCodeActions(head, host, lang) {
    const getValue = () => host._dsMonacoEditor?.getModel()?.getValue() || "";
    head.querySelector('[data-action="copy"]')?.addEventListener("click", () => {
      const raw = getValue();
      if (navigator.clipboard?.writeText) navigator.clipboard.writeText(raw).catch(() => {});
    });
    head.querySelector('[data-action="download"]')?.addEventListener("click", () => {
      const raw = getValue();
      const ext =
        { python: "py", javascript: "js", typescript: "ts", csharp: "cs", shell: "sh", html: "html" }[
          (lang || "").toLowerCase()
        ] || "txt";
      const blob = new Blob([raw], { type: "text/plain;charset=utf-8" });
      const a = document.createElement("a");
      a.href = URL.createObjectURL(blob);
      a.download = "snippet." + ext;
      a.click();
      URL.revokeObjectURL(a.href);
    });
  }

  function renderTextSegment(node, content) {
    const { html, store } = renderMarkdownPart(guardOpenEmptyFenceInText(content));
    node.innerHTML = html;
    renderMathSlots(node, store);
    renderMathFallback(node);
  }

  function showCodePending(node, streaming) {
    const body = node.querySelector(".ds-code-body");
    if (!body) return;
    window.DsMonacoHost?.dispose(body.querySelector(".ds-monaco-host"));
    body.innerHTML =
      '<div class="ds-code-empty">' +
      (streaming ? "代码生成中…" : "（空）") +
      "</div>";
    node.classList.toggle("ds-code-pending", true);
    node.classList.toggle("ds-code-streaming", !!streaming);
  }

  async function mountMonacoOnSegment(node, seg) {
    const host = node.querySelector(".ds-monaco-host");
    if (!host || !window.DsMonacoHost) return;
    const content = trimLeadingBlankLines(seg.content || "");
    const streaming = !seg.complete;
    node.classList.remove("ds-code-pending");

    if (!hasSubstantialCode(content)) {
      showCodePending(node, streaming);
      return;
    }

    try {
      await window.DsMonacoHost.ensureLoaded();
    } catch (_) {
      /* mount() 会在 Monaco 不可用时回退到 pre/code */
    }
    node.classList.toggle("ds-code-streaming", streaming);
    if (!host._dsMonacoEditor && !host.querySelector(".ds-code-fallback")) {
      host.innerHTML = "";
    }
    const ed = window.DsMonacoHost.mount(host, {
      lang: seg.lang,
      value: content,
      streaming,
    });
    if (!ed && !host.querySelector(".ds-code-fallback")) showCodePending(node, streaming);
  }

  async function syncSegmentDom(el, segments) {
    const sig = segmentSignature(segments);
    const rebuild = !el._dsSegmentWrap || el._dsSegmentSig !== sig;

    if (rebuild) {
      window.DsMonacoHost?.disposeAll(el);
      el.innerHTML = "";
      const wrap = document.createElement("div");
      wrap.className = "ds-segments";
      el._dsSegmentNodes = [];

      segments.forEach((seg, i) => {
        if (seg.type === "text") {
          const part = document.createElement("div");
          part.className = "ds-segment ds-segment-text";
          part.dataset.dsIndex = String(i);
          renderTextSegment(part, seg.content);
          wrap.appendChild(part);
          el._dsSegmentNodes.push({ type: "text", el: part });
        } else if (hasSubstantialCode(seg.content)) {
          const { wrap: block, host, head } = createCodeBlockShell(seg.lang, seg.complete);
          block.dataset.dsIndex = String(i);
          wireCodeActions(head, host, seg.lang);
          wrap.appendChild(block);
          el._dsSegmentNodes.push({ type: "code", el: block, seg });
        }
      });

      el.appendChild(wrap);
      el._dsSegmentWrap = wrap;
      el._dsSegmentSig = sig;
    } else {
      segments.forEach((seg, i) => {
        const slot = el._dsSegmentNodes[i];
        if (!slot) return;
        if (seg.type === "text" && slot.type === "text") {
          renderTextSegment(slot.el, seg.content);
        } else if (seg.type === "code" && slot.type === "code") {
          slot.seg = seg;
        }
      });
    }

    for (let i = 0; i < segments.length; i++) {
      const seg = segments[i];
      const slot = el._dsSegmentNodes[i];
      if (seg.type === "code" && slot?.type === "code") {
        await mountMonacoOnSegment(slot.el, seg);
      }
    }
  }

  async function mountFilePreview(slot, preview) {
    if (!slot) return;
    const path = preview.path || "file";
    const lang = preview.lang || preview.language || "text";
    const content = preview.content || "";
    const complete = preview.complete !== false;

    slot.innerHTML = "";
    const { wrap, host, head } = createCodeBlockShell(lang, complete, path);
    wireCodeActions(head, host, lang);
    slot.appendChild(wrap);
    slot._dsPreviewBlock = wrap;

    await window.DsMonacoHost?.ensureLoaded();
    if (host && window.DsMonacoHost) {
      window.DsMonacoHost.mount(host, { lang, value: content, streaming: !complete });
    }
  }

  function updateFilePreview(slot, preview) {
    const block = slot._dsPreviewBlock || slot.querySelector(".ds-code-block");
    const host = block?.querySelector(".ds-monaco-host");
    if (!host || !window.DsMonacoHost) return;
    window.DsMonacoHost.mount(host, {
      lang: preview.lang || preview.language || "text",
      value: preview.content || "",
      streaming: preview.complete === false,
    });
    block?.classList.toggle("ds-code-streaming", preview.complete === false);
  }

  async function apply(el, text) {
    if (!el) return;
    const src = prepareAnswerForDisplay(text ?? el._dsRawText ?? "");
    el._dsRawText = src;
    el.classList.add("ds-msg-answer", "ds-md-root");

    const segments = parseSegments(src);
    await syncSegmentDom(el, segments);
  }

  function scheduleApply(el, text, delayMs) {
    if (!el) return;
    el._dsRawText = text;
    clearTimeout(el._dsRenderTimer);
    el._dsRenderTimer = setTimeout(() => {
      apply(el, text).catch(() => {
        el.textContent = text || "";
      });
    }, delayMs == null ? 80 : delayMs);
  }

  window.DsMessageRender = {
    apply,
    scheduleApply,
    prepareAnswerForDisplay,
    mountFilePreview,
    updateFilePreview,
    normalizeMathDelimiters,
    extractMathBlocks,
    parseSegments,
    renderMarkdownHtml: renderMarkdownPart,
  };
})();
