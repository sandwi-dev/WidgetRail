# Platform feature physical acceptance

The features are implemented and checked automatically. This list is the remaining
human acceptance pass; none of its checkboxes is claimed complete.

Use `artifacts/platform-features-final/0.1.0-preview.29/WidgetRail-0.1.0-preview.29-win-x64/OverlayFrontend.WinUI.exe`, with
its complete folder intact. Quit the currently running overlay before launching
another candidate. It uses the normal application profile unless a normal supported
launch-root override is provided. No testing-only profile behavior was added.
The candidate has not been installed or launched by this task.

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

## 3. Widget marketplace

- [ ] Browse descriptions/screenshots and use LB/RB for screenshot navigation.
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
- [ ] Save a full-resolution screenshot; verify exported PNG dimensions on a 4K or
      ultrawide display. Test real-game/HDR/protected-content limitations separately.
- [ ] Preview clips, seek/play/pause, and check focus, layout and scaling.
- [ ] Browse enough entries to page; LB/RB should enter the first new row.
- [ ] Export and delete with confirmation. Restart and confirm saved originals remain.
- [ ] In Game Help choose a saved capture, edit the question, and explicitly Send.
      Selecting alone must not contact Gemini. The original stays unchanged.
- [ ] Revoke library access and check temporary references become unavailable;
      saved originals should remain. Test the capture permission separately.

No merge, push, public release, application-update installation or worktree cleanup
is authorized by completion of the automated work. Those remain separate decisions.
