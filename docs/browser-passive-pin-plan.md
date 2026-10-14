# Passive browser pin rendering

## Report and reproduction

2026-10-14: a browser/webapp pin opens a peer window, but its WebView pixels do not appear until View enters interaction. Closing/reopening the overlay does not restore them. Twitch itself was reported to work.

Work is isolated on `codex/browser-passive-pin`, following the local Twitch widget commit `e3846d3f`. Main's unrelated goal edit and the normal user profile remain preserved. Only synthetic fixture pages are instrumented or captured.

The new `--webapp-passive-pin` native fixture gives the native page focus, then pins through the real tray command, rather than directly calling the pin method. `scripts/Validate-BrowserPassivePin.ps1` captures the generated peer HWND without activation and samples its known green page interior. It checks initial passive display, hidden overlay, reopened overlay, interaction, and return to passive display. `-Radial` and `-Popup` cover the alternate switcher and hosted child path. Page-state checks alone are insufficient: failing pages report visible, have nonzero layout and advancing animation frames, but their native region is transparent (ARGB 0).

## Evidence and decisions

- `baseline-menu.json.passive.png`: blank native page, while the widget's ordinary XAML buttons paint. Entering interaction paints the same page without navigation. This reproduces the reported symptom using no Twitch/account content.
- Waiting for native menu `Closed`, then also `Unloaded`, improved individual runs but failed repetition. Those experimental tray changes were removed; do not treat menu timing as a confirmed root cause.
- `original-experiment.json.rasters.json` and `isolated-probes-2.json.rasters.json`: initial passive raster has 0/24 green samples. Obtaining a composition visual restores 24/24 samples while remaining passive. However, doing this on every transfer, deferring it to a render pass, explicitly joining native load events, or changing focus did not reliably fix repetition. Those changes were removed.
- Disabling native-window occlusion passed weaker trials, then failed with native browser focus (`native-focus-no-occlusion.json`). No browser optimization policy change is retained.
- Re-publishing the child visual after visible layout passed six runs, but subsequent variants/repetitions failed again, including explicit native Loaded handling, per-attachment completion flags, and combined handoff-node/child-visual refresh. Native focus release also failed repetition. No production workaround is retained. Candidate patches and logs are preserved under `artifacts/browser-passive-pin`; do not present isolated passing runs as a reliable fix.
- A critical measurement limitation remains: adding an opaque synthetic backdrop makes the original code pass. This happened both with an extra WinUI peer and a separate WinForms source process. Four `screen-proof-*` and four `external-screen-*` runs passed without a production fix. Therefore the background changes the rendering conditions; a successful covered screen capture does not validate the original failure.
- The helper's optional `-MatteSource` starts only the generated `WebAppSharingSource --matte`, validates that it covers the pin rectangle, captures a bounded screen crop while the overlay is hidden, and closes both owned test processes. It never activates the pin for capture. Without that option, only the target window capture is collected. Never enable uncovered desktop capture without user permission.

## Validation and delivery

Managed shell tests passed 234/234; an intermediate webapp regression passed 152 checks. These are baseline/prototype checks, not proof that the pin defect is fixed. All behavioral prototypes have been removed. Current unmerged changes are diagnostic fixtures, their driver, the optional synthetic matte source, and this record.

An async permission question is pending: may we capture only the actual pin rectangle during a live reproduction, including any underlying window exposed by its transparency? The user's earlier instruction excluded personal-window captures, so this cannot be inferred from silence. The accepted `native-dialog-session/candidate-01` host is being restored with Twitch selected. No fix candidate, merge, push, release, installer or profile reset has been performed.

Next: after permission, compare a nonactivating on-screen crop with the native window capture in a failing state before pressing View. Establish whether both show the same missing pixels before choosing another correction. Do not repeat previously failed timing, focus, global-occlusion or visual-refresh workarounds based on a single passing trial. Preserve the existing profile and sign-ins; no account DOM inspection is needed.
