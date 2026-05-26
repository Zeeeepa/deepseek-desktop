/**
 * Embedded panel URL builder (single source for DSD API / settings iframes).
 * Loaded before agent-app.js.
 */
(function () {
  "use strict";

  function embeddedUiBuild() {
    const m = /[?&]build=(\d+)/.exec(location.search || "");
    return m ? m[1] : "0";
  }

  function embedScheme() {
    try {
      if (window.__dsEmbedScheme) return window.__dsEmbedScheme;
      if (location.protocol === "dsnative:") return "dsnative:";
    } catch (_) {}
    return "https:";
  }

  function embedUrl(path) {
    const sep = path.indexOf("?") >= 0 ? "&" : "?";
    return embedScheme() + "//ds-agent.local/" + path + sep + "build=" + embeddedUiBuild();
  }

  window.DsAgentEmbed = {
    embeddedUiBuild: embeddedUiBuild,
    embedScheme: embedScheme,
    embedUrl: embedUrl,
  };
})();
