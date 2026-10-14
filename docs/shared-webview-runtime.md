# Shared WebView runtime and embedded-media memory policy

The browser owner and embedded-media owner now share a shell-owned WebView2
environment. The environment keeps the existing settings-root `WebBrowser`
directory. Existing browser/webapp profile names, cookies, permissions and saved
sign-ins remain in place. Embedded media keeps its separate per-widget `media-`
profile names and InPrivate controllers. Its former standalone environment is no
longer opened; no user-data directory is moved, copied or deleted.

Sharing removes the second browser-process group when both types of surface are
in use. Renderers remain WebView2-managed and may stay separate. Profiles isolate
website data; they are not separate browser-process crash boundaries. A shared
browser-process exit affects both kinds of surface, which retain their existing
explicit recovery behavior. Recoverable GPU/utility exits remain runtime-managed.
An environment-creation failure clears only the matching failed attempt so a
later request can retry. Closing one controller never closes another controller.

Both surfaces use one per-controller memory policy: a 15-second delay before
requesting Low for a hidden document or passive pin, and synchronous restoration
of Normal for visible main/expanded presentation or pin interaction. Short
transfers cancel the pending timer. Temporary modal input suppression alone does
not make a visible main surface passive. The policy never suspends or navigates
the document and never inspects third-party DOM activity. Unsupported memory
hints disable only the optimization and report a bounded diagnostic.

Validation uses sealed synthetic media and a local browser fixture. No real
accounts, personal windows, website messages or calls are used. The standalone
fixture creates a fresh temporary environment and can terminate only its own
identified WebView browser process. `scripts/Test-WinUiSharedWebView.ps1` compares
separate and shared process layouts, checks same-origin storage and permission
isolation, hidden/passive media playback, immediate memory restoration and
recovery after a shared browser exit. Resource snapshots are illustrative,
not a guaranteed memory-saving percentage.

## Validation and candidate (2026-10-14)

- Debug build: zero warnings/errors (`artifacts/shared-webview/build-04.binlog`).
- Presentation-session tests: 333 passed, zero failed/skipped.
- Separate-environment baseline: 68 native checks; 11 processes, including two
  browser and two GPU processes; 396.6 MiB private bytes in this synthetic run.
- Shared environment: 82 native checks; seven processes, including one browser
  and one GPU process; 250.6 MiB private bytes in this synthetic run. Tests verify
  same-origin storage/cookie and permission isolation, private media lifetime,
  Low/Normal independence, advancing playback and shared-browser crash recovery.
- Existing webapp background test: 22 checks, including alternating Normal/Low
  WebRTC loopback trials with advancing video, delayed policy and renderer fault.
- Existing embedded-media regression: 131 checks, including sealed resources,
  trusted activation, parking, modal coverage, fullscreen, pins, capacity and
  controller retirement. Both regression fixture windows shut down cleanly.
- Complete self-contained publication succeeded, including frontend, native
  boundaries, bridge, workers, bundled widgets and CLI. Candidate assembly uses
  the standard edition-copy and inventory/catalog verification routines. All 15
  bundled identities passed catalog/package/icon admission; the complete file
  inventory and published async cleanup also passed verification.

Evidence is under `artifacts/shared-webview/`. Source review checked retry
ownership, profile namespaces, unchanged admission/permission handlers, timer
cleanup, input-before-trim restoration and process-failure routing.

The complete candidate is under
`artifacts/shared-webview/candidate-01/WidgetRail-Developer-0.1.0-preview.31-win-x64`.
It is marked as a candidate built from uncommitted source, not a release. The
previous running candidate and normal user profile were left untouched.

Physical checklist: open a signed-in webapp and YouTube player; play and pin
media, leave it passive for 15 seconds, hide/reopen the overlay, interact with the
pin, then return it to the widget/fullscreen. Check playback, input and sign-in
continuity. Physical acceptance is pending. No merge, push, release or installer
creation is part of this change.
