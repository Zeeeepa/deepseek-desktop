(function () {
  "use strict";
  if (window.__dsDsdApiPreload) return;
  window.__dsDsdApiPreload = true;

  try {
    localStorage.setItem("i18nextLng", "zh-CN");
  } catch (_) {}

  (function ensureDefaultRoute() {
    try {
      var hash = location.hash || "";
      if (
        !hash ||
        hash === "#" ||
        hash === "#/" ||
        hash.indexOf("#/proxy") === 0 ||
        hash.indexOf("#/about") === 0
      ) {
        location.replace(location.pathname + location.search + "#/");
      }
    } catch (_) {}
  })();

  var pending = new Map();
  var seq = 0;
  var eventSubs = new Map();

  function isInAgentIframe() {
    try {
      return window.parent !== window;
    } catch (_) {
      return false;
    }
  }

  function post(type, payload) {
    var body = Object.assign({ type: type, __dsEmbed: true }, payload || {});
    var json = JSON.stringify(body);
    if (isInAgentIframe()) {
      window.parent.postMessage(json, "*");
      return;
    }
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(json);
      return;
    }
    if (window.parent && window.parent !== window) {
      window.parent.postMessage(json, "*");
    }
  }

  var ipcTimeoutMs = 20000;

  function ipcChannelHasTimeout(channel) {
    if (!channel) return true;
    if (channel.indexOf("oauth:") === 0) return false;
    if (channel.indexOf("toolCalling:") === 0) return false;
    return true;
  }

  function invoke(channel) {
    var args = Array.prototype.slice.call(arguments, 1);
    return new Promise(function (resolve, reject) {
      var id = ++seq;
      pending.set(id, { resolve: resolve, reject: reject });
      post("ipcInvoke", { id: id, channel: channel, args: args });
      if (ipcChannelHasTimeout(channel)) {
        setTimeout(function () {
          if (!pending.has(id)) return;
          pending.delete(id);
          reject(new Error("IPC timeout: " + channel));
        }, ipcTimeoutMs);
      }
    });
  }

  function on(channel, callback) {
    if (!eventSubs.has(channel)) eventSubs.set(channel, new Set());
    eventSubs.get(channel).add(callback);
    return function () {
      eventSubs.get(channel)?.delete(callback);
    };
  }

  function emit(channel) {
    var args = Array.prototype.slice.call(arguments, 1);
    var subs = eventSubs.get(channel);
    if (!subs) return;
    subs.forEach(function (cb) {
      try {
        cb.apply(null, args);
      } catch (e) {
        console.warn("[DSD API preload] event handler error:", e);
      }
    });
  }

  function parseHostMessage(data) {
    if (data == null) return null;
    if (typeof data === "object") return data;
    if (typeof data === "string") {
      try {
        return JSON.parse(data);
      } catch (_) {
        return null;
      }
    }
    return null;
  }

  function handleHostMessage(data) {
    var msg = parseHostMessage(data);
    if (!msg) return;
    if (msg.type === "ipcResult") {
      var p = pending.get(msg.id);
      if (!p) return;
      pending.delete(msg.id);
      if (msg.error) p.reject(new Error(msg.error));
      else p.resolve(msg.result);
    } else if (msg.type === "ipcEvent") {
      emit.apply(null, [msg.channel].concat(msg.args || []));
    }
  }

  function bindHostMessages() {
    if (isInAgentIframe()) {
      window.addEventListener("message", function (e) {
        if (e.source !== window.parent) return;
        handleHostMessage(e.data);
      });
      return;
    }
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.addEventListener("message", function (e) {
        handleHostMessage(e.data);
      });
      return;
    }
    if (window.parent && window.parent !== window) {
      window.addEventListener("message", function (e) {
        if (e.source !== window.parent) return;
        handleHostMessage(e.data);
      });
      return;
    }
    setTimeout(bindHostMessages, 20);
  }

  bindHostMessages();

  var proxyAPI = {
    start: function (port) {
      return invoke("proxy:start", port);
    },
    stop: function () {
      return invoke("proxy:stop");
    },
    getStatus: function () {
      return invoke("proxy:getStatus");
    },
    onStatusChanged: function (callback) {
      return on("proxy:statusChanged", callback);
    },
  };

  var storeAPI = {
    get: function (key) {
      return invoke("store:get", key);
    },
    set: function (key, value) {
      return invoke("store:set", key, value);
    },
    delete: function (key) {
      return invoke("store:delete", key);
    },
    clearAll: function () {
      return invoke("store:clearAll");
    },
    onInitError: function (callback) {
      return on("store:initError", callback);
    },
    retryInit: function () {
      return invoke("store:retryInit");
    },
  };

  var providersAPI = {
    getAll: function () {
      return invoke("providers:getAll");
    },
    getBuiltin: function () {
      return invoke("providers:getBuiltin");
    },
    add: function (data) {
      return invoke("providers:add", data);
    },
    update: function (id, updates) {
      return invoke("providers:update", id, updates);
    },
    delete: function (id) {
      return invoke("providers:delete", id);
    },
    checkStatus: function (providerId) {
      return invoke("providers:checkStatus", providerId);
    },
    checkAllStatus: function () {
      return invoke("providers:checkAllStatus");
    },
    duplicate: function (id) {
      return invoke("providers:duplicate", id);
    },
    export: function (id) {
      return invoke("providers:export", id);
    },
    import: function (jsonData) {
      return invoke("providers:import", jsonData);
    },
    updateModels: function (providerId) {
      return invoke("providers:updateModels", providerId);
    },
    getEffectiveModels: function (providerId) {
      return invoke("providers:getEffectiveModels", providerId);
    },
    addCustomModel: function (providerId, model) {
      return invoke("providers:addCustomModel", providerId, model);
    },
    removeModel: function (providerId, modelName) {
      return invoke("providers:removeModel", providerId, modelName);
    },
    resetModels: function (providerId) {
      return invoke("providers:resetModels", providerId);
    },
  };

  var accountsAPI = {
    getAll: function (includeCredentials) {
      return invoke("accounts:getAll", includeCredentials);
    },
    getById: function (id, includeCredentials) {
      return invoke("accounts:getById", id, includeCredentials);
    },
    getByProvider: function (providerId) {
      return invoke("accounts:getByProvider", providerId);
    },
    add: function (data) {
      return invoke("accounts:add", data);
    },
    update: function (id, updates) {
      return invoke("accounts:update", id, updates);
    },
    delete: function (id) {
      return invoke("accounts:delete", id);
    },
    validate: function (accountId) {
      return invoke("accounts:validate", accountId);
    },
    validateToken: function (providerId, credentials) {
      return invoke("accounts:validateToken", providerId, credentials);
    },
    getCredits: function (accountId) {
      return invoke("accounts:getCredits", accountId);
    },
    clearChats: function (accountId) {
      return invoke("accounts:clearChats", accountId);
    },
  };

  var oauthAPI = {
    startLogin: function (providerId, providerType) {
      return invoke("oauth:startLogin", providerId, providerType);
    },
    cancelLogin: function () {
      return invoke("oauth:cancelLogin");
    },
    loginWithToken: function (providerId, providerType, token) {
      return invoke("oauth:loginWithToken", {
        providerId: providerId,
        providerType: providerType,
        token: token,
      });
    },
    validateToken: function (providerId, providerType, credentials) {
      return invoke("oauth:validateToken", {
        providerId: providerId,
        providerType: providerType,
        credentials: credentials,
      });
    },
    refreshToken: function (providerId, providerType, credentials) {
      return invoke("oauth:refreshToken", {
        providerId: providerId,
        providerType: providerType,
        credentials: credentials,
      });
    },
    getStatus: function () {
      return invoke("oauth:getStatus");
    },
    startInAppLogin: function (providerId, providerType, timeout) {
      return invoke("oauth:startInAppLogin", {
        providerId: providerId,
        providerType: providerType,
        timeout: timeout,
      });
    },
    cancelInAppLogin: function () {
      return invoke("oauth:cancelInAppLogin");
    },
    isInAppLoginOpen: function () {
      return invoke("oauth:inAppLoginStatus");
    },
    onCallback: function (callback) {
      return on("oauth:callback", callback);
    },
    onProgress: function (callback) {
      return on("oauth:progress", callback);
    },
  };

  var logsAPI = {
    get: function (filter) {
      return invoke("logs:get", filter);
    },
    getStats: function () {
      return invoke("logs:getStats");
    },
    getTrend: function (days) {
      return invoke("logs:getTrend", days);
    },
    getAccountTrend: function (accountId, days) {
      return invoke("logs:getAccountTrend", accountId, days);
    },
    clear: function () {
      return invoke("logs:clear");
    },
    export: function (format) {
      return invoke("logs:export", format);
    },
    getById: function (id) {
      return invoke("logs:getById", id);
    },
    onNewLog: function (callback) {
      return on("logs:newLog", callback);
    },
  };

  var requestLogsAPI = {
    get: function (filter) {
      return invoke("requestLogs:get", filter);
    },
    getById: function (id) {
      return invoke("requestLogs:getById", id);
    },
    getStats: function () {
      return invoke("requestLogs:getStats");
    },
    getTrend: function (days) {
      return invoke("requestLogs:getTrend", days);
    },
    clear: function () {
      return invoke("requestLogs:clear");
    },
    onNewLog: function (callback) {
      return on("requestLogs:new", callback);
    },
  };

  var statisticsAPI = {
    get: function () {
      return invoke("statistics:get");
    },
    getToday: function () {
      return invoke("statistics:getToday");
    },
  };

  var noopUnsub = function () {
    return function () {};
  };
  var disabledUpdateStatus = {
    checking: false,
    available: false,
    downloading: false,
    downloaded: false,
    error: null,
    progress: null,
    version: null,
    releaseDate: null,
    releaseNotes: null,
  };

  var appAPI = {
    getVersion: function () {
      return Promise.resolve("1.3.0-edge");
    },
    minimize: function () {
      return invoke("app:minimize");
    },
    maximize: function () {
      return invoke("app:maximize");
    },
    close: function () {
      return invoke("app:close");
    },
    showWindow: function () {
      return invoke("app:showWindow");
    },
    hideWindow: function () {
      return invoke("app:hideWindow");
    },
    openExternal: function () {
      return Promise.resolve();
    },
    checkUpdate: function () {
      return Promise.resolve({
        hasUpdate: false,
        currentVersion: "1.3.0-edge",
        latestVersion: "1.3.0-edge",
      });
    },
    downloadUpdate: function () {
      return Promise.resolve(null);
    },
    installUpdate: function () {
      return Promise.resolve(null);
    },
    getUpdateStatus: function () {
      return Promise.resolve(disabledUpdateStatus);
    },
    onUpdateChecking: noopUnsub,
    onUpdateAvailable: noopUnsub,
    onUpdateNotAvailable: noopUnsub,
    onUpdateProgress: noopUnsub,
    onUpdateDownloaded: noopUnsub,
    onUpdateError: noopUnsub,
  };

  var configAPI = {
    get: function () {
      return invoke("config:get");
    },
    update: function (updates) {
      return invoke("config:update", updates);
    },
    onConfigChanged: function (callback) {
      return on("config:changed", callback);
    },
  };

  var promptsAPI = {
    getAll: function () {
      return invoke("prompts:getAll");
    },
    getBuiltin: function () {
      return invoke("prompts:getBuiltin");
    },
    getCustom: function () {
      return invoke("prompts:getCustom");
    },
    getById: function (id) {
      return invoke("prompts:getById", id);
    },
    add: function (prompt) {
      return invoke("prompts:add", prompt);
    },
    update: function (id, updates) {
      return invoke("prompts:update", id, updates);
    },
    delete: function (id) {
      return invoke("prompts:delete", id);
    },
    getByType: function (type) {
      return invoke("prompts:getByType", type);
    },
  };

  var sessionAPI = {
    getConfig: function () {
      return invoke("session:getConfig");
    },
    updateConfig: function (config) {
      return invoke("session:updateConfig", config);
    },
    getAll: function () {
      return invoke("session:getAll");
    },
    getActive: function () {
      return invoke("session:getActive");
    },
    getById: function (id) {
      return invoke("session:getById", id);
    },
    getByAccount: function (accountId) {
      return invoke("session:getByAccount", accountId);
    },
    getByProvider: function (providerId) {
      return invoke("session:getByProvider", providerId);
    },
    delete: function (id) {
      return invoke("session:delete", id);
    },
    clearAll: function () {
      return invoke("session:clearAll");
    },
    cleanExpired: function () {
      return invoke("session:cleanExpired");
    },
  };

  var managementApiAPI = {
    getConfig: function () {
      return invoke("managementApi:getConfig");
    },
    updateConfig: function (updates) {
      return invoke("managementApi:updateConfig", updates);
    },
    generateSecret: function () {
      return invoke("managementApi:generateSecret");
    },
  };

  var contextManagementAPI = {
    getConfig: function () {
      return invoke("contextManagement:getConfig");
    },
    updateConfig: function (updates) {
      return invoke("contextManagement:updateConfig", updates);
    },
  };

  var toolCallingAPI = {
    getStatus: function () {
      return invoke("toolCalling:getStatus");
    },
    runSmoke: function (input) {
      return invoke("toolCalling:runSmoke", input);
    },
  };

  var trayAPI = {
    openDashboard: function () {
      post("trayOpenDashboard", {});
    },
    setHeight: function (height) {
      post("traySetHeight", { height: height });
    },
    quitApp: function () {
      post("trayQuitApp", {});
    },
  };

  window.electronAPI = {
    proxy: proxyAPI,
    store: storeAPI,
    providers: providersAPI,
    accounts: accountsAPI,
    oauth: oauthAPI,
    logs: logsAPI,
    requestLogs: requestLogsAPI,
    statistics: statisticsAPI,
    app: appAPI,
    config: configAPI,
    prompts: promptsAPI,
    session: sessionAPI,
    managementApi: managementApiAPI,
    contextManagement: contextManagementAPI,
    toolCalling: toolCallingAPI,
    tray: trayAPI,
    on: on,
    send: function (channel) {
      var args = Array.prototype.slice.call(arguments, 1);
      post("ipcSend", { channel: channel, args: args });
    },
    invoke: invoke,
  };
})();
