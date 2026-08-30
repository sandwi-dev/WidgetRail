(function (global) {
  "use strict";

  const authorityFields = Object.freeze([
    "environmentGeneration",
    "surfaceGeneration",
    "sessionGeneration",
    "controllerGeneration",
    "documentGeneration",
  ]);
  const playbackStates = new Set([
    "ready",
    "loading",
    "playing",
    "paused",
    "ended",
    "error",
  ]);
  const commandHooks = Object.freeze({
    load: "load",
    cue: "cue",
    "arm-activate": "activate",
    pause: "pause",
    toggle: "toggle",
    seek: "seek",
    "seek-back": "seekBackward",
    "seek-forward": "seekForward",
    volume: "setVolume",
    "playback-rate": "setPlaybackRate",
    muted: "setMuted",
    loop: "setLoop",
    previous: "previous",
    next: "next",
    back: "back",
  });

  class AdapterError extends Error {
    constructor(code) {
      const boundedCode = fixedCode(code);
      super(boundedCode);
      this.name = "AdapterError";
      this.adapterCode = boundedCode;
    }
  }

  function fixedCode(value) {
    return String(value || "adapter-error")
      .toLowerCase()
      .replace(/[^a-z0-9-]/g, "")
      .slice(0, 48) || "adapter-error";
  }

  function finiteNumber(value, fallback) {
    return Number.isFinite(value) ? value : fallback;
  }

  function clamp(value, minimum, maximum) {
    return Math.max(minimum, Math.min(maximum, value));
  }

  function create(options) {
    if (!options || typeof options !== "object") {
      throw new TypeError("Embedded-media adapter options are required.");
    }
    if (typeof options.snapshot !== "function") {
      throw new TypeError("Embedded-media adapters require a snapshot function.");
    }
    if (!options.driver || typeof options.driver !== "object") {
      throw new TypeError("Embedded-media adapters require a player driver.");
    }

    const focus = String(options.focus || "media-plane").slice(0, 128);
    const readBounds = typeof options.bounds === "function"
      ? options.bounds
      : () => ({ x: 0, y: 0, width: 0, height: 0 });
    let authority = null;
    let eventSequence = 0;
    let inFlight = null;
    const abortControllers = new WeakMap();

    function readSnapshot() {
      const source = options.snapshot() || {};
      const playbackState = playbackStates.has(source.playbackState)
        ? source.playbackState
        : "ready";
      const durationSeconds = Math.max(0, finiteNumber(source.durationSeconds, 0));
      const value = {
        mediaKey: String(source.mediaKey || "").slice(0, 256),
        playbackState,
        positionSeconds: clamp(
          finiteNumber(source.positionSeconds, 0),
          0,
          durationSeconds > 0 ? durationSeconds : Number.MAX_SAFE_INTEGER),
        durationSeconds,
        volume: clamp(finiteNumber(source.volume, 0.8), 0, 1),
      };
      if (Number.isFinite(source.playbackRate)) {
        value.playbackRate = clamp(source.playbackRate, 0.25, 4);
      }
      if (typeof source.muted === "boolean") value.muted = source.muted;
      if (typeof source.loop === "boolean") value.loop = source.loop;
      return value;
    }

    function envelope(type, operation, errorCode, result) {
      const observed = readSnapshot();
      const playbackState = errorCode
        ? "error"
        : playbackStates.has(result?.playbackState)
          ? result.playbackState
          : observed.playbackState;
      const mediaKey = String(
        result?.mediaKey ?? operation?.mediaKey ?? observed.mediaKey).slice(0, 256);
      const bounds = readBounds() || {};
      const value = {
        type,
        ...authority,
        eventSequence: ++eventSequence,
        commandId: operation?.id ?? 0,
        commandSequence: operation?.sequence ?? 0,
        focus,
        playing: playbackState === "playing",
        bounds: {
          x: finiteNumber(bounds.x, 0),
          y: finiteNumber(bounds.y, 0),
          width: Math.max(0, finiteNumber(bounds.width, 0)),
          height: Math.max(0, finiteNumber(bounds.height, 0)),
        },
      };
      if (mediaKey.length > 0) {
        value.mediaKey = mediaKey;
        value.playbackState = playbackState;
        value.positionSeconds = observed.positionSeconds;
        value.durationSeconds = observed.durationSeconds;
        value.volume = observed.volume;
        if (observed.playbackRate !== undefined) value.playbackRate = observed.playbackRate;
        if (observed.muted !== undefined) value.muted = observed.muted;
        if (observed.loop !== undefined) value.loop = observed.loop;
      }
      if (errorCode) value.errorCode = fixedCode(errorCode);
      return value;
    }

    function emit(type, operation = null, errorCode = null, result = null) {
      if (!authority) return;
      global.chrome.webview.postMessage(
        envelope(type, operation, errorCode, result));
    }

    function complete(operation, result, errorCode = null) {
      if (inFlight !== operation) return;
      emit(result?.type || "media", operation, errorCode, result);
      inFlight = null;
    }

    function resolveError(error, operation) {
      if (error?.adapterCode) return fixedCode(error.adapterCode);
      if (typeof options.errorCode === "function") {
        return fixedCode(options.errorCode(error, operation));
      }
      return fixedCode(error?.name || error?.message);
    }

    function matchesAuthority(message) {
      return authority && authorityFields.every(
        field => message[field] === authority[field]);
    }

    async function dispatch(message) {
      if (!message || typeof message !== "object") return;
      if (message.command === "initialize") {
        abortControllers.get(inFlight)?.abort();
        authority = Object.fromEntries(
          authorityFields.map(field => [field, message[field]]));
        eventSequence = 0;
        inFlight = null;
        options.driver.initialize?.();
        const initial = {
          id: message.commandId,
          sequence: 0,
          mediaKey: readSnapshot().mediaKey,
        };
        emit("ready", initial);
        return;
      }
      if (!matchesAuthority(message) || inFlight) return;

      const hookName = commandHooks[message.command];
      const abortController = new AbortController();
      const operation = {
        id: message.commandId,
        sequence: message.commandSequence || 0,
        mediaKey: String(message.mediaKey || readSnapshot().mediaKey).slice(0, 256),
        command: message.command,
        positionSeconds: finiteNumber(message.positionSeconds, 0),
        volume: clamp(finiteNumber(message.volume, 0), 0, 1),
        playbackRate: finiteNumber(message.playbackRate, 1),
        muted: message.muted,
        loop: message.loop,
        signal: abortController.signal,
      };
      abortControllers.set(operation, abortController);
      inFlight = operation;

      if (!hookName || typeof options.driver[hookName] !== "function") {
        complete(operation, null, "command-unsupported");
        return;
      }
      if (message.command !== "load" && message.command !== "cue" &&
          operation.mediaKey !== readSnapshot().mediaKey) {
        complete(operation, null, "media-key-mismatch");
        return;
      }

      try {
        if (message.command === "arm-activate") {
          const activation = await options.driver[hookName](operation);
          if (inFlight !== operation) return;
          if (!activation || typeof activation.completion?.then !== "function") {
            throw new AdapterError("activation-contract-invalid");
          }
          emit("armed", operation);
          complete(operation, await activation.completion);
          return;
        }
        complete(operation, await options.driver[hookName](operation));
      } catch (error) {
        complete(operation, null, resolveError(error, operation));
      }
    }

    const api = Object.freeze({
      observe() {
        if (!authority || inFlight) return false;
        emit("media");
        return true;
      },
      observeError(code) {
        if (!authority || inFlight) return false;
        emit("media", null, fixedCode(code));
        return true;
      },
      error(code) {
        return new AdapterError(code);
      },
    });

    global.chrome.webview.addEventListener(
      "message", event => void dispatch(event.data));
    return api;
  }

  global.WidgetRailEmbeddedMediaAdapter = Object.freeze({ create, error: code => new AdapterError(code) });
})(globalThis);
