# Widget notification adoption and tray improvements

Status: planned, not implemented. Agreed scope through 2026-10-15.
Start from main after the accepted theme and widget-WRSS work is integrated.
Preserve the normal profile, sign-ins, Guide shortcut, and unrelated changes.

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
  banners start disabled and are introduced during setup.
- Keep configuration under the existing notification-preferences entry. Categories
  cannot bypass global quiet intervals, accessibility overrides or host mute.

## 4. Notification visibility and lifecycle

- Add a host-owned policy for notifying when the originating widget is not visible,
  including while another widget is open. Account for visible main and pinned
  surfaces; preserve existing Always and Overlay hidden choices.
- Keep brief passive banners and explicit Open/Dismiss pending entries. Preserve
  stable keys, deduplication and quiet updates: progress/metadata updates must not
  replay banners or extend their lifetime.
- Reuse validated notification intents for destination opening and existing stale
  entry behavior. Notifications must not create extra polling, wake idle workers,
  or extend residency solely to publish a notice.

## 5. Widget adoption

- **Spotify:** report its own local playback versus playback elsewhere through
  independent activity sources. Add optional song banners with artwork, track,
  artist and duration using shared categories, opacity and quiet policy. Reuse
  existing playback observations and deduplicate track changes.
- **YouTube Music:** adopt the shared category configuration while retaining quiet
  song-banner behavior and independent playback activity reporting.
- **Game Help:** add actionable result-ready notifications when the user is elsewhere,
  with a result-opening intent. Retain existing job lifecycle rules.
- **Capture Library:** fill useful completion-feedback gaps and provide an intent to
  open the saved capture when actionable. Avoid redundant announcements when the
  result is already visible.
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
- Build and launch a complete candidate including changed community packages;
  provide a concise physical checklist. Future merge/release requires its own
  user authorization; the theme merge approval does not approve implementation
  or publication of this planned scope.

## Sequence and deferred work

Investigate shared focus ownership first, then implement tray actions, notification
framework additions, Spotify adoption and the remaining widget audit findings.

Deferred: local capture thumbnails in notifications, arbitrary action-button
collections, inline replies, persistence across restarts and additional keep-alive
behavior. If local thumbnails become necessary, add a shared validated host-owned
artwork-handle API rather than exposing file paths or starting local web servers.
