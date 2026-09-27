# WinUI migration implementation status

2026-09-27; branch `codex/winui3-frontend`. Main and the installed native candidate
are untouched. Migration is active and incomplete.

## Current implementation

- Microsoft WinApp CLI 0.7.0 installed with user authorization. Developer Mode
  enabled with separate explicit UAC approval. Existing .NET SDK reused.
- Official WinUI MVVM template created `src/OverlayFrontend.WinUI`; resolved stable
  dependencies pinned (Windows App SDK 2.5.1). Counter sample removed; this is a
  frontend foundation, not a widget port. Development package registration is
  separate from the existing installed product.
- XAML shell builds with Microsoft WinUI analyzer enabled: zero warnings/errors.
- OS backdrop compositor requires a Windows dispatcher queue. The initial startup
  crash is diagnosed in `artifacts/winui-shell/debug-launch.log`. Fixed using the
  framework's `DispatcherQueue.EnsureSystemDispatcherQueue`, avoiding custom P/Invoke
  or a competing message loop. Frontend then stayed live and exposed Shell.Status
  and Shell.Close through UIA; invoking Close exited normally.
- `WidgetUi.State` selects background/presentation sources from immutable logical
  membership, independent of realization. Its 15 tests pass. This policy component
  does not implement layout, spatial navigation, painting, or animation.
- Feature inventory: [contracts](winui-feature-contracts.md).
- Replaced custom backdrop code with pinned WinUIEx 2.9.3. Basic transparency
  now passes a controlled red/green desktop-background test at three margin
  coordinates. WinUIEx owns the native DWM/message integration rather than a
  copied implementation in our frontend. License retained and packaged.
- The shared script `scripts/Test-WinUiTransparency.ps1` records pixel results and
  screenshots. A real pointer click reached Close after the test. Test PID 22148
  exited; no validation frontend is intentionally left running.
- [Native binding inventory](winui-platform-binding.md) identifies existing ABI
  reuse and the separate View+Menu observer that must also be exported/preserved.

## Failed / unproven gates

The original top-level Window plus transparent SystemBackdrop brush did NOT show the
desktop through the empty margins. Both window and desktop-composited screenshots
show opaque margins. Extending content into the titlebar did not correct this.
An external diagnostic DwmExtendFrameIntoClientArea(-1) call returned success but
made the window white; it was not added to production. A subsequent external style
probe did not establish transparency either. Test window PID 11060 was closed
through Shell.Close afterward. This attempt was replaced, not hidden behind a
passing build. WinUIEx then passed the basic alpha proof described above.

The first winapp launch with --output-appx-directory failed registration (missing
appxmanifest.xml); default project-mode deployment succeeded. Revisit a supported
short staging path if package size/path limits require it. No manifest was deleted,
no unpackaged fallback was introduced to suppress the failure.

Raw debug-process sample was roughly 98 MB private / 142 MB working set; this is an
empty-shell observation, NOT a full-product memory comparison or acceptance result.

No controller routing, real widget transport, lazy collection port, native media,
pinned projection, transparent-area input policy, or frame-latency gate has passed.
Do not claim migration readiness based on shell build or source-selection tests.

## Next actions

1. Extend basic desktop-alpha proof to input regions, native media, pinning,
   monitor/DPI changes and recovery. The shell still has a visible DWM outline;
   finish intended chrome policy through supported window APIs. No undocumented
   root surgery or unstable CompositionEngine opt-in as a workaround.
2. Connect existing platform ABI input/activation rather than rewrite it. Prefer
   framework APIs whenever they remove custom code.
3. Connect immutable source selection to real page/item metadata and shared WinUI
   presentation slots; retain cursor membership independently from realized controls.
4. Deliver the real sandboxed Playnite/collection slice, followed by remaining
   migration features and the pre-WIDGE-293 comparison from the plan.

Evidence: ignored `artifacts/winui-shell/` (build binlogs, UIA/launch output, screenshots,
source-reference excerpt) and `artifacts/winui-state/` (focused tests/binlog).
