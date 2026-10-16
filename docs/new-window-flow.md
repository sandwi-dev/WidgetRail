# New-window flow and Website settings

## Decisions

- The Browser widget explicitly requests `BrowserPopupMode.Hosted` on startup
  and intent navigation. SDK defaults and enum names remain compatible; ordinary
  navigation, private profiles and browser chrome are unchanged.
- User-facing settings are named **Website settings** in the tray, gear and
  dialog. One scrollable list groups permissions, new-window placement choices,
  and profile/session actions. Permissions retain their explicit Ask/Allow/Block
  confirmation. New-window choices save directly without another submenu.
- **Open new window** offers **Open in pinned surface** / **Open over current page**
  for website-triggered windows. **Remember for this website** saves the choice.
  **Website settings → New windows** offers **Ask each time**, **Pinned surface**,
  and **Over current page**. Remembered
  placement remains per widget, account and requesting website. Existing pages
  do not move when the preference changes.
- A second additional page offers replacement, including requests originating
  inside the current additional page. It uses the existing page's placement by
  default, with an explicit alternative. Replacement closes that page and ends
  its activity; a remembered placement never silently authorizes replacement.
- Replacing another widget's pin is explained in the same decision. Pin geometry
  comes from the destination widget's remembered geometry, not the replaced
  widget. Replacing an existing pin from the same widget saves its current bounds.

## Ownership and handoff

Page actions replaces native new-tab/new-window link entries with a **New window**
section: **Open in pinned surface** and **Open over current page**. These direct
actions skip placement selection, override the saved preference for that request,
and never change it. When a page or pin must be replaced, one **Replace / Cancel**
confirmation identifies all affected surfaces and the selected destination.
For hosted pages this is a host action: it captures WebView2's
link destination and source document epoch before showing the menu, completes
the menu deferral, then dispatches the explicit destination into the shared placement pipeline.
It does not depend on a second native new-window event or replay a menu command
against a potentially changed page. Hiding, navigation, account changes and
retirement invalidate the captured request. Other menu commands still use
WebView2's `SelectedCommandId`.

Website-created windows continue through native `NewWindowRequested`, including
its opener and deferral semantics. Host-opened links navigate only after final
placement and do not claim an opener relationship. Unrelated/non-user-initiated
requests do not gain input or permission authority.
One further admitted request may wait for a committed window operation or close
to finish; it is revalidated against its source page and current presentation.
This handles the interval where a new pin is already visible but its predecessor
is still finishing native teardown. It does not build an unbounded request queue.

Each root still owns one resident child; two roots can reserve child slots.
One serialized replacement per root may prepare a temporary extra controller
while retaining its old child. Confirmation cancellation and preparation failure
preserve the existing document. After native acceptance the old controller is
closed and the new one becomes the sole child. A child-originated replacement
uses native window semantics; its former opener is being deliberately retired,
so scripts must not rely on continuing to communicate with that old page.

The shared pin owner returns an old borrowed controller before retirement so
queued old-pin invalidation cannot race the new pin transaction. A new page that
cannot be pinned can fall back to the widget. Requests are bounded, rejection
diagnostics are rate-limited, and logs contain state/reason codes without URLs
or page contents. No site-specific DOM code or widget-author API is added.
Native navigation is released after final placement, so startup prompts do not
arrive while the controller is still parked. Returning an additional pinned page
to the widget selects its parent widget even if another widget was open.

## Validation

The direct-destination follow-up is validated in
`artifacts/browser-extensions/direct-new-window02/result.json`: 179 native checks
pass. Eight fixed-pointer Page actions cycles alternate destinations with the
opposite saved preference, proving that direct actions skip placement selection
and preserve the preference. Coverage includes native dialog teardown, captured
link changes, requests from child pages, combined page/pin replacement, Cancel,
preparation failures, automatic windows/Remember, settings, pin/keyboard lifecycle,
profile isolation and shutdown. Dialog screenshots were reviewed. Long shared
decision-button labels now wrap rather than truncate the destination name.

Preference storage and widget SDK contracts are unchanged. No migration or
website-specific detection is needed.

Candidate10 uses the production frontend from `frontend-final07` and the complete
unchanged runtime/packages from `publication05`. Verification passed for 15 widgets
and 1,133 inventoried files, including published async cleanup. It was launched on
the existing WidgetRail profile as PID 37420 and verified responsive after startup.
Physical follow-up: try both direct Page actions, replacement Cancel/confirm, and
a website-triggered pop-out with Ask each time and with a remembered preference.

`scripts/Test-WinUiNewWindowFlow.ps1` accepts an existing synthetic
`BridgeWebAppFixture` configuration, verifies its marker, and creates a fresh
profile under a fresh evidence directory. It never uses the normal profile.
The native suite covers fixed-position native Page actions repeatedly, temporary
input suppression during command delivery, parent and child replacement,
cancellation/hide, preparation failure, pinned geometry, combined page/pin
replacement, settings, existing popup lifecycle, permissions, media and shutdown.

Completed checks (2026-10-14):

- Debug build and trimmed self-contained Release publication pass.
- `final-native-06/result.json`: 140 native checks pass through shutdown. Eight
  consecutive native Page actions selections use the same pointer position;
  alternating iterations explicitly exercise temporary input suppression at
  native command delivery. Replacement tests include a synthetic initialization
  failure, hidden confirmation, requests from a child, preserved pin geometry,
  another widget open behind the pin, and combined replacement cancellation.
- `popup-regression-02.json`: 15 native opener/profile, `postMessage`, close,
  about:blank startup, narrow-toolbar and pending-initialization cleanup checks.
- Browser widget tests: 2 passed. Managed shell tests: 234 passed. No failures or
  skips. The Browser test's stale protocol number now uses the protocol constant;
  the native narrow-toolbar check explicitly reveals the on-demand toolbar before
  measuring it. Neither correction changes production protocol or toolbar policy.
- The Website settings screenshot was visually reviewed. Shared dialog theme,
  bounded scrolling, accessible labels and native focus controls remain in use.

Evidence is under `artifacts/new-window-flow/`. Source review checked admission
cancellation, queue bounds, deferral completion, stale child callbacks,
reservation ownership, pin guards, profile isolation and startup visibility.

The complete candidate is assembled at
`artifacts/new-window-flow/candidate-01/WidgetRail-Developer-0.1.0-preview.31-win-x64`.
It combines the full current payload with the final published frontend and is
marked as an uncommitted-source candidate. The existing running candidate and
normal profile are unchanged; the new candidate has not been launched.
Final catalog/package/icon verification passed for all 15 bundled identities;
the regenerated complete file inventory and published async cleanup checks pass.

Physical acceptance remains pending: check Browser and Twitch new-window menus,
both Website settings entry points, cancellation, replacing a page or another
widget's pin, and returning to the parent widget. No merge, push, release or
installer creation is part of this change.

## Follow-up: direct links and initial focus (2026-10-14)

The selected-link action now uses a direct host handoff and the explicit pin
label. Shared choice dialogs retry initial focus after native template layout
and keep visible system focus outlines on action buttons. A template refresh
repairs missing focus without resetting an existing choice/checkbox selection;
the notification inbox retains its explicit first-entry behavior.

The new regression checks initial Cancel focus without focusing it in the test,
then navigates left with a controller frame and activates Open in widget. Eight
cycles retain the same pointer position; alternating cycles temporarily suppress
browser input during menu completion. URL-free diagnostics distinguish menu
selection, placement requests, stale admission, host dialog rejection and
cancellation. Fresh evidence is under `artifacts/new-window-focus/`.

Follow-up checks completed: Debug build and trimmed Release publication pass;
`new-window-focus/native-final/result.json` passes 157 checks, including the
captured destination surviving a changed DOM link and direct replacement from
a child without a menu/disposal deadlock. `popup-regression.json` passes all 15
native opener/profile/message/close checks. Managed shell and Browser tests pass
234 + 2 checks. The synthetic `result.placement-initial-focus.png` shows Cancel's
focus outline; no personal page was captured. Review covered request ownership,
source cancellation, deferred navigation, native opener behavior, focus layout
ordering, preserved user navigation, and notification-inbox focus isolation.

The fresh complete candidate is
`artifacts/new-window-focus/candidate-02/WidgetRail-Developer-0.1.0-preview.31-win-x64`.
It reuses unchanged verified runtime/widget payloads with the newly published
frontend. No installer, merge, push or release is part of this follow-up.
Physical Twitch acceptance remains pending.

## Correction: action selection is not dialog focus

Physical testing of the previous candidate showed a highlighted first Page
actions row while Cancel actually owned keyboard/controller focus. The native
link action never ran: A closed the dialog through Cancel. The first regression
suite had manually focused the menu row, concealing this distinction.

Shared `HostChoiceDialog` action-list selection now follows actual focus and
clears when focus moves to a footer control. This applies to Page actions,
Website settings, notification panels and other host choice lists, without
changing persistent selection in ordinary widget lists. Removing a notification
while a footer is focused does not select an unrelated entry.

Initial focus is a separate explicit host policy. Page actions and the top-level
Website settings menu opt into first-option focus through `BrowserChoiceHandler`;
permission and replacement confirmations retain Cancel. The notification panel
retains its existing first-entry policy. No behavior is inferred from translated
dialog titles or website identity.

The corrected native tests open Page actions and press A without assigning focus
to its row, then navigate the placement dialog from Cancel. Long-list pin tests
assert that moving to Cancel clears selection. `Test-WinUiNewWindowFlow.ps1
-IncludeNotifications` also checks notification footer selection and existing
notification activation/dismissal behavior. Evidence is under
`artifacts/new-window-failure/`; physical acceptance remains pending.

During expanded validation, synchronously changing ListView selection from a
native focus callback failed during pinned-dialog teardown. Selection painting
is now coalesced onto the dispatcher after that focus transaction and disabled
while closing. Activation resolves the actual focused row independently of the
queued visual update. Removed or retiring containers cannot become selections.
The initial-focus callback also stops once closing begins.

Final validation: `new-window-failure/native-qualified/result.json` passes all
204 checks with notifications enabled, including A/X before a queued selection
repaint, eight menu-to-placement cycles without row focus injection, footer
highlight clearing, full-widget and pop-out pin teardown, permissions, stale
notifications, and shutdown. Debug build, trimmed Release publication and 234
managed shell tests pass. The synthetic Page actions screenshot now has one
focused action and no Cancel outline. The complete candidate is staged under
`artifacts/new-window-failure/candidate-03/WidgetRail-Developer-0.1.0-preview.31-win-x64`.

## Physical acceptance (2026-10-14)

The user accepted the current candidate and authorized integration into main.
This covers the new-window placement/replacement flow, Website settings, direct
host link action, and shared choice-dialog focus/selection corrections above.
The planned tiered tray menus, rail Back navigation, automatic app updates,
notification intents, recovery simplification, and Marketplace improvements are
separate future work and are not included in this acceptance.
