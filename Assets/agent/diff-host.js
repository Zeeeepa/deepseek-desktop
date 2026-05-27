/* global monaco, require */
(function (global) {
  const editors = new Map();

  function ensureMonaco(cb) {
    if (global.monaco) {
      cb();
      return;
    }
    if (typeof require !== "function") return;
    require.config({ paths: { vs: "monaco/vs" } });
    require(["vs/editor/editor.main"], () => cb());
  }

  function mountDiff(container, original, modified, language) {
    return new Promise((resolve) => {
      ensureMonaco(() => {
        const el = typeof container === "string" ? document.getElementById(container) : container;
        if (!el) {
          resolve(null);
          return;
        }
        const id = "diff_" + Math.random().toString(36).slice(2, 9);
        const diffEditor = monaco.editor.createDiffEditor(el, {
          readOnly: true,
          renderSideBySide: true,
          automaticLayout: true,
          minimap: { enabled: false },
        });
        const origModel = monaco.editor.createModel(original || "", language || "plaintext");
        const modModel = monaco.editor.createModel(modified || "", language || "plaintext");
        diffEditor.setModel({ original: origModel, modified: modModel });
        editors.set(id, { diffEditor, origModel, modModel });
        resolve({ id, diffEditor });
      });
    });
  }

  function disposeDiff(id) {
    const entry = editors.get(id);
    if (!entry) return;
    entry.origModel.dispose();
    entry.modModel.dispose();
    entry.diffEditor.dispose();
    editors.delete(id);
  }

  function buildPatchCard(patch, onResolve) {
    const wrap = document.createElement("div");
    wrap.className = "ds-patch-card";
    wrap.dataset.patchId = patch.patchId;

    const head = document.createElement("div");
    head.className = "ds-patch-head";
    head.innerHTML =
      '<span class="ds-patch-path"></span>' +
      '<span class="ds-patch-actions">' +
      '<button type="button" class="ds-btn ds-btn-sm ds-patch-accept">Accept</button>' +
      '<button type="button" class="ds-btn ds-btn-sm ds-patch-reject">Reject</button>' +
      "</span>";
    head.querySelector(".ds-patch-path").textContent = patch.path || "file";
    wrap.appendChild(head);

    const body = document.createElement("div");
    body.className = "ds-patch-diff";
    body.style.minHeight = "180px";
    wrap.appendChild(body);

    mountDiff(body, patch.originalContent || "", patch.patchedContent || "", patch.language || "plaintext");

    head.querySelector(".ds-patch-accept").addEventListener("click", () => {
      onResolve(patch.patchId, true);
      wrap.classList.add("ds-patch-resolved");
    });
    head.querySelector(".ds-patch-reject").addEventListener("click", () => {
      onResolve(patch.patchId, false);
      wrap.classList.add("ds-patch-rejected");
    });

    return wrap;
  }

  global.DsDiffHost = { mountDiff, disposeDiff, buildPatchCard };
})(typeof window !== "undefined" ? window : globalThis);
