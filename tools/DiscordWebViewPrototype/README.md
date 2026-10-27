# Discord WebView2 feasibility prototype

This directory contains capture/adapter tooling and the original standalone
validation host. The current integrated widget is in
`samples/DiscordWebViewWidget`, with controller controls, voice/stream pins and
host-owned capture consent. It remains isolated from the production overlay.
See `docs/discord-webview-prototype-plan.md` for the current candidate and evidence;
the standalone build instructions below are for diagnostic fixtures.

The current isolated candidate uses widget0.0.14 / adapter20,
`artifacts/discord-webview/production-candidate-8`, and
`artifacts/discord-webview/improvements-runtime-1/Launch-Candidate.ps1`.
It adds participant audio, voice-device settings, favorite/recent channels,
connection status and remembered Smoothness/Clarity. The exact-source advertised
quality correction is verified against the live native receiver's1440p60 label;
actual encoded dimensions match the smaller selected source window.
The measured60FPS-source capture now delivers about59FPS locally, while the
encoder still adapts1440p to720p; see `QUALITY.md` for this remaining limitation.

## Build and launch

Use a native host payload built from a compatible checkout. The current experiment
uses the installed preview.40 native DLLs; neither production DLL has changed.

```powershell
./tools/DiscordWebViewPrototype/Build-Prototype.ps1 -NativeHostDirectory '<absolute host payload directory>'
./tools/DiscordWebViewPrototype/Start-Prototype.ps1
```

Build outputs are fresh directories. The launcher reads `latest-candidate.txt`.
A separate WebView2 profile is stored under ignored
`artifacts/discord-webview/assets/discord-profile`. Sign in yourself. Enable the
microphone explicitly before joining; the grant is limited to this test session.
The source chooser does not preselect an application. Select the window and
application-audio option yourself before Start sharing.

Close the native prototype to stop its media. The prototype does not install,
patch or launch Vesktop. The earlier Vesktop prototype remains in
`samples/DiscordNativePrototype`.

## Local transport fixture

The unattended runners launch their own generated source and bind capture to its
exact process ID and creation time. They never search for an existing window by
title. For a short production-worker check with a quiet generated tone:

```powershell
./tools/DiscordWebViewPrototype/Test-CaptureWorker.ps1 -CandidateDirectory <candidate-directory> -NativeDirectory <native-directory> -ApplicationAudio
```

The default source is the native GDI fixture. Add `-SourceKind Browser` to use a
separate WinUI/WebView2 source rendering an animated canvas and optional WebAudio
tone. Its fresh profile blocks network and device permission requests; only the
generated source's autoplay is allowed. This tests a
Chromium-rendered source, not an existing Chrome window, account, or Discord call.
The runner records exact source identity and local WebRTC video/audio counts.
No microphone permission is needed.

For a longer browser-source run:

```powershell
./tools/DiscordWebViewPrototype/Measure-WebCapture.ps1 -CandidateDirectory <candidate-directory> -NativeDirectory <native-directory> -SourceKind Browser -ApplicationAudio -Seconds 100
```

`source/source-status.json` records actual canvas draws, page visibility and audio
clock progress, helping distinguish source rendering pauses from capture failures.
`Start-Prototype.ps1 -Mode Capture` remains available for manual source selection;
its old title-based `-SelfTest` route now directs callers to the owned-source runners.

```powershell
node --test tools/DiscordWebViewPrototype/*.test.mjs samples/DiscordNativePrototype/adapter.test.mjs
```

## Boundaries and remaining work

### Background-session contract fixture

Build a validation-enabled candidate with `Build-Prototype.ps1`, then run:

```powershell
./tools/DiscordWebViewPrototype/Test-BackendSession.ps1 -CandidateDirectory <candidate-directory>
```

This uses the real host browser and offscreen session backing with synthetic
documents. Each run gets a fresh profile and an atomic `result.json` under
`artifacts/discord-webview/backend-runs/`. It checks startup and cancellation,
main-world versus isolated-world behavior, origin boundaries, navigation and
teardown. It does not open Discord or touch the signed-in prototype profile.

### Media prototype boundaries

- WebView2's capture event cannot accept a selected native window. This experiment
  instead supplies generated tracks backed by Windows Graphics Capture and process
  loopback audio. It blocks the browser's separate screen chooser.
- Raw frames use bounded shared buffers, not JSON/base64, screenshots, or recordings.
  The prototype still performs GPU readback and CPU copies; it is not zero-copy.
- The source PID, creation time, HWND and class are validated. Protected capture
  sources are rejected. A stale source never switches to another window.
- Application audio means the selected process tree, potentially including other
  windows belonging to that application. It never falls back to system audio.
- Discord internals are unsupported and feature-probed. Missing/changed APIs fail
  closed. Credentials stay in the browser profile.
- This is not a finished production SDK contract. See the plan for live evidence,
  unresolved limitations and the framework ownership assessment.
