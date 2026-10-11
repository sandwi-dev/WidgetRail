# Platform feature physical acceptance

The features are implemented and checked automatically. This list is the remaining
human acceptance pass; none of its checkboxes is claimed complete.

Use `artifacts/platform-improvements/final-candidate-04/0.1.0-preview.29/WidgetRail-0.1.0-preview.29-win-x64/OverlayFrontend.WinUI.exe`, with
its complete folder intact. Quit the currently running overlay before launching
another candidate. It uses the normal application profile unless a normal supported
launch-root override is provided. No testing-only profile behavior was added.
The complete candidate was launched by this task using the normal profile (PID
97684 at handoff), with Capture Library selected. It was not installed. Both local
editions were assembled from clean commit `97a6e57a`; catalog/integrity/icon gates
passed for 13 production and 17 developer widgets. Browser and Game Help are included.
The Developer sibling contains SDK Gallery. Later commits only document the evidence.

Local UI Automation/visual checks confirmed cold Capture Library loading, actual
thumbnail pixels and saved-image preview, responsive settings, live system graphs,
the compact pin without Automatic, and left-aligned Marketplace cards. Prior native
fixtures cover permission dialogs, capture lifecycle, encoder quality and application
audio. These are not physical-controller or real-game acceptance. Evidence and
remaining limitations are recorded in `platform-feature-improvements.md`.

## 1. Settings diagnostics

- [ ] Navigate health, recent failures and technical details with the controller.
- [ ] Export a support report; confirm the location and readable summary.
- [ ] Check a real failed/restarted widget and its timestamp/recovery action.

## 2. Performance Monitor

- [ ] Inspect CPU/GPU/RAM/VRAM; missing readings should say unavailable, not zero.
- [ ] Enable FPS and approve/cancel the Windows prompt. The app itself stays unelevated.
- [ ] Test actual DirectX, Vulkan and OpenGL games, including multiple swap chains /
      frame generation if applicable; compare displayed FPS and collection overhead.
- [ ] Change the target, close the target, pin/unpin, hide the overlay and stop FPS.
      Collection should end when no visible/pinned consumer renews its demand.
- [ ] Review the 60-second graphs, target-change gaps, average FPS and 1% low.
      The low uses the slowest 1% of individual frame intervals and needs at least
      1,000 frames/five seconds. The compact pin has no Automatic control.

## 3. Widget marketplace

- [ ] Browse descriptions/screenshots and use LB/RB for screenshot navigation.
- [ ] Open Marketplace directly from the tray and through Settings. Additional
      GitHub repositories/private-source sign-in are intentionally deferred.
- [ ] Cancel a download; verify focus remains on the busy operation.
- [ ] Review/install an add-on, including full-access and new-permission warnings.
- [ ] Enable it explicitly, update it, and select a retained prior version.
      Version selection retains data; widget authors still own data-schema compatibility.

## 4. App updates/recovery

- [ ] Check release notes in Settings. A development candidate must refuse to replace
      the registered installation.
- [ ] A real upgrade/recovery exercise requires explicit authorization for a controlled
      Inno installation and a matching newer published installer/recovery pair.
      Do not treat the temporary-fixture tests as installation acceptance.
- [ ] After Continue, controller focus should stay in WidgetRail until you quit.
      The Windows confirmation should then appear; the desktop shortcut also works.
- [ ] Observe/cancel preparation, cancel Setup, and simulate a failed new app launch.
      Recover through the standalone shortcut; inspect settings and newer-profile preservation.
- [ ] Check redirected storage, free-space errors and any required account sign-in.
- [ ] Uninstall data removal is optional; selecting it also removes recovery copies.
      Exported files and Windows credentials remain.

## 5. Capture Library and Game Help

- [ ] Confirm a screenshot/clip, observe the countdown, and reopen to review.
- [ ] Change Capture settings and restart. Check supported quality/audio options,
      duration and bitrate validation, preparation delay and cursor behavior.
      Unavailable saved combinations must explain the problem without downgrading.
- [ ] Test optional Application audio, synchronization and overhead in actual games;
      audio defaults off. Microphone/system-wide audio are not advertised.
- [ ] Reopen during recording to stop and keep the clip; during preparation it
      cancels. Explicit cancel must discard it. Verify target close/resize handling.
- [ ] Save a full-resolution screenshot; verify exported PNG dimensions on a 4K or
      ultrawide display. Test real-game/HDR/protected-content limitations separately.
- [ ] Preview clips, seek/play/pause, and check focus, layout and scaling.
- [ ] Browse enough entries to page; LB/RB should enter the first new row.
- [ ] Inspect thumbnail sizing and focus at your normal scaling and pin sizes.
- [ ] Export and delete with confirmation. Restart and confirm saved originals remain.
- [ ] In Game Help choose a saved capture, edit the question, and explicitly Send.
      Selecting alone must not contact Gemini. The original stays unchanged.
- [ ] Revoke library access and check temporary references become unavailable;
      saved originals should remain. Test the capture permission separately.

## 6. Permission recovery and Settings intents

- [ ] From Capture Library/Game Help with missing access, explicitly request it.
      Cancel/Deny should preserve your work; Allow should continue the action.
- [ ] Test the host dialog with a controller in both main and pinned views;
      initial focus is Cancel and dismissing restores the initiating control.
      Pins smaller than 320×280 DIPs require the main view or Settings.
- [ ] Follow widget-details/permissions links to Settings and confirm the correct
      widget and capability are selected, including a cold Settings start.
- [ ] Ordinary reads/background checks must never prompt for permission.

No merge, push, public release, application-update installation or worktree cleanup
is authorized by completion of the automated work. Those remain separate decisions.
