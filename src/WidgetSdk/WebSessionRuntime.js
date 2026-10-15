/* WidgetRail packaged-page messaging v1. No host objects or browser-data access. */
(() => {
  'use strict';
  const handlers = new Map();
  const active = new Map();
  const encoder = new TextEncoder();
  const send = value => {
    const json = JSON.stringify(value);
    if (encoder.encode(json).length > 16384) throw new Error('Web session message exceeds its bound.');
    chrome.webview.postMessage(value);
  };
  const api = Object.freeze({
    handle(method, handler) {
      if (typeof method !== 'string' || !/^[A-Za-z0-9._-]{1,64}$/.test(method) || typeof handler !== 'function')
        throw new TypeError('A bounded method name and handler are required.');
      if (handlers.has(method)) throw new Error('A handler already exists for this method.');
      handlers.set(method, handler);
      return () => { if (handlers.get(method) === handler) handlers.delete(method); };
    },
    post(value) { send(value); }
  });
  Object.defineProperty(window, 'WidgetRailWebSession', { value: api, writable: false, configurable: false });
  chrome.webview.addEventListener('message', async ({ data: request }) => {
    if (!request || request.widgetRail !== 1 ||
        typeof request.id !== 'string' || !/^[a-f0-9]{32}$/.test(request.id)) return;
    if (request.kind === 'cancel') { active.get(request.id)?.abort(); return; }
    if (request.kind !== 'request') return;
    const respond = payload => send({ widgetRail: 1, kind: 'response', id: request.id, payload: payload ?? null });
    const fail = () => send({ widgetRail: 1, kind: 'response', id: request.id, error: 'page_request_failed' });
    const handler = handlers.get(request.method);
    if (!handler || active.has(request.id) || active.size >= 16) { fail(); return; }
    const controller = new AbortController();
    active.set(request.id, controller);
    let timer;
    try {
      const timeout = new Promise((_, reject) => {
        controller.signal.addEventListener('abort', () => reject(new Error('Request canceled.')), { once: true });
        timer = setTimeout(() => { controller.abort(); }, 30000);
      });
      respond(await Promise.race([Promise.resolve().then(() => handler(request.payload, { signal: controller.signal })), timeout]));
    } catch { if (!controller.signal.aborted) fail(); }
    finally { clearTimeout(timer); active.delete(request.id); }
  });
})();
