# Shared dialog presentation and passive-pin memory

## Decisions

- Keep native ContentDialog modality, focus restoration and each specialized
  dialog's completion flow. Do not replace keyboards, installation approvals or
  notification actions with a generic choice implementation.
- Share only template-part access (`FrameworkDialog`), scoped theme/placement
  leases (`NativeDialogPresentation`) and identical choice-list navigation.
  Bookmarks/history reuse navigation without inheriting host-choice actions.
- Register pin theme ownership at its window root. Host prompts originating at
  the outer root can now find the same theme as browser-originated prompts.
- Resolve dedicated optional dialog color roles through the existing WRSS
  appearance transport. Existing themes derive defaults; high contrast wins.
  Surface and footer default to one color with a themed separator. Theme brushes
  update in place, and native template replacement refreshes action-button hooks.
- Anchor choices, library/install dialogs and buffered keyboards over their
  requesting widget/pin; clamp to the native window with a 16-DIP margin. A larger
  keyboard may extend beyond a small main widget. Retain in-place live editing,
  bell-anchored notifications, widget-local modals and native Windows picker flows.
- Visible passive browser pins become eligible for Low after 15 seconds, as do
  hidden pages. Input admission restores Normal synchronously. No suspension,
  reload, website activity detection or new widget-author option is introduced.

## Evidence and corrections

The pop-out theme miss was an ancestor lookup starting above the registered
browser slot. Native ContentDialog also moves template parts into a popup, so
walking only the dialog's visual descendants is insufficient for shared chrome
and focus hooks. The small shared base exposes those parts without replacing
native behavior.

Tests caught duplicate resource assignments while changing an open theme. The
old shell-color assignments were removed from dialog styling, and unchanged
WinRT resources are not reinserted. They also caught delayed Normal restoration
when presenter input was enabled before queued placement reconciliation; input
admission now clears passive status on that synchronous path.

The bridge appearance fixture's expected field list omitted the already-existing
`pageAnimation` field. Its assertion was updated along with the new role inventory;
no page-animation behavior changed.

## Validation

- Debug build succeeds without warnings/errors.
- Presentation-session tests: 333/333 pass.
- Real bridge shell-palette transport and bounded appearance checks: 2/2 pass.
- `artifacts/dialog-presentation/native-09.json`: 217 native checks pass through
  shutdown, including left/right/center placement for choices, library dialogs,
  install dialogs and buffered keyboards; dedicated role colors; live theme
  updates; focused native actions; high contrast; full-widget/pop-out dialog
  lifecycle; list scrolling; notifications; expansion; recovery and teardown.
- Four native pin captures show matching full-widget/pop-out styling and visible
  Cancel buttons at both ends of the choice list.
- Both pin types retain visible synthetic video with advancing frames in Low,
  remain unsuspended when the main overlay closes, and restore Normal without
  navigation when interaction resumes. Real-site playback smoothness remains a
  physical acceptance check; Low is a runtime hint, not guaranteed memory savings.

The complete self-contained payload rebuilt the frontend, bridge, workers,
bundled widgets and developer CLI together. Catalog/package/icon verification
passed for 15 bundled identities; all 136 top-level deployed frontend DLLs match
the verified publication. Release async-cleanup verification passed as well.

The updated candidate is running with Twitch selected at
`artifacts/dialog-presentation/candidate-01/WidgetRail-Developer-0.1.0-preview.31-win-x64`
(PID 42764 at launch). It uses the normal profile and existing sign-ins, without
registration, trust-store changes or installation. Its local metadata marks it
as an uncommitted-source candidate, not a published release.

Physical checklist: compare Page actions in the main widget and a pop-out pin;
switch the overlay between left/right positions and open choices/bookmarks or
buffered keyboard entry; confirm the live browser-field keyboard stays in place;
leave a playing pin passive for at least 15 seconds, hide/reopen, then reenter.
Physical acceptance remains pending. No merge, push, published release, installer
creation or profile reset was performed.
