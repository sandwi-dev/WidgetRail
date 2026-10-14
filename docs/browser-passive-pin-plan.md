# Passive browser pin rendering

## Report and reproduction

2026-10-14: a browser/webapp pin opens a peer window, but its WebView pixels do not appear until View enters interaction. Closing/reopening the overlay does not restore them. Twitch itself was reported to work.

Further physical testing confirmed this is **Pin full widget**. Repeating pinning sometimes succeeds and sometimes fails. The border appears first, “Opening browser…” flashes briefly, and the placeholder disappears even on a failed attempt. This supports an intermittent presentation/handoff problem, but does not identify the failing native operation.

Source trace: `BrowserSlot.SetSurface(non-null)` collapses the placeholder after `BrowserOwner.MoveAsync` joins native loading/layout. It is an attachment acknowledgment, not a first-frame acknowledgment. Therefore the disappearing text does not establish that WebView pixels reached the destination window. The existing page-state observations likewise cannot prove visible output. Do not classify this as a website navigation failure or introduce a fixed delay on the strength of the placeholder alone.

Work is isolated on `codex/browser-passive-pin`, following the local Twitch widget commit `e3846d3f`. Main's unrelated goal edit and the normal user profile remain preserved. Only synthetic fixture pages are instrumented or captured.

The new `--webapp-passive-pin` native fixture gives the native page focus, then pins through the real tray command, rather than directly calling the pin method. `scripts/Validate-BrowserPassivePin.ps1` captures the generated peer HWND without activation and samples its known green page interior. It checks initial passive display, hidden overlay, reopened overlay, interaction, and return to passive display. `-Radial` and `-Popup` cover the alternate switcher and hosted child path. Page-state checks alone are insufficient: failing pages report visible, have nonzero layout and advancing animation frames, but their native region is transparent (ARGB 0).

## Evidence and decisions

- `baseline-menu.json.passive.png`: blank native page, while the widget's ordinary XAML buttons paint. Entering interaction paints the same page without navigation. This reproduces the reported symptom using no Twitch/account content.
- Waiting for native menu `Closed`, then also `Unloaded`, improved individual runs but failed repetition. Those experimental tray changes were removed; do not treat menu timing as a confirmed root cause.
- `original-experiment.json.rasters.json` and `isolated-probes-2.json.rasters.json`: initial passive raster has 0/24 green samples. Obtaining a composition visual restores 24/24 samples while remaining passive. However, doing this on every transfer, deferring it to a render pass, explicitly joining native load events, or changing focus did not reliably fix repetition. Those changes were removed.
- Disabling native-window occlusion passed weaker trials, then failed with native browser focus (`native-focus-no-occlusion.json`). No browser optimization policy change is retained.
- Re-publishing the child visual after visible layout passed six runs, but subsequent variants/repetitions failed again, including explicit native Loaded handling, per-attachment completion flags, and combined handoff-node/child-visual refresh. Native focus release also failed repetition. No production workaround is retained. Candidate patches and logs are preserved under `artifacts/browser-passive-pin`; do not present isolated passing runs as a reliable fix.
- Adding an opaque synthetic backdrop masks the original failure. This happened both with an extra WinUI peer and a separate WinForms source process. Four `screen-proof-*` and four `external-screen-*` runs passed without a production fix. Therefore a successful covered screen capture does not validate the original failure.
- The user subsequently authorized **only the pin rectangle**. `authorized-screen-02.json` compares native window capture with a nonactivating screen crop: both lack the page before interaction, and both show it afterward. This establishes a real presentation failure rather than a capture artifact. No account page was instrumented; the page remained the synthetic fixture.
- Enabling the WebView's own hit testing restored a failed passive surface in `split-web-hit-test-01` and `web-hit-proof-4`. Showing the focus indicator also restored a failure. Merely updating layout or raising the pin did not. Moving the input gate to the ancestor was then tested and failed (`parent-gate-2`), so that prototype was removed. These probes identify visual updates that wake rendering, not a reason to grant passive input.
- The helper defaults to target-window capture. `-MatteSource` optionally supplies the opaque synthetic backdrop; `-CapturePinScreenRectangle` explicitly opts into the actual pin rectangle, using owned-HWND, visibility and physical-pixel checks. The latter requires permission for anything exposed through a transparent pin. Neither capture path activates the pin.

## Correction

`BrowserOwner.MoveAsync` now awaits `Compositor.RequestCommitAsync` after the existing native unload join and before attaching the same browser subtree to its destination. XAML unload readiness alone was insufficient at this cross-root composition boundary. The commit request lets the previous composition detachment be submitted before the native visual target is reused. This is a bounded asynchronous lifecycle step, not a fixed sleep or repeated repaint loop.

The shared move path covers full-widget pins, hosted pop-outs, expansion and return. Browser creation, navigation, profiles, pointer admission, window styles, focus and layering remain unchanged. The old controller is not recreated. The tests establish that this boundary resolves the reproduced failure; they do not identify the exact internal WinUI/Chromium race.

## Validation and delivery

Earlier managed shell tests passed 234/234; an intermediate webapp regression passed 152 checks. Those remain baseline evidence, not validation of the final correction.

The corrected build passed 18 consecutive fresh-process runs (`commit-boundary-1` through `18`), alternating rail/radial menus and including attached pop-outs. Each verifies passive, overlay-hidden, reopened, interactive and passive-after-input pixels, with unchanged navigation. Runs 7 and 18 also have authorized screen crops. Eight pin/unpin cycles within one process (`repeated-session-rail`) passed all 40 pixel observations and 24 lifecycle checks. `-Cycles` now supports 1–10 repetitions without restarting the fixture.

The final broader webapp regression (`webapp-regression-final.json`) passed 92 checks through shutdown, including shared profiles, permission dialogs, pin layering/input, live pop-out placement, expansion, preserved synthetic media tracks, memory policy and session recovery. Review confirmed the composition wait is cancellation-aware, bounded by the existing two-second transfer timeout, and does not add activation or recreate controllers. There is no new background loop.

The Release frontend published successfully and was overlaid onto a fresh copy of the accepted complete candidate payload. All 136 top-level deployed DLLs match the new frontend publication. The candidate is running with Twitch selected at `artifacts/browser-passive-pin/candidate-01/WidgetRail-Developer-0.1.0-preview.31-win-x64` (PID 34164 at launch). The normal profile, sign-ins and opening shortcut were preserved. An initial 12-second UI wait expired while the existing installed-package catalog validated; afterward the Twitch tray item invoked successfully and the process remained responsive.

Physical checklist: repeatedly pin the full Twitch widget without pressing View; hide/reopen the overlay; enter/exit pin interaction; unpin and repeat. The content should appear passively every time and the live page should persist. No merge, push, release, installer or profile reset was performed. Account-dependent physical acceptance remains the user's.
