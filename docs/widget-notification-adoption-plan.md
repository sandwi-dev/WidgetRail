# Widget notification adoption and tray improvements

Status: implementation and candidates accepted by the user on 2026-10-15; local integration into main authorized. The separate external SDK packaging defect remains deferred.
Start from main after the accepted theme and widget-WRSS work is integrated.
Preserve the normal profile, sign-ins, Guide shortcut, and unrelated changes.

Scope correction, 2026-10-15: remove Capture Library completion notifications and
their destination intent. Successful capture already prepares the review page;
the overlay remains hidden until reopened, when that result is available. Keep
the existing capture/review flow and recording badge. This correction supersedes
the Capture Library notification work in the earlier conversational goal.

## 1. Shared focus ownership during widget navigation

User reproduction: in YouTube Music, press Y to open the widget's own settings;
focus jumps to the notification bell in the tray. Back to the library screen
also sends focus to the bell. A missing shared focus policy is a hypothesis,
not an established cause. Investigate input ownership, snapshot replacement,
page transitions, focus requests, and native focus fallback before choosing a fix.

- Reproduce both directions and identify the event that transfers focus to the
  shell. Check tap/hold/release routing so widget-owned Y cannot become tray Y.
- Keep focus owned by the active widget during in-widget navigation. Preserve a
  valid retained target; honor the incoming page's declared initial target when
  changing pages. On Back, restore the originating library item/control and its
  viewport where possible. If that target disappeared, choose a valid in-widget
  fallback instead of the bell or rail.
- Delayed snapshots, animation completion, notification/status refreshes and stale
  focus requests must not override newer navigation or an intentional focus move.
- Preserve deliberate transitions to the rail, radial, bell, dialogs, keyboard,
  and pins. Restore dialog focus to its valid owner. Do not solve the symptom by
  making the bell unfocusable, adding arbitrary delays, or forcing focus on every
  widget update.
- Fix in the shared presentation/focus layer where evidence supports it; correct
  missing widget navigation declarations when appropriate. Avoid a YouTube Music
  special case and audit analogous transitions in other widgets.
- Add a synthetic regression for outgoing-target removal, incoming initial focus,
  Back restoration, asynchronous updates, stale completions, and intentional shell
  navigation. Physically verify YouTube Music's Y/settings and Back/library paths.

## 2. Tray shortcuts and management

- Hold Y for **500 ms** opens the existing global notification panel in rail and
  radial. Tap Y continues to reorder. Release after a successful/cancelled hold
  must not trigger another action or reach a different owner.
- Restart/Start remains in the Widget submenu without the hold shortcut.
  Preserve X Reload on stopped-widget surfaces; update guide/menu/accessibility hints.
- Add **Manage widget inside Widget**, ordered Manage widget, Reorder widgets,
  Restart/Start widget. It is not a new top-level menu item.
- Use shared host-authorized navigation to the existing Settings widget-details
  destination and loading behavior. Resolve the selected package securely; no
  extra widget-author declarations are required. Do not start a stopped source
  widget, and omit the item when no corresponding management page exists.

## 3. Notification categories and shared preferences

- Add bounded, validated category declarations with stable IDs, readable names,
  descriptions and initial defaults. Existing callers and webapps use a default
  category; do not infer website categories from message text or DOM content.
- Keep overall per-widget preferences as the master control. Category preferences
  control enabled state, banner/list presentation and applicable appearance
  overrides, including opacity. Defaults never overwrite a saved user choice.
- Widget setup/settings should reach the same host-owned category configuration,
  rather than maintain a second competing enable/opacity setting. Optional song
  banners start enabled for new installations and are introduced during setup.
- Keep configuration under the existing notification-preferences entry. Categories
  cannot bypass global quiet intervals, accessibility overrides or host mute.

## 4. Notification visibility and lifecycle

- Add a host-owned policy for notifying when the originating widget is not visible,
  including while another widget is open. Account for visible main and pinned
  surfaces; preserve existing Always and Overlay hidden choices. Game Help's
  result-ready notification is the concrete consumer: its answer operation already
  uses Widget lifetime and can complete while the user is elsewhere. Spotify and
  YouTube Music song banners retain their existing always-eligible quiet behavior.
- Keep brief passive banners and explicit Open/Dismiss pending entries. Preserve
  stable keys, deduplication and quiet updates: progress/metadata updates must not
  replay banners or extend their lifetime.
- Reuse validated notification intents for destination opening and existing stale
  entry behavior. Notifications must not create extra polling, wake idle workers,
  or extend residency solely to publish a notice.

### Required host/SDK work and existing mechanisms

| Area | Scope and consumer |
| --- | --- |
| Focus ownership | Investigate and correct the shared transition/input/focus behavior demonstrated by YouTube Music; retain explicit widget navigation declarations. |
| Tray routing | 500 ms Hold Y, consistent hints/release handling, and Manage widget inside the Widget submenu. |
| Settings navigation | A host-authorized route to the existing widget-details destination; widget authors need no new declaration for tray management. |
| Notification categories | Bounded SDK declarations, broker validation, host persistence and configuration UI; Spotify and YouTube Music are the initial consumers. |
| Category configuration access | A reusable way for widget setup/settings to open the same host-owned category preferences, without duplicate widget-local switches. |
| Source visibility | Shared host policy used by Game Help; do not make widgets inspect shell layout or track pin visibility. |
| Notification activation | Reuse the existing validated OpenIntent transport and lifecycle checks. Add a Game Help destination contract/handler in the widget, not a second host notification-navigation system. |
| Playback activity, permissions, private state | Reuse the existing APIs and retirement/update-continuity mechanisms; add extensions only if the widget audit establishes a concrete gap. |

No local-notification artwork handle API is required for this scope. Music
artwork already uses supported HTTPS URLs; Game Help can use an ordinary
result-ready card. No capture-completion host events, capture visibility rules,
background-capture service, or capture-specific notification handling are needed.

## 5. Widget adoption

- **Spotify:** report its own local playback versus playback elsewhere through
  independent activity sources. Add optional song banners with artwork, track,
  artist and duration using shared categories, opacity and quiet policy. Reuse
  existing playback observations and deduplicate track changes.
- **YouTube Music:** adopt the shared category configuration while retaining quiet
  song-banner behavior and independent playback activity reporting.
- **Game Help:** add actionable result-ready notifications when the user is elsewhere,
  with a result-opening intent. Retain existing job lifecycle rules.
- **Capture Library:** no new completion notifications, notification category,
  saved-capture opening intent, or thumbnail integration in this work. Preserve
  its current recording activity badge and capture/review behavior.
- **YouTube Video, Browser, Discord, Twitch:** audit shared media, notification,
  permission and lifecycle integration; retain automatic host audio detection.
- **Other widgets:** fill actual feature-triggered permission, cancellation, denial
  recovery and Settings-navigation gaps. Monitoring another app's playback does
  not make Audio Mixer or Media Sessions a local audio-producing widget.

## 6. Preference storage

- Use PrivateState for small ordinary settings where this reduces duplication.
  The user accepts defaults for legacy preferences moved from widget-owned files;
  do not add permanent import/migration code for those files.
- Retain reviewed-update continuity for preferences already in PrivateState.
  Keep credentials, caches and databases in their appropriate stores.

## 7. Authoring, validation and delivery

- Update SDK Gallery, relevant templates and documentation with categories,
  visibility, quiet updates, destination intents, independent activity sources,
  permission requests and navigation/focus expectations.
- Exercise controller routing, focus retention/restoration, cancellation, stale
  notifications, worker retirement, preference persistence and old-widget
  compatibility. Use synthetic content for automation and avoid account actions.
- Use Spotify/YouTube Music song changes and Game Help result completion as the
  notification acceptance cases. Do not create a capture-notification test or
  alter the capture workflow to manufacture a notification use case.
- Build and launch a complete candidate including changed community packages;
  provide a concise physical checklist. Future merge/release requires its own
  user authorization; the theme merge approval does not approve implementation
  or publication of this planned scope.

## Sequence and deferred work

Investigate shared focus ownership first, then implement tray actions, notification
framework additions, Spotify adoption and the remaining widget audit findings.

Deferred: local capture thumbnails and a corresponding notification-artwork API,
arbitrary action-button collections, inline replies, persistence across restarts
and additional keep-alive behavior. Revisit capture notifications only if a future
workflow establishes a useful need beyond the current review page.

## Implementation evidence (2026-10-15)

- Shared focus failure reproduced with a native pointer-focused outgoing control:
  WinUI fallback entered the bell during presentation replacement and disabled the
  widget's pending focus restoration. The bell now uses the rail's shared ownership
  guard. Synthetic replacement and deliberate bell/background-update checks pass.
  Evidence: `artifacts/notification-adoption/focus-baseline-pointer.json` and
  `focus-fixed-result01.json`. Physical YouTube Music acceptance remains pending.
- Tray routing and host-authorized Manage widget navigation are implemented, with
  regression validation pending. Category contracts, persistence, configuration UI
  and source visibility are in progress. Widget adoption and complete candidate
  validation are not yet complete.

### Current implementation checkpoint

- Category UI now uses the existing shared themed choice dialogs, including pin
  ownership. Native controller selection, scrolling to opacity values, B return,
  and persisted choices passed; synthetic XAML captures were visually reviewed.
- Host master/category persistence, strict declarations, configuration admission,
  SourceHidden admission and quiet-update behavior are implemented. Shell tests:
  241 passed. Broker: 81; SDK: 158; catalog: 39; intent bridge: 19 passed.
- YouTube Music shared setup/preferences: 32 tests passed. Game Help background
  completion, result opening, cleared-result fallback and existing behavior: 48
  passed. Game Help uses its own non-reserved community contract namespace.
- Spotify adoption uses separate local/remote activity contributions and the
  existing local-player event stream while hidden. Visible cloud refresh remains
  unchanged. Hidden remote-device changes cannot be observed without new polling;
  this limitation is documented, not hidden behind a new background loop.
- SDK Gallery and generated README guidance cover categories, configuration,
  quiet updates, source visibility, independent activity sources and focus policy.
- Complete candidate/package build, final regression review and launch remain.

### Widget audit and storage decisions

- Browser, Discord and Twitch continue using automatic host WebView audio and
  website notification handling; no duplicate DOM/audio observers were added.
  YouTube Video retains its shared embedded-media handling and declared intents.
- Capture Library already reports/clears its own recording activity and requests
  capture/library access at the feature action. It remains unchanged. Performance
  Monitor and Display Profiles already route relevant actions through
  WidgetPermissionAction; Settings/Marketplace retain their existing deep links.
- Audio Mixer and Media Sessions observe other producers and deliberately do not
  report themselves as audio-playing widgets. Spotify's local/remote split covers
  the actual missing ordinary-widget producer.
- Game Help's spoiler/search choices now use shared PrivateState, removing its
  separate file writer. Existing legacy values may reset as agreed; no importer
  or permanent migration path was added. API keys remain in the existing secret
  store. Database/artwork/library caches were not moved into preferences.
- A pre-existing Spotify test waited for the request count before asserting the
  response was published. The extra event work exposed that race. It now waits
  for the actual observed remote playback result, retaining the source-loss check.

### Review findings addressed

- Preserve descriptor identity when catalog refreshes deserialize equivalent
  notification category lists. The existing in-flight refresh regression now
  includes a nonempty category; the old reference comparison canceled refreshes.
- Configuration has its own admission clock, so background music metadata does
  not consume an interactive preferences request. Named categories are validated
  again at presentation-session admission; replay/foreign runtimes remain ignored.
- Pin-owned configuration uses pin foreground ownership and shared pin dialog
  placement. It does not require the main widget window to own foreground.
- YouTube Music deduplicates by song ID rather than object reference, allowing
  replaced metadata to update quietly. Hidden Spotify source-loss events clear
  activity without announcing an old cached cloud track.

- General remains discoverable even for widgets without named categories, avoiding
  a hidden category mute when ConfigureAsync uses its default. General opacity
  also applies to website banners without changing their content or callbacks.
- CLI validation uncovered an incomplete external-consumer test fixture: its
  copied CLI omitted the existing PackageDistribution dependency. The fixture now
  copies that required DLL; production publication already includes it. Console
  worker-fixture suites are run through their EXE, not `dotnet <test>.dll`.

## Candidate delivery (2026-10-15)

Candidate: `artifacts/notification-adoption/candidate01/OverlayFrontend.WinUI.exe`,
PID 19436 at launch. Complete developer payload: 1,121 inventoried files, 15 bundled
and developer widgets; catalog/integrity/icon verification and async-cleanup
verification passed. All 136 top-level deployed DLLs match publication03.

Normal profile and Guide configuration retained; platform-settings SHA-256 is
unchanged. Existing sign-in stores were not inspected or modified. Main checkout's
unrelated goal/plan edits are preserved. Updated local community packages through
the reviewed-update route (ordinary settings retained): Spotify 0.3.87 and YouTube
Music 0.3.40. Game Help 0.1.3 is installed as a community widget for testing, not
bundled into the installer. No merge, push, release or installers were performed.

### Validation results

- WinUI shell 241; presentation session 346; Spotify 82; YouTube Music 32;
  Game Help 48; SDK 158; SDK compatibility 17; broker 81; catalog 39;
  SDK Gallery 13; intent bridge 19; full-trust host services 1: passed.
- Native focus/category fixture passed after final changes, including pointer
  fallback, deliberate bell focus, controller delivery/opacity selection, B return
  and persistence. Synthetic dialog images were visually reviewed.
- Evidence lives in `artifacts/notification-adoption`: `native-final-result.json`,
  `notification-category.png`, `notification-preferences.png`, `live-payload.json`,
  `startup-health.json`, package/update logs, suite logs and unique build binlogs.
- Correct EXE invocation passed the CLI stalled-probe, development ownership and
  basic-scaffold checks. The earlier broad DLL invocation is invalid evidence for
  tests that launch themselves as child processes; it was stopped and not counted.

### Separate external SDK packaging gap

The isolated external full-trust consumer probe remains red after fixing its
incomplete CLI-copy fixture. Reproduction: run WrailCli.Tests.exe with exact test
`Built wrail artifacts support an isolated external SDK consumer`; evidence:
`artifacts/notification-adoption/cli-external02.log`. The generated application
builds, then exits before its widget connection. The unchanged
WidgetApplicationRuntime.csproj references PlatformBroker, while the unchanged
LocalWidgetSdkPackage exporter supplies only WidgetSdk, WidgetProtocol and
WidgetApplicationRuntime. The test explicitly expects no broker assembly in that
export. Resolving that existing bootstrap/export boundary needs a separate SDK
packaging correction; this work does not bypass it by exposing host domains in
an author package. The community package build paths used for this candidate
already include the required runtime dependency, and their full-trust service
regression passes. Do not report the complete external-authoring gate as green.

### Physical checklist

1. Open using Guide. In YouTube Music, Y to Settings then B to the library should
   retain widget focus and its return item, without jumping to the bell.
2. In rail/radial, tap Y reorders; holding Y for 500 ms opens Notifications once.
   Release must not trigger a second action. X Reload still starts stopped widgets.
3. Widget submenu: Manage widget opens the selected widget's Settings details,
   including a stopped widget without starting it.
4. Enable Song changes through each music widget's setup entry. Confirm shared
   delivery/opacity choices also appear in tray preferences and survive restart.
   Song banners start enabled for fresh settings; saved choices are retained. Check artwork,
   artist/duration, open/hidden-overlay banners, quiet pause/resume and unchanged
   bell count. Hidden Spotify remote-device changes are not polled; local-player
   events are supported while hidden.
5. Ask Game Help, switch elsewhere before completion, then Open its pending notice.
   It should return to the answer. Completion while Game Help is visible (main or
   pin) should create no redundant notice. Cleared results return quietly to chat.
6. Open category preferences from an interactive pin, change a choice, then B/View
   back out; confirm input returns to the proper owner. Check master Off suppresses
   enabled categories. Game Help's old spoiler/search values may reset; API-key
   storage is unchanged.

## Completion audit

Previous goal turn: progress (implementation, automated checks, package updates,
complete candidate assembly and launch). This audit inspected the current source,
artifacts and running process instead of relying on the earlier delivery message.

| Requirement | Authoritative evidence and disposition |
| --- | --- |
| Isolated main-based worktree; corrected scope; preserve unrelated edits | HEAD and main both resolve to 4cc81d5367218240018ec5ee6d2ce4a1e71be5c2; work is isolated on codex/widget-notification-adoption. Plan carries the Capture Library exclusion. Main goal-file SHA-256 remains 6A5214A0357BA0A6EDC96B056FD24FCF62B385716BB43EB26EEB2C5B931C6D04. Complete. |
| Reproduce and correct focus ownership | Pointer-state removal reproduction fails before the shared admission guard; native-final-result.json passes repeated library/settings replacements, deliberate bell entry and background publication. Existing YouTube Music controller/navigation tests pass. Physical account-dependent acceptance remains with the user. Complete for autonomous scope. |
| Hold Y 500 ms, tap reorder, stopped X Reload | TrayHoldGesture exact-boundary, winning-hold, stale-owner and reconnect tests pass; rail and radial use the shared route and updated hints. Stopped reload path is unchanged. Complete. |
| Manage widget inside Widget | Tray group ordering reviewed; closed host Settings intent resolves the actual package, rejects replacement and does not start a stopped source. Intent bridge 19/19 passes. Complete. |
| Categories, defaults, master preferences and configuration | Manifest/session validation, schema-1 preference preservation, first-use defaults, concurrent/cancelled writes, master override and native configuration tests pass. Setup actions and tray call the same UI, with pin-specific ownership supplied to the existing shared dialog mechanism. Complete. |
| Host visibility and quiet notification semantics | Dispatcher-owned source check covers visible main and current visible pin. Center tests prove visible-source suppression, hidden admission, no resurrection, stable identity, replacement and category restrictions. Existing banner deadline, placement, retention, interaction and accessibility behavior remains in the passing shell suite. Complete. |
| Spotify and YouTube Music adoption | 82 Spotify and 32 YouTube Music checks pass, including background local events without polling, teardown, local versus remote activity, shared setup action and stable-song metadata updates. Optional host category defaults are off. Hidden remote-device polling is deliberately absent as required. Complete. |
| Game Help result intent and small preferences | 48 tests pass including background answer completion, valid result opening, cleared-result fallback without resubmission, declaration validation and PrivateState restart continuity. Existing answer operation lifetime remains Widget. Complete. |
| Other-widget audit and exclusions | Audit source locations and decisions are recorded above. No Capture Library implementation files changed; existing recording activity/capture review retained. No duplicate WebView audio reporting or added polling/residency. Complete. |
| SDK Gallery, templates and documentation | Gallery 13/13, SDK 158/158 and SDK compatibility 17/17 pass. Correct EXE invocation of Generated widget completes the offline external package journey now passes (cli-offline-final.log). Notification reference, tray/radial docs, sample READMEs and generated README guidance updated. Complete. |
| Review and runnable complete candidate | Review findings and corrections are recorded above. Payload inventory verifies 1,121 files and 15 developer/bundled widgets; 136 deployed DLL hashes match the qualified publication. Process 19436 is responsive and recorded input-ready startup. Changed community packages installed and the physical checklist supplied. Complete. |
| Profile, enabled/stopped choices and safety constraints | completion-audit-state.json records unchanged platform preferences (theme/Guide), stopped-widgets.json last-write 2026-10-15T11:11:38Z (before this goal), unchanged main goal file and exact live candidate path. Existing seven installed widgets remain enabled; Spotify/YTM updated in place; Game Help added for requested testing. No credentials or personal-window captures accessed; no merge, push, release, installer creation or worktree removal. Complete. |

The separate external full-trust SDK export gate is **not** green and is not used
as completion evidence. Its missing-dependency boundary predates this change:
there is no diff from main in WidgetApplicationRuntime.csproj or
LocalWidgetSdkPackage.cs. It is recorded as an existing SDK packaging defect,
not silently repaired by expanding the public SDK with host-domain assemblies.
The requested notification API, examples and generated ordinary-widget path have
passing evidence independently of that defect. No further implementation work is
required by this goal; physical/account-dependent acceptance remains outstanding.

## Accepted follow-up: meaningful delivery choices and preference focus

User testing found two omissions: banner-only categories incorrectly offered
pending-list modes, and notification preferences inherited safe Cancel focus.
This follow-up adds optional declared category retention, enforces it at both
admission boundaries, and renders On/Off plus opacity for banner-only categories.
Spotify/YouTube Music and SDK Gallery declare BannerOnly; Game Help declares
Pending. Unspecified/General remain compatible with both retention kinds.

A shared preference-dialog entry point selects the first option or the current
value, scrolls that value into view and uses Back/Done. Nested lists retain their
return row. Existing action-list focus and safe confirmation focus are retained.
Validation and updated candidate delivery are complete; prior completion
records above describe the earlier candidate. Physical acceptance of this
follow-up remains with the user.

Follow-up evidence (2026-10-15):

- Shell tests: 242 passed; presentation-session tests: 347 passed. Contract
  cases reject incompatible retention and preserve legacy/General support.
- Native result `category-refinement-native-result02.json` passes current-value
  focus, full viewport visibility, return-row restoration, Back handling,
  pending delivery choices and safe Cancel focus for confirmations. Reviewed
  settled synthetic screenshots `category-refinement-toggle.png` and
  `category-refinement-preferences.png`. The 350 ms settling wait is test-only.
- Full `publication04` payload built successfully. `candidate02` inventory
  verifies 1,121 files and 15 developer/bundled widgets; all 136 deployed DLLs
  match the publication. PID 40320 is responsive and logged input-ready.
- Updated Spotify to 0.3.88, YouTube Music to 0.3.41 and Game Help to 0.1.4 through
  reviewed package updates, preserving private settings and enabled state.
  All eight previously enabled installed widgets remain enabled.
- Before/after hashes match for platform settings, stopped widgets, shell/pin
  state and notification preferences. No sign-in stores were inspected.
- Focus/retention review and whitespace checks found no further blockers. The
  separate SDK export defect remains deferred. No merge, push or release.

Physical follow-up: open Song changes in either music widget, check On/Off and
opacity, change a value and reopen to verify focus, then use B to return. Confirm
song banners remain absent from the notification list and permission/replacement
confirmations still focus Cancel.

## Follow-up: enabled song defaults and website banner visibility

Song changes now default to enabled for fresh Spotify/YouTube Music category
settings (package versions 0.3.89/0.3.42). Existing host choices and opacity remain
authoritative; no saved preference is overwritten. Setup copy and README guidance
reflect the new default.

Native website notifications now resolve to Always at the shared notification
entry, covering Browser, webapps, hosted windows and CLI-generated webapps. Both
banner arrival and ongoing eligibility use this policy. Permission, master/category
preferences, quiet intervals, fullscreen/picker suppression and placement remain
unchanged. CLI webapp source comments and both README templates explain the policy;
no wrapper-specific forwarding or new browser option is needed.

Audit: the only production OverlayHidden default remaining is the ordinary SDK
WidgetNotification default, retained for compatibility. Music, SDK Gallery and
app-update notices explicitly use Always; Game Help explicitly uses SourceHidden.

Validation: 244 shell tests, 82 Spotify tests and 32 YouTube Music tests passed.
The Release WinUI build passed with zero warnings/errors. Evidence is under
`artifacts/notification-adoption/defaults-*01.*`; regression cases cover website
visibility, explicit widget policies and preserving existing Off/opacity choices.
Whitespace checks pass. This follow-up is built but has not replaced the running
candidate02 or installed packages; no live profile settings were changed.

## Follow-up: organized Website settings

Website settings now renders separate setting/value columns using the shared
HostDialogChoice presentation. Permissions, Behavior and Data stay in one
scrollable list. New windows is one current-value row; its three choices live in
a preference picker, focused on the saved value. B returns without saving and
restores the originating row; choosing a value saves and returns there. The root
uses Done. The heading keeps the origin and shows the profile only when it is
non-default. Permission-change and clear-data confirmations retain safe Cancel.

The browser choice contract carries optional shared preference policy, so both
toolbar-owned and host-invoked dialogs use the same focus/Back implementation,
including pinned owners. Value text remains in accessibility names and shared
choice updates. Existing popup/new-window fixtures follow the new nested route.

Native synthetic validation `website-settings-ui02/result.json` passes ten checks
across main and pin: list contents, saved-value focus, cancelled changes, persisted
changes, restored rows and safe clear-data cancellation. Six dialog captures were
visually reviewed (including cropped pin views); the bounded list scrolls to the
selected lower row. The first fixture's toolbar-driven pin setup did not reach
its dialog; the focused test now invokes the same host-owned
settings route directly and waits for the pin's XamlRoot transfer. It validates
the dialog flow rather than claiming a physical compact-toolbar acceptance test.
Both synthetic processes were closed. The ordinary candidate/profile was retained.
Debug and Release builds cover the shared handler and all validation delegates;
no merge, push or release was performed.

Latest requested candidate delivery: `publication05` assembled into `candidate03`
(1,121 inventory files, 15 developer/bundled widgets). Spotify 0.3.89 and YouTube
Music 0.3.42 updated through reviewed settings-preserving package updates. PID
27096 is responsive and logged input-ready; all 136 deployed DLLs match the
publication. Platform, stopped, shell, pin and notification preference hashes are
unchanged. This candidate includes the defaults and Website settings follow-ups.
The newly reported right-stick choice-list scrolling issue is still outstanding:
the shared host dialog currently sends right-stick deltas to its message viewport,
not its options ListView. No scrolling fix is included in this delivery.


## Follow-up: native group headings, shared list scrolling and Manage widget

The earlier group labels were inside ListViewItem content. Native CollectionViewSource
and GroupStyle now render headings outside every actionable row. Website settings
was the only production caller declaring these groups; Permissions, Behavior and
Data all use the correction. Ordinary ungrouped dialogs keep their existing rows.
Header templates use compiled bindings and native non-tab-stop header containers;
selection, activation, notification visibility callbacks and accessibility names
remain tied to choices rather than headings.

Right-stick samples previously reached only the message ScrollViewer used by
some confirmations. Shared choice lists now receive them too. Existing deadzone,
hitch cap and scroll kinetics are reused. After stick scrolling settles, an
out-of-view focused choice is reconciled to a visible choice; D-pad/pointer focus
is not overridden. A native test caught an over-broad reconciliation during rapid
selection, which was corrected before candidate publication.

Manage widget failed before Settings navigation: PrepareWidgetSettingsIntent was
classified as a widget-scoped ApplicationControl request, but that kind accepts
only global scope. It now uses the existing widget-scoped PrepareIntent scheduling
kind. Runtime/generation/management checks remain intact. The regression suite
covers serialization/classification and real session/bridge/worker pipes into
Settings, including a stopped managed widget that must remain stopped.

Evidence: dialog-groups-ui02/result.json passes all 16 main/pin checks; native
captures show standalone headings and clean row focus. dialog-regression-result.json
passes notification preference, focus and confirmation checks. The Intent bridge
suite passes 19/19, including actual Settings navigation through both pipes.
The superseded dialog-groups-ui01 failure exposed the rapid-selection reconciliation
issue; its corrected rerun passed. All synthetic fixture processes were closed.

Candidate04 delivery: publication06 inventory verifies 1,121 files / 15 developer
widgets, with all 136 deployed DLL hashes matching. PID 45776 launched using the
normal profile. Platform, stopped, shell, pin and notification preference hashes
remain unchanged. Installed community widget versions are unchanged. No merge,
push, release, installer creation or worktree removal. Physical checks: confirm
headings never receive a selection outline; right stick scrolls Website settings
and notification preferences; Widget > Manage widget opens the matching Settings
details. This delivery supersedes the earlier outstanding-scrolling note.


## Follow-up: shared banner surface styling

All passive banners now use NotificationBannerChrome with the existing
WidgetNativeDepth renderer: rounded theme-derived edge lighting, a subtle themed
surface gradient and a soft outer-only shadow. The 30-DIP transparent gutter
covers the native mask's 29-DIP maximum shadow extent and participates in window
sizing and obstacle placement. Shadow masking preserves translucent interiors;
text/artwork are not faded with surface opacity. High contrast uses solid opaque
colors and a contrasting outline with zero shadow. No new widget layout/API,
Windows notification UI, residency or polling was added. Existing fade and quiet
behavior are retained.

Native validation banner-style-result02.json passes shared theme/opacity samples,
shadow gutter bounds, high-contrast shadow suppression and compositor-resource
reuse on resize. banner-style-visual02.png was visually reviewed at the actual
125% display scale, covering dark/light surfaces, 55% opacity and high contrast.
The first specimen screenshot clipped its test grid because the fixture window
was too narrow; only the fixture was enlarged. Both fixture processes were closed.
The complete publication07 payload built successfully.

Candidate05 is running as PID 44392 from publication07. Inventory verified 1,121
files / 15 developer widgets; all 136 deployed DLLs match. Platform, stopped,
shell, pin and notification preference hashes are unchanged. No widget package
updates were necessary for this host-only styling change. No merge, push or release.


## Accepted integration and deferred planning

The user accepted all implemented changes and authorized a local merge to main.
No push or release was requested. The current candidate remains available.

Next planning items, without implementation in this change:

1. Investigate Spotify X toggling twice during one apparent press. X is configured
   without repeat. Trace physical press/release, dispatch and playback reconciliation
   before selecting the shared fix; do not mask it with a Spotify cooldown.
2. Repair the external full-trust SDK export dependency boundary. Extract the shared
   client/wire transport cleanly rather than exporting the whole host broker.
3. Consider declarative browser/webapp notification defaults through WebBrowserOptions,
   with saved user preferences authoritative. Current website notices inherit host
   Always visibility; scaffolding documents that behavior.
4. Longer-term notification options: additional layouts, richer actions/inline replies,
   retained notifications across restart, and validated local artwork handles only
   when a useful consumer needs them. Additional residency and capture-completion
   notices remain out of scope until an actual workflow justifies them.

Final integration review corrected a validation-only assertion that still expected
website banners to be hidden while the overlay was open. The isolated native
notification integration now passes callbacks, pending-list actions, visible/hidden
banner continuity, quiet timing, stale activation and teardown. Evidence:
`integration/native-notifications01/result.json`. Production behavior is unchanged
from the accepted candidate. No new blocking review findings; the SDK export gap
is explicitly deferred rather than represented as a passing gate.
