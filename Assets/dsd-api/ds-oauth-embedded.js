(function () {
  "use strict";
  if (window.__dsOauthEmbedded) return;
  window.__dsOauthEmbedded = true;

  var DESKTOP_OAUTH_HINT = "登录失败，请重试或改用「手动输入」。";

  function patchI18nMissingKeys() {
    try {
      if (!window.i18next || !window.i18next.addResourceBundle) return;
      var extras = {
        providers: {
          loginFailed: "登录失败",
          validateFailed: "验证失败",
        },
        oauth: {
          loginFailed: "登录失败",
        },
      };
      window.i18next.addResourceBundle("zh-CN", "translation", extras, true, true);
    } catch (_) {}
  }

  function normalizeOAuthResult(result) {
    if (!result || typeof result !== "object") {
      return { success: false, error: DESKTOP_OAUTH_HINT };
    }
    if (result.success) return result;
    var err = (result.error || "").trim();
    if (!err) result.error = DESKTOP_OAUTH_HINT;
    return result;
  }

  function wrapOAuthApi() {
    var api = window.electronAPI && window.electronAPI.oauth;
    if (!api || api.__dsEmbeddedWrapped) return;
    api.__dsEmbeddedWrapped = true;

    var origStart = api.startInAppLogin && api.startInAppLogin.bind(api);
    if (origStart) {
      api.startInAppLogin = function () {
        return Promise.resolve(origStart.apply(api, arguments))
          .then(normalizeOAuthResult)
          .catch(function (e) {
            return {
              success: false,
              error: (e && e.message) || "登录失败",
            };
          });
      };
    }

    var origLogin = api.startLogin && api.startLogin.bind(api);
    if (origLogin) {
      api.startLogin = function () {
        return Promise.resolve(origLogin.apply(api, arguments))
          .then(normalizeOAuthResult)
          .catch(function (e) {
            return {
              success: false,
              error: (e && e.message) || "登录失败",
            };
          });
      };
    }
  }

  function boot() {
    patchI18nMissingKeys();
    wrapOAuthApi();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", boot);
  } else {
    boot();
  }
  window.addEventListener("load", boot);
  setTimeout(boot, 0);
  setTimeout(boot, 500);
})();
