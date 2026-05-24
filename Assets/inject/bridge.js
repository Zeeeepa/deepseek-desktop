(function () {
  if (window.__dsBridge) return;
  window.__dsBridge = true;

  const WEB_API = "https://chat.deepseek.com/api";
  const WASM_URL = "https://ds-inject.local/sha3_wasm_bg.7b9ca65ddd.wasm";

  const HEADERS = {
    Accept: "*/*",
    "Accept-Encoding": "gzip, deflate, br, zstd",
    "Accept-Language": "zh-CN,zh;q=0.9,en;q=0.8",
    Origin: "https://chat.deepseek.com",
    Referer: "https://chat.deepseek.com/",
    "Sec-Ch-Ua": '"Chromium";v="134", "Not:A-Brand";v="24", "Google Chrome";v="134"',
    "Sec-Ch-Ua-Mobile": "?0",
    "Sec-Ch-Ua-Platform": '"Windows"',
    "Sec-Fetch-Dest": "empty",
    "Sec-Fetch-Mode": "cors",
    "Sec-Fetch-Site": "same-origin",
    "User-Agent":
      "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36",
    "X-App-Version": "20241129.1",
    "X-Client-Locale": "zh-CN",
    "X-Client-Platform": "web",
    "x-Client-Timezone-Offset": String(-new Date().getTimezoneOffset()),
    "X-Client-Version": "1.8.0",
  };

  let tokenCache = null;
  let sessionCache = null;

  function getUserToken() {
    try {
      if (window.__dsWebUserToken) return String(window.__dsWebUserToken);
      const raw = localStorage.getItem("userToken") || "";
      if (!raw) return "";
      try {
        const parsed = JSON.parse(raw);
        if (typeof parsed === "string") return parsed;
        if (parsed && typeof parsed === "object" && typeof parsed.value === "string") return parsed.value;
      } catch (_) {}
      return raw;
    } catch {
      return "";
    }
  }

  function biz(data) {
    return data?.data?.biz_data || data?.biz_data || data?.data || {};
  }

  async function acquireToken() {
    const userToken = getUserToken();
    if (!userToken) throw new Error("未登录：请先在网页登录 DeepSeek");

    const now = Math.floor(Date.now() / 1000);
    if (tokenCache && tokenCache.userToken === userToken && tokenCache.expiresAt > now) {
      return tokenCache.accessToken;
    }

    const r = await fetch(WEB_API + "/v0/users/current", {
      headers: { Authorization: "Bearer " + userToken, ...HEADERS },
    });
    if (r.status === 401 || r.status === 403) throw new Error("网页登录已过期，请重新登录");
    if (!r.ok) throw new Error("获取网页 Token 失败: " + r.status);

    const j = await r.json();
    if (j?.code === 40003 || j?.data?.biz_code === 40003) {
      throw new Error("网页登录已过期，请在普通对话页重新登录 DeepSeek");
    }
    if (j?.code && j.code !== 0) {
      throw new Error(j.msg || j.data?.biz_msg || "获取网页 Token 失败");
    }
    const bd = biz(j);
    const accessToken = bd.token || bd.access_token;
    if (!accessToken) throw new Error("网页 Token 为空: " + (j?.msg || j?.data?.biz_msg || "unknown"));

    tokenCache = { userToken, accessToken, expiresAt: now + 3500 };
    return accessToken;
  }

  async function createSession(accessToken) {
    if (sessionCache && Date.now() - sessionCache.createdAt < 300000) {
      return sessionCache.sessionId;
    }

    const r = await fetch(WEB_API + "/v0/chat_session/create", {
      method: "POST",
      headers: {
        Authorization: "Bearer " + accessToken,
        "Content-Type": "application/json",
        ...HEADERS,
      },
      body: "{}",
    });
    if (!r.ok) throw new Error("创建会话失败: " + r.status);

    const j = await r.json();
    const bd = biz(j);
    const sessionId = bd.chat_session?.id || bd.id;
    if (!sessionId) throw new Error("session id 为空");

    sessionCache = { sessionId, createdAt: Date.now() };
    return sessionId;
  }

  class DeepSeekHash {
    constructor() {
      this.offset = 0;
      this.cachedUint8Memory = null;
      this.cachedTextEncoder = new TextEncoder();
    }

    getCachedUint8Memory() {
      if (!this.cachedUint8Memory || this.cachedUint8Memory.byteLength === 0) {
        this.cachedUint8Memory = new Uint8Array(this.wasmInstance.memory.buffer);
      }
      return this.cachedUint8Memory;
    }

    encodeString(text, allocate, reallocate) {
      if (!reallocate) {
        const encoded = this.cachedTextEncoder.encode(text);
        const ptr = allocate(encoded.length, 1) >>> 0;
        this.getCachedUint8Memory().subarray(ptr, ptr + encoded.length).set(encoded);
        this.offset = encoded.length;
        return ptr;
      }
      const strLength = text.length;
      let ptr = allocate(strLength, 1) >>> 0;
      const memory = this.getCachedUint8Memory();
      let asciiLength = 0;
      for (; asciiLength < strLength; asciiLength++) {
        const code = text.charCodeAt(asciiLength);
        if (code > 127) break;
        memory[ptr + asciiLength] = code;
      }
      if (asciiLength !== strLength) {
        if (asciiLength > 0) text = text.slice(asciiLength);
        ptr = reallocate(ptr, strLength, asciiLength + text.length * 3, 1) >>> 0;
        const result = this.cachedTextEncoder.encodeInto(
          text,
          this.getCachedUint8Memory().subarray(ptr + asciiLength, ptr + asciiLength + text.length * 3)
        );
        asciiLength += result.written;
        ptr = reallocate(ptr, asciiLength + text.length * 3, asciiLength, 1) >>> 0;
      }
      this.offset = asciiLength;
      return ptr;
    }

    calculateHash(algorithm, challenge, salt, difficulty, expireAt) {
      if (algorithm !== "DeepSeekHashV1") throw new Error("Unsupported algorithm: " + algorithm);
      const prefix = salt + "_" + expireAt + "_";
      const retptr = this.wasmInstance.__wbindgen_add_to_stack_pointer(-16);
      try {
        const ptr0 = this.encodeString(
          challenge,
          this.wasmInstance.__wbindgen_export_0,
          this.wasmInstance.__wbindgen_export_1
        );
        const len0 = this.offset;
        const ptr1 = this.encodeString(
          prefix,
          this.wasmInstance.__wbindgen_export_0,
          this.wasmInstance.__wbindgen_export_1
        );
        const len1 = this.offset;
        this.wasmInstance.wasm_solve(retptr, ptr0, len0, ptr1, len1, difficulty);
        const view = new DataView(this.wasmInstance.memory.buffer);
        const status = view.getInt32(retptr, true);
        const value = view.getFloat64(retptr + 8, true);
        return status === 0 ? undefined : value;
      } finally {
        this.wasmInstance.__wbindgen_add_to_stack_pointer(16);
      }
    }

    async init(url) {
      const buf = await fetch(url).then((r) => r.arrayBuffer());
      const { instance } = await WebAssembly.instantiate(buf, { wbg: {} });
      this.wasmInstance = instance.exports;
    }
  }

  let hashInstance = null;
  async function getDeepSeekHash() {
    if (!hashInstance) {
      hashInstance = new DeepSeekHash();
      try {
        await hashInstance.init(WASM_URL);
      } catch (err) {
        hashInstance = null;
        throw new Error("PoW WASM 加载失败: " + (err && err.message ? err.message : err));
      }
    }
    return hashInstance;
  }

  async function getChallenge(accessToken) {
    const r = await fetch(WEB_API + "/v0/chat/create_pow_challenge", {
      method: "POST",
      headers: {
        Authorization: "Bearer " + accessToken,
        "Content-Type": "application/json",
        ...HEADERS,
      },
      body: JSON.stringify({ target_path: "/api/v0/chat/completion" }),
    });
    if (!r.ok) throw new Error("PoW challenge 失败: " + r.status);
    const j = await r.json();
    const ch = biz(j).challenge;
    if (!ch) throw new Error("challenge 为空");
    return ch;
  }

  async function calculateChallengeAnswer(challenge) {
    const { algorithm, challenge: challengeStr, salt, difficulty, expire_at, signature } = challenge;
    const hasher = await getDeepSeekHash();
    const answer = hasher.calculateHash(algorithm, challengeStr, salt, difficulty, expire_at);
    if (answer === undefined) throw new Error("PoW 计算失败");
    return btoa(
      JSON.stringify({
        algorithm,
        challenge: challengeStr,
        salt,
        answer,
        signature,
        target_path: "/api/v0/chat/completion",
      })
    );
  }

  function messagesToPrompt(messages) {
    const processed = messages.map((message) => {
      let text;
      if (message.role === "assistant" && message.tool_calls?.length) {
        text = message.tool_calls
          .map(
            (tc) =>
              "<tool_calling>\n<name>" +
              tc.function.name +
              "</name>\n<arguments>" +
              tc.function.arguments +
              "</arguments>\n</tool_calling>"
          )
          .join("\n");
      } else if (message.role === "tool" && message.tool_call_id) {
        text =
          '<tool_response tool_call_id="' +
          message.tool_call_id +
          '">\n' +
          (message.content || "") +
          "\n</tool_response>";
      } else if (Array.isArray(message.content)) {
        text = message.content
          .filter((i) => i.type === "text")
          .map((i) => i.text)
          .join("\n");
      } else {
        text = String(message.content || "");
      }
      return { role: message.role, text };
    });

    if (!processed.length) return "";

    const merged = [];
    let current = { ...processed[0] };
    for (let i = 1; i < processed.length; i++) {
      const msg = processed[i];
      if (msg.role === current.role) {
        current.text += "\n\n" + msg.text;
      } else {
        merged.push(current);
        current = { ...msg };
      }
    }
    merged.push(current);

    return merged
      .map((block, index) => {
        if (block.role === "assistant") {
          return "<｜Assistant｜>" + block.text + "<｜end of sentence｜>";
        }
        if (block.role === "user" || block.role === "system" || block.role === "tool") {
          return index > 0 ? "<｜User｜>" + block.text : block.text;
        }
        return block.text;
      })
      .join("")
      .replace(/!\[.+\]\(.+\)/g, "");
  }

  function resolveOptions(model, prompt, opts) {
    const m = (model || "deepseek-chat").toLowerCase();
    const searchEnabled =
      opts?.search === true || (opts?.search !== false && (Boolean(opts?.search) || m.includes("search")));
    const thinkingEnabled =
      opts?.thinking === true ||
      (opts?.thinking !== false &&
        (Boolean(opts?.thinking) || m.includes("r1") || m.includes("think") || m.includes("reasoner")));
    let modelType = "default";
    if (opts?.modelType === "expert" || opts?.expert) modelType = "expert";
    else if (m.includes("pro") || m.includes("expert") || m.includes("reasoner")) modelType = "expert";
    return { modelType, searchEnabled, thinkingEnabled };
  }

  async function waitFileParsed(accessToken, fileId, timeoutMs) {
    const deadline = Date.now() + (timeoutMs || 120000);
    while (Date.now() < deadline) {
      const r = await fetch(WEB_API + "/v0/file/" + encodeURIComponent(fileId) + "/status", {
        headers: { Authorization: "Bearer " + accessToken, ...HEADERS },
      });
      if (r.ok) {
        const j = await r.json();
        const bd = biz(j);
        const st = String(bd.status || bd.parse_status || bd.state || "").toLowerCase();
        if (st.includes("success") || st.includes("done") || st.includes("ready") || st === "parsed")
          return fileId;
        if (st.includes("fail") || st.includes("error")) throw new Error("文件解析失败");
      }
      await new Promise((res) => setTimeout(res, 600));
    }
    return fileId;
  }

  async function uploadUserFile(file) {
    const accessToken = await acquireToken();
    const form = new FormData();
    form.append("file", file, file.name || "upload.txt");
    const r = await fetch(WEB_API + "/v0/file/upload_file", {
      method: "POST",
      headers: { Authorization: "Bearer " + accessToken, ...HEADERS },
      body: form,
    });
    if (!r.ok) {
      const t = await r.text();
      throw new Error("文件上传失败 " + r.status + ": " + t.slice(0, 200));
    }
    const j = await r.json();
    const bd = biz(j);
    const fileId = bd.file_id || bd.id || bd.file?.id || bd.file_id_str;
    if (!fileId) throw new Error("上传响应无 file_id");
    try {
      await waitFileParsed(accessToken, String(fileId));
    } catch (_) {
      /* 部分版本无 status 接口，仍尝试使用 fileId */
    }
    return String(fileId);
  }

  function cleanStreamPiece(text) {
    if (!text) return "";
    let c = String(text).replace(/FINISHED/g, "");
    return c.replace(/^(SEARCH|WEB_SEARCH|SEARCHING)\s*/i, "");
  }

  function postBridgeStream(streamId, payload) {
    if (!streamId) return;
    try {
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(
          JSON.stringify({ channel: "bridge_stream", streamId, ...payload })
        );
      }
    } catch (_) {}
  }

  function applyStreamParsed(parsed, state, onDelta) {
    if (parsed.v && typeof parsed.v === "object" && parsed.v.response) {
      state.currentPath = parsed.v.response.thinking_enabled ? "thinking" : "content";
      const fragments = parsed.v.response.fragments;
      if (Array.isArray(fragments)) {
        for (const f of fragments) {
          if (!f.content) continue;
          const c = cleanStreamPiece(f.content);
          if (!c) continue;
          if (f.type === "THINK") {
            state.accumulatedThinking += c;
            if (onDelta) onDelta("reasoning", c);
          } else if (f.type === "ANSWER" || f.type === "RESPONSE") {
            state.accumulatedContent += c;
            if (onDelta) onDelta("content", c);
          }
        }
      }
    } else if (parsed.p === "response/fragments" && Array.isArray(parsed.v)) {
      for (const f of parsed.v) {
        if (!f.content) continue;
        const c = cleanStreamPiece(f.content);
        if (!c) continue;
        if (f.type === "THINK") {
          state.currentPath = "thinking";
          state.accumulatedThinking += c;
          if (onDelta) onDelta("reasoning", c);
        } else if (f.type === "ANSWER" || f.type === "RESPONSE") {
          state.currentPath = "content";
          state.accumulatedContent += c;
          if (onDelta) onDelta("content", c);
        }
      }
    } else if (typeof parsed.v === "string") {
      const c = cleanStreamPiece(parsed.v);
      if (!c) return;
      const isThinking =
        (parsed.p && String(parsed.p).includes("thinking")) || state.currentPath === "thinking";
      if (isThinking && parsed.p && String(parsed.p).includes("thinking")) {
        state.currentPath = "thinking";
        state.accumulatedThinking += c;
        if (onDelta) onDelta("reasoning", c);
      } else if (parsed.p === "response/content" || (!isThinking && parsed.p !== "response/thinking")) {
        state.currentPath = "content";
        state.accumulatedContent += c;
        if (onDelta) onDelta("content", c);
      } else if (state.currentPath === "thinking") {
        state.accumulatedThinking += c;
        if (onDelta) onDelta("reasoning", c);
      } else {
        state.currentPath = "content";
        state.accumulatedContent += c;
        if (onDelta) onDelta("content", c);
      }
    }
  }

  function parseStreamText(reader, onDelta) {
    return new Promise((resolve, reject) => {
      const dec = new TextDecoder();
      let buf = "";
      const state = {
        accumulatedContent: "",
        accumulatedThinking: "",
        currentPath: "",
      };

      function pump() {
        reader
          .read()
          .then(({ done, value }) => {
            if (done) {
              resolve({
                content: state.accumulatedContent,
                thinking: state.accumulatedThinking,
              });
              return;
            }
            buf += dec.decode(value, { stream: true });
            const lines = buf.split("\n");
            buf = lines.pop() || "";

            for (const line of lines) {
              if (!line.trim() || !line.startsWith("data:")) continue;
              const data = line.slice(5).trim();
              if (!data || data === "[DONE]") continue;
              try {
                applyStreamParsed(JSON.parse(data), state, onDelta);
              } catch (_) {}
            }
            pump();
          })
          .catch(reject);
      }
      pump();
    });
  }

  function detectLikelyTruncated(content) {
    if (!content || typeof content !== "string") return false;
    const t = content.trimEnd();
    if (t.length < 800) return false;
    if (/Final Answer:/i.test(t)) return false;
    if (t.endsWith("```") || t.endsWith("</tool_calling>")) return false;
    if (/Action Input:$|Action:$|Thought:$/i.test(t.slice(-40))) return true;
    const last = t[t.length - 1];
    if (".。!?)]}\"".includes(last)) return false;
    return t.length > 1500;
  }

  function parseToolCalls(text) {
    const toolCalls = [];
    const re = /<tool_calling>\s*<name>([^<]+)<\/name>\s*<arguments>([\s\S]*?)<\/arguments>\s*<\/tool_calling>/gi;
    let m;
    let clean = text || "";
    while ((m = re.exec(text || "")) !== null) {
      toolCalls.push({
        id: "call_" + Math.random().toString(36).slice(2, 10),
        type: "function",
        function: { name: m[1].trim(), arguments: m[2].trim() },
      });
      clean = clean.replace(m[0], "");
    }
    return { content: clean.trim(), toolCalls };
  }

  async function fetchWebCompletion(messages, model, opts) {
    const accessToken = await acquireToken();
    const sessionId =
      opts && opts.chatSessionId
        ? String(opts.chatSessionId)
        : await createSession(accessToken);
    const challenge = await getChallenge(accessToken);
    const pow = await calculateChallengeAnswer(challenge);
    const prompt = messagesToPrompt(messages);
    const { modelType, searchEnabled, thinkingEnabled } = resolveOptions(model, prompt, opts);
    const refFileIds = Array.isArray(opts?.refFileIds)
      ? opts.refFileIds.filter(Boolean).map(String)
      : [];

    const r = await fetch(WEB_API + "/v0/chat/completion", {
      method: "POST",
      headers: {
        Authorization: "Bearer " + accessToken,
        "Content-Type": "application/json",
        "X-Ds-Pow-Response": pow,
        ...HEADERS,
      },
      body: JSON.stringify({
        chat_session_id: sessionId,
        parent_message_id: null,
        prompt,
        model_type: modelType,
        ref_file_ids: refFileIds,
        search_enabled: searchEnabled,
        thinking_enabled: thinkingEnabled,
        preempt: false,
      }),
    });

    if (!r.ok) {
      const t = await r.text();
      throw new Error("网页 API 失败 " + r.status + ": " + t.slice(0, 300));
    }

    return { response: r, sessionId, model: model || "deepseek-chat" };
  }

  function buildChatResult(content, thinking, sessionId, model, opts) {
    const suppressTools = !!(opts && opts.suppressToolCalls);
    let mainContent = (content || "").trim();
    if (!mainContent && suppressTools && thinking) {
      mainContent = String(thinking).trim();
    }
    const parsed = suppressTools
      ? { content: mainContent, toolCalls: [] }
      : parseToolCalls(content);
    const textOut = parsed.toolCalls.length ? null : parsed.content || "(无回复)";
    const likelyTruncated = detectLikelyTruncated(textOut);
    return {
      content: textOut,
      reasoning_content: thinking || undefined,
      tool_calls: parsed.toolCalls.length ? parsed.toolCalls : undefined,
      model: model || "deepseek-chat",
      finish_reason: likelyTruncated ? "length" : parsed.toolCalls.length ? "tool_calls" : "stop",
      is_likely_truncated: likelyTruncated,
      chat_session_id: sessionId,
    };
  }

  async function webChatCompletion(messages, model, opts) {
    const { response, sessionId, model: m } = await fetchWebCompletion(messages, model, opts);
    const { content, thinking } = await parseStreamText(response.body.getReader());
    return buildChatResult(content, thinking, sessionId, m, opts);
  }

  async function webChatCompletionStreaming(streamId, messages, model, opts) {
    postBridgeStream(streamId, { type: "delta", kind: "status", text: "connecting" });
    const { response, sessionId, model: m } = await fetchWebCompletion(messages, model, opts);
    const onDelta = (kind, text) => {
      postBridgeStream(streamId, { type: "delta", kind, text });
    };
    const { content, thinking } = await parseStreamText(response.body.getReader(), onDelta);
    const result = buildChatResult(content, thinking, sessionId, m, opts);
    postBridgeStream(streamId, { type: "done", result });
    return result;
  }

  window.dsDesktopBridge = {
    getToken: getUserToken,
    ping: () => ({ ok: true, token: !!getUserToken() }),
    uploadUserFile,
    webChatCompletion: async function (...args) {
      try {
        return await webChatCompletion(...args);
      } catch (e) {
        const msg =
          (e && (e.message || e.stack) && String(e.message || e.stack)) ||
          (typeof e === "string" ? e : JSON.stringify(e));
        throw new Error(msg || "网页 Chat API 调用失败");
      }
    },
    webChatCompletionStreaming: async function (streamId, ...args) {
      try {
        return await webChatCompletionStreaming(streamId, ...args);
      } catch (e) {
        const msg =
          (e && (e.message || e.stack) && String(e.message || e.stack)) ||
          (typeof e === "string" ? e : JSON.stringify(e));
        postBridgeStream(streamId, { type: "error", message: msg || "网页 Chat API 调用失败" });
        throw new Error(msg || "网页 Chat API 调用失败");
      }
    },
  };
})();
