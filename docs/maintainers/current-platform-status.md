# Current platform status

Updated 2026-10-05 for application `0.1.0-preview.26` (SDK/CLI `0.4.1-dev`,
presentation protocol 76). The user accepted the SVG recovery, optional F1
shortcut and Browser/Game Help icons for main integration and public release.
This page describes the implemented product. Migration reports preserve the
decisions and evidence from earlier stages; their pending work and candidate
process states are not current release status.

## Frontend and distribution

- `OverlayFrontend.WinUI` is the production frontend. XAML owns ordinary controls,
  layout, virtualization, focus and accessibility; Microsoft.UI.Composition owns
  presentation motion. The original renderer and Rust/Taffy build graph are retired.
- `OverlayPlatformInterop` and `WinUiWindowPreviewNative` retain native Windows,
  controller and GPU capture work. Browser and embedded-media surfaces retain
  host-owned WebView2 instances; ordinary widgets publish declarative documents.
- Distribution uses unsigned per-user Inno installers with an unpackaged,
  self-contained WinUI frontend and private service runtimes. No application MSIX,
  identity-package registration or certificate trust is required. Bridge and
  workers launch from the installed payload; full-MSIX startup-copy caches are retired.
- Production contains Settings plus nine widgets, including Browser and Game Help.
  Developer contains Settings plus thirteen widgets, including SDK Gallery and
  samples. `eng/release-content.json` selects edition membership.
- Both preview.25 installers were built from the accepted commit and their
  inventories/checksums verified. This does not claim clean-machine installation,
  upgrade, rollback or every system-provider workflow has been physically qualified.

## Implemented authoring and behavior

F1 keyboard opening is optional and off by default, including profiles that do
not contain the new setting. Settings > Controllers applies it immediately;
disabling it releases the global registration. SVG icon loading coalesces requests,
retries transient failures in bounded batches and preserves ready artwork across
compatible label updates. Settings is 0.1.9, Browser 0.1.4 and Game Help 0.1.1.
Validation passed 54 native SVG checks, nine native F1 checks, 14 icon-session
tests, 83 Settings tests and 37 catalog checks, plus trimmed Release publication.
The first F1 fixture attempt lacked its required activation flag and timed out
before its assertions; the corrected fixture invocation passed.

Use the [WinUI authoring contract](../reference/winui-authoring.md) and
[indexed/discovered collections](../developers/indexed-collections.md). The legacy
eager cursor presentation metadata is not a WinUI virtualization path.
Shared elements include native media playback, browser/provider documents, rich
text with inline links, capture preview and scroll reveal. Full-trust and sandboxed
widgets can use permission-gated shared host services. Game Help owns its Gemini
client, credentials and provider-specific response handling in its own application.

[Intents](../reference/widget-intents.md) support per-handler availability,
delivery feedback, Windows routing and passive pinned-destination preference.
B follows normal widget/tray navigation; there is no return-to-sender stack.
Capture targets the foreground application after the overlay hides, without
selecting or foregrounding another application. Video has a five-second preparation
countdown followed by five seconds of silent capture. The user reopens the overlay
to review, edit the question, send, retry or discard.

Settle is the default focus animation. Directional navigation can scroll remaining
content after reaching the last focusable control. The old `HoldDpadToScroll`
preference and gated Settings action still exist, but WinUI does not advertise or
consume that feature. It is distinct from current navigation scrolling.
The startup backend targets the WinUI executable and Inno offers startup at sign-in;
the current presentation-session handshake does not advertise the Settings startup
control. Use installer startup selection and Windows Startup Apps for that workflow.

Pins are session-only: placement preferences survive restart, pinned selection does
not. Replace pin, Unpin, Move/resize and Change opacity precede Interact in the tray
menu. Local widget installation uses a Windows file picker and themed overlay
dialogs, followed by automatic catalog refresh. Clear local data removes only
overlay-owned private state. Built-in widget capabilities are granted by default
while preserving explicit user denials.

## Validation and remaining boundaries

The accepted feature closeout and preview.25 edition correction are recorded in
the [Game Help progress record](game-help-implementation-plan.md). Focused managed
suites, native presentation checks, trimmed Release publication and installer
inventory checks passed at that checkpoint. No installer was run as part of the
preview.25 delivery and no remote push was performed.

Physical system-provider, controller/backend, mixed-monitor/DPI and clean-machine
installer coverage must be reported for its tested scope. Memory/performance
investigations remain deferred; old native-renderer benchmarks are not WinUI
performance claims. See [release preparation](release-checklist.md),
[verification/building](building.md), and [known limitations](../users/known-limitations.md).

## Documentation ownership

User and developer guides and reference pages describe current behavior. Maintainer
implementation records keep dated observations without rewriting failed experiments
as successes. Consult current source and the linked reference for live contracts;
do not run retired scripts or restore obsolete MSIX/renderer workflows from a log.
Documentation tests check local links and selected source/contract assertions, not
every prose claim or UI behavior. Update the owning reference when changing code.
