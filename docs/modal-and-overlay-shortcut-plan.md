# Modal dismissal and configurable overlay shortcut

Scoped 2026-11-12. Implementation and qualification now follow
[the active refinement record](input-discord-refinements-plan.md). The user
explicitly chose Escape/outside-click dismissal without adding a Close button.
The original findings below distinguish source evidence from live reproduction.

## 1. Modal dismissal across input methods

### Current behavior and evidence

- `src/WidgetSdk/Widget.cs`, `WithModal`, installs a scoped B/Pressed dismissal
  shortcut from `WidgetModal.DismissActionId`. Generic dismissal already has an
  SDK contract; widgets should not add their own keyboard/pointer listeners.
- `src/OverlayFrontend.WinUI/Presentation/WidgetViewPresenter.Modals.cs`,
  `ModalKeyDown`, already maps an unhandled Escape to that action. It checks
  active presentation/scope, excludes browser keyboard ownership and transient
  controls, and coalesces repeated requests for the same dialog incarnation.
  Therefore the user's Escape report is a routing/ownership defect to reproduce,
  not evidence that Escape support is entirely missing.
- `WidgetViewPresenter.InputRoots.cs`, `AttachPresentationInput`, attaches the
  bubbling handler to ordinary and portal roots. `PresentationPortal.cs` discovers
  its owner through the anchor's visual ancestry. Check this route and keyboard
  focus when a real Playnite details modal opens; do not weaken the authority
  checks or globally handle already-consumed Escape as a shortcut fix.
- `WidgetModalLayer.xaml` and `.xaml.cs` supply a hit-testable scrim with no
  dismissal gesture. Outside-click dismissal is a confirmed missing feature.
  The older [modal checkpoint](maintainers/winui-modal-control.md) explicitly
  describes the current blocking-only behavior.
- `samples/PlayniteLibraryWidget/PlayniteLibraryDetailsPresentation.cs`, `Create`,
  replaces the SDK Close button through `HeaderActions` with controller hints.
  `src/WidgetSdk/ControllerGlyph.cs`, `ControllerHint`, deliberately produces
  passive text/glyphs. Thus the visible B/Close hint is not a mouse close control.

### Proposed ownership and behavior

1. First reproduce Escape using real Playnite details from both mouse and
   controller entry. Record native focus, original/handled key, presenter owner,
   active scope and transient/browser ownership. Correct the specific shared host
   route; retain dropdown/text-entry/browser precedence and stale-action checks.
2. Add explicit optional backdrop-dismiss policy to `WidgetModal` and its
   protocol representation. Keep legacy declarations' blocking default; opt in
   read-only details/inspection surfaces. This is a genuine shared contract gap.
   The host owns hit testing, pointer gesture lifetime and action dispatch; the
   widget supplies policy and its existing dismiss action.
3. A primary click that begins and ends on the same modal's backdrop dismisses
   only that modal. It never activates the obscured page or tray. Dragging out of
   content, right-click, scroll, focus loss and overlay hiding do not dismiss.
   An open child popup gets the first dismissal; no single event closes both.
4. Preserve existing headers without adding a Close button, as directed by the
   user. Do not parse labels, assume child ordering or turn controller hints into
   buttons to infer the dismiss action. Escape and backdrop clicks use the
   explicit domain dismissal contract.
5. Escape, B and Close dispatch the same domain cancellation action; none may
   imply confirmation. Restore focus/scroll to the invoking item via the existing
   remembered scope, including after refresh, owner retirement and animations.

### Consumer matrix for implementation

| Consumers | Applicability and proposed treatment |
| --- | --- |
| Playnite details, filters and setup dialogs | Shared Escape fix; details gets backdrop dismissal. Keep filter draft/setup cancellation semantics explicit; restore selected game and scroll. |
| PoB inspection, search, website and edit/confirmation dialogs | Shared Escape fix; opt in inspection only after checking nested controls/browser ownership. Preserve unsaved edits and confirmation safety. |
| Discord participant/voice settings | Shared Escape/header behavior; inspect pending-command and call-change dismissal. No automatic backdrop opt-in until checked. |
| Game Help options, library, capture preview and confirmations | Shared Escape behavior; distinguish ordinary inspection from discard/delete confirmation. |
| Game Capture delete confirmation | Escape/Close remain Cancel; backdrop blocking remains unless explicitly approved as cancellation behavior. |
| Catalogue and Settings package/permission reviews | Shared Escape behavior where cancel is admitted; preserve review authority and pending installation rules. |
| SDK Gallery, authoring examples and modal fixtures | Demonstrate and verify opt-in backdrop dismissal and legacy blocking behavior. |
| Other shipping widgets and templates | No direct `WithModal` consumer found in the source scan. Host-owned chooser, native flyout and browser dialogs retain their own dismissal lifecycles. |

No consumer has been migrated as part of this scoping task. Search direct protocol
construction again during implementation. Update protocol version/validation,
SDK API compatibility, documentation and generated native contracts if the policy
adds a field. Avoid adding a general navigation system for this bounded change.

### Complexity, risk and acceptance

Medium overall: Escape alone is likely a small-to-medium host correction, but its
root cause is unconfirmed. Backdrop policy and a compatible header adjustment
touch SDK/protocol/host plus the consumers above. Main risks are double dismissal,
click-through, closing a parent while a native popup owns input, and focus loss.

Native checks must cover Playnite Overview/Achievements, nested menus/selects and
text entry, pointer and keyboard opening, held Escape, outside drag, refreshed
snapshots, pending dismissal, replaced dialogs, focus/scroll restoration, pins,
and overlay reopening. Existing anchored/native-keyboard and modal fixtures are
starting points; synthetic action invocation alone is insufficient.

Relevant precedents: WinUI `Popup.IsLightDismissEnabled` makes outside dismissal
an explicit policy, while `ContentDialog` has explicit close/cancel semantics.
Those semantics inform the existing declarative modal; replacing it wholesale
with an imperative `ContentDialog` is unnecessary.

## 2. User-configurable global overlay shortcut

### Current behavior and evidence

- `src/PlatformSettings/PlatformSettingsModels.cs`, `ControllerSettings`, stores
  only `F1ShortcutEnabled`, default false.
- `src/FirstPartyWidgets/SettingsWidget/ControllerSettingsPresentation.cs` exposes
  one F1 switch. `SettingsWidget.cs`, `ToggleF1ShortcutAsync`, saves the boolean.
- `src/OverlayFrontend.WinUI/Input/OverlayKeyboardShortcut.cs` uses Windows
  `RegisterHotKey` with hard-coded F1, no modifiers and `MOD_NOREPEAT`.
  Registration failure is logged; the settings switch can still say enabled.
- `MainWindow.xaml.cs`, `ApplyKeyboardShortcutSettings`, applies settings at
  hidden startup and on settings changes. It does not rebind a changed chord
  because no chord is stored today. Existing native tests cover F1 ownership,
  repeat suppression, hide/reopen and unregistration.

### Recommended UX and contract

Replace the F1 switch with **Overlay keyboard shortcut**: enabled/disabled,
current binding, **Change…**, and **Reset to F1**. Change opens a small host-owned
recorder with the captured chord, Save and Cancel. Show a useful error if another
application or Windows owns it. Keep the current settings-page location initially;
renaming the whole Controllers page is a separate navigation decision.

Support one primary keyboard key with Ctrl/Alt/Shift/Windows modifiers, including
standalone function keys where Windows permits them. Avoid a small preset list.
Windows-reserved combinations and some keys cannot be promised; validate the
actual registration. Bare typing keys consume that key globally when enabled, so
show that consequence when chosen. Multi-step chords, mouse shortcuts and separate
left/right modifier bindings are outside this first change.

The host owns key recording, Windows registration and effective availability.
Platform settings own a validated key/modifier record and legacy migration.
Settings supplies the user's edit/enable intent and displays the host result;
other widgets should not receive raw global keyboard hooks or manage registration.
Use the existing settings/host command path where it can return that result;
otherwise add a narrowly scoped settings command. The current controller shortcut
declarations are not a global keyboard-binding API and should not be repurposed.

### Migration and lifecycle safeguards

- Legacy true becomes enabled F1; legacy false or missing stays disabled with F1
  available as the initial choice. An explicit new disabled record must override
  a legacy true value. Normalize/reject corrupt keys and modifier masks safely.
- On Save, reserve the proposed combination before replacing the old one, keep
  the old binding on conflict, and release the candidate if persistence fails.
  Serialize edits and ignore stale results after cancel, page departure or exit.
  Handle saving the already-active binding without treating it as an external conflict.
- Suspend toggle handling while recording so entering the current shortcut does
  not hide the editor. Restore the current registration on every exit path.
  Held keys and queued messages from retired registrations must not toggle twice.
- Keep hidden startup, tray/controller recovery, shutdown cleanup and the current
  foreground handoff path. Report startup conflicts as unavailable without deleting
  the saved preference or claiming the shortcut works.
- Define the supported relationship between preferred settings and active Windows
  registration; the settings switch alone is not proof of effective availability.

### Complexity, consumers and acceptance

Medium, larger than changing a key constant: settings/migration, a host-owned
recorder, transactional registration, error feedback and documentation. Risk is
manageable using `RegisterHotKey`, with no new low-level global keyboard hook.
Windows `RegisterHotKey`/`WM_HOTKEY` and PowerToys' shortcut-recording settings are
relevant precedents.

Affected: Settings, PlatformSettings serialization, the settings-to-host update
path, MainWindow's hotkey owner, native keyboard fixture, controller-settings
tests, and user controls/getting-started/troubleshooting documentation. Widget
controller shortcuts and other shipping widgets need no migration. Check templates
and developer instructions for F1 assumptions during implementation.

Tests: fresh/legacy/malformed settings, enable/disable/rebind, same-key save,
registration conflict, storage failure, cancel, stale completion/messages, held
keys, hidden startup/restart, and cleanup. Native checks must show both open and
close from another application's foreground, including a pin and an active game,
without changing controller opening or foreground restoration.

## Suggested sequence

Finish and verify Discord source cleanup, then fix modal Escape and add the
scoped backdrop policy. Implement configurable keyboard activation separately so
its registration and foreground tests have a clear baseline. Neither scoped feature
is claimed runtime-correct by this scoping document; current implementation and
evidence are recorded in the linked refinement record.
