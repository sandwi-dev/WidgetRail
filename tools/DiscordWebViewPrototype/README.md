# Discord WebView2 feasibility prototype

This is an isolated, validation-enabled host. It does not replace the installed
Discord widget, the working Vesktop prototype, or the production overlay.
The current UI is a native test window with an embedded Discord web client,
native voice controls, an application/audio chooser, and a floating incoming
stream receiver. Controller-shell integration is a later gate, not completed here.

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

The build's `native/WindowCaptureFixture.exe` generates only colored graphics.
`--audio` adds a quiet synthetic tone; `--static` disables its moving marker.
Run that fixture, then:

```powershell
./tools/DiscordWebViewPrototype/Start-Prototype.ps1 -Mode Capture -SelfTest -Audio
```

Automatic selection is restricted to the named repository fixture. This mode
sends media through a local WebRTC connection; it does not contact Discord.
Check `assets/capture-result.json` for native result, stop reason, bounded audio
queue, submitted frames, dropped video frames, and decoded receive-side counts.
Maximize, restore, minimize, restore and close the synthetic window to exercise
capture lifecycle. No microphone permission is needed for the generated tone.

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
