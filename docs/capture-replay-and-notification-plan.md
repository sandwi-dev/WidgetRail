# Capture replay, quick-action feedback and notification banners

Implementation plan, November 20, 2026. The baseline below describes behavior before these changes.

## Current notification behavior

- `OverlayShellPage.Capture.cs` publishes each screenshot or clip result after the capture completes, using the key `capture-result`, informational interaction, Always visibility and BannerOnly retention.
- `NotificationCenter.Publish` applies source visibility and widget/category preferences. `NotificationInbox.Add` replaces an entry with the same widget, profile, origin and nonempty tag. Two successful screenshots therefore replace each other even when the files are distinct.
- `NotificationBannerState.Offer` has one active slot. Another notification from the same widget can replace its contents until the original expiry. A different widget cannot enter the occupied slot. After the default four seconds of display, an eight-second global quiet interval suppresses new banners. Rejected banners have no display queue. A list-eligible entry can remain in the bell, while a rejected banner-only entry is released.
- The WinUI shell measures one card, places one nonactivating, click-through window around pins and overlay UI, and fades it in and out. Existing inbox and transient capacities are 256 and 32. Web notifications also use the shared presenter and have source-scoped replacement and lifecycle callbacks.

## 1. Distinct events and a bounded banner stack

- Retain the newest **128 list-eligible notifications** across all widgets. At capacity, evict the oldest retained entry on insertion. Release its provider callbacks and invalidate any queued, visible or activating reference to that entry. Replacing a tag removes its previous entry first. An in-place content update keeps its original age. Banner-only results use their separate transient capacity.

- Give each accepted capture request its own completion key and publish the final result once. A transport retry for that request keeps the same identity. Two explicit screenshots get two identities, even if their pixels are identical. Keep routine capture confirmations banner-only.
- Display up to **three** cards in one click-through peer, newest at the top. A second screenshot appears immediately and moves the first card down. Each card receives its full configured display duration from first presentation. Use measured heights and compositor translation for movement. Reduced motion uses the same final positions without travel.
- Preserve deliberate updates and withdrawals. A replace/update of the same logical notification updates its card in place, preserves its deadline and does not reannounce it as new. A web notification with the same replacement tag occupies the same slot while honoring the old and new callbacks. Preserve the web producer's explicit renotify behavior when the old card has expired. Distinct keys can coexist across widgets. Keep source visibility, category preferences, retention and activation routing.
- Replace the global quiet interval with bounded admission. At capacity, let visible cards finish and admit queued events oldest first. Proposed starting limits are eight waiting cards and a 15-second maximum wait. Under sustained overflow, aggregate excess transient events per source into a count and retain individually list-eligible entries under the existing inbox policy. Do not count an undisplayed entry as shown. Check eligibility, withdrawal and source ownership again before showing a queued entry.
- Keep the stack anchored within the available work area. Try fewer visible cards if the full stack would cover a pin, picker or overlay control. Preserve click-through and focus through insertion, expiry, monitor/DPI changes, overlay hide/show and reduced motion. Start the shared timer on demand and stop it when idle.
- Keep the existing broker admission guard for SDK notifications (one request in flight per worker and a 200 ms interval for announcements, including updates). Hosted web notifications and host-generated capture results enter through different paths. Apply banner fairness in the common presenter, keyed by widget instance and profile across categories. Allow two consecutive distinct events from one source to appear immediately when space is available. During a sustained burst, collapse additional banners from that source into a single count and give other sources the next available slot. Updates to visible cards and withdrawals consume no new banner slot. Keep list-eligible events in the inbox subject to its capacity. Tune the burst threshold with a replay of two captures alongside a busy Discord conversation.

## 2. Reasons for unavailable quick actions

- Extend the unreleased quick-action protocol and SDK with an optional, bounded user-facing reason for an unavailable declared action. Keep availability separate from executable offers so a reason cannot authorize a command or mask an executable fallback. A failure that happens after dispatch needs a terminal outcome and optional reason correlated with the exact invocation. Replace the unreleased boolean-only path directly, without a parallel compatibility API or migration.
- The shell supplies facts it owns, including a stopped or missing provider, stale target and recorder state. Widgets supply domain reasons such as “Join a voice call to mute your microphone.” A resident widget can publish its reason. A sleeping widget can use manifest/host state, or return a fresh reason on an explicit wake-enabled press. Opening the tray or menu must remain passive.
- The desktop menu exposes the reason on its row and to accessibility tools. A deliberate radial press on an unavailable binding produces one banner. If a fallback is configured and eligible, execute the displayed fallback after revalidation. Report the most specific current reason when neither action is available. Keep a generic last resort for unknown failures, and avoid duplicate host and widget banners for the same attempt.

## 3. Multiple replay approvals and a sticky recording target

- A source check of all 28 local release tags through `v0.1.0-preview.40` found no persisted `ApprovedApplication` field in PlatformSettings. Preserve the existing development profile through a one-time conversion before candidate deployment. The isolated build persists stable approved identities and display names in a profile-scoped list with revision checks. The broker owns persistence and Game Capture owns the management UI. Keep transient HWNDs and process IDs out of preferences.
- In the existing application chooser, highlight a window and offer two explicit commands, **Record** and **Record and add to approved**. Make Record the default. Cancel starts nothing. Record starts the selected session once and preserves the approval list and auto-start preference. The second command also records approval after successful start. Deduplicate identities and indicate an existing approval. Use the same chooser from Game Capture and the tray fallback. Automatic starts never add approvals.
- Under **Game Capture → Capture settings → Replay → Approved applications**, show the list and current recorded app. Offer Add from eligible open applications and Remove, independent of whether a recording is active. Adding from settings does not require starting a recording. Proposed removal behavior applies to future automatic starts. **Stop replay** ends the current session and disables automatic recording intent under the existing stop behavior. Keep the distinction visible when removing the active app.
- Automatic replay waits for a complete scan with one open approved application and one eligible window in that application. A second open approved app, including a minimized one, leaves replay waiting. Opening the overlay never resolves ambiguity by focus or saved order. An explicit Start uses the exact eligible approved window that had focus before the overlay opened. An unapproved, missing or stale foreground opens the chooser, including when another approved app is available.
- Start discovery immediately when eligible and wait five seconds between unsuccessful scans. Keep manual Start and Stop available while automatic discovery is waiting. An explicit choice supersedes a pending scan. A late automatic result cannot replace that choice.
- Publish one named notification on the first confirmed Recording state of each session, including automatic startup. Retain it in the notification center. Waiting, cancelled starts, failed admissions and pause/resume do not announce a new session.
- Hold the chosen target through focus changes, another approved app opening and minimization. Minimization keeps the existing pause/resume behavior. On target closure, finish the old session and any accepted save before acquiring a new target. Start a separate buffer for the next eligible approved app. Preserve explicit Stop, sleep, permission revocation, storage failures and app exit as distinct outcomes. Use the existing host scan and single recorder, independent of widget residency.

## 4. Save and Stop controller hints

The gallery already binds LB/L1 to Save replay and RB/R1 to Stop when applicable. The replay controls render plain buttons, and Capture settings uses a separate render path. Reuse the existing hinted-action control, native controller-family glyphs and exact session-bound handlers. Check both gallery and settings scopes, including tab/bumper ownership. Show a glyph only where the matching shortcut is enabled and routed to that command. Cover saving, waiting, paused, stopping, selection mode and modal ownership.

## 5. Immediate screenshots

With zero preparation time, keep the overlay and the invoking widget or tray in place.
The host remembers the external foreground window on entry, checks its exact identity
and eligibility, and uses window-scoped Windows Graphics Capture. Capturing with consent
does not require activating the source. If that window closes, fails protection checks
or becomes unavailable, end the request. Delayed screenshots, recordings and replay
keep their existing target selection.

| Consumer | Effect |
|---|---|
| Capture Library and its tray shortcut | Both respect the saved preparation delay. |
| Game Help and SDK Gallery | Both explicitly request five seconds of preparation. |
| Other SDK consumers | Configured zero-delay screenshots use the shared host behavior. Legacy requests use the default delay. |
| Playnite capture integration and replay | Separate capture providers and entry points. |

### Default for Capture Library screenshots

Use **Immediately** when Capture Library has no saved screenshot delay. Apply the same default to gallery captures, the tray shortcut and the value shown in Capture settings. Honor an explicitly saved delay, including five seconds. Capture Library currently shares preparation settings between screenshots and clips. Resolve the default by capture kind so changing the screenshot default preserves the recording default. Game Help explicitly requests five seconds and keeps that countdown.

Verify a fresh profile from both capture entry points, a profile with a saved nonzero delay after restart, the recording default and Game Help’s five-second request.

## 6. Capture Library gallery Menu shortcut

Move the controller Menu shortcut from the focused capture’s Favorite context menu to the gallery toolbar’s existing **More** command menu. Label that control **Menu**, prefix it with the active controller family’s Menu glyph, and remove its trailing arrow. Pressing Menu from a gallery item should open the toolbar menu in one step. Keep Favorite available in capture details and through the item’s pointer context menu.

Verify that opening and dismissing the menu from a scrolled gallery restores focus to the invoking item and preserves its favorite state and scroll position. Check keyboard and mouse activation of the same toolbar control, multiselect ownership, and that an open modal retains its own input.

## Delivery and verification

Replay start selection and notices passed four native scenarios in `artifacts/notification-replay/start-rules/native-06`. A manual chooser start and an automatic startup each recorded only the guarded generated window and showed one named Capture Library banner. With two open approved executables, startup stayed idle. A manual press with an unapproved foreground opened a cancellable chooser and launched zero recorders. An explicit start with an approved foreground recorded that app. The last two scenarios inject the remembered foreground identity of a verified real test window. The catalog, chooser, native recorder, broker and notification center use their production paths. All generated windows and test hosts closed. The replay suite passed 33 tests, the platform broker 114 and the Windows app provider 98, including bounded complete scans and once-per-session notification delivery across pause/resume.

Unavailable quick actions now support widget-authored explanations in the published view. The SDK and protocol bound explanations to 160 characters and 16 action IDs and validate full snapshots and incremental updates. The shell reads reasons only after resolving eligible offers. The isolated native test in `artifacts/notification-replay/quick-reasons/native-01` verified the disabled desktop Stop replay row and the banner on a D-pad press, with zero Capture Library starts and the adjacent Save slot displaying its Start fallback. The default-profile candidate predates this work.

| Quick-action consumer | Adoption and evidence |
|---|---|
| Discord | Distinguishes disconnected, outside a call, command pending and unavailable voice controls. Tests exercise call exit and disconnection. |
| Media Sessions, Spotify, YouTube and YouTube Music | Report missing player or media and provider-specific playback restrictions. Existing transport authority and queue checks still apply. |
| Capture Library | Invalid saved settings withdraw screenshot and clip offers after preparation. The host explains unavailable replay operations from recorder state while the worker sleeps. |
| Other widgets, examples and templates | No named tray actions declared. The optional view property is omitted by default. |

The SDK, Media Sessions, Spotify, YouTube Music, YouTube and Capture Library suites passed 205, 31, 90, 44, 65 and 146 tests respectively. Discord passed 1,071 checks. Live media-account failures have not been exercised.

After dispatch, the SDK handler returns `TrayActionResult`. The runtime retains the completion task from admission through the worker reply, and the bridge correlates the outcome with the widget, worker run and press sequence. A capture handed to the host still waits for registered file work. The tray displays one rejection reason and does not retry the command. Discord’s tray mute and deafen now await the provider reply. The generated worker-process test covers immediate rejection for resident and wake-enabled commands, overlapping presses with different outcomes, worker retirement and no replacement launch. A scripted bridge test checks wrong-worker and wrong-sequence events. The Discord fixture delays and refuses mute. Evidence is in `artifacts/notification-replay/tray-outcomes`. The default-profile candidate still needs coordinated replacement of the frontend, bridge, runtime and participating widget packages.

Native screenshot qualification passed all four cases in `artifacts/notification-replay/instant-03`. The immediate screenshot contained the generated source pixels and kept WidgetRail in the foreground with the radial shortcut accessible. The delayed screenshot and five-second clip saved successfully. Closing the source during confirmation produced an access rejection, kept the radial available and created no library entry.

The first implementation checkpoint passed 32 notification tests, including 128-entry eviction, three visible cards, eight waiting cards, same-source grouping at capacity and expiry after 15 seconds. A native WinUI fixture displayed two separate screenshot results with independent deadlines, added a third card from another widget, withdrew one card and verified expiry of the remaining stack. It used synthetic notifications. Capture Library's 146 tests passed, including the Save/Stop session and shortcut checks for gallery, settings and selection mode. The default-profile candidate predates these changes. Remaining work includes native controller qualification of the hints, interrupted layout and reduced-motion checks, native end-to-end custom failure banner qualification and managed replay approvals.

| Increment | Owners and consumers | Acceptance check |
|---|---|---|
| Banner identity and stacking | Capture producer, shared notification center/inbox and WinUI presenter. Check native widgets, hosted Discord/browser notifications, Spotify and YouTube Music now-playing updates, Game Help and the SDK Gallery. | Two rapid screenshots each get a card and a full display interval. A message from another source can appear during that interval. Verify replacement, withdrawal, receipt callbacks, overflow, pending count, reduced motion and interrupted animations with native evidence. |
| Quick-action reasons | SDK, protocol, runtime, bridge, radial and desktop surfaces. Check Capture Library, Discord, Spotify, YouTube Music, YouTube and Media Sessions. Widgets with no tray declarations need no adoption. | Verify unavailable, both actions unavailable, eligible fallback, permission remediation, post-dispatch failure, retired worker and duplicate completion. Assert zero worker starts merely to render reasons. |
| Replay approvals | Host preferences, broker, source chooser and Capture Library. Audit Catalogue update/removal, system tray and hotkey callers. Playnite’s integration capture settings are a separate provider domain. | With generated apps A and B, verify both chooser commands, add/remove, restart persistence, A running before launch, B arriving later, both available together, minimize/restore, exit/handoff, manual unapproved recording, explicit stop and revocation. Never mix buffers across apps. |
| Controller hints | Capture Library and existing SDK hint rendering. | In each supported screen, glyph, activation and availability agree for Xbox and PlayStation. Mouse and keyboard can invoke the same buttons. |

### November 20 verification

- The automatic discovery interval is five seconds. A deterministic clock test covers the initial scan, 4,999 ms and 5,000 ms. An active recording uses its existing health checks.
- Capture Library shows separate screenshot and clip delays. Fresh gallery and tray screenshots request zero preparation time, clips request five seconds, and existing explicit shared delays remain effective after restart. Game Help explicitly requests five seconds. The Capture Library suite passed 148 checks.
- The Capture Library settings dialog reads approved applications when opened and when the settings revision changes. The SDK exposes read, choose and remove operations. Only the host picker supplies a new identity. The broker checks the owner, revision and capture permission before persisting. Save and recorder work continue on a separate path. System tray controls and other widgets have no approval management UI. All 35 replay checks passed, including active-save removal, stale selection and permission loss.
- The native management fixture added and removed a generated application without starting recording. The same run covered manual, automatic, ambiguous and explicitly focused starts, bumper saves from gallery and settings, and a bumper stop. See `artifacts/notification-replay/start-rules/managed-03`.
- The shared WinUI banner presenter draws the newest cards that fit. A placement interruption preserves the original expiry for previously shown cards and keeps waiting entries eligible within the existing 15-second bound. Full and reduced motion passed synthetic native stacking and interruption scenarios. All notification producers use the shared presenter.
- Fullscreen widget content now acts as a preferred area to avoid, with fallback to a visible corner when surfaces fill the work area. The passive banner window keeps its original deadline through shell close and reopen. Native Playnite Home tests in `artifacts/fullscreen-banners` exercised three concurrent banners, withdrawal, constrained space, expiry, native window order and the physical hide/show round trip at full and reduced motion.

  | Consumer | Applicability |
  | --- | --- |
  | Capture Library, Discord, Spotify, YouTube Music, Game Help and SDK Gallery | Shared host placement. Existing delivery preferences and source visibility policies apply. The Playnite native fixture publishes capture and message notices through the shared notification center. |
  | Website notifications | Same passive peer after admission. Website permissions and callbacks retain their existing contract. |
  | Playnite and other expanded widgets | Can be underneath eligible banners without implementing notification UI. Templates and SDK examples require no API changes. |

| Scoped command-menu consumer | Result |
|---|---|
| Capture Library | Uses the toolbar Menu glyph and shared shortcut. Gallery cards keep mouse and keyboard Favorite actions. |
| Discord, Spotify, YouTube Music, Display Profiles and SDK Gallery | Existing action-surface context menus keep their controller bindings. |
| Playnite, Path of Building and Discord scoped menus | Existing container menus continue to resolve within their own input scope. |
| Other widgets, SDK examples and templates | No scoped command-menu or native-only context opt-in. |

### Release candidate verification

The staged Release frontend and packaged settings, bridge and capture workers passed startup. The default-profile candidate restarted on November 20. The one-time conversion preserved one replay approval and rebound two existing unsigned-package shortcuts to the reviewed replacements. The catalog validated 18 packages, the two slot identities matched their installed content and the new frontend started one replay worker. The production settings reader also validated a converted copy before restart. Backups of the original replay and tray preferences remain beside the originals.

The final native replay run in `artifacts/notification-replay/start-rules/managed-05` includes a post-dispatch rejection from a real fixture worker, the approved-app picker, manual and automatic notifications, ambiguous startup, exact focused starts, bumper controls and Menu return. Four packaged screenshot/clip scenarios in `instant-default-01` cover a fresh instant screenshot, a saved delay, recording and source loss. The full- and reduced-motion runs in `placement-full-8ad177` and `placement-reduced-5df331` also verified recovery when a lone active card temporarily lost its placement.

The 36-image fixture in `scrolled-menu-input/result-02.json` exercised Menu and B on a scrolled grid, Shift+F10, and a native right click. It verified item identity, favorite state and resting viewport offset before and after dismissal. Its controller inputs used the host's digital input route. The deterministic recorder suite passed 36 checks, including holding the approved target through pause/restore and draining an accepted save before handing off to a separate empty buffer. Platform Broker passed 114 checks, Capture Library 148 and the SDK 205. Live third-party account failures and physical controller hardware were outside these fixture runs.
