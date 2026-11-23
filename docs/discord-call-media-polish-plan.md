# Discord call and media polish

Status: implementation authorized and goal active, November 18, 2026. Preserve
all existing uncommitted work and update the isolated candidate only after
qualification. This follows
`discord-messaging-refinements-plan.md`.

The current isolated candidate is Discord 0.1.0.47. It includes in-widget live
streams, fullscreen, incoming-stream pins, attachment previews and advance chat
paging. The user owns the remaining two-account checks for unread receipts,
incoming calls, and remote picture/sound. The real Playnite Library traversal
showed lower allocation after correcting repeated lease validation, with occasional
long viewport gaps still open. The [completion audit](discord-call-media-completion-audit.md)
summarizes the current candidate and outstanding live checks.

## Review loop and live-test authorization

The user renewed the goal on November 18 with these additional requirements:

- After each UI refinement, capture and inspect screenshots of the main screens
  (Servers/call, Messages and relevant modal/pin), record concrete visual flaws,
  and iterate. Include compact and expanded layouts where affected. A passing
  structural assertion alone is not visual acceptance.
- Review controller routes for speed as well as correctness: direct contextual
  access to frequent actions, predictable panel entry/return, and no unnecessary
  traversal through secondary controls.
- Before introducing UI elements or navigation patterns, inspect existing host
  capabilities and research mature framework precedents, preferring Android TV
  D-pad/focus guidance. Record sources and how their patterns fit this host.
- Live calls in Test server and test messages to Sanky are authorized. The signed
  in account is also named Sanky: verify recipient identity instead of relying
  on display-name matching. Leave promptly if another server is joined by mistake.
  Authentication and privacy grants remain user actions; avoid unrelated calls.

## Implementation evidence (in progress)

- Initial content sizing: native reproduction showed 8 DIP overflow from scroll
  cue strips attaching after measurement; after preparing those strips, 150%
  text still overflowed by 4 DIP because scoped typography bound on Loaded.
  Prepare existing realized styles and scroll decorations before freezing the
  opening measurement. No arbitrary height allowance or continuous remeasurement.
- `artifacts/discord-call-media-polish/short-typography-1/result.json`: all 54
  native checks pass (420/1040 DIP, 100/125/150% text, 1/2/12 links, passive and
  entered preview plus Back). Existing entered-gallery regression passes 14
  checks in `gallery-sizing-regression/result.json`; anchored-dialog regression
  passes 136 in `anchored-sizing-regression/result.json`. Broader regressions pending.
- Stable Servers labels and contextual L3 call focus are implemented using the
  existing SDK shortcut/focus-request APIs. The managed Discord suite passes
  924 checks (`widget-controls-tests.log`), including no call/microphone side
  effects from L3 and no advertised shortcut while the call action is busy.
  Native interaction and visual review are still required for this change.

### Framework precedents reviewed

- [Android TV navigation](https://developer.android.com/training/tv/get-started/navigation):
  minimize clicks/screens, predictable directional movement, and test actual
  D-pad routes. Apply through the host's directional links, scope boundaries,
  preferred collection entry and existing contextual shortcuts.
- [Android TV lists](https://developer.android.com/design/ui/tv/guides/components/lists):
  scannable two-line rows; avoid competing nested buttons and color-only
  selection. Conversation metadata stays inside one primary row action; it
  adds no controller stops. Messages with multiple actions need an explicit
  reading/action design, not another ad hoc nested-focus implementation.
- [Android TV focus system](https://developer.android.com/design/ui/tv/guides/styles/focus-system):
  focus groups and distinct visual focus/pressed/selection states. Reuse native
  host focus decoration rather than painting a second selection-like border.

### Rich conversations — implementation and review 2

Implemented optional domain metadata (visible presence, exact snowflake date,
bounded cached last-message text/author). Only Discord's resident MessageStore
is consulted; no per-row REST reads. Existing provider observation lifetime now
tracks optional PresenceStore/UserStore changes for known conversation users,
ignores unchanged signatures, and detaches on account/route retirement. Private
message edits/deletions also invalidate inbox metadata outside the open history.
No numerical unread badge is invented from mention counts.

Rows now use a two-line grid, avatar/initial fallback and presence indicator,
timestamp, unread emphasis/dot and the current conversation's In call label.
Existing spoiler/markdown parsing sanitizes snippets and accessibility text.
One indexed row action remains; metadata is not independently focusable.
Sidebar widened from 260 to 300 DIP, with a recent-conversations heading/count.
Actual public/runtime presence data still needs live verification.

Evidence: provider suite 57 passing (`conversation-metadata-tests.log`), adapter
and session suites 179 passing (`conversation-adapter-tests.log`), widget suite
931 passing (`conversation-rows-tests.log`). The sealed backend builds to
102424 bytes, within the 128 KiB limit. Native navigation/layout rerun after the
avatar fallback/grid-track adjustment passes 85 compact and 89 expanded checks
in `conversation-layout-native`. Hosted/minified messaging, adapter and backend
suites pass 236 checks (`conversation-minified-tests.log`).
Final follow-up suite passes 932 widget checks (`conversation-final-tests-2.log`)
and 237 hosted/minified checks (`conversation-minified-tests-2.log`), adding
replacement-store queued-callback retirement and requiring a connected voice
state before showing In call. Existing native runs preceded only this stricter
call-state condition; no layout or focus structure changed afterward.

Captured Servers/call, the six-row synthetic inbox, and its chat/sidebar using
`visual-rich-rows-2`. The metadata hierarchy is clearer and long names truncate
without expanding rows. Review findings to carry into the next pass: presence
glyphs are visually too small; the empty-cache subtitle is wordy; the full-width
initial inbox is sparse compared with the integrated chat; automatic previews
still obscure sidebar/chat; the dock remains too thin. Do not mistake these
screenshots for completion of the broader redesign. The dedicated
`-RichConversations` staging flag supplies six synthetic rows without changing
the ordinary navigation fixture's count or touching accounts.

### Attachment-modal / inline action ownership finding

The ordinary RichText API already supports intent-button spans, but that API
cannot be adopted inside today's indexed message row: `IndexedCollections.cs`
requires a Button/ActionSurface root, and
`ViewSnapshotValidator.ValidateActionSurfaceContent` rejects every interactive
descendant. `WidgetIndexedRowView` deliberately renders its fragment in
presentation-only mode. Thus the requested clickable inline links and explicit
attachment controls need a bounded shared indexed-row interaction contract;
adding buttons in Discord alone would violate both validation and dispatch.

Investigate an opt-in reading row with explicitly admitted inline actions,
reusing ordinary intent links, row lease authority, host focus groups, collection
return memory and modal navigator. Keep whole-row navigation fast and make the
active subcontrol unambiguous (Android TV's list guidance cautions against
confusing nested actions). Do not disguise inline actions as preview actions,
flatten message/attachment keys into invented provider records, or weaken the
existing ActionSurface contract globally. Estimated medium/high complexity due
to native focus, lease retirement and modal-return coverage. This is an inferred
framework gap with concrete validator/renderer evidence; implementation remains
pending and must establish behavior before migrating Discord.

#### Reading-group contract stage (native integration still pending)

Added an opt-in `UI.ReadingGroup` declaration and protocol 132 capability rather
than weakening ordinary ActionSurface. It admits at most 32 inline buttons/rich
actions, retains bounded layout/inline text, and forbids nested groups, menus,
scopes, service surfaces and interactive media. Passive animations remain
eligible. A group has no competing primary action or A shortcut.

`IndexedCollectionInlineAction` is separate from preview input. Host/worker
admission resolves the exact child in the retained semantic row, checks enabled
ancestors and current scope/query/lease, and preserves both the child ID and
parent occurrence for modal return. Standard host intents now use that same
indexed admission path. No Discord-specific input transport or permission was
added. The native host preflight deliberately rejects this new opt-in until
child focus/routing is implemented and qualified; Discord has not migrated yet.

Initial authoring/admission regression passed (`reading-groups-tests.log`),
covering unsupported versions, forged/disabled/retired targets and exact return
identity. Expanded content-policy, intent, API/parity and native checks remain
in progress. API baseline is updated; generated protocol parity is being rebuilt.

Follow-up: indexed inline intents pass all 28 intent tests
(`reading-group-intents-tests.log`). Generated native-header source parity
passes (223 constants), and API baseline generation completes. The first full
SDK run passed 198/199: `CollectionBoundaryTests` expected exactly version 122,
contradicting the existing `Widget.cs` policy that indexed templates advertise
CurrentVersion. Corrected to that policy and retained a separate explicit
version-121 rejection check. This is a stale test expectation, not removal of a
compatibility safeguard. Full rerun plus nested interactive-media rejection is
in progress (`reading-groups-qualified-*`).

The completed rerun passes **199/199 SDK tests**
(`reading-groups-qualified-tests.log`), including ordinary tiles, reading
surfaces, scope/query/lease admission, the corrected deferred-reveal test and
the nested-player rejection. Native integration is still pending; the gate
remains. Repository search confirms the only ReadingGroup calls are the SDK
factory and its tests, so no shipping widget is using the unfinished native path.
The presentation-session layer also builds with zero warnings/errors
(`reading-group-session-build.log`). No new UI was rendered in this contract
stage; main-screen screenshot review resumes with the native integration.

#### Native reading-group integration (qualification in progress)

Native row presenters now render opt-in child actions using the same control,
theme and intent code as ordinary widget UI. Row-root paint stays on its existing
native item container; A/Right enters actions, internal spatial navigation stays
inside the realized fragment, B returns to the reading row, and vertical exits
resume normal indexed movement. Child pointer activation does not also invoke
the row. Selection mode disables inline interaction and takes A precedence at
both native routing and shared lease admission.

Focus restoration follows the existing presenter memory with an enabled-action
fallback; this is consistent with Android Compose's
[focus groups](https://developer.android.com/develop/ui/compose/touch-input/focus/focus-group)
and [focus restoration](https://developer.android.com/develop/ui/compose/touch-input/focus/focus-restoration)
patterns. Widget authors do not retain native controls or implement input queues.

Added a synthetic real-bridge fixture (`ReadingGroupPresentationFixture`) and a
native `--reading-groups-output` scenario covering child focus/action counts,
intercepted web intents, scrolled message→modal→message return, selection and
retired native commands. The temporary preflight gate is removed for this
implementation, but **Discord is still not migrated and native behavior is not
yet claimed verified**. The matching bridge fixture builds cleanly.

Rebuilt protocol-consuming platform DLL to `native-132/platform`. Preview and
capture native inputs are reused from their qualified build because repository
include search finds the generated protocol header only in the platform boundary;
they have no source changes in this integration. Frontend compilation is underway.

Reading-group applicability review (source inspection, not runtime acceptance):

| Consumer | Assessment |
|---|---|
| Discord message history | Affected, migration pending native qualification. Explicit attachment and link actions require real child controls. |
| Discord participants and connection summaries | Existing reading surfaces remain appropriate; no migration. Audio previews are intentional secondary surfaces. |
| Playnite achievement rows and related captures | Existing reading surfaces, no inline-action requirement; retain existing behavior and include indexed navigation regression. |
| Capture Library gallery/filter/view options | Existing primary cards and focus previews remain appropriate; no migration. Selection and previews need shared regression coverage. |
| PoB inspections, Marketplace, SDK Gallery | Intentional primary actions or focus previews; no migration to reading groups. |
| Templates and developer examples | No production ReadingGroup usage yet; ordinary ActionSurface remains presentation-only. Document opt-in interaction after native qualification. |

Review follow-ups before qualification: test pointer origins inside composed
action content (not only a Button's own native element), retain the actual old
command in the stale-command test, and cover B-release state across suspension.
Do not claim normalized controller routing proves physical pointer/keyboard input.

Native qualification checkpoint:
- Corrected the synthetic server's accept timeout to the existing 30-second
  contract, and corrected composed-action ID segments (`secondary-icon/text`,
  not dotted segments). Added a real-worker bridge contract test so fixture
  authoring/admission failures surface before native focus checks. It passes
  `reading-bridge-contract-3.log` (1/1).
- Runs 2–5 timed out because those invalid fixture IDs prevented row acquisition;
  they do not establish a production focus regression. Retained failure evidence.
- Once rows loaded, run 6 reproduced a stack overflow. The local crash dump and
  `reading-crash-1.log` show the cycle through `FindIndexedCollection`, pressed
  style reset and sibling reading-row presenters. `FindIndexedCollection` now
  resolves the presenter's own semantic binding instead of walking from global
  focus into a foreign collection. Existing plain indexed rows should follow the
  same binding; legacy navigation regression remains required. Native rerun pending.
- Screenshot acquisition returned an unrelated image despite the fixture's
  correct accessibility tree; that image is not visual acceptance. Recheck capture
  when reviewing the next actual UI refinement, rather than trusting that output.

Run 7 passes all **14 native reading-group checks** (`reading-native-run-7/result.json`)
after the semantic collection-ownership fix: exact dispatch, intent routing,
modal return and scroll offset, selection precedence, retired commands and no
automatic preview. This is normalized controller routing through actual WinUI,
bridge and worker; physical pointer/keyboard and static reading-group coverage
remain pending.

The legacy selection comparison also reproduced the same recursion in the
pre-fix binary (`reading-legacy-crash.log`). Restrict pressed-state forwarding to
opted-in reading rows as well, avoiding work in ordinary presentation-only rows.
The first post-fix legacy run reached 56 checks but exposed a test-ordering race:
being on row zero did not prove that a newly published default-entry request had
completed. The withdrawal scenario now waits for the presenter's existing guide
readiness condition (including pending authored entry), retaining all assertions.
Post-fix legacy suite and final reading-group rerun are in progress.

Completed checkpoint on the same `reading-native-6` Release validation build:
- `reading-legacy-selection-2/result.json`: **201/201 native checks pass**, including
  list/grid/grouped selection, L2 precedence, physical mouse/keyboard paths,
  delayed focus-request ownership and lifecycle transitions.
- `reading-native-run-8/result.json`: **14/14 native reading-group checks pass**.
- `reading-native-build-6.log`: matched frontend publish succeeds; platform uses
  the rebuilt protocol-132 DLL. The bridge fixture contract also passes.
- Temporary fixture processes are closed; the existing user candidate PID 25940
  (`discord-navigation-up/frontend-final`) remains unchanged.

Next boundary: finish pointer/keyboard and static reading-group interaction
coverage, then migrate Discord's attachment/link presentation and review actual
main-screen screenshots. The Discord migration, call dock, gallery visual work,
secure media transport, Klipy/posters and remaining visual/live acceptance are
still outstanding. This is an implementation checkpoint, not goal completion.

### Discord explicit message actions — implementation and visual review

The indexed interaction fixture now passes **19 native checks**, including an
actual click on composed action text, Space activation, Enter-to-interact with
remembered child focus and arrow navigation (`reading-native-run-10`). Keyboard
entry adopts the same host-owned path; selection keeps precedence. Static
reading-group coverage is still pending.

Discord now uses ReadingGroup rows, inline URL actions and text-only embed cards.
Duplicate destinations have one action. Attachment media is collected in provider
order and opened explicitly through a shared modal gallery; normal message focus
and hover no longer open rich previews. GIF animation remains inline with the
existing host motion policy. Unsupported files retain only truthful file/link
presentation. Spoilers still withhold media and link declarations until revealed.
`MessageMedia.cs` owns captured Discord data/account policy; SDK navigator owns
the modal visit and exact collection return. Added the history collection to the
existing return-source mapping, with no widget-owned focus counters or offsets.

Managed suite: **963 checks pass** (`message-modal-tests-7.log`), including native
declaration validation, all attachments/embeds/stickers in the explicit gallery,
no background action admission during its modal, exact message return, and
retirement on account replacement/stale dismissal. No new provider calls are
needed to render these controls.

First synthetic screen captures (`message-visual-expanded-1`) found centered
card text, overly heavy message bubbles, a thin dock, and unused gallery space.
Refinement removes message-bubble fills, explicitly left-aligns card text, uses
the theme's data-green for links, fills the bounded gallery content area, gives
missing avatars initials, and adds up to four passive participant avatars to a
slightly taller call dock without extra focus stops. The missing-cache subtitle
is now the truthful, shorter “Direct message.” No Discord palette is hardcoded.

The first native Discord modal run restored the correct row but failed its
offset comparison. The next test captures the viewport after entering the child
action (the actual invocation point) and records both extents/offsets; do not
declare return behavior qualified until that evidence passes. Initial snapshots
also preceded virtualized row painting; capture now waits for native composition
frames. These early incomplete screenshots are not visual acceptance. Compact
and expanded follow-up runs are staged in isolated catalogs/profiles; the user's
running candidate is unchanged.

**Build boundary:** protocol is now 132 in source. Existing running .29 candidate
and previously published fixture/frontend/native/service payloads predate this
contract. Keep them isolated; do not mix freshly rebuilt indexed workers with
the old 131 bridge/frontend. Final qualification must rebuild matching CLI,
bridge, frontend and protocol-consuming native binaries. Generated-header
parity alone does not prove an old native DLL contains the new constants.

| Reading-group applicability | Decision |
|---|---|
| Discord message history | Planned adopter: explicit inline links and attachment modal actions. Migration waits for native qualification. |
| Existing static rich-text links (Game Help / SDK examples) | Already supported outside indexed ActionSurface; retain existing API and behavior. |
| Capture Library and Playnite tiles | Single primary action/selection remains appropriate; retain existing tiles and context actions. |
| PoB inspection previews | Intentional contextual inspection is a different interaction; do not migrate just because an alternative exists. |
| Templates and other shipping/sample widgets | Opt-out unchanged. Repository searches found no existing ReadingGroup consumer; validate ordinary tile/reading-surface regressions. |

### Visual review 1 — controls and sizing candidate

Captured and inspected native screenshots through Computer Use of the isolated
`visual-controls` synthetic fixture: Servers with incoming call, inbox, open
conversation, active call, and conversation with call dock. No real Discord
call/message was made. Findings:

- Stable Servers text reads correctly before/during the synthetic call. Header
  L3 glyph is visible beside Call/Switch & call, but its distance and grouping
  need another review as the header is redesigned.
- Existing passive attachment popup covers the center of the chat even on
  ordinary pointer navigation. This confirms the urgency of replacing that
  interaction with explicit modals, rather than polishing the popup alone.
- Inbox rows are visually sparse and their generic subtitle is uninformative.
  Implement the agreed cached snippets/presence/timestamps without adding focus
  stops. Sidebar truncates even moderately long names; reassess its width with
  realistic names while preserving chat space.
- Current dock is a thin outlined strip with a long text summary and tiny
  actions. The planned modest height increase and participant avatars should
  replace this visual hierarchy, not merely add another line of text.
- Empty activity column still reserves substantial width on Servers/call pages.
  Reassess collapse/empty-state behavior during the layout pass.
- Found stale accessible Back to Call label in inbox; changed to Back to Servers.
- Existing fixture's huge attachment-bearing message exposes wasted tile space
  for nonmedia files. Compact file actions should avoid image-sized empty tiles.

These screenshots establish review findings, not acceptance of the redesign.
Native L3 routing checks have been added to the synthetic private-call fixture;
their first run verified direct composer-to-Call focus without side effects, then
exposed a stale-target bug after starting the call. Call disappears when its
conversation becomes active, but the retained focus request still referenced it,
causing `invalid_exact_focus_target` and a Retry screen. Evidence is retained in
`call-focus-native/minimum/result.json` and that profile's overlay log.

Corrected `Views.cs` to resolve pending exact control targets through the existing
`WidgetFocusTargetLookup` before publication, as it already does for restored
initial focus. The private-call managed suite now passes 36 checks, including
focus → call → removed-target validation. Native rerun passes 18 checks each at
420×360 and 1040×680 DIP (`call-focus-native-2/{minimum,expanded}/result.json`),
including L3/A handoff, incoming/decline/answer, notifications and call teardown.

Ownership finding: `MessageControlFocus` retains an intent beyond one render,
while `WidgetFocusRequest.FocusElement` creates a target descriptor rather than
a lifecycle owner. Existing lookup supports safe publication; this fix adopts
it rather than weakening protocol validation. A future SDK helper that resolves
retained focus intents against current presentation could reduce repeated
guards (medium complexity; compare Compose FocusRequester lifecycle). Keep it
out of this bounded patch until all request consumers and retirement semantics
are audited; silently dropping arbitrary invalid authored targets would mask bugs.

## Confirmed user decisions

### Visual direction added during implementation

Use the user's `codex-clipboard-2634a6a1-7c44-4a02-8252-5f5a40c649a7.png`
reference as a visual target, not an input specification. Match its integrated
dark shell, restrained dividers, compact avatar-led conversation rows, stronger
selected-row treatment, clear chat header, quiet message grouping, structured
link/media cards, fixed composer and substantial participant-aware call dock.

Keep the agreed Servers/Messages naming and controller mappings. Represent an
ongoing call with a separate truthful status/action and the dock; do not silently
copy the mockup's changing labels, third primary destination, Y/X/LT assignments,
bookmark/video actions or informational badges. L3 still focuses the open chat's
Call/Join/Answer button without activating it.

Use existing theme semantic colors and surface materials rather than hardcoding
the reference's green palette for every theme. Preserve keyboard/controller focus
contrast and high-contrast behavior. No decorative “Live Sync”, invented unread
counts, elapsed times, frame rates or latency. Show presence/activity, latest
message snippets and precise unread counts only if the actual provider exposes
them through bounded metadata. Do not fetch every conversation's history to make
the sidebar look rich. Plain unread state and available titles/avatars are valid
fallbacks; distinguish those data limits from styling limitations.

Include this visual direction in the call dock, conversation list/header,
message embed/media presentation and composer work. Retain virtualization,
reading position, stable item identity and the minimum chat-space checks.

### Rich conversation rows — confirmed follow-up

Use `codex-clipboard-2db14f30-bc90-43fa-9ca8-3e57d5e9a38f.png` for the
conversation sidebar: avatar with presence indicator, name and timestamp on
the first line, one truncated latest-message summary on the second, and a
compact unread/call indicator. Preserve a strong selected surface/accent and
distinct keyboard/controller focus. Use theme semantic colors and accessible
status text; color alone must not carry status.

- Reuse the visible `PresenceStore.getStatus` source already used by
  `web/provider/activity-provider.js`; the existing conversation DTO does not
  expose it yet. Normalize online/idle/do-not-disturb/offline, preserving unknown
  as unknown and invisible as offline. Group conversations have no single-person
  presence: use a group avatar, not an invented aggregate online dot.
- Replace the repeated Direct message/Friend subtitle with a bounded cached
  latest-message snippet when available. Summarize attachments/GIFs and include
  the sender for groups or “You” for own messages. Conceal spoilers. Do not fetch
  every conversation history; unavailable cache data gets an honest fallback.
- Show a compact localized time/date from validated latest-message metadata
  (or a validated Discord message snowflake), with full accessible date text.
- Use real unread state for emphasis/dot. Add a numeric badge only when its
  semantics and source are verified: mention count is not unread message count.
- Show In call only for a known call associated with that conversation; online
  presence or activity alone cannot establish it. Keep this separate from unread.
- Keep provider observation and normalization in the Discord adapter; generic
  focus, virtualization and artwork demand remain framework responsibilities.
  Reuse existing observation/subscription paths rather than per-row polling.
- Cover presence changes, absent stores/cache, deleted/edited latest messages,
  account changes, unread-to-read transitions and selected-row/scroll retention.
  Metadata refresh must not produce a full-widget transition or reorder rows
  independently of Discord's existing conversation ordering.

Source audit: `DiscordWebViewWidget.Conversations.cs:ConversationRow` currently
renders avatar/name and a generic Unread/Group message/Friend/Direct message
subtitle. `Domain/DiscordMessages.cs:DiscordConversation` lacks presence,
snippet and timestamp fields. The activity provider already reads visible
presence, and `web/provider/observations.js` observes PresenceStore. This is
an adapter/domain adoption gap, not a new host presence API. Runtime behavior
of the richer rows remains to be verified during implementation.

The user subsequently chose **left alignment for all messages**, including their
own. Keep author/avatar/“You” identity and restrained own-message styling; remove
the alternating right-aligned bubble layout to preserve usable content width.

The user then replaced chat focus/hover rich previews with **explicit attachment
modals and inline URL actions/cards**. This supersedes the chat-preview interaction
requirements below, not the shared sizing/medialifecycle correctness work:

- Navigating or hovering a message must not open a floating preview.
- Explicitly activating an attachment opens a bounded modal gallery with all of
  that message's supported media. B/Escape/outside dismissal restores the exact
  message and reading position; fullscreen returns to that gallery.
- Render URLs as clickable inline links or a compact in-transcript card with
  source/title/description, matching the new reference. One action per destination;
  no duplicate raw-URL/preview-button action or popover just to open a link.
- Keep inline animated media subject to the established lifecycle policy; opening
  its larger viewer is explicit. Unsupported files keep clear explicit actions.
- Inspect SDK/protocol/host support for multiple inline actions in indexed reading
  rows before authoring. Do not bypass row authority, silently flatten messages
  into provider identities, or recreate nested focus/input dispatch in Discord.
- Preserve spoiler concealment, dialog focus boundaries, attachment-switch focus,
  lazy loading, exact return identity and input precedence. Adapt native fixtures
  to the new interaction rather than treating the old popup tests as acceptance.

- Fix the spurious scroll indicator in a single-link preview.
- Make the active-call dock taller and show participants with avatars, visually
  related to the call page rather than a long empty summary button.
- Keep the primary section and bumper hint named **Servers** regardless of call
  state. Communicate ongoing calls separately.
- L3 focuses the **open conversation's Call / Join / Answer button**, never
  activates it. This was explicitly clarified by the user.
- Improve the gallery toolbar, its arrows/alignment and fullscreen ownership.
- Supply actual video thumbnails where supported; the current play glyph is not
  a failed thumbnail request.
- Fix the GIF sent from Discord that appears as a link. The supplied example is
  `https://klipy.com/gifs/wednesday-193`, titled “Best Wishes for a Wonderful
  Wednesday.” No GIF-sending picker is requested.

## Evidence and present limitations

| Area | Current implementation and assessment |
|---|---|
| Short preview overflow | `DiscordWebViewWidget.Messages.cs:MessageRow` still wraps a single link in `UI.VerticalScroll`; only media-only previews bypass it. `ContentSizedPresentationState` caches the opening measurement. `WidgetScrollEdgeCues.Attach` reserves 4-DIP strips above and below after native template attachment. A late reservation/measurement mismatch is a hypothesis, not yet a reproduced cause. A scroll wrapper alone should not require scrolling when content fits. |
| Call dock | `DiscordWebViewWidget.Navigation.cs:MessageCallDock` currently has a growing summary, count and actions, but no participant avatars. The last two-row compact design failed the native minimum-history-space check; do not repeat it or weaken that check. |
| Header / shortcut | `CallSectionLabel` switches Server/Call; `Views.cs` reuses that label for LB. L3 already opens the picker on the call page, but is free for the requested contextual action in a conversation. |
| Gallery chrome | `MediaPlayerView.Gallery.cs` adds the toolbar to `mediaFrame`. `MediaPlayerView.Fullscreen.cs` explicitly passes it in fullscreen `Chrome`; the native video plane has a separate fullscreen geometry path. This is player UI, not encoded video content. The user reproduced overlap; confirm actual bounds and transition behavior before changing ownership. |
| Video thumbnail | `MessageRow` deliberately renders a Play icon for video attachments. `DiscordMessageAttachment` has no poster field; `messaging-provider.js:attachment` discards proxy/poster metadata. There is no attempted video-thumbnail decode in this path. |
| GIF support | Uploaded `image/gif` attachments use native animated-image rendering. Recognized `gifv` embeds use passive video animation when a permitted direct video URL exists. Offscreen/hidden/reduced-motion policy and larger preview playback exist. This is not a general GIF-page resolver. The adapter and C# validator admit Discord media origins and the exact Tenor media origin, not direct Klipy media. The screenshot establishes the Klipy page URL, but not the raw embed media URL/type. |

The public Klipy page could not be retrieved by the web tool during planning.
Inspect the actual resident Discord embed, with only sanitized media metadata,
before claiming which field or origin was rejected. No token access, contact
ringing, message sending or GIF posting is needed for this investigation.

## Initial implementation investigation

The first native short-preview reproduction failed with exactly **8 DIPs** of
unnecessary scroll (`artifacts/discord-call-media-polish/short-repro-1/result.json`):
one 40-DIP link, 48-DIP allocated scroll body and only a 32-DIP content viewport.
That matches the two 4-DIP native edge-cue reservations attaching after the cached
initial fit measurement. The regression fixture covers 1/2/12 links, compact/wide
surfaces and three text scales. Correct initial decoration/measurement ordering
before considering any widget-only wrapper removal.

Source review found that `NativeArtworkStream.OpenAsync` currently checks HTTPS
and encoded byte bounds but delegates redirect handling to the native HTTP
filter; it does not explicitly enforce the goal's redirect/destination policy.
`MediaPlayerView.LoadAsync` also hands remote video URIs to the native media
source, so hardening only the artwork HTTP path would leave video animations
outside the same transport guarantees. These are source findings, not an
exploitation test. Inspect and qualify a shared transport/validation boundary
before expanding the Klipy media allowlist. Keep existing local Capture/Playnite
file custody and browser navigation separate from remote-media policy.

## Implementation sequence

### 1. Reproduce and correct short-preview sizing — medium

- Add a native fixture matching the production link preview, its stylesheet,
  controller footer and text scaling. Exercise one link, two short links and
  genuinely overflowing content at compact/wide sizes and 100/125/150% text.
- Record desired height, allocated body height, content extent, actual viewport
  and edge-cue reservation before/after template attachment and preview entry.
- If host decoration appears after the cached measurement, correct that initial
  presentation/layout lifecycle in the host. Do not add a Discord-specific pixel
  allowance, continuously resize on every update, or hide genuine overflow cues.
- Use an ordinary action as the root for a lone bounded link where appropriate,
  but do not treat removing its wrapper as proof that shared sizing is correct.
  Long text and multiple-action previews must remain usable when clamped.
- Check previews and content-sized dialogs, since both use the measurement helper.
  Preserve stable reading geometry after asynchronous content changes.

### 2. Stable Servers navigation and contextual L3 — low to medium

- Use **Servers** for the tab and LB guide in all states. Do not rename routes or
  change the existing destination behavior as part of a label fix.
- Add a small distinct call-status indicator beside the navigation and retain the
  active-call dock. Its accessible description includes the state; color alone
  is insufficient. Connection/reconnection states remain distinguishable.
- In an open conversation, publish L3 as **Focus call** and show the matching
  controller glyph beside Call/Join/Answer. Use the existing exact-focus request
  API; do not add widget-managed focus counters.
- L3 must only focus the enabled current button. No microphone prompt, ringing,
  answering or current-call replacement occurs until explicit activation.
- On the Servers call page, retain the existing L3 **Servers & channels** picker
  shortcut. The same button is contextual; its visible hint must agree with the
  active route. Omit the chat shortcut when no eligible call control exists.
- Modal, keyboard, selection and entered-preview ownership must still take
  precedence over section/chat shortcuts.

### 3. Participant call dock — medium

- Target a comfortable approximately 64–72-DIP expanded dock, subject to actual
  measurement. Replace the broad empty summary with a concise call title/status
  and a bounded row of participant avatar/name chips; keep Mute, Deafen, Share
  and Leave grouped at the right.
- Reuse the call page's avatar, speaking, muted/deafened and “You” semantics.
  Render only a few visible participants and a `+N` overflow affordance that
  opens the full call. Do not duplicate the participant audio menus unnecessarily.
- Keep the call summary clickable to return to the call. Participant previews
  may reuse the existing participant-audio contract if the layout remains clear
  and the native input checks pass; no fake clickable avatars.
- Compact layouts use fewer avatars, icon actions and a bounded height increase.
  Preserve usable message history, composer visibility and text scaling. Do not
  silently raise the minimum widget size or use fixed empty columns.
- Add no dock when disconnected. Participant/count/state changes must not move
  focus or rebuild the chat section.

### 4. Gallery viewer structure and toolbar — medium to high

- Treat collection navigation as **gallery-viewer chrome**, separate from the
  selected image/video canvas, decoder and playback toolbar. Keep one shared
  selection/lifecycle authority; do not copy this into Discord or add a second
  media session for the toolbar.
- Inline design: equal, compact themed chevron buttons with reliable hit targets;
  an aligned attachment count; a shortened filename/type on a secondary line
  only when useful. Avoid a long filename competing with arrows in one oversized
  floating capsule. Full names remain available accessibly/on hover.
- Left/Right traverses the navigation row; Down enters media controls; Up returns
  to gallery navigation. LB/RB changes attachments without changing focus owner,
  including boundary, loading, failure and rapid-reversal cases.
- Fullscreen has no persistent filename ribbon over the video. Use fullscreen's
  own compact previous/count/next chrome and visibility policy, laid out outside
  the fitted media rectangle when shown. Reserve/coordinate that geometry through
  the fullscreen host; moving a XAML row alone does not constrain the native
  video plane. Do not bake the toolbar into the video surface or captured pixels.
- Reassess whether `MediaPlayerView` should delegate gallery chrome to a small
  shared viewer component. Prefer a bounded extraction of chrome/placement over
  rewriting source resolution, authority, media ownership or fullscreen playback.
- Preserve the recently fixed stable-focus parking and intentional outside-focus
  dismissal. Verify actual image→video→image fullscreen transitions and resource
  limits, not just source declarations.

### 5. Video posters and attachment tiles — medium, higher if host extraction is needed

- Inspect Discord's actual attachment and rendered-video metadata for a supplied
  poster/thumbnail or supported proxy image representation. Do not guess a
  `.jpg` URL or assume a video proxy URL is an image.
- Normalize verified poster metadata in the Discord adapter/domain and render it
  through the existing shared artwork service. Show a Play overlay and concise
  type/duration where provided; retain explicit attachment counts and bounded
  visible tiles.
- Preserve signed query parameters, exact attachment identity, spoiler privacy,
  expiry handling and aspect ratio. Prefer supplied small images over decoding a
  video solely to paint each message row.
- If the provider supplies no usable poster, assess a shared bounded first-frame
  thumbnail facility. Capture Library's saved-file thumbnail capability is not a
  general remote-video API and must not be repurposed with its custody checks
  bypassed. Any new facility belongs to the host/SDK, with caching, cancellation,
  byte/decode/concurrency limits and demand only for visible rows.
- Retain a deliberate file/video tile when no poster can safely be obtained.
  Clearly distinguish that fallback from a broken or still-loading image.

### 6. Klipy and real Discord GIF normalization — medium

- Inspect the failing message's REST/resident-store embed fields and updates:
  type, proxy URL, direct media URL, thumbnail, MIME/container and load timing.
  Use the actual current client as evidence and existing open-source clients
  where they implement the same provider behavior.
- Fix the normalization and corresponding C# validation together. Prefer Discord
  proxy media. Add only verified required provider media origins, retaining
  redirect/size/credential restrictions. A public provider page URL is not itself
  an image/video source.
- Cover uploaded GIFs, Discord GIF-picker embeds (including the reported Klipy
  shape), existing Tenor `gifv`, GIF image embeds and delayed embed enrichment.
  Distinguish genuine direct media from a provider webpage with no usable embed.
- Reuse native animated images / `UI.Animation` and the shared media gallery.
  Preserve reduced motion, hidden/offscreen suspension and bounded decoder slots.
  Suppress redundant external-open actions only after usable media is established.
- Improve the fallback when media metadata is unavailable; do not silently claim
  all pasted GIF links animate or fetch/scrape arbitrary pages behind the scenes.

## Shared-consumer applicability

| Change | Consumers / required check |
|---|---|
| Content-sized measurement or edge-cue lifecycle | Discord, PoB previews, Capture Library previews, Game Help/SDK preview examples and anchored dialogs. Verify actual changed entry points; no widget-specific sizing patches. |
| Gallery/fullscreen presentation | Discord is the gallery consumer; Capture Library, Game Help and SDK Gallery share the media player/fullscreen path and need regression coverage. Browser-hosted Discord stream pins are a separate WebView path. |
| Remote video poster capability, if needed | Check generic media/artwork APIs, Capture Library and other URL media consumers. Add an opt-in shared capability only after the provider-metadata path is ruled out. |
| GIF normalization / domain allowlist | Discord-specific; generic animation rendering/lifecycle stays in the host. No unrelated global origin expansion. |
| Servers labels, call dock, chat L3 | Discord authoring using existing SDK navigation, hints, focus and participant semantics. Preserve other widgets and contextual input owners. |

Initial measurement applicability audit (source inspection November 18): the
only shared measurement entry points are `WidgetViewPresenter.Previews.cs` and
`WidgetModalLayer.xaml.cs`. Both adopt preparation automatically; no consumer
changes or author-managed measurement counters are needed.

| Consumer | Assessment for initial measurement fix |
|---|---|
| Discord message and voice-device previews | Affected; native short-content and gallery cases pass. Chat is still scheduled to migrate to explicit modals. |
| PoB candidate, passive/row, gem-effects, skill and workspace previews | Affected through shared content-sized path; no widget patch. Dedicated PoB visual regression remains pending. |
| Capture Library gallery/actions previews | Affected through shared path; gallery regression passes, dedicated Capture visual regression pending. |
| Marketplace browse previews | Affected through shared path; requires no migration. Native fixture models same measurement mechanics; dedicated Catalogue run pending. |
| Playnite Library filters | Affected anchored content-sized dialog; anchored native suite passes. No changes to Playnite needed. |
| Game Help, SDK Gallery and templates | No explicit content-sized opt-in found in source search; not affected by this measurement call. Their shared media/fullscreen changes still need separate regression checks. |
| SDK documentation examples | Already use the supported content-sized options; no manual size workaround to remove. |

The existing state-cache tests also pass (3 checks in `sizing-state-tests.log`):
late updates do not resize a reader's panel, while geometry/text-scale/reopening
still invalidate correctly. This complements rather than replaces native evidence.

## Verification and delivery

### Compact message-layout investigation (November 18)

Native minimum-size runs exposed a reproducible `LayoutCycleException` while
revealing the newest message or entering its attachment action. The debugger
trace in `artifacts/discord-call-media-polish/message-visual-compact-8/native-layout-2.log`
and bounded row trace show the preceding inline-URL row alternating between
83.2 and 84.8 DIPs. `WidgetRichTextView.cs` constrained a link from the paragraph's
arranged `ActualWidth`, feeding a layout result back into its input. The host
now derives the link budget during measurement from the available parent width.
This is a reproduced host defect, not widget-owned focus plumbing. The first
corrected native run (`message-visual-compact-9`) reached gallery switching
without crashing. The narrowed final source is built in `message-native-24`.

The compact modal-return failure remains real and unresolved. Reacquiring the
current collection and scroller did not fix it: `message-gallery-roundtrip-9`
confirmed both native instances were unchanged. The earlier retired-instance
explanation was a hypothesis, disproved by those identity checks. The test now
compares viewport-relative message position; total scroll offsets can legitimately
change when virtualization refines unrelated row estimates.

`message-gallery-trace-1/reading-layout.jsonl` captures a native `ViewChanging`
after the return checkpoint had already reported ready: offset 5678.4 moves to
approximately 5640, shifting the message from Y=-34.4 to Y=4. The saved checkpoint
itself correctly contained leading position -38.4. This also happened while
`BringIntoViewOnFocusChange` was false. The visible before/after captures confirm
the attachment action becomes clipped on Back; this is not merely a numeric test
failure. A same-offset `ChangeView` probe returned false and did not cancel it.
Do not infer which native/request path queued that change from this event alone.

Source inspection also found an ownership risk in
`WidgetIndexedCollectionView.Navigation.cs:FocusedIndex`: WinUI's container index
alone does not establish that an item belongs to that list. A retained/retired or
sibling list could claim another list's focus. The resolver now verifies actual
visual ancestry before using the native index. No author-owned focus counters or
new protocol are needed. This guard's applicability is inferred from source;
the specific stale-list failure is not established as the return jump's cause.
Removed all unsuccessful scope-checkpoint, clamping, passive-scroll, redundant
reveal, focus-arrival and native-focus-callback experiments; existing return
mechanics remain. Preserve their failure artifacts, but do not repeat them as
if untried. Next investigation should identify the actual late native scroll
request and its completion boundary, before adding another ownership mechanism.

| Consumer | Inline-width / modal-return applicability |
|---|---|
| Discord Messages | Reproduced inline-width cycle fixed; compact modal return remains unresolved. Focus lookup ancestry guard adopted. |
| Game Help citations | Uses focusable inline links; shared measurement fix applies, native regression pending. |
| SDK Gallery Text example | Uses inline links and citations; shared fix applies, equivalent native long-link/resize fixture added. |
| PoB and Playnite formatted text | Rich text without inline-link buttons in inspected entry points; link budgeting is not applicable. |
| Capture Library | Uses collection return for gallery; existing return rules retained. Shared native return suite passes 51 checks; dedicated widget visual regression pending. |
| Spotify / FullApplication sample | Use collection return for route changes; existing identity, query and cancellation checks retained. Shared native return suite passes; dedicated consumer regression pending. |
| Other first-party widgets and templates | No direct rich-text inline-link or collection-return entry points found in applicability search; no widget migration. |

The design pass uses the installed Impeccable skill and repository native rules.
Screenshots of compact Servers, conversations, chat actions and attachment modal
were reviewed in `message-visual-compact-9`: dock avatars are round and correctly
sized; the minimum conversation viewport is still cramped; header density and
gallery error whitespace need refinement. Synthetic unavailable media verifies
failure/navigation only, not successful image decoding. No visual acceptance is
claimed from these captures.

`qualified-boundary-rich-text-1/result.json` passes 82 native checks for long-link bounds,
repeated resizing, Light/Dark, 1×/1.5× text and one controller activation.
The first fixture run used unsupported dotted ID selectors and failed its padding
assertion; it was corrected to role selectors, preserving the bounds assertion.
This is native layout evidence; full live Discord and high-contrast coverage
remain pending.

Clean source boundary: `message-native-24` builds successfully and passes 165
native checks: `qualified-boundary-rich-text-1` (82),
`qualified-boundary-collection-return-1` (51),
`qualified-boundary-reading-group-1` (19), and `qm24/expanded` (13). The expanded
Discord scenario verifies gallery input and exact message-position return.
Its first staging attempt under `qualified-boundary-messages-expanded-1` failed
worker admission before UI testing; the same candidate/payload passed from the
shorter `qm24` fixture path. Path sensitivity is observed, not yet diagnosed as
a particular Windows path-limit defect. No production workaround was added.

The final expanded Servers/conversations/chat screenshots were reviewed in
`qm24/expanded`: conversation rows show useful snippets/presence/time and the call
dock retains participant avatars. Remaining design issues include oversized
stream rows, repeated self-label text, cramped compact conversation space, and
uncertain avatar/text rasterization in RenderTargetBitmap captures. Live native
capture is still required before visual acceptance. There are no new shipping
return-scroll state machines, timers or callbacks from the rejected experiments.
The bounded `--trace-reading-layout` fixture diagnostics remain available for
the unresolved compact bug; remove diagnostic-only scaffolding before final
delivery. The goal is active, not complete. The user's existing PID 25940
candidate has not been replaced by these protocol-132 fixtures.

### Compact modal return resolved (November 18, native return boundary)

The earlier missing-`BringIntoViewRequested` observation did not identify the
request's origin. Native CDB traces now do: `ret27/native.log` shows the public
`ListViewBase.ScrollIntoView` request being applied by
`ModernCollectionBasePanel::ArrangeOverride / SetScrollViewerOffsetsTo`.
With that redundant request omitted, `ret29/native.log` still records the late
`ScrollViewer::MakeVisible` call from its native `OnBringIntoViewRequested`
handler, dispatched by `CXcpDispatcher::MessageTimerCallback`. A subscriber on
the ScrollViewer observes the request after the native handler, or misses it
because it was already handled. Subsequent `NotifyBringIntoViewportNeeded`
calls synchronize the native manipulation transform; they are not evidence of
another widget request. The source-only and scroller-level suppression
hypotheses above are superseded by this native evidence.

The host now captures collection return memory when a modal changes input scope
even if its backdrop retains the collection (`WidgetViewPresenter.CollectionReturns.cs`).
An oversized row retains its saved intersecting slice rather than being clamped
to its top (`WidgetIndexedCollectionView.Memory.cs`). For a matching saved
query/layout/axis checkpoint, exact entry leaves viewport restoration in charge
of reveal. Realized rows avoid a redundant panel `ScrollIntoView`; unrealized
rows still use native bounded realization. `WidgetIndexedCollectionView.ReturnFocus.cs`
intercepts the returning row's one deferred focus reveal before it bubbles to
the ScrollViewer's native handler. The handler detaches on consumption, focus
departure, unload, or canceled navigation; it has no timers, polling, retries,
widget state, or new protocol. Already-focused rows install no handler. Missing
checkpoints and rebased queries retain ordinary native reveal. Complexity is
small and host-owned, using the existing exact-entry and viewport-memory
lifecycle rather than adding author-managed scroll/focus counters.

Final candidate `return-native-4` builds with retained unique binlog
`return-native-4-20261119-004329--37740--OpG_7R.binlog`.
Native validation passes 101 checks:
`ret32/minimum` (15), `ret32/expanded` (15), `return-suite-4` (52), and
`reading-return-4` (19). The compact message is exactly Y=-34.400390625 before
and after Back. Both Discord sizes also cover a second modal round trip and
ordinary Up/Down navigation afterward. The shared return suite covers delayed
rows, cancellation, teardown/resume, identity/query replacement, horizontal
return, wheel ownership, and the added missing-checkpoint fallback. Captured
compact/expanded return screenshots were inspected; the attachment remains
visible. Synthetic unavailable media still does not establish successful
decoding or live Discord behavior. No unrelated live candidate was stopped;
PID 25940 remains running.

| Consumer / actual entry point | Return-fix applicability and validation |
|---|---|
| Discord `DiscordWebViewWidget.Views.cs` | Affected retained modal backdrop; shared host fix adopted without widget changes. Both sizes, repeated return and subsequent navigation pass natively. |
| Capture Library `CaptureLibraryWidget.cs` / `Gallery.cs` | Already uses matching collection-return contract. Shared native return suite passes; dedicated Capture gallery visual run deferred to the media-validation pass. |
| Spotify `SpotifyWidget.cs`; FullApplication reference widget | Already use navigator return memory. Shared native lifecycle/identity/horizontal suite passes; dedicated service/widget visual runs deferred. |
| YouTube Music `Application/Program.cs` → `StandaloneMusicWidget.cs` | Shipping entry point uses `ReturnToCollection`, not just a legacy fixture. Shared fix applies; dedicated music native navigation run deferred. |
| Settings `SettingsWidget.InstalledList.cs` | Already uses exact return when restoring the installed list. Shared suite covers checkpoint-present/absent and query replacement; dedicated installed-list visual run deferred. |
| PoB `PobWidget.UnifiedSearch.cs` / `PobWidget.BuildComparison.cs` | Already use return requests for modal search and comparison rows. Shared host fix applies; dedicated PoB visual run deferred. |
| SDK Gallery, templates, remaining shipping widgets | No additional direct return-contract calls or equivalent modal-return plumbing found in the bounded applicability search; no migration required. Ordinary reading actions retain 19 passing native checks. |

Transport audit checkpoint (source evidence, not an exploit reproduction):
`Presentation/NativeArtworkStream.cs:OpenAsync` bounds encoded image bytes but
only checks the initial HTTPS scheme; its WinRT HTTP filter does not explicitly
enforce redirect destinations, cookie isolation or connected public addresses.
`Media/MediaPlayerView.cs` uses `MediaSource.CreateFromUri` for remote video,
so the widget's metadata allowlist cannot enforce those transport guarantees.
`WidgetBridge/MediaPlayerSourceResolver.cs` passes web sources through after
declaration validation. This is a host transport gap, not something to solve by
scraping or adding provider-side URL checks alone. Assess a bounded shared
transport with explicit public-media policy and retained authority; preserve
existing local/capture media semantics. The loopback provider's explicit
`SocketsHttpHandler.ConnectCallback` is an internal lifecycle/connection example,
not a public-network policy that can be copied unchanged. Native playback,
seeking, cancellation and expiry still require evidence before replacing the
working video path.

The goal also requires shared media-security acceptance: exact verified HTTPS
media origins, no embedded credentials/unusual ports/private destinations,
redirect revalidation, no forwarded Discord credentials/cookies/auth headers,
no signed-URL logging, actual format checks and bounded encoded bytes, dimensions,
frames, decode work, concurrency and timeouts. Resolve provider media from
Discord metadata, not arbitrary page scraping/execution. Inspect the native
video transport as well as image downloads; a widget allowlist alone does not
establish that redirects or decoders enforce these protections. Invalid media
must fail safely without an automatic external-open fallback.

1. Add reproduction cases before changing shared behavior; retain failed evidence.
2. Run proportional managed and raw/minified host-wrapper adapter tests using
   realistic provider payloads, including delayed embed and poster metadata.
3. Native fixtures: short/overflowing previews, both message layouts, enlarged
   text, L3 focus without dispatch, dock geometry, changing participants,
   gallery arrows/LB/RB, slow/rejected media and fullscreen plane/chrome bounds.
4. Check light/dark/high-contrast styles, real decoded animation, thumbnail
   resource bounds and intentional focus departure. Do not infer runtime success
   from source audits or mocked provider data alone.
5. Build a matched Release candidate and update the isolated widget catalog using
   the reviewed update flow with the default settings root. Do not merge, push,
   publish or create installers. Restart the candidate only during implementation
   under the existing user authorization; leave other applications alone.
6. Verify the reported live Klipy rendering and Call-button focus. Calls in Test
   server and synthetic test messages to the verified Sanky recipient are now
   authorized; do not infer recipient identity from the shared display name.
   Do not broadcast unrelated content or grant microphone access. Ask for a
   designated user action only where live acceptance requires it.

Reject or narrow any proposed shared abstraction that weakens authority, adds
hidden coupling or makes consumer behavior more brittle. Document the evidence
and keep the working safeguards rather than completing a refactor at any cost.

### Provider media normalization checkpoint (November 18)

`web/provider/messaging-provider.js:webUrl` and
`Domain/DiscordMessages.cs:EmbedMediaUrl` rejected Discord's exact
`images-ext-1.discordapp.net` and `images-ext-2.discordapp.net` external-media
proxies. A `gifv` or supplied thumbnail on those hosts therefore became missing
media despite valid metadata. This provider-owned admission gap is reproduced
with a synthetic Discord message shape, not yet with the user's actual Klipy
message. Both validators now admit these two exact HTTPS origins for **embed
media only**. Attachments and avatars retain their existing CDN admission;
provider webpages, unverified direct Klipy media, other numbered hosts, host
suffix lookalikes, credentials, unusual ports and fragments remain rejected.
Existing `proxy_url`/resident `proxyURL` preference and signed query retention
are preserved. Delayed enrichment/removal uses the existing watched-message
patch stream with no page scraping or additional HTTP requests.

Evidence: `artifacts/discord-call-media-polish/gif-metadata-1` passes 239 raw
provider/adapter/backend tests and 246 minified/composed tests. The sealed backend
is 102,515 bytes. Matching C# assertions are added to
`MessagingTests.Presentation.cs`; their execution must be recorded after the
coordinated managed build. This provider correction does not secure redirects
or native decoding; those remain the separate shared-transport gate.

Sources inspected:

- [Discord Message resource](https://docs.discord.com/developers/resources/message#embed-object)
  documents `gifv`, video/image proxy fields, and image animation flags. Its
  [attachment schema](https://docs.discord.com/developers/resources/message#attachment-object)
  exposes the proxied original media and a ThumbHash placeholder, not a separate
  video-poster image URL.
- [Serein's pinned GIF/media admission](https://github.com/ViceVerse-cz/Serein/blob/a89da93a8d77577f3b22249c7aebe0e13b5c9c09/crates/model/src/gifs.rs)
  establishes an existing-client precedent for the two exact external proxy
  hosts. [Vencord's pinned GIF-provider switcher](https://github.com/Vendicated/Vencord/blob/e75ac9ace41f6212cfaa2d5f3670830be392da4e/src/plugins/gifProviderSwitcher/index.tsx)
  confirms Discord's Klipy-backed picker, but does not establish the failing
  message's returned media fields.
- [Yapfast's Discord notes](https://github.com/yapfast/yapfast/blob/386b396193b0c1d11e1a8045a0c959330adc8f23/docs/discord.md)
  describe an unofficial `proxy_url` image-format request for a video's first
  frame. Do not adopt that transformation as verified poster metadata without
  checking the service response and decoded pixels. Existing supplied embed
  thumbnails now benefit from the corrected proxy admission.

Remaining evidence: inspect only sanitized type/field names, origins, extensions,
dimensions, MIME and animation flags from the actual Klipy message. The normal
`messageHistory` result has already discarded rejected raw media addresses; no
established raw-metadata diagnostic endpoint was found. A page URL or another
client's allowlist alone does not justify broadening direct-provider origins.
The current spoiler implementation also relies on the legacy `SPOILER_`
filename while the official attachment schema has `IS_SPOILER`; preserve that
flag through provider/domain/consumer before claiming complete spoiler coverage.

### Updated design guidance (November 18)

Read the installed Impeccable skill and the updated root `AGENTS.md`. Apply its
Operate-mode principles to the existing native WidgetRail design: restrained
semantic color, clear selected/focused states, consistent typography, useful
content density and short paths to frequent actions. This is refinement of the
accepted direction, not permission to introduce a different visual identity.
The skill's context loader does not recognize this C#/WRSS surface and found no
PRODUCT.md/DESIGN.md; the existing implementation, accepted references and this
plan remain the source of product context. Do not infer absent native features
from that detection result or introduce browser styling workarounds.

Before each UI edit, read the skill's craft floor and inspect current rendered
screens. After each refinement, capture and critique the actual native main
screens at the applicable sizes; retain the controller/navigation checks and
consumer matrix required above. Web-only screenshots or static source checks
cannot qualify WinUI focus, clipping, media planes or controller behavior.

Reviewed the existing `qm24/expanded/chat.png` and
`ret32/minimum/conversations.png` as incumbent evidence, not new acceptance.
At the compact size, the combined section/header/search/dock leaves roughly one
complete conversation row visible. Recover useful list area in the next design
pass while preserving direct call controls and legible presence/snippets. Check
the actual app capture before attributing the jagged text in these fixture
renders to shipping typography. No visual layout was changed during the native
transport qualification step.

### Checked media stream: native adapter checkpoint (November 18)

The prior bounded transport stage has 92 passing tests and the retained
`public-media-range-network.json` probe reads the head/tail of a 4,372,373-byte
public MP4 with 112,533 bytes transferred. Do not rerun these unchanged tests
merely to recreate their counts.

Added the opt-in `WidgetStylesValidationPage.PublicMediaStream.cs` fixture over
`Media/NativePublicMediaStream.cs`. `secure-media-native-3` builds successfully
with binlog `secure-media-native-3-20261119-011454--12380--B_QmFK.binlog`.
`public-media-native-2/result.json` passes 10 native checks: actual MP4 signature,
independent clone positions, tail/EOF reads, native open metadata (854×480,
52.209 seconds), playback-clock advancement, forward seek to about 37 seconds,
backward seek and reader teardown/rejected admission after retirement. Only the
checked WinRT stream reaches `MediaSource.CreateFromStream`; no remote URI is
passed to the native player. This fixture is excluded from shipping builds and
does not touch Discord, capture sources or microphone grants.

The earlier `public-media-native-1` result is retained but its frame counter is
not valid decoding evidence: frame-server callbacks repeat until consumed and
reported an implausibly large count. Removed that instrumentation rather than
weakening an assertion. The replacement uses an ordinary `MediaPlayerElement`,
disables system-media-command ownership and offers paused visual checkpoints at
12 and 35 seconds. Computer Use correctly reports those checkpoints through
accessibility but twice captured an unrelated terminal window, including after
explicit target activation. No input was sent to that unrelated window. Visible
decoded pixels are therefore **not verified** by this run; neither the passing
clock checks nor the incorrect captures establish visual decoding correctness.

The shipping image/video renderer still uses its existing network paths. Next:
obtain reliable pixel evidence, qualify supported containers and decode bounds,
then integrate the checked transport with source authority, expiry, cancellation
and player retirement. Preserve local/package/capture semantics and run the
consumer matrix before claiming shared integration. Native seek qualification
for this public MP4 is useful progress, not acceptance of all media formats or
the completed security/UI goal. The user's existing candidate was not replaced.

### Protocol-133 public media integration (November 18)

Implemented an additive host/SDK contract: `MediaPlayerSource.PublicFile`,
`MediaPlayerSource.PublicImage`, `UI.PublicImage` and `UI.PublicAnimation`.
`MediaPlayerSource.IsPublicMedia` and `ViewNode.ImageIsPublicMedia` preserve the
policy through JSON, galleries, lazy rows, pinned projections and incremental
resource updates. The existing SDK already advertises its current protocol for
deferred collections (`Widget.cs`, `HasIndexed`); no widget-owned feature counter
or eager row enumeration was added. Older hosts must reject protocol 133 rather
than silently fall back to an unchecked transport. The generated native contract
and SDK API baseline were updated; 224 contract constants pass parity validation.

The earlier source-only transport finding is now addressed for opted-in media:
`NativeArtworkStream.OpenAsync` uses the checked client for public images and
`NativePublicMediaSource.OpenAsync` feeds `MediaSource.CreateFromStream` through
`NativePublicMediaStream`. The source origin is locked across redirects;
container signatures, native video/audio metadata and existing image/GIF bounds
are checked. Native errors expose fixed failure text. The bridge retains the
exact declared policy: a request cannot downgrade it while keeping the same URL.
`MediaPlayerView.AttachPublicSourceAsync` owns acquisition until transfer to the
player, disposes late/rejected results and includes acquisition cleanup in
`PlaybackRetirement`. Playback controls retain their existing unavailable state
until checked acquisition finishes; no new author-managed readiness state exists.
This is a medium-complexity shared extension; Discord supplies domain-approved
URLs and intent, not HTTP or decoder plumbing. Local/capture authorities and the
legacy adaptive/native-URL path remain separate.

A proposed 32-waiter decode cap was **rejected before delivery**. Source inspection
of `NativeArtworkDemand.LoadAsync` showed that a normal image burst hitting that
cap would be cached as failed artwork rather than retried. The host now limits
active acquisition/decode to four and uses the already bounded, cancelable live
presentation demands for waiting work, before allocating media bytes. A queued
demand does not spend its decode timeout while another image is working. Tests
cover 80-request bursts, four active operations, cancellation of 64 waiting
demands, subsequent admission and idempotent lease release. This retains useful
behavior without adding retry timers, widget flags or another scheduler.

Microsoft's [MPEG-4 source documentation](https://learn.microsoft.com/en-us/windows/win32/medfound/mpeg-4-file-source#limitations)
states that external tracks are unsupported and media belongs in the container.
This supports using the native byte-stream source instead of adding an unproven
MP4 parser to widgets. It is platform documentation, not an exploit reproduction
or proof that every installed codec is bug-free. Adaptive playlists and web
documents are rejected by the public-file signature path.

| Consumer / actual entry point | Applicability and current evidence |
|---|---|
| Discord `Avatar`, `MessageRichText`, attachment GIFs/stickers and `AddMessageExtras` | Migrated to public images/animation, including poster policy. Domain host allowlists remain provider-owned. Managed row assertions verify policy survival. |
| Discord `MessageMediaItems` / explicit gallery modal | Migrated all supported attachment, embed and sticker sources. Same-origin checked transport; gallery source authority stays exact. |
| Discord live incoming stream, voice and outgoing capture | Not applicable: browser-session/host-capture media have different authority and pipelines; unchanged. |
| Capture Library / Game Help capture preview | Local/custodied sources unchanged. Shared native image/video gallery and animation regression suites pass on the matching host. |
| SDK Gallery remote URL field and packaged examples | Legacy native URL path intentionally retained for arbitrary supported streams/playlists; package media unchanged. Public API examples added to media reference. |
| Spotify / shipping YouTube Music / YouTube artwork | Legacy APIs retained. Their redirect/cache behavior has not been qualified for same-origin opt-in; no silent transport migration. |
| Playnite achievement/progress HTTPS icons; Settings/Marketplace screenshots | Legacy image behavior retained; opaque local artwork unaffected. Strict-source migration deferred until actual redirect/provider contracts are qualified. |
| Remaining first-party widgets, PoB, SDK examples and templates | Bounded applicability search found no further public-player/animation entry points requiring migration. Existing local/opaque/browser media remain under their own contracts. |

Discord normalization now also carries documented attachment `IS_SPOILER` into
`DiscordMessageAttachment.HasSpoiler`; explicit metadata and the legacy filename
prefix both suppress inline acquisition and gallery actions before reveal.
The exact images-ext-1/2 proxy-origin fix above remains provider-specific.
Direct Klipy origins, image-animation flags and attachment poster transforms are
not claimed complete without the outstanding provider evidence.

Verification at this boundary:

- SDK: 200/200 tests; public API compatibility: 17/17; media bridge: 3/3,
  including both pipes and transport-policy downgrade rejection.
- Checked transport and the actual host admission helper: 96/96 tests.
- Discord managed harness: 975 checks. Raw provider/adapter/backend: 240 tests;
  minified equivalents: 240; two budget checks and seven exact composed-backend
  checks also pass. Fresh composed backend is 102,630 bytes, adapter 46.
- `secure-media-native-7` builds against the regenerated platform DLL with binlog
  `secure-media-native-7-20261119-020512--23916--8CZqQp.binlog`.
  `public-media-integrated-native-final/result.json` passes 17 checks over real
  HTTPS image and MP4 bytes: native metadata, playback clock, seeks, shared-viewer
  admission, queued-close cancellation, authority withdrawal and cleanup.
- On the same host, `public-media-legacy-gallery-final/result.json` passes 20
  checks, including native mouse activation, focus, loading replacement and
  image/video fullscreen transitions; `public-media-legacy-animation-final`
  passes 12 visibility/reduced-motion/teardown checks. Both end with no retained
  player/animation slots. The gallery pointer trace reports the intended window
  as both hit-test root and foreground owner.

Desktop pixel evidence remains limited: the sky capture returned an unrelated
terminal; WinApp's target-window capture produced black pixels and its screen
capture showed the unrelated foreground content. Retained artifacts are in
`public-media-integrated-native-1`. These images do not prove video rendering or
UI appearance. No cause is asserted from these captures. The passing native
navigation and lifecycle assertions do not replace that missing visual evidence.

Remaining goal work is unchanged: finish the live Klipy/poster evidence, complete
the compact/wide UI refinements and screenshot critique, verify remaining native
flows/theme/text-scale cases, remove temporary diagnostics, then build and test
a matched trimmed Release widget/host candidate through the isolated update
path. The running user candidate was not replaced, and nothing was merged,
pushed, released or installed. The goal remains active.

### Compact conversation and call layout pass (November 18)

Reviewed native Servers, conversations, chat and attachment-modal captures after
each refinement. All production changes in this pass are Discord presentation
policy/styles; no shared host/SDK navigation or media mechanism was changed.
The existing responsive-visibility and themed-row APIs were sufficient.

- Hide the redundant conversation heading at compact width. Flatten conversation
  rows, reduce vertical padding/margins and retain an invariant full selected
  border instead of changing one edge's width. At ordinary text scale the compact
  capture now shows two full conversation rows, versus roughly one previously.
  Avatars, snippets, time, presence and call/unread indications remain.
- Reduce stream-card padding and the watch action's minimum height to 48 DIPs.
  Both stream rows now fit in the expanded call-screen capture. Keep participant
  avatars and the 64-DIP call dock; no extra focus stops or removed call actions.
- Suppress the secondary “You” only when the participant's displayed name is
  already “You.” Real account names still retain the self label.
- Move the existing call count/status beside Participants, retaining its ID and
  behavior. This removes the clipped count under the compact channel title.
- A 150% text capture showed the compact Messages tab being cut off. Hide the
  decorative header logo only at compact width; the expanded header and rail
  retain the logo. The resulting native capture shows both full tab labels.
  Large channel names still use deliberate ellipsis at the minimum size rather
  than overriding the user's text scale or increasing the minimum widget size.

Evidence uses protocol-133 `secure-media-native-7`, matched `ui-install` Bridge,
and `compact-ui-payload-1` (directory `compact-ui-payload`) through payload-4.
`artifacts/u1` and `u2` contain the first two before/after layout passes.
Payload-3 passed 204 native assertions across both sizes: `u4` high contrast
(30), `u5` synthetic private calls (36), `u6` directional entry (78), `u7`
Mineral Light at 150% text (30), and `u8` ordinary dark (30). The managed Discord
harness passed 975 checks. After the final compact-logo adjustment, payload-4
passed `u9/minimum` light/150% media flow (15) and `u10/minimum` directional
entry (39); expanded logo behavior is unchanged.

The private-call run verifies the visible L3 hint, focusing Call without starting
it, outgoing call, Leave, incoming notification, explicit Answer and withdrawal
using only the synthetic provider. Directional checks verify selected-conversation
entry, composer entry and current history position rather than stale row memory.
Media runs verify explicit gallery ownership, RB and repeated exact reading
position return. No live Discord calls/messages or user settings were changed.

Visual critique remains qualified. `OverlayShellPage.xaml` places `WidgetSurface`
(the host material) beside `WidgetHost`; the fixture's `SaveImage(surface, ...)`
captures only the presenter subtree. This explains why those images omit the
material behind transparent text, especially in light/high-contrast themes.
Some icon captures also show semantic fallbacks; asset readiness must be checked
before treating those frames as settled icon styling. Do not approve contrast,
SVG stability or video pixels from these captures. The next visual-test change
should capture the widget together with its actual host backdrop (using an
isolated solid-material scenario where appropriate), not patch production colors
to accommodate an incomplete screenshot. The full goal, live-media gates and
matched trimmed-candidate delivery remain outstanding; the user's running
candidate was not replaced.

### Solid-material captures and image-animation metadata checkpoint

The visual-test correction described above is now implemented and verified.
`visual-host-2` captures the whole Page and crops using the widget surface's
coordinates, with actual solid host material. Visible package icons must finish
preparation and remain stable through the pixel read. Sidecars retain bounds and
readiness evidence. The earlier failed `visual-host-1` capture-root experiment
remains preserved; its crops are not qualification evidence.

`v2/minimum` and `v2/expanded` each passed 31 checks. After the animation metadata
changes, fresh `image-animation-ui-payload` and the same host passed another 186
checks in six runs: `v3` dark, `v4` Mineral Light at 150% text and `v5` high
contrast, each at minimum and expanded sizes. Conversations, chat and Servers
captures were inspected across all three themes. Main observations:

- Correct backdrop composition removes misleading transparent-text artifacts;
  prepared SVGs and avatar monograms are stable in these captures.
- Conversation names, snippets and state have a coherent hierarchy. Compact
  ordinary text fits two complete rows; 150% text fits one complete row plus the
  next row's beginning. Keep user text size and scrolling rather than shrinking
  labels to force density.
- Expanded chat keeps left-aligned messages and explicit attachment actions.
  The participant dock fits its avatars and call controls without dominating it.
- Minimum Servers remains dense; at enlarged text the expanded Servers screen
  also requires scrolling to reach the second stream card. This is a remaining
  density consideration, not evidence that controls are inaccessible.
- High contrast keeps legible labels and focus outlines. Decorative surfaces
  and avatar fills intentionally simplify; live desktop acrylic and decoded
  video planes remain unqualified by these solid-material XAML captures.

Discord's documented message media `IS_ANIMATED` flag (bit 5) is now carried for
attachments and embed images/thumbnails. Reference:
https://docs.discord.com/developers/resources/message . The chosen image owns
its animation flag; prefer the actual image before its thumbnail fallback.
An animated thumbnail must not make an ordinary video autoplay. GIF MIME remains
recognized, and attachment flags cannot turn audio into autoplay content.
Image-only animated embeds now retain the existing anonymous public-image
transport and explicit gallery animation intent. Spoiler checks run before
creating requests/actions. No provider-page scraping or new media origin was added.

Ownership/applicability: Discord owns provider flag normalization and media
intent; the existing SDK public image/animation APIs and host artwork/gallery
lifecycle own rendering and reduced motion. Other shipping widgets, templates
and samples are not affected by these Discord-only declarations; no new shared
contract was needed. Native supported-format qualification remains outstanding:
recognizing animated WebP/PNG metadata is not proof the installed Windows decoder
animates that format, nor proof of live Klipy behavior.

The managed regression's old video-only animation assertion was updated to
accept a valid image source while still rejecting no source, deceptive origins
and invalid video URLs even when another image is valid. Results: 989 managed
checks; 290 raw and 290 minified provider/session checks; seven exact composed
backend checks. Backend output is 102,786 bytes, adapter 46. Evidence lives under
`artifacts/discord-call-media-polish/image-animation-*`; the fresh fixture build
has a unique binlog. README now describes Servers/Messages, contextual L3,
left alignment, explicit attachment galleries and the qualified GIF behavior.

No live messages/calls were sent and the user's candidate was not replaced.
Outstanding gates remain live provider/poster evidence, actual native decoded
media qualification, final cleanup and a matched trimmed Release candidate.
This checkpoint does not complete the goal and authorizes no merge or release.

### Native image-format and visible-video qualification

Added the validation-only `--image-format-output=<absolute-json-path>` scenario
in `WidgetStylesValidationPage.ImageFormats.cs`. Generated two-frame red/blue GIF,
APNG and lossless WebP bytes exercise the production public-media artwork
admission and ImageBrush; sampled rendered pixels distinguish actual animation
from a loaded static image. The APNG/WebP fixtures were independently reopened
with Pillow 13 and have two frames. Pillow was installed only under ignored
`artifacts/media-fixture-python` to generate these fixtures; it is not an app or
test-runtime dependency. The embedded fixed bytes require no network or account.

`format-host-1` built successfully with a unique binlog. Result
`native-image-formats-1.json` reports five passing checks. GIF presents both red
and blue frames and stops changing after suspension. On this Windows decoder,
APNG and WebP both decode a visible first frame but report `IsAnimatedBitmap=false`
and never animate. This is a reproduced host codec limitation, not a Discord
metadata loss. The README now states it explicitly. Keep provider animation
intent and safe static fallback; do not claim PNG/WebP animation support or add
a widget-local timer/decoder. Any full format extension belongs in the shared
artwork decoder and needs its own resource/codec assessment. Uploaded GIF and
GIF-like video requirements still use the supported paths.

Desktop capture also yielded actual decoded native video pixels in the
`public-media-visible-4` run on `format-host-1`: the Computer Use screenshot at
12 seconds showed the bearded character, and at 35 seconds showed the flying
dragon scene. Both screenshots included the corresponding native checkpoint
label. This is new evidence beyond playback-clock/metadata assertions: the
checked HTTPS source renders distinct pictures after seeking in the normal
MediaPlayerElement path. The transparent fixture also reveals unrelated desktop
content behind it, so it is not a clean product-design screenshot. No frame-server
mode or alternative renderer was enabled. This does not qualify actual Discord
attachments, arbitrary codecs or the final trimmed candidate.
The run completed all 17 checks (`public-media-visible-4.json`, 854x480 source),
including shared-viewer admission, cancellation and retirement. Both test
frontends closed normally; process inventory retains the original user candidate
25940. No live Discord call, message, capture or permission was changed.

### Shipping build preparation and unresolved pointer smoke test

Built the full matched payload with `scripts/Build-WinUiReleasePayload.ps1` into
`artifacts/discord-call-media-polish/release-candidate-1`. This uses the normal
Release trimming/ReadyToRun settings, validation disabled, freshly compiled native
inputs and matching Bridge/worker/Settings services. Publication succeeded;
the verifier checked 317 local managed dependencies and the published async
cleanup lock acquisition/release paths. No installer or release was created.

Bumped Discord to 0.1.0.30 and built it through the normal marketplace package
builder into `release-discord-30`. The actual packaged backend passes seven
composition/behavior checks. Local update review against the existing isolated
catalog confirms 0.1.0.29 -> 0.1.0.30, unchanged full-trust execution and unchanged
optional capture permission. Review only: update is not applied. Reviewed archive
SHA-256: `ef2dd829933b3c5951a32592aeb3241ae6aff88ea84b83e01ffa4be1ebf4bd86`.
Prepared `release-candidate-1/live-shell.json` with the new installation root,
existing isolated catalog and default settings root; it has not been launched.
Updated the user guide for Servers, contextual L3, left-aligned messages,
explicit galleries, participant dock and actual animation-format limitations.

Opened the existing user candidate with its configured Shift+F1 shortcut. Its
UI confirms an active private Sanky call, so do not restart it as though it were
the previously approved Test server call. The reported Klipy message is present
in this older build and still appears as a link. No message was sent. The old
candidate subsequently became hidden and remains running; its call was not ended.

Added fixed numeric media facts to the existing validation-only messaging probe:
known provider/type counts, external Discord proxy use, animation flags and
attachment proxy/thumbnail presence. Only already-requested responses are
observed; no independent history/media requests or message text, identities,
filenames, full URLs or arbitrary hostnames are exported. Bounds and redaction
tests pass (seven tests total). Built `media-probe-host-1` successfully using the
new Release native inputs. This instrumentation is excluded from the shipping
host and must not be represented as live provider evidence until run.

Shipping-host smoke fixture `artifacts/trimmed-discord-smoke-1` uses the real
trimmed host with synthetic current worker data and isolated profile/catalog.
Native screenshots show the revised Servers and chat layouts. Mouse activation
opens Messages and Avery; however clicking the visible View media action several
times did not open its gallery. Frontend diagnostics record indexed-input
foreground delegation, but no error explaining the missing gallery. A comparison
using the untrimmed `media-probe-host-1` with the same fixture also did not show
the gallery after the mouse action. This is NOT a proven trimming regression.
Computer Use also reported one unavailable geometry error before a fresh
screenshot, so establish a controlled native pointer-command reproduction before
changing production behavior. Both fixture processes were closed afterward.

Next: extend the existing native MediaMessages scenario to invoke the attachment
Button command while focus remains in the composer, comparing against its
passing controller-focused route, and observe the actual indexed admission
result. Relevant path: `WidgetViewPresenter.InvokeAsync` ->
`WidgetIndexedCollectionView.DispatchReadingActionAsync` ->
`WidgetPresentationIndexedLease.AdmitInlineActionAsync` -> worker row delegate ->
`OpenMessageMedia`. Do not bypass `PushFromCollectionAction`, weaken source/lease
validation or add widget return-focus flags to make the action work. If a shared
gap is confirmed, update its consumer matrix and add a behavior regression.
The prepared candidate is not qualified for replacement while this is unresolved.

### Retained reading-row input origin: reproduced and corrected

The pointer smoke failure was a real host issue, not trimming or an inability to
click. New native `-PointerMediaClick` evidence records one pressed event, one
Click and the correct foreground/hit-test root. A fresh row opens correctly.
After further parent publications, retained row presenters still submitted their
original frame. `pointer-media-3/expanded/result.json` records:

- First open: displayed 13, current 18, enqueued.
- Next open: displayed 13, current 24, enqueued.
- Reopen: displayed 13, current 30, rejected with
  `stale_indexed_input_authority`.

The bridge retains 16 input-origin snapshots, correctly rejecting the expired
origin. `WidgetIndexedCollectionView.DispatchReadingActionAsync` now captures
the collection's current displayed parent binding at input time, exactly as
ordinary indexed rows already do. The row body remains governed by its exact
current lease and inline action identity. Owner/scope matching, disabled/busy
checks, selection precedence, admission after interaction handoff and bridge
revalidation remain in place. No history-limit expansion, blind retries, widget
focus bookkeeping or new protocol/SDK API was added.

Consumer matrix for this bounded host fix:

| Consumer | Result |
| --- | --- |
| Discord message ReadingGroup buttons/link intents | Affected; shared input owner fixed. Actual pointer and repeated gallery return tested. |
| Other shipping `samples` / `src/FirstPartyWidgets` | No other ReadingGroup or raw IsReadingGroup declarations found; ordinary indexed controls already capture current collection ancestry. |
| Templates and SDK examples | No ReadingGroup consumer found; public API remains unchanged. |
| Generic reading-group fixture | 19 native checks pass, including web-intent routing, mouse/keyboard/controller activation and retired native command rejection. |

`pointer-host-4` passes `pointer-media-4` at both sizes: 35 checks each. A retained
row still reports its original sequence while admitted displayed/current
sequences advance together. Screenshots of Servers, conversations, chat and the
gallery are retained with the run. The earlier failed runs remain preserved.

The generic fixture initially failed its existing scroll-return assertion in
both the fixed host and the pre-fix `pointer-host-3` baseline. It asserted before
queued layout/memory restoration settled. Waiting for existing presentation
readiness resolves it without changing production layout. Evidence in
`reading-parent-regression-2/return-position.json`: before/after offset
514.4000244140625 and before/after message Y 209.5999755859375. The assertion now
checks both quantities. This strengthens the behavior check rather than relaxing
its tolerance. The fixture's validation-only admission callback records sequence
facts for regressions; it is absent from shipping builds.

Publishing the host-only correction into `release-frontend-2`, using the already
matched Release native/service inputs. The Discord 0.1.0.30 package is unchanged.
Live replacement remains pending because the user's current private call is
outside the previously approved Test server call.

### Approved live update and inline-animation ownership

The user explicitly approved restarting the private-call candidate and continuing
testing. The old process 25940 exited normally. Applied the reviewed SHA-256
update using CLI disable -> local update -> enable against the isolated catalog,
preserving default-profile settings and matching permission decisions. Discord
is now 0.1.0.30 there. No microphone grant, call join, capture or message send was
performed. Authentication persisted normally.

The corrected trimmed `release-frontend-2` passed the actual pointer smoke:
Messages, conversation selection, gallery open, Next to item 2, Escape and reopen
all worked. Native dependencies match the full Release payload; dependency and
cleanup verification passed, and validation method markers are absent from its
assembly. The temporary synthetic frontend was closed before the live update.

Live validation metadata (`live-media-facts-1.jsonl`, fixed counts only) now
establishes the actual provider shape: 20 history rows include two Klipy `gifv`
embeds, both with video on the exact Discord external proxy hosts; three embed
images also use those proxies. Three video attachments have ordinary proxy
metadata, and none expose thumbnail/poster fields. No direct Klipy-origin
expansion or page scraping is needed for the reported GIFs.

Live rendering still failed initially. The failure is before download: the
host selected indexed media authority only when `presentationOnly` was true.
Interactive ReadingGroup rows intentionally create interactive presenters, so
their passive animations were incorrectly looked up in the ordinary parent
tree instead of their row lease. `WidgetViewPresenter.MediaPlayer.cs` now selects
the indexed lease for a row animation regardless of whether sibling controls
are interactive. Current lease, row item, source definition and visibility
checks are unchanged. Temporary decode-probe instrumentation was removed after
this diagnosis; it recorded no live failure because the source was revoked
before the load/decoder error paths.

The new `--serve-animation-reading-validation` fixture represents the real
interactive-row composition, alongside the original passive action-row variant.
Before the fix, `reading-animation-before/result.json` fails waiting for playback
after 14 checks. After the fix, `reading-animation-after` and
`passive-animation-after` each pass 20 checks, including reduced motion,
visibility, parent suspension/resume and zero resident decoders on teardown.
This is a host ownership fix. Applicability search finds Discord as the shipping
animation/ReadingGroup consumer; other samples, first-party widgets and templates
declare neither affected combination. Existing passive-row compatibility is
covered by the second native run. `reading-media-host-1` contains the correction;
live picture verification and a refreshed trimmed frontend remain outstanding.

Live follow-up after the indexed ownership correction still shows the Wednesday
animation's unavailable message. Do not claim the real GIF works from the passing
indexed fixture. `release-frontend-3` was published with normal trimming and both
host fixes, and its dependency/cleanup verifier passes, but it is not yet a
qualified final live-media candidate.

The current live process is 26404, `live-media-host-2`, using
`release-candidate-1/live-shell.json`, default settings and the updated isolated
0.1.0.30 catalog. It has no active widget call. The user's approval to restart and
continue testing was received; no additional restart confirmation is needed for
this session unless an unrelated call begins. `live-media-decode-2.jsonl` now
records `load / other / HRESULT 0x80070005` followed by `LoadAsync` failure. This
distinguishes the remaining access-denied load error from the previously fixed
row-authority rejection, but does not yet identify which load stage returns it.
Next diagnostic: record bounded stage markers around public range acquisition,
container inspection, native metadata inspection and MediaSource creation; do
not print source URLs or exception message/stack data or weaken transport checks.

Temporary validation-only `PublicMediaDecodeProbe.cs` and guarded load/player/
authority diagnostics are currently present for this investigation. Remove their
temporary hooks after establishing the failure, or retain only a justified
test-only contract. They are not compiled into shipping frontend publications.
The current `Fail` caller-name parameter is diagnostic scaffolding, not a new
product API. No auth or microphone permissions were automated.

Additional live UI observations to address during the remaining polish pass:
the selected conversation still says Direct message even after history was
loaded, so verify whether already-fetched messages should populate its cached
snippet without an extra provider read. An ordinary article's decorative embed
image also appears as an Open attachment action beside the URL card; distinguish
provider embed semantics from genuine image/video messages before changing this.
These are observed UI issues, not permission to infer message type by labels or
scrape provider pages. The actual video poster requirement remains unresolved:
the observed attachment payload has no explicit thumbnail/poster fields.

### Live GIF rendering correction — November 18, evening

The remaining live animation failure was isolated past checked acquisition and
native metadata (498 by 498, 25/6 FPS) to host opacity relocation. There were two
independent host defects: taking a handout Visual from a XAML Scale-facade owner
throws E_ACCESSDENIED; the full ancestor expression also exceeds WinUI's string
limit in a deep reading-row hierarchy. The 64-level native fixture reproduces
that second failure before the fix (`opacity-depth-before.json`, first rejected
prefix 49 of 68 terms). No URL admission or transport security was relaxed.

The universal UIElement-reference experiment was rejected: it lost raw Visual
opacity (pixel evidence `opacity-after.json`). `CompositionOpacityBinding` now
uses each existing ownership model and reduces ancestry into compositor-owned
products of at most 16 ancestors per expression. Both `MediaPresentationStage`
and `PresentationPortal` use it. Products are disposed on relocation rebinding,
fullscreen entry, page-motion override, slot detach and owner retirement.
`opacity-chain-after.json` passes seven checks with observed red values 63/31/15
and green 63/15, including fullscreen/detach product cleanup. A further check for
common-root opacity applying once is in the next clean validation build.

Live `opacity-chain-host` with isolated Discord 0.1.0.30 now displays the real
Wednesday Klipy animation inline and in its gallery, with visible video pixels
and a progressing playback control. Escape dismisses the gallery back to the
same message. No call, message, authentication or permission action was issued.
The screenshot critique still finds the stale Direct message summary and an
article's decorative thumbnail offered as an attachment; a parallel provider
review is addressing these without extra history requests or webpage scraping.

Consumer applicability: both relocation consumers migrated in the host. Discord
uses reading-row animations and galleries; Capture Library uses native media and
galleries; Game Help and SDK Gallery use media. Other modal/preview consumers
inherit PresentationPortal's correction. Templates and SDK consumers require no
new API or migration. Shared native layer/fullscreen regressions and final
trimmed-candidate verification remain pending; source inspection does not
establish runtime correctness for every individual widget.

Temporary PublicMediaDecodeProbe and production load/authority hooks have been
removed from source after the live diagnosis. The old diagnostic executable is
only being used until the clean regression build is ready. The broader goal is
still active, particularly video posters and final UI/candidate qualification.

Clean build follow-up: `opacity-clean-after.json` passes **8** native checks,
including equal root opacity on media and portal, and zero products on detach.
`opacity-clean-layers/layers.json` passes **24** checks across native/embedded
media, modal ordering, inherited viewport/text-scale, fullscreen and teardown.
`opacity-clean-discord-2/{minimum,expanded}/result.json` each pass **35** native
pointer/controller checks, repeated gallery return and continued navigation.
The first fixture invocation pointed at the production widget instead of the
synthetic fixture payload and stopped before launch; it is not passing evidence.
The corrected run uses `image-animation-ui-payload` (unchanged managed UI), while
provider .31 is independently covered by the raw/minified and packaged checks.

Reviewed screenshots of compact chat actions and expanded chat return: focus is
legible, call dock remains compact with participants, and attachment count has
one explicit action. Remaining design critique: the URL card's left accent rule
should be revisited against Impeccable's no-one-sided-decoration guidance; use a
whole-surface treatment unless the user's reference requires otherwise. Real
video posters remain unresolved; do not add distracting inline rich thumbnails
merely to fill this gap after the user chose explicit attachment galleries.

Discord 0.1.0.31 was packaged and installed through the reviewed isolated update
path (SHA-256 `9b27283c9f7ce9536d34ee28985c5522e3f934f177257b7c91465bc615548748`).
Default profile and existing permissions retained; production catalog untouched.
Managed harness: **989** checks. Exact packaged backend: **7** checks. Provider
polish evidence is in `docs/discord-provider-polish-evidence.md` (**248** raw and
**248** minified checks for its suites). `release-frontend-4` uses normal Release
trimming/ReadyToRun, excludes temporary diagnostics, and passes the verifier's
317 managed-reference checks and async-cleanup contract. Final live .31 visual
and startup verification is in progress. No merge, push, installer or release.

Live clean Release follow-up: PID 30372, `release-frontend-4`, uses the same default
settings and isolated .31 catalog. Servers populated after startup; mouse access
to Messages and Sanky succeeded. The selected row now displays its already-fetched
latest content instead of Direct message. The article has one URL card and no
separate Open attachment action. The real Wednesday animation displays decoded
pixels in the normal trimmed build. No new call or message was initiated. Native
mouse scrolling was verified with the current screenshot geometry; text-only
snapshots alone are insufficient for some sky input calls. This is a tooling
observation, not evidence of an application navigation defect.

Poster follow-up assessment: Discord documents first-frame extraction in its
Media Proxy, but the exact current query transformation remains unverified. The
adapter currently drops attachment proxy metadata; the gallery protocol has no
per-item poster field. Do not invent a URL conversion or add an unbounded native
video-thumbnail service. A bounded next step is to verify a provider-supplied
`media.discordapp.net/attachments/...` proxy with image conversion through the
existing anonymous checked transport, recording only image format/size facts.
If verified, preserve signing parameters and add optional domain poster metadata;
use existing host image lifetimes, spoiler gating and a labeled fallback. Any
per-item gallery poster extension must include a shared consumer matrix and
native image/selection/lifecycle evidence. Inline rich thumbnails should remain
absent in accordance with the user's later attachment-modal decision.
Reference: https://discord.com/blog/how-discord-resizes-150-million-images-every-day-with-go-and-c

Poster assessment narrowed after real trimmed-build evidence: the two-attachment
message opens image 1, Next selects video 2, and the selected video displays its
decoded first frame with AutoPlay false. The user's later switch away from inline
rich previews removes the original inline-thumbnail surface. No additional
proxy conversion or remote thumbnail decoder is justified merely for the current
explicit gallery: the existing checked native media path now supplies its paused
initial frame. Retain this working path, rather than extending protocol/lifecycle
for an obsolete preview design. Optional preloading posters for slow networks
remain an optimization proposal, not a release requirement; evaluate only with
latency evidence and an actual destination surface. No extra image fetch or
arbitrary URL transformation was added. Fullscreen and final return checks follow.

Live gallery on clean .31: native mouse selects the counted two-attachment action,
Next changes 1/2 image to 2/2 video without dismissing the modal, and the video
shows its first frame. The Expand controller hint reaches fullscreen; its Play
control explicitly shows 0:00 / 0:37 (paused initial frame). This verifies the
poster reassessment against actual decoded content, not only a synthetic fixture.
The fullscreen gallery toolbar is separate chrome, but remains visible when
focus stays on its Previous button. Remaining polish: review auto-hide/focus on
fullscreen entry and narrow-screen overlap before claiming the transient-chrome
requirement fully satisfied. Do not hide a keyboard/controller-focused button
without transferring focus to the video viewport and retaining return focus.
Reuse MediaControlsVisibility where possible; avoid introducing widget timers.

### Explicit user decisions — November 18 evening follow-up

The user clarified both open design choices: fullscreen attachment arrows remain
visible; the filename/title is unnecessary. Small static thumbnails belong inside
the single View media action. These decisions supersede the proposed fullscreen
auto-hide grouping and the earlier poster narrowing. The auto-hide experiment's
own edits were removed before shipping; no generic visibility API is needed.
Fullscreen now suppresses its gallery filename label while preserving its two
arrow targets and restoring the label inline. Thumbnails remain a required gate,
with spoiler gating, one controller action per message, bounded static images and
verified provider metadata. The passing first-frame observation is playback
evidence only and does not close that gate.

### Single-video playback steering

The user now requests Discord-style inline playback for a message containing
only one video attachment. One video plus any other attachments uses the gallery
(user explicitly confirmed). Preserve GIF behavior; ordinary video does not
autoplay. Inline video pauses when leaving the chat (explicitly confirmed), and
its host owns offscreen/hidden suspension, controller interaction, fullscreen,
source authority, bounded resources and return focus. Static thumbnails remain
inside the one action for multi-attachment messages. Inspect and extend the
indexed ReadingGroup media/input contract rather than routing body media through
preview authority or adding widget focus/lifetime plumbing.

The user's question about player settings was answered from current code:
SuspendPlayer preserves position/volume/mute/loop/rate for the same retained
player; gallery replacement clears this memory, and there is no persisted shared
volume/mute preference across videos/restarts. No persistent-preference feature
has been claimed implemented or added merely by this observation.

### Crash and native-tree redesign — user-directed, November 18

User observed GIF drift while scrolling and an overlay exit. Confirmed from
Windows Application events: production-like PID 30372 (`release-frontend-4`)
crashed at 20:57:10–14 local time, exception c0000005. This was NOT an agent close.
Managed stack runs ReleasePlayer -> ReconcileAnimation -> presentation suspension
-> SetVisible(false). The retained dump is
`C:/Users/dwive/AppData/Local/CrashDumps/OverlayFrontend.WinUI.exe.30372.dmp`.
CDB evidence in `crash-30372-native.txt` shows Windows MediaPlayer Close/Shutdown
and MFMediaEngine::CancelDelayedBreakMarker leading to a fatal access violation.
This locates the failure during native shutdown; it does not prove root-level
placement itself caused the crash.

The user explicitly chose normal WinUI native-tree media and accepted transition
tradeoffs. Native MediaPlayerElement now remains inside its existing stage/card;
its controls stay with it. Removed native root-plane creation, inline chrome
portal, per-frame page placement callbacks and unused native page-fade plumbing.
Embedded WebView media remains on its existing resident path (different renderer
contract). Native-video outgoing page retention is disabled; no promise of a
seamless custom page-exit animation replaces the user's stability preference.
Standard subtree layout/clipping/scrolling now owns video position.

Teardown now snapshots and clears owned references before WinRT calls and detaches
the player source before Close, with resource cleanup in finally. Treat this as
a candidate fix pending stress evidence, not a proven crash cure. Indexed GIF
validation now runs 24 extra suspend/resume cycles. `intree-gallery-1/result.json`
passes 23 checks with normal in-tree playback, gallery switching and fullscreen
return. This predates the extra stress fixture and full live qualification.

Remaining newly confirmed work: inline single-video interaction through a proper
ReadingGroup host/session/bridge contract; static attachment thumbnails (verified
proxy probe prepared, 11 JS tests passed, not yet run live); per-widget remembered
mute/volume with muted first use, independent of content playback memory. Inline
videos pause when leaving chat. No new microphone, call or message action was
performed. Old Release candidate is crashed, not a qualified running deliverable.

`intree-animation-2.json` now passes **45** native checks, including 24 extra
indexed ReadingGroup hide/resume cycles, reduced motion, offscreen release,
current lease resolution and teardown with zero animation/interactive decoders.
The media bytes in this fixture are the bundled local MP4; this is not proof of
live checked-network stream teardown. `intree-gallery-1` remains 23 native checks.
Current priority is live in-tree GIF scrolling and hide/show over the real source,
then complete the newly authorized inline-video and per-widget audio work.

User live acceptance: scrolling the real chat/GIF on `intree-media-host-2` feels
much better and showed no visible positioning problem. This supports the
native-tree placement change beyond fixture/source inspection. The user was
interacting with the live candidate, so the agent refreshed its observation and
did not inject the queued wheel action. PID 53352 remains responsive; no new
OverlayFrontend Application crash event was found in the recent check. This is
not yet a long-run guarantee for the prior shutdown crash.

The first poster probe expired before the authorized chat was opened; it made no
successful qualification claim. Its opt-in waiting period is now 15 minutes
instead of 60 seconds (still one candidate, 25-second decode/request deadline and
bounded bytes). All 11 actual-script tests pass after that fixture-only timing
change. A new live probe invocation remains required.

Per-widget audio preferences are now implemented in source (native qualification
pending): host ShellPreferences stores only explicit mute/volume choices, bounded
to 128 widget IDs so even maximum-length order/IDs fit its 64-KiB document bound.
Old files keep defaults. First use starts muted; source/gallery/player retirement
does not clear preferences. Passive animation and forced preview mute do not
write preferences. Native player instances find the host profile through logical
presentation ancestry; pin roots attach the same preference owner. The shell's
existing state writer now coalesces rapid volume changes to one active writer
and the latest immutable snapshot, and existing shutdown awaits the final write.

Seven ShellPreferencesTests pass (`audio-preferences-tests.log`), including new
widget/profile/restart isolation, immutable state, malformed volume fallback and
maximum serialized bounds. Added `--media-audio-output` native fixture to test
actual volume/controller mute events and fresh player replacement; not run yet.
No widget is asked to add settings plumbing. Embedded browser-player providers
retain their independent domain transport; this change covers the shared native
MediaPlayer control used by Discord, Capture Library, Game Help and SDK Gallery.

Fullscreen refinement follows the explicit keep-arrows decision: the filename is
hidden only in fullscreen, and the empty full-width toolbar fill is removed.
Each arrow retains a compact theme-backed surface for contrast over video; normal
preview title/count and toolbar styling return on collapse. The generic auto-hide
experiment remains removed. Native visual/interaction qualification of the latest
arrow backing is pending the next build.

Native audio preference qualification: `media-audio-2.json` passes **9** checks
with exactly **2** preference writes (explicit volume/unmute and controller
mute). Fresh player instances restore only their widget's values; another widget
starts muted, and decoder counts return to baseline. The first test stopped
because it queried a control before the new player entered the XAML tree; the
fixture now waits for both IsLoaded and CanControl before interaction. It is
recorded as a fixture readiness correction, not a product fix or passing run.
`intree-media-host-4` contains this and the fullscreen compact-arrow backing.
Production .31 backend remains unchanged; the live frontend is still the earlier
`intree-media-host-2` until the next approved restart/probe.

`intree-layers-2/layers.json` passes **24** native checks after the tree migration:
background native media, dialog-owned video, shell overlays, embedded WebView
compatibility, fullscreen/return and final owner teardown. This fixture proves
its named structural and lifetime assertions; it does not replace the user's
live scrolling acceptance or the remaining live shutdown stress.

### November 19 checkpoint: thumbnails, GIF cards and readable presence

User refinements: remove the redundant Open attachment card below inline GIFs;
enlarge the tiny avatar status mark. Implemented in isolated Discord 0.1.0.32.
GIF-only media no longer adds an action; mixed-media galleries retain their items.
Presence uses package SVG masks (circle/crescent/minus/ring), 18-DIP backing and
14-DIP visible symbol, with existing theme tokens and accessible status names.

Attachment posters now preserve provider-supplied signing query bytes and request
an aspect-preserving JPEG rendition up to 512 pixels, without upscaling. Only the
exact supplied Discord attachment proxy and matching attachment ID are admitted.
The widget declares at most three static anonymous images inside one action;
spoilers do not declare demand before reveal. No extra authenticated request,
page scraping, provider-host expansion or arbitrary URL resolver was added.
The prior live probe decoded an actual 512x512 JPEG (22,053 bytes). Production
aspect-ratio preservation still needs visual evidence from the live candidate.

The rich-message test caught two shared ownership gaps in the unreleased reading
contract: nested action presentation depth was counted from the outer reading
row, and the ordinary 32-layout-node tile budget could not contain a bounded rich
message plus thumbnails. `ActionSurfaceContentBudget.ChildDepth`, SDK validation,
and protocol validation now grant each admitted child action its existing four
levels; ordinary containers cannot reset depth. Reading groups have 128 total
layout descendants, 32 actions and the existing separate rich-text span budget.
Ordinary action surfaces remain limited to 32 descendants; nested interactive
controls/scopes remain rejected. This refines the unshipped protocol132 contract
(main is protocol127), not a retrospective change to a released capability.
The candidate bridge, worker and frontend were rebuilt together.

Consumer matrix for this bounded validation change:
- Discord: affected and migrated; exact rich-message and mixed-media tests.
- Other shipping samples/first-party widgets: no ReadingGroup call sites; their
  ordinary ActionSurface limits and admission are unchanged.
- Templates/examples: no reading-group use found.
- SDK/bridge/native reading fixtures: retain exact child/lease ownership; SDK
  rejection tests cover over-depth containers/actions and oversized groups.

Evidence in `artifacts/discord-call-media-polish/`:
- `poster-provider-1.log`, `poster-provider-minified-1.log`: 70/70 each.
- `poster-status-tests.log`: 993 managed widget checks.
- SDK Reading groups focused run: 1/1 scenario passes, including new budget checks.
- `thumbnail-native-32/{minimum,expanded}/result.json`: 31 checks each; all visible
  package icons prepared, gallery RB stays open and repeat return preserves the
  message position. Screenshot review: larger statuses are distinguishable;
  expanded thumbnails use one focus target. Compact chat has little height when
  the call dock is present, so its gallery card scrolls; no false claim of full
  compact-content visibility. These fixtures show fallback icons, not real JPEGs.

Updated only the isolated catalog, reviewed hash
`43a6dd55a99bd8b1b347666d87c4e9c80ad27a8391862f49ae5d291a31e65e36`.
Host `intree-media-host-5`, runtime `thumbnail-candidate-32`, default profile,
PID54988 at launch. This is a validation build, not final trimmed qualification.
No calls, permission grants, capture or messages were initiated. Remaining goal
includes inline single-video admission/lifecycle, final media safety/stability,
live screenshot review and matched trimmed publication. Do not mark complete.

Live .32 review completed in the authorized Sanky conversation: the Wednesday
GIF renders with no attachment card beneath it; the status symbols are visibly
larger in the actual acrylic layout. Scrolling to a previously uploaded MP4
shows its real landscape JPEG thumbnail and Play overlay, contained without
stretching in the 80x60 slot. No original video decoder is needed to show that
thumbnail. Remaining critique: the single-video action is still dominated by
a long generated filename and wastes width; the agreed inline-video work should
replace that surface, rather than polishing this transitional modal action.
The native fallback fixtures are retained separately from this live visual proof.

### November 19 chat-switch crash (PID54988)

The user reported the overlay exiting during chat switching. It was not closed
by the agent. WER at 21:57:22 local confirms `c0000409` / CFG failure. Dump
`C:/Users/dwive/AppData/Local/CrashDumps/OverlayFrontend.WinUI.exe.54988.dmp`
and `crash-54988-native.txt` identify WinUI `CTextAdapter::GetSelectionEndPointers`
via cross-process UIA GetSelection. The managed thread was in artwork metadata's
DecoderInformation getter, whose COM wait pumped the accessibility callback.
This is distinct from PID30372's MediaPlayer shutdown crash; neither proves the
other is fixed. Mini-dump heap coverage does not identify the exact retired text.

Added a validation-only native retained-text-provider fixture. With stock WinUI
RichTextBlock/TextBlock, it crashes on query4 after four text owners are collected,
at the same GetSelectionEndPointers function (PID56524; text-provider-baseline-crash.txt).
No Discord, networking, media, or user input is needed for that reproduction.
Upstream TextAdapter.h retains a raw m_pTextOwner; TextAdapter_partial.cpp invalidates
its projection owner separately. A scoped rich-text peer/provider lifetime adapter
is under implementation and MUST be tested before calling this fixed. It retains
native text ownership while external providers/ranges exist, preserving the actual
native text patterns and inline children instead of disabling accessibility.

Inline-video implementation remains pending. The user additionally wants the
single-video surface to show only poster/Play, without filename or Open attachment
copy; retain accessible naming. No single-video behavior changes have been made
while this crash is being investigated.

The retained-provider baseline also fails with plain TextBlock alone (PID38260,
query7). Lifetime adapter validation in `text-lifetime-host-3` passes 24 cycles
for rich and plain text (`text-provider-lifetime-3.json` and
`plain-text-provider-lifetime-3.json`). Those initial tests cover provider reads;
production projection and independently retained ranges are being strengthened
in host4. The first wrapper run exposed a normal null-selection-array result;
it now returns the conventional empty selection, preserving native text semantics.

Shared source applicability: `WidgetRichTextView` serves Discord text/reactions,
Game Help citations, Playnite achievement headings, PoB formatted descriptions/
trade/workspace text, and SDK Gallery. Its peer retains real text-pattern behavior
and inline children. The existing `WidgetMotionHost` projects plain native text
children using the same lifetime adapter, without new visual wrappers or layout
changes. Other controls' own native peers remain unchanged; do not claim this
patch repairs every possible WinUI accessibility lifecycle bug. The baseline
stock tests are evidence of the platform issue, not an exhaustive consumer test.

Native host4 navigation: `text-lifetime-messages-33` passes 31 checks per surface,
including link/action entry, gallery scope, RB and exact repeated return. Initial
host4 range checks caught that WinUI FindText returns HRESULT 80131515 for these
nonselectable documents even while loaded; the strengthened fixture now compares
native-before-retirement with proxy-after-retirement rather than pretending that
unsupported platform operation exists. GetText on independently cloned ranges
already succeeds; final host5 receipt is pending.

Final lifetime regression receipts: `text-provider-lifetime-5.json` and
`plain-text-provider-lifetime-5.json` each pass 73 checks: actual production text
peer projection, 24 owner-removal/GC cycles, retained-provider queries, independent
cloned-range reads and preservation of native FindText behavior. Host4/5 product
code is identical except preserving the authored automation ID; host5 strengthens
validation. Earlier build/readiness/unsupported-operation failures are retained,
not counted as passes. No native text selection functionality is suppressed.

The fix is a provider/range lifetime lease and an accessibility projection on
existing presentation hosts; it adds no visual layer and does not alter WinUI
paragraph layout. `NativeTextAutomationPeer` uses weak-key peer identity caching;
there is no permanent strong collection of retired texts. Native text owners live
as long as the client-retained provider/range needs them. Generic button/editor
peers outside the affected projection remain platform-owned and unqualified by
this fixture. This is scoped mitigation of the reproduced failure, not a claim
that all upstream WinUI issues or the earlier media shutdown crash are resolved.

Isolated update installed Discord .33 (reviewed SHA256
`c16eed99d66a3d8e0b0acf2089fdda46a6a482db3836dd4820f7089d25e89df2`).
Live host `text-lifetime-host-5`, runtime `thumbnail-candidate-32`, default profile,
PID52292. The new single-video action is a 280x158 poster with centered Play and
no visible filename/Open attachment copy; its accessible name retains the file.
Mixed galleries retain count/labels. Real MP4 poster and inline GIF were inspected
in Sanky, with external UIA inspection active. No sign-in/consent/call/share/send
was performed. The screenshot looks substantially quieter than the previous
filename-heavy card. It still opens the gallery: inline playback is not done.
All diagnostic fixture windows were closed before leaving this candidate running.

User requested closing computer-use/UIA tools. Reset this chat's node_repl session and stopped both verified Codex computer-use helper processes. Keep those tools closed; continue source/build work only until user authorizes UI automation again. WidgetRail PID52292 remains running. Inline-video implementation has not started beyond source investigation; no partial protocol changes were made.

Correction to helper shutdown: the main codex-computer-use process stays stopped, but Codex ChatGPT.exe PID18108 automatically respawns codex-computer-use-swift.exe after termination (observed twice with new PIDs). Do not kill the Codex app or repeatedly terminate its managed helper. This chat's automation session is cleared and no UI actions/tests should run. Report the auto-restart limitation honestly; WidgetRail PID52292 is responsive.

### Chat-switch flashing and transition verification

User supplied recording `20261119-0632-49.1744513.mp4` (7.77s, 1342x950/30fps).
Extracted contact sheets offline to `chat-flash-recording` without using UI tools.
The header stays present, but both conversation list and history disappear for
several frames. Source showed OpenConversationAsync resets navigation to Messages
then pushes Conversation, publishing a structurally different intermediate page.
A new test observing every invalidation reproduces that intermediate workspace
loss (`chat-switch-regression-before.log`); it passes after the fix (995 checks,
`chat-switch-regression-after.log`). This does not yet prove visual qualification.

Added SDK `WidgetNavigator.ReplaceCurrent(route)`: one publication, fresh visit
cancellation, stable parent/Back destination, preserved root lifetime for nested
replacement; root replacement retires root-owned work. Discord adopts it for
chat-to-chat navigation. This is a missing navigation primitive, not a widget
counter or publication delay. Comparable precedent: replacement/single-top
navigation in Android Navigation. Complexity low; source and focused SDK tests
cover one publication, stable scope/depth, cancellation and Back. Other widgets
have no equivalent chat detail reset pair found. Discord notification entry and
incoming-file handoff have different root-stack semantics and remain unchanged;
do not force them into ReplaceCurrent.

A native frame-sampling check now switches Avery -> Morgan -> Avery and checks
that the sidebar object and painted rows stay present on every sampled frame,
with before/after screenshots and no section-key restart. Build/test pending.
Future visual refinements must also review transition frames (loading, rapid
replacement, cancellation and return), not merely settled screenshots.

The user explicitly reauthorized UIA/computer use after requesting their closure.
Previous restrictions on authentication, privacy grants and unrelated calls remain.

Inline-video groundwork also landed while the candidate stays unchanged:
protocol134 `MediaPlayerPresentation.Inline`, `UI.InlineVideo`, no autoplay/GIF/
image/gallery combination, admitted reading controls distinct from widget actions,
exact body-lease resolution and bridge denial beneath another scope or selection.
Focused SDK contract and bridge admission tests pass. Runtime playback/controller
implementation and Discord adoption are still absent: DO NOT claim inline playback
is implemented or ship this capability before finishing and testing the host.

User additionally reports flashing while loading older chat messages. Treat this
as a separate required continuity regression: existing visible messages and their
screen position must survive prepend/page-window replacement, with no blank frame
or whole-chat fade. Message rows already use SDK PreserveOccurrences; bounded
cursor windows prepend/trim via a new query generation. Host currently detaches
ItemsSource and swaps WidgetIndexedRows, despite seeding old paint by occurrence.
Investigate preserving native slot/container identity across related bounded-key
query changes instead of masking the blank with an animation or retaining stale
input authority. Establish a native frame-sampled before test, delayed results,
exact occurrence anchoring, old-action rejection and memory bounds. This is not
yet fixed by the chat-switch navigation change.

### Older-history continuity: native reproduction and bounded host fix

Implemented in `Collections/IndexedItemsSource.Window.cs`, `WidgetIndexedRows.cs`
and the indexed presenter. This is host-owned presentation mechanics; Discord's
provider paging, occurrence IDs and 40-message retained window are unchanged.
The initial native baseline (`prepend-native-before-3/expanded/result.json`)
observed source replacement on 21/44 frames and seven frames without painted
rows. Earlier launch failures were mismatched fixture/runtime setup, not test
passes. All paths below are under `artifacts/discord-call-media-polish/`.

For same-owner/scope/source bounded occurrence maps, ordered insert/remove edits
retain the native source and surviving slot objects. It does not transfer old
leases: requests/batched publications are cancelled, query authority is checked
again, pages release, and current ranges are reacquired. Explicit retention now
follows a slot rather than its reusable numeric index. Removed slots cannot alias
new occupants. Reorders, groups, absent occurrence maps, empty/disjoint windows
and large lazy collections retain the existing replacement path. Maps remain
limited to 512 entries; caching and provider concurrency remain bounded.

WinUI's `ModernCollectionBasePanel::BeginTrackingFirstVisibleElement` deliberately
skips `KeepItemsInView` tracking at offset zero (upstream source retained locally).
Use its supported `KeepLastItemInView` mode during timeline reconciliation, then
restore the previous mode only after demanded payloads and realized row layouts
settle. Switching back after only the anchor row arranged caused a measured jump;
enabling this mode globally broke cancellation of deferred reveal. Both approaches
were rejected. The bounded lifecycle version passes the existing 34-check native
end-following suite, including pointer/programmatic departure, hidden state,
reader preservation and source retirement. Relevant platform precedent:
https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/inverted-lists

Explicit held-scroll edge requests now wait for current row data/layout and
pending continuity just like automatic observations. Without that gate a second
request could protect unfinished viewport contents and fail the cursor capacity
check. Repeated older/newer traversals now pass without weakening cursor bounds.

Consumer matrix (repository search includes shipping samples, first-party widgets,
templates and test entry points):

| Consumer | Applicability |
| --- | --- |
| Discord history | Migrated through existing PreserveOccurrences contract; native compact/expanded prepend/trim evidence. No widget workaround. |
| Discord participants, friends and picker | Eligible ordered bounded edits reuse slots; unrelated/reordered membership still replaces. No domain changes. |
| Spotify queue, standalone YouTube Music queue | Existing bounded occurrence maps eligible; occurrence identity preserves duplicate queue entries. No end-following timeline mode without the existing opt-in. |
| Playnite achievements, achievement hub and related captures | Eligible ordered bounded edits; actual grid/ordinary query behavior retains its layout and navigation contract. Native generic continuity/entry fixtures cover shared mechanics, not full live widget acceptance. |
| PoB passive groups | Existing occurrence maps eligible for ordered membership changes; build/domain reset and ordering remain widget-owned. Live consumer acceptance not rerun. |
| Other first-party widgets, templates/examples | No direct PreserveOccurrences consumers found; ordinary indexed providers, grouped sources and large lazy queries retain replacement behavior. Shared retention refcount changes covered by native source suites. |

Evidence with final product code (`prepend-after-9`; host10 only strengthens a
validation test):
- `prepend-native-focused-9/{minimum,expanded}`: 21 assertions per surface,
  three delayed prepend/trim loads each; 203/188 sampled frames, zero source or
  anchor replacements, zero blank frames, maximum drift <0.4 DIP. Sampling starts
  before the delayed publication, after the fixture's deliberate teleport to the
  edge. It does not claim that arbitrary unloaded-position teleport is instant.
- `prepend-native-full-9/expanded`: 90 assertions, including bounded older/newer
  traversal, exhaustion, controller/keyboard navigation, composer, upload cancel,
  section return, inbox return and no unsolicited account mutations.
- `prepend-shared-native-9`: continuity16 and end-following34 pass. Entry initially
  failed because the test required an unfocused, undemanded row to load after
  cancelling navigation. Baseline sometimes acquired it through incidental native
  cache. `prepend-entry-native-10`: all65 pass after the fixture explicitly retains
  that row, proving actual late delivery does not reclaim focus. Production demand
  cancellation was preserved, not bypassed to satisfy the test.
- Real-dispatcher source window checks cover exact slot identity, duplicate
  retentions, removed-token disposal, ignored-cancellation late results, exact
  lease release, cache eviction and rejected reorder/duplicate/oversized maps.
  Existing retention, reentrant publication and bounded batch suites also pass.

Native before/after screenshots reviewed: the same message slice stays in place,
with only the scrollbar/edge indication changing. Expanded chat remains readable;
420x360 with an active call has little message height. The older broad compact
fixture's 116-DIP height assertion fails at94.4 DIPs before paging; this existing
layout limitation is recorded, not disguised as a transition pass. The focused
compact transition fixture validates paging at that actual size.

No live Discord calls/messages or installed profile changes were needed for these
deterministic checks. No merge/release. Goal remains active for the previously
unfinished inline-video/runtime qualification and candidate work. Animated WebP
and APNG support was discussed as possible future shared decoding/playback work;
it has not been implemented or silently added to this fix.

### Inline single-video completion and candidate .34

Protocol134 and SDK `UI.InlineVideo` now have a complete host path and Discord
consumer. `MediaPlayerView.Inline.cs` starts with a poster/Play control, acquires
no video source or decoder before explicit Play, and reuses the normal in-tree
WinUI player, transport, fullscreen and per-widget mute/volume preferences.
Offscreen/hidden playback releases its decoder; returning remains idle. Late
source acquisition cannot resume audio after departure. Current poster delivery
can still fill the idle row after playback cancellation. Default mute is retained;
user audio preferences persist per widget rather than per attachment.

Reading controls use the exact current indexed-body lease. `IsInlineControl`
enables host presentation input separately from worker `IsInlineAction` admission.
No widget-maintained focus counters, decoder pool or playback timers were added.
Native tests exercise A enter/Play, X pause/resume, R3 fullscreen and B return,
including avoiding a held-seek guide when only the poster is present.

Discord chooses inline playback only for exactly one nonanimated MP4/WebM
attachment and one supported media item. One video plus any additional attachment,
including unsupported files, retains the gallery. Filenames remain accessibility
labels rather than redundant visible cards. Verified poster metadata uses existing
public artwork. The 280x158 player has a themed fallback and no decorative border.
Compact screenshot review found a clipped Play focus outline; the existing outline
now sits2 DIP inside the control, with a repeated compact native check.

Consumer matrix (repository search of shipping samples, first-party widgets,
templates and tests):

| Consumer | Status |
|---|---|
| Discord single-video messages | Adopted `UI.InlineVideo`; mixed attachments retain gallery. Managed/native/live evidence below. |
| Discord GIF/gifv | Existing passive animation path retained; shared viewport observation reused with distinct autoplay policy. Native animation regression passes. |
| Capture Library gallery/detail, Game Help preview | Ordinary `UI.MediaPlayer` presentation retained; no forced inline conversion or capture-custody change. Shared audio regression passes; full live consumer acceptance not rerun. |
| SDK Gallery players | Existing ordinary player examples unchanged. Inline API contract and real indexed bridge fixture provide focused coverage. |
| Other widgets/templates/examples | No direct inline consumer found. Ordinary media definitions preserve their existing presentation defaults. |

Evidence under `artifacts/discord-call-media-polish/`, re-read from result files:

- `inline-widget-tests-2.log`:993 checks; `inline-sdk-tests.log` and
  `inline-bridge-authority.log`:one named contract/authority scenario each.
- `inline-lifecycle-7`:inline26, audio9, animation12; includes12 hide/reopen
  cycles, source retirement, late results and zero resident players after cleanup.
- `inline-indexed-visible-6b/result.json`:12 checks through real worker/bridge
  and native indexed row. The earlier `6` inspection timeout is not a pass.
- `inline-discord-native-4`:compact38/expanded46, including chat-switch frame
  sampling; `inline-discord-focus-5/minimum`:38 after outline correction.
- `inline-release-host-build.log`:trimmed Release with validation disabled;
  `inline-release-verification.log`:317 managed dependencies and2/2 cleanup locks.
  `candidate-package-34-backend.log`:256 exact packaged backend tests.
- `inline-candidate-34/launch.json`:host/config/PID47172 and .34 package hash.
  Reviewed isolated update preserved optional permissions, default profile and
  sign-in. Explicit full-trust enable completed after the required disabled update.

Live computer-use smoke observed the existing Sanky MP4 as actual game pixels
inline and expanded, default mute, Escape back to the same visible chat slice,
and Servers -> Messages returning to idle Play controls. The short clip finished
before section departure: active-audio interruption is established by the native
fixture, not this live sequence. UIA reported the outer pane after mouse expansion
and Escape; do not infer exact keyboard/controller return focus from that live
observation. No calls joined or messages sent. The candidate remains running.

Visual critique: inline video stays left aligned with messages and offers one
clear Play target. Native controls fit the compact media width; expanded playback
has no redundant filename ribbon. The tall player can occupy most of the minimum
chat viewport, so final enlarged-text and active-call density still need review.
History transition evidence is recorded above rather than inferred from settled
captures. Full goal acceptance remains open in the completion audit.

### Final inline layout matrix: light150% and high contrast

`Test-Messages.ps1` ran serially against the existing matched validation host7,
fixture payload and isolated runtime, with `-MediaMessages -SolidSurface`:

- `inline-light-150-8`:Mineral Light, TextScale1.5, minimum38/expanded46 checks.
- `inline-high-contrast-8`:High contrast, minimum38/expanded46 checks.

All168 assertions pass, including expanded frame-sampled chat switching,
gallery/modal scope and return, source-free inline Play, and no unsolicited
message/call/upload. These fixtures intentionally use unavailable synthetic
remote media: screenshots qualify layout/loading/fallback, not decoded pixels.
Actual decoded MP4 evidence remains the lifecycle/native/live checks above.

Screenshots reviewed: light compact/expanded inline, compact Servers and expanded
gallery; high-contrast compact inline, expanded chat and Servers. Play remains
reachable and its focus outline fully fits. Composer and call actions remain
visible. At420x360 with150% text and an active call, the viewport shows only part
of the video and little message context; long call/server labels truncate. This
is a recorded density limitation, not evidence of full visual polish. The
high-contrast fallback has no colored poster surface, but its explicit outlined
Play target and controls remain clear. The gallery loading screenshot does not
establish fullscreen/media visibility or idle chrome behavior.

The idle live candidate was closed for these checks and the same .34 trimmed
Release was relaunched using its original profile/catalog config. Receipt:
`inline-candidate-34/relaunch-after-theme-checks.json`. No production package,
system theme/text settings, account, call or message was changed by these fixtures.
GIF worst-case resource qualification remains open: source bounds are not runtime
proof, and an arbitrary new frame cutoff was not introduced to satisfy the audit.

### GIF stress evidence exposed late visibility subscription defects

Native generated-media testing reproduced two host lifecycle failures, not a
Discord provider issue. A decoded 10,000-frame GIF could remain paused with its
element loaded, visible and admitted: `gif-resources-tiny-10000-5/result.json`
records no effective-viewport callback after subscribing. A deliberately delayed
source/replacement then reproduced a second case with a valid viewport but
`IsLoaded=false`: `gif-delayed-lifecycle-8.json`.

The WinUI implementation explains both observations. At upstream commit
`371729d1c1c5816b67cabeafb44456510d949b8f`,
[`uielement.cpp`](https://github.com/microsoft/microsoft-ui-xaml/blob/371729d1c1c5816b67cabeafb44456510d949b8f/dxaml/xcp/core/core/elements/uielement.cpp)
suppresses unchanged effective-viewport notifications; subscribing does not replay
the cached value.
[`FrameworkElement_partial.cpp`](https://github.com/microsoft/microsoft-ui-xaml/blob/371729d1c1c5816b67cabeafb44456510d949b8f/dxaml/xcp/dxaml/lib/FrameworkElement_partial.cpp)
defines IsLoaded using pending Loaded-event delivery as well as active-tree state.
[`eventmgr.cpp`](https://github.com/microsoft/microsoft-ui-xaml/blob/371729d1c1c5816b67cabeafb44456510d949b8f/dxaml/xcp/core/dll/eventmgr.cpp)
can add an already-active object to that pending list when a listener is added.
Source research explains the reproduced cases; it is not standalone runtime proof.

`Presentation/NativeMotionVisibility.cs` now separates element-lifetime viewport,
load/unload and size observation from source-lifetime playback policy. A weak-key
table retains only the element's native viewport/attachment state, not a bitmap,
source URI, decoder or widget authority. Standard artwork, presentation surfaces
and the media viewer's image prepare this observation before their first layout;
native artwork demand also prepares it before resolving its source. Replacement
animation owners reuse current geometry. Their policy/current-authority checks,
root visibility, hidden-parent checks and retirement remain enforced. No polling,
forced scrolling, layout nudges, provider counters or new SDK/protocol API.

Ownership/consumer matrix:

| Consumer | Assessment and verification |
|---|---|
| Discord GIF artwork and image gallery | Affected; corrected by host observation lifetime. Native delayed/replaced-source and real-pixel cases reproduce then pass. |
| Ordinary image artwork in Playnite, PoB and other shipping widgets | Static images acquire no animation controller or decoder. Their realized elements retain a small geometry observation for possible source replacement; real Playnite artwork-scroll comparisons are recorded separately. Full live PoB/other-widget acceptance not rerun. |
| Background/focus presentation surfaces | Prepare the same mechanism at construction; no provider policy moved. Source audit confirms the entry point, not every live consumer. |
| Capture Library/Game Help/SDK Gallery image players | Shared viewer prepares its image before loading. Ordinary media/audio and image-animation regressions pass; capture custody unchanged. |
| Inline/passive native video | Existing visibility observer uses the same state source. Inline26 and video-animation12 native regressions pass. |
| Templates/examples | No new API or author migration; standard renderer entry points inherit the fix. |

Final native evidence under `artifacts/discord-call-media-polish/`:

- `gif-delayed-lifecycle-9.json`:37 checks, including three source replacements
  in an already arranged viewport, reduced motion, explicit Play/Pause, hidden
  image viewers and retained-authority retirement.
- `gif-visibility-media-lifecycle-9`:inline26, audio9, passive video12.
- `gif-resources-{tiny-10000,standard-120,large-600,large-canvas-deltas-10000}-9`:
  27 checks each,108 total. Six acquisition/play/suspension/release cycles per
  case; all six decoded images and artwork elements become collectible. The
  external768-MiB/90-second watchdog never fires. Tools and interpretation are in
  `tests/WinUiShell.Tests/GifResourceFixtures.md`.
- Final process-private peaks:216.9,132.3,147.9 and294.5 MiB respectively;
  baselines117.7,114.8,116.3 and118.7 MiB. Released private bytes do not return to
  startup values, so do not label process allocator retention a proven absence
  of all native retention. Actual image/view collection and stable suspended
  pixels are directly asserted. `gif-resources-final-summary.json` records values.

Earlier direct-decoder fixtures1-3 did not follow the production demand sequence
and never reached animation. Their failures remain preserved, not counted as
resource passes. Production-demand cases4-5 established the missing viewport;
the delayed-source test then caught the additional Loaded-state case. The first
sample was removed from the validation stack before subsequent tests to avoid
testing an accidentally clipped viewport. Diagnostic trace hooks were removed.

Resource limits are not silently tightened to make this audit pass. These short
runs exercise the beginning of each file and repeated teardown, not every frame
or every compression/disposal mode. Upstream `WicAnimatedGifDecoder.cpp` retains
current/saved composition surfaces and a delta, while `AsyncImageDecoder.cpp`
schedules frames incrementally; that supports keeping the native path, but is
not proof of every shipped codec/driver's worst-case allocation. The encoded,
canvas/delta-pixel, timeout and admission bounds remain; animated WebP/APNG remain
unimplemented feasibility work. Full goal completion is still unproven.

### November19 visual refinement: .35 applied, live smoke completed

Latest user decisions supersede the earlier thumbnail/card dimensions:

- Expanded chat must retain its own quality; compact layout is a separate
  adaptation and regression surface, not the design ceiling.
- Inline video is larger but must not fill the whole chat column. The existing
  star-grid aspect-ratio mechanism now caps it at480x270 DIPs and shrinks it to
  available width. The host removes surplus inline transport padding/card chrome
  while retaining its native control hit targets and controller commands.
- Attachment groups contain up to two larger static tiles and `+N` for extra
  gallery items. No visible "View media" title or filename summary. One existing
  ActionSurface opens the host gallery; accessible labels retain identification.
  Tile IDs use the existing `WidgetIds.Scope(...).KeyedId(...)` API so provider
  occurrence keys are retained without parsing labels or hand-built identifiers.
- Presence glyphs now have a dedicated rounded badge background and SVG inset
  rather than drawing to the raster boundary. Themed colors and non-color shapes
  remain; status policy still belongs to Discord and unknown status is omitted.
- The gallery omits its duplicate modal header and filename ribbon, retaining
  compact arrows/counter. Fitted-image zoom controls sit in a stable footer
  outside the picture; fullscreen retains visible arrows without a title.

Shared ownership and consumer check:

| Change / source | Owner, applicability and evidence |
|---|---|
| `WidgetModal.ShowHeader` in SDK composition | Small additive SDK capability, default true. Discord gallery adopts false; title becomes the modal accessible name. Other shipping/sample/template modal callers retain their previous tree and dismissal defaults. Scoped modal scenario and17 compatibility tests pass; native Discord verifies scope/return. No protocol extension. |
| `MediaPlayerView.Gallery.cs` counter and `Chrome.cs` fitted-image footer | Host owns gallery controls, not widget-generated toolbar state. Discord is the only shipping `UI.MediaGallery` caller found. Native decoded gallery checks24 validate image/video replacement, footer geometry, fullscreen and return. |
| Ordinary Capture Library, Game Help, SDK Gallery media | Ordinary players retain overlay chrome because no gallery is declared; source authority/capture custody unchanged. The latest shared template adds a zero-height unused row for these players. Current-build shared image/audio/inline/passive-video smoke now passes56 checks; this is shared-player evidence, not a live sweep of every consumer. |
| Inline sizing and attachment tiles | Widget design policy adopts supported explicit-grid sizing; host retains measurement, input and media lifetime. No widget-owned pixel observers or playback plumbing. Expanded49/compact40 native assertions cover sizing, source-free Play and gallery return. |
| Static avatar/presence badges | Widget supplies domain status and badge composition through existing Grid/Icon APIs. No new host drawing primitive or focus stop. Native Canopy/light/high-contrast screenshots reviewed. Live photo-avatar acceptance remains pending. |

Framework precedents:
[WinUI dialog guidance](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs)
explicitly permits an omitted visual title; the SDK keeps a separate accessible
name and unchanged dismissal contract.
[Android TV PlaybackSupportFragment](https://developer.android.com/reference/androidx/leanback/app/PlaybackSupportFragment)
separates playback from interaction-driven control visibility. This pass retains
the existing host timing/focus policies; it does not add widget timers or force
a focused control to disappear.

Visual review found and corrected two additional issues. A fixed480-DIP tile
group with `max-width:100%` overflowed compact chat: the current native length
projection does not implement percentage maxima (`WidgetViewPresenter.Layout.cs`,
`Length`). The scoped fix uses supported `width:100%` stretch plus a pixel maximum;
the broader parser/renderer contract mismatch remains a framework audit finding,
not a reason to lower expanded dimensions or add widget resize arithmetic.
The first high-contrast capture lost unavailable-tile outlines, so each tile now
has a subtle themed border. SVG badges retain a small transparent inset for edge
antialiasing. Screenshots use real WinUI layout; fixture media URLs deliberately
fail, so thumbnail/photo quality still needs the live candidate.

The screenshot fixture also compared future icon realization after PNG encoding
against already-captured pixels. Its saved capture receipt showed icons ready
while the later assertion saw a newly realized icon. The assertion now uses the
same frozen icon state that produced the image; readiness requirements remain.
The failed receipt under `media-design-native-35` is retained.

Evidence under `artifacts/discord-call-media-polish/`:

- `media-design-35-tests-2.log`:993 managed checks. The initial run caught invalid
  concatenated key segments; adopting SDK keyed IDs fixed that authoring error.
- `media-design-35-modal-tests.log`:focused modal scenario passes;
  `media-design-35-sdk-compat.log`:17 compatibility tests pass.
- `media-design-expanded-35b`:49 native checks; `media-design-minimum-35b`:40.
  `media-design-light-150-35` and `media-design-high-contrast-35`:40/49 each.
  `media-design-high-contrast-final-35b`:49 with final tile borders. Captures cover
  main screens, gallery loading, row/action return and chat transition frames.
- `media-gallery-design-35b-run/result.json`:24 decoded gallery checks. The first
  launcher used a file path where that fixture expected a directory and was
  stopped early; it is not a pass. Final footer geometry is asserted against the
  actual image viewport rather than inferred from screenshots.
- `media-design-release-verification-35.log`:317 local managed references and
  matching2/2 async cleanup locks in `media-design-release-host-35`, trimmed with
  validation disabled. Temporary GIF diagnostic hooks are absent.
- `candidate-package-35-final-backend-all.log`:256 packaged/minified checks.
  Final package review keeps required permissions empty and optional capture
  unchanged. SHA256:
  `dfbcc53a7178590251a43b651535a961366d6b49772b796644d3ecd341ce1a04`.

The native-scoped Impeccable review preserved existing WRSS/theme conventions;
its mechanical scan returned no findings but cannot establish native quality.
Smallest150% text with an active call remains cramped; scrolling and direct
controller actions stay available. Do not substitute compact for expanded review.

Computer Use initially reported a physical Escape stop. The user subsequently
authorized resuming desktop checks and updating the candidate. The reviewed .35
package is now applied/enabled in the isolated catalog; the matched trimmed
Release host is running as PID54060 with the default profile. See
`candidate-package-35-{disable,apply,enable}.txt` and
`media-candidate-35/launch.json` for receipts.

Live review inspected Servers and Messages, actual photo-avatar presence badges,
the authorized Sanky chat, and two real attachment tiles. Both posters are
visible, the video tile has a Play marker, and there is no duplicate filename or
View media card. The480x270 inline game video decoded and reached its end; its
muted transport occupies a smaller lower portion of the picture. Gallery image
controls appear below the picture. Next/previous switch image/video without
dismissing the modal; fullscreen keeps arrows without the filename. Escape
returns from fullscreen, then dismisses to the same visible attachment message
with its row outline restored. UIA returned the outer pane, so exact keyboard
focus identity is not claimed. No call joined or message sent.

The live snapshots are point-in-time observations, not frame-sampled transition
proof. Initial immediate post-click captures showed the old page; subsequent
captures showed successful navigation. This observation does not establish an
application latency measurement. The existing native frame-sampled fixture
results remain the bounded transition evidence.

`media-design-lifecycle-35-live-followup` adds current-host inline26, per-widget
audio9, passive video12 and remote image9 passing checks. The image fixture
decodes HTTPS artwork, exercises zoom/fit/fullscreen/B, rejects stale resolution
and unexpected local-file authority. No rebuild or repeated broad suite was
needed. Keep the goal active for the remaining peer/read-state, pin, physical
controller and long-duration stability gates.

At the end of this smoke pass, process54060 remains running at the verified .35
Release path. The overlay is hidden and Computer Use no longer exposes its
window. No additional live outside-click dismissal or call action was attempted.
The candidate was not replaced or restarted again merely to obtain that check.

### November19 live defect follow-up — not yet deployed

At the start of this follow-up the user candidate was .35, PID54060 (superseded
by the diagnostic host recorded below). Do not describe the following
source changes as installed. The user reports three further failures beyond the
gallery smoke scope: participant previews repeatedly close on speaking changes;
the left conversation list flashes on chat switches and overlay reopening;
continuous right-stick scrolling rubberbands after older pages arrive. The user
also reports repeated history-load failures and requires equivalent downward
scrolling coverage. Composer focus is correct; this is not a focus-entry report.

**Live test authorization:** use TriggerFinger for reading/scrolling tests only.
Never send messages or make calls to TriggerFinger. The .35 UI visibly displayed
the reported history error during this read-only check. Its provider cause is
not yet established; the earlier narrow prepend checks do not qualify continued
input during asynchronous publication. Older Test-server/Sanky authorization
does not override this explicit restriction.

Participant reproduction and fix:

- `participant-speaking-baseline-1/result.json` fails the new native-frame hover
  continuity check across real synthetic-worker speaking publications. The trace
  clears the preview during a content-revision authority gap, with the same
  query, visible anchor and participant. Avatar bounds show0.8 DIP horizontal and
  0.4 DIP vertical offset at125% scaling.
- `WidgetViewPresenter.Previews.cs` now retains paint for the exact hovered or
  focused occurrence during that gap, as it already did for entered previews.
  It does not rebind retired content to the new frame or admit stale input.
  `PreviewPointer.cs` retains the resolved native anchor rather than a replaceable
  child label/image. Query retirement, hidden/recycled sources and real departure
  still revoke the preview. This is a host lifecycle gap, not Discord policy.
- Participant/voice avatars use the existing explicit Grid centering contract,
  removing padding whose rounding crowded fixed-size children. No new control
  or widget-owned positioning observer.
- `participant-speaking-fixed-1/result.json`:10 native checks pass, including
  hover frame continuity, exact entered mute/slider focus, held mouse capture,
  controller adjustment and publication gaps. Avatar bounds are32x32 at(4,4)
  within40x40, center error0. Current evidence is synthetic; live .35 is unchanged.

Sidebar source finding: the provider's full catalog version changes with
presence/snippet/unread metadata; `ConversationCollection` previously interpreted
every such version as ordered-query replacement. Additive provider `orderVersion`
describes exact filtered ordered membership while retaining full `version` checks
for paging consistency. Discord adopts existing SDK `UpdateContent` when order
and count match. Missing order metadata retains the conservative prior behavior.
Provider71 and managed995 checks pass (`conversation-order-provider-2.log`,
`live-defects-managed-1.log`); final native/live sidebar continuity is pending.
The synthetic provider now changes full versions on unread metadata too, and the
native screen test checks ItemsSource identity, not just the outer collection.

Consumer applicability: shared preview changes affect Discord participant rows,
Capture Library media previews, Playnite, PoB, Marketplace and SDK Gallery preview
consumers. Source identity/lease rules remain generic; no consumer migration is
required. Participant positive coverage is recorded above; shared query-retirement,
ordinary/deferred-preview negative coverage and final screenshots remain gates.
YouTube Music/Spotify/Settings do not declare these preview surfaces in the
repository search. Templates have no affected direct preview caller.

Resource work before these reports: finite1,200-frame GIF fixtures now verify a
distinct terminal frame after two minutes, then six retire/collect cycles.
`gif-endurance-full-2/result.json` passes30 checks, terminal at120078ms versus
119900ms authored, all six images/views collected. The first run failed with a
native cleanup exception that obscured its origin; it is retained as a failed
receipt, not explained away. Test reporting now preserves primary/cleanup errors.
The large-canvas restore-previous endurance case has not run. Production decode
behavior is unchanged; no universal memory or all-codec claim is made.

Continuation evidence: the current diagnostic process is53636 at
`live-defects-host-1`, with `live-history-diagnostic-1/shell.json`; widget .35
remains installed. The user saw no history-loading error here. The short probe
expired without capturing a useful failing request; loading-error investigation
is deferred at the user's instruction, with cause unknown.

`live-defects-media-screens-1` passes40 compact/49 expanded checks, including
native conversation ItemsSource identity across chat switches and unread updates.
Inspected expanded chat/Servers and compact chat captures: participant ring is
centered, sidebar hierarchy is legible, expanded media stays capped, and the
compact view remains very tight with an ongoing call. Fixture artwork fails by
design, so fallback tiles do not prove poster decoding. Overlay reopen continuity
and shared preview retirement regressions remain unverified.

The corrected held-scroll test waits for initial latest-message positioning.
`held-scroll-settled-prefetch-full-1` records18 wrong-direction events over2272
upward frames with5 older reads, and2 over2452 downward frames with3 newer reads.
That host included a failed prefetch/mode experiment, since removed. Longer
native-mode retention and UpdateLayout fences also failed and were removed.
No production rubberband fix is claimed. Compare plain ListView incremental
updates against the host's bounded windows, delayed row realization and pending
absolute scroll destinations before making another production correction.

### Native chat comparison and pending-destination correction

Added a validation-only plain `ListView`/`ObservableCollection<string>` scenario
(`WidgetStylesValidationPage.NativeChatScroll.cs`, runner
`tests/WinUiShell.Tests/Test-NativeChatScroll.ps1`). It uses variable-height
messages,100ms delayed20-item pages, a40-item bounded window and held up/down/up
input. It has no widget bridge, query recovery, row leases or host placeholders.
This is a controlled comparison, not a replacement chat implementation.

- `native-chat-direct-1`: native immediate scrolling passes all three directions,
  4/3/3 page reads and0 wrong-direction samples.
- `native-chat-direct-animated-1`: native animated scrolling also passes,
  3/2/2 reads and0 wrong-direction samples.
- `native-chat-accumulated-animated-2`: adding the host accumulator produces one
  wrong-direction event on reversed-up input. The previous attempt did not reach
  input because native initial estimated extent had not settled. Setup now waits
  for the measured tail; that failed result remains recorded.
- `held-scroll-request-trace-1`: current host baseline reproduces14 upward and1
  downward wrong-direction events. At5806ms upward input proposes1027.20 while
  actual offset is436.68. The native-only trace independently shows the same
  stale-destination mechanism after extent changes.

`ScrollOffsetAccumulator.Propose` now discards the portion of a pending target
that lies behind the user's current direction. Queued same-direction deltas
continue accumulating; bounds, rounding and existing gesture ownership remain.
Owner: host. This is a reproduced generic input-coordinate defect, not a Discord
workaround or new SDK abstraction (low implementation complexity). The precedent
is native ListView anchoring: layout may change coordinates independently of an
outstanding absolute ChangeView destination. This correction alone does not
qualify chat paging: `held-scroll-request-fixed-1` still finds3 upward and1
downward event, now without opposite-direction requests, and
`native-chat-accumulated-fixed-1` still has one row-measurement transition event.
Do not describe those runs as passes or extrapolate counts into a performance
improvement; timing varies between runs.

Two new numerical regressions fail before the correction; all6 accumulator
checks pass afterward (`scroll-accumulator-{before,after}.log`). The existing
reversal test now explicitly resets the gesture exactly as ScrollBy already does,
instead of encoding an opposite-direction pending target as expected behavior.
`native-scroll-preview-regressions-1` passes74 native scroll/focus/lifecycle
checks and61 interactive-preview checks, including interruption, directional
return, source removal, drag teardown and late admission rejection. This
qualifies those bounded shared behaviors, not the unresolved history jump.

Shared applicability (no widget migration or public API change):

| Consumers / entry path | Assessment |
|---|---|
| Discord history/conversations; Playnite Library native grid/rails; Capture Library; Games & Apps; Settings indexed lists | Existing shared right-stick ScrollBy path adopts host correction; Discord paging remains unqualified. |
| PoB native scroll/collections; Spotify and YouTube Music native surfaces; YouTube and FullApplication sample native collections | Existing host input mechanism; no widget counters or scroll bookkeeping added. Shared native regression coverage, not individual live acceptance. |
| Other first-party native scroll surfaces (Marketplace, AudioMixer, MediaSessions, NetworkControls, PerformanceMonitor), SDK Gallery and ordinary SDK scroll elements | Same renderer path when a native viewport is active; covered at mechanism level. |
| Embedded browser scrolling, fullscreen image pan, shell utility list and popup-owned scroll routing | Separate routes before ScrollBy/accumulator; not changed. |
| Templates and non-scrolling sample surfaces | No direct accumulator use or author migration; generated native scroll content inherits host behavior. |

The runtime entry is `OverlayShellPage.Input.cs`; only the host presenter owns
`ScrollOffsetAccumulator` in production. Repository-wide searches covered
samples, first-party widgets, templates, SDK elements and relevant tests. No
provider-specific pagination or account policy was moved into the host.

The occurrence trace also corrected the downward assessment:
`held-scroll-occurrence-trace-1` records13 samples whose viewport anchor index
is-1 after trimming. WinUI keeps recycled containers loaded briefly; the host's
`VisibleOccurrences` included these logically removed slots. The apparent
20-message backward jumps all used such retired anchors, so those samples do
not prove equivalent visible-pixel jumps. This is nevertheless a real host
tracking defect: a removed occurrence must not become a saved reader position
or protected boundary key. `WidgetIndexedCollectionView.Memory.cs` now excludes
negative/out-of-range indices while preserving retained rows during lease refresh.
This uses the existing slot retirement marker, not a new widget contract.

`held-scroll-valid-occurrences-1` (host5) verifies0 invalid anchors and0 missing
anchors in both directions. Downward scrolling has0 wrong-direction samples
over2487 frames and2 page reads; upward still has1 over2381 frames and3 reads.
The upward failure has valid identities and is a confirmed unresolved prepend
jump. This source state is not installed in the live diagnostic candidate.

Re-evaluated half-viewport held-input prefetch and limiting alternate native
anchoring to offset zero after those corrections. The host6 experiment still
failed (`held-scroll-prefetch-corrected-1`:2 upward events, plus2 upward/14
downward frames without a ready anchor). Both experimental edits were removed;
host5 is the latest diagnostic binary matching current production scrolling
source. Merely starting the request earlier does not make asynchronous empty-slot
publication equivalent to native insertion of ready rows. Next investigation:
separate native insertion from delayed row acquisition/measurement, preserving
authority and bounded demand; do not add another saved-position replay or ship
prefetch as a completed fix. Loading-error investigation remains deferred.

Next lifecycle comparison: `CancelInputContinuity` now drops the saved
viewport/focus intent while preserving an in-flight native insertion anchoring
mode until the existing demanded-row/layout completion retires it. Input no
longer switches WinUI's edge interpretation between collection notifications and
arrange. `held-scroll-layout-lifetime-1` passes the expanded full-motion run:
2077 upward/2556 downward frames,4/3 page reads,0 wrong-direction, invalid or
missing anchors. This is not yet compact, slow-response or teardown qualification.
Diagnostic hosts `native-chat-layout-lifetime-host-{1,2}` include this source
change; host2 adds stronger validation and optional live reversal.

The next slow-response/reversal run exposed an unexpected loading error:
`held-scroll-slow-reversal-1` failed because downward requests stopped reaching
the provider. The user supplied a screenshot of its visible “Could not load
messages. Try again.” state. This error was NOT injected: the synthetic provider
only delays successful history responses by500ms (default100ms). The run's audit
has the initial fetch and3 older fetches, no newer fetches, and0 mutations.
The ordinary message-history error mapper displayed the retry state. Its caught
exception has not been identified. The prior deferral was conditional on finding
no diagnostic reproduction; this new evidence warrants a bounded investigation
to distinguish an app/SDK failure from a synthetic provider contract failure.
Do not claim it proves the live Discord error has the same cause. The fixture
closed normally; the user's diagnostic host1/widget .35 remains untouched.

### Slow paging, lifecycle and shared regression follow-up

The slow failure is not yet explained. A fixture-only, bounded first-chance
exception observer records cursor/history exceptions in `cursor-exceptions.jsonl`
before the normal user-facing mapper hides their details. It runs only in the
account-free synthetic worker, not in the installed widget. The instrumented
`held-scroll-slow-exception-1` passes expanded up/down/up with500ms replies and
immediate reversals; no error recurred. Do not equate that pass with fixing the
previous visible retry state or the live Discord error.

`held-scroll-compact-slow-1` found one frame without a reading anchor but no
backward movement. Its trace also showed a same-direction oversized scroll
after native extent remeasurement. `ScrollOffsetAccumulator` now invalidates
pending absolute destinations when the native scrollable extent changes, in
addition to rejecting opposite-direction destinations. Two new numerical
regressions fail before this change; all8 pass afterward
(`scroll-extent-{before,after}.log`). `held-scroll-compact-extent-1` passes all20
checks with500ms replies and up/down/up, including no missing/retired anchors
and native anchoring teardown. No animation or motion preference was removed.

The new native history-lifecycle scenario cancels a pending500ms load through
the real shell's hide/background path, resumes, checks the retry state, confirms
the same native conversation ItemsSource, and loads another page. Cancellation
is correctly classified; only expected TaskCanceledException records appear.
It exposed an existing readiness overconstraint: item zero remains a current,
loaded native container offscreen but has no registered renderer layout, so an
all-demanded-container gate never finished. `history-hide-reopen-diagnostic-1`
records settled data, ready visible anchor1101, and that sole unresolved1081
container. It is not a removed/recycled container (`obsoleteContainers=0`);
the attempted native-container identity filter was therefore removed.

`WidgetIndexedCollectionView.Continuity.cs` now uses ItemsStackPanel's native
visible indices: missing renderer layout is required for visible rows, while
loaded cache renderers still must complete their layout. An offscreen container
without a registered renderer does not hold the whole native mode indefinitely.
`history-hide-reopen-settled-1` passes15 checks and records nativeMode=null,
no pending continuity, and settled data. Its screenshot was inspected: retained
sidebar, readable left-aligned messages, composer and call dock are intact.
This is a host lifecycle correction using the existing native control contract;
the same consumer matrix above applies, with no widget migration.

Shared native `layout-lifetime-indexed-regressions-1` passes continuity16,
end-following34 and deferred-preview40. The ordinary preview lane stopped at an
unrelated exact-zero offset assertion after successfully closing its hovered
preview. A comparison against the older .35 host fails at the identical check;
current diagnostics show offset353.2 after estimated rows materialize, with the
old occurrence offscreen and its preview closed. The fixture now waits for the
actual requirement—loss of that occurrence's visible anchor authority and a
closed preview—while retaining all subsequent stale-query/recycling checks.
Its full rerun remains required; do not count the earlier eight-check partial
run as a pass.

Current source diagnostic build: `native-chat-layout-lifetime-host-7`. The latest
matched compact/expanded slow-reversal qualification is running under
`held-scroll-qualified-slow-1`. The live user process remains the old diagnostic
host1 with widget .35; no new package, merge, installer or release was applied.

### Loading error reproduced with offscreen message focus

The first instrumented run with composer focus did not reproduce the error.
A new focused-row case starts the same native held-scroll scenario with message
1120 focused. `held-scroll-focused-error-1` reproduces it deterministically:
the SDK throws `InvalidOperationException: Visible collection exceeds the
configured retained capacity` in `WidgetCursorResource.Merge`, and the widget
shows its normal generic history retry state. The provider received successful
initial/before1101/before1081 requests and then paging stopped. No account
mutation occurred. This establishes a concrete loading-error cause without
Discord network involvement; it does not prove every previously reported live
failure has that cause.

The host's `BoundaryProtectedKeys` added the focused message even when it was
offscreen during free scrolling. That pinned an unrelated page in the40-message
window, making the next20-message page impossible to admit. The fix protects
actual visible occurrences, which already includes visible focused messages.
The SDK's retained-capacity invariant remains unchanged. No widget-specific
retry, larger window, or exception suppression was added. Owner: host; reproduced
failure. All boundary-observing indexed consumers inherit the correction; the
existing consumer matrix applies. Source change is in
`WidgetIndexedCollectionView.Boundaries.cs` and native fixture coverage is
`FocusHistoryBeforeScrolling`. Diagnostic host2 under `native-chat-focus-paging`
contains the fix; focused compact/expanded reversal qualification is in progress.
The previously prepared shipping host .36 predates this last correction and must
be rebuilt before deployment. The managed .36 package is unchanged.

Other completed evidence: `layout-lifetime-final-regressions-1` passes all104
indexed checks (preview14/continuity16/end-following34/deferred-preview40).
`qualified-media-screens-36` passes40 compact/49 expanded checks; inspected
Servers, switched chat, inline video and compact chat captures preserve the
approved hierarchy, centered speaking ring, capped video and fixed composer.
`discord-36-managed-tests.log` passes995 checks; backend component tests pass324
with supported minification paths, and the packaged backend passes syntax checks.

The remaining large-canvas GIF endurance case now passes30 checks:
`gif-endurance-large-canvas-1` reaches its expected green terminal frame at
120213.8ms (119900ms authored), with6 collected images after teardown cycles.
The watchdog records peak private memory275.7MiB and no watchdog failure.
The longest measured heartbeat was347.5ms over the whole fixture; this does not
establish smoothness for every codec, device, or simultaneous playback load.

Computer Use smoke in `manual-controller-chat-36` opened the synthetic inbox,
opened its attachment gallery, dismissed with an actual Escape key, and scrolled
the chat up/down with wheel input. Inspection showed the retained sidebar,
returned attachment row and inline video moving with the reading viewport.
These are point-in-time desktop observations, not frame-sampled proof of every
transition. The synthetic audit records0 mutations and the expected1120 read
acknowledgment. The owned fixture PID38240 was closed normally; the real user
candidate was not touched. UIA reports an outer pane, so exact keyboard-focus
identity comes from the native fixtures, not this desktop smoke.

### Focused-row paging qualification follow-up

The initial ownership correction preserves the existing scroll gesture across an
in-place native-source rebase, resetting obsolete coordinates but retaining the
owner until settlement. Ordinary publication must not re-enter a parked old
message while that same collection owns ongoing scroll input. Continuity capture
also excludes a newly captured offscreen focus target; already admitted pending
focus restoration retains its previous contract. These are host fixes, with no
widget-specific counters, cache increase, or retry suppression.

`held-scroll-focused-owned-1/minimum` is a failed qualification, not a pass. Its
420-pulse legs only traversed part of the variable-height retained window: the
first leg fetched one older page, never evicted the focused page, and the reverse
leg never reached a newer-page boundary. The runner now drives to two initial
older-page requests and at least one reverse boundary, within a bounded budget.
It also clamps the diagnostic ordinal fraction to the current row: negative
fractions represent leading gaps, and comparing them across unequal-height rows
created a false backward report even as native offset and message order moved
upward. Raw fractions, offsets, extents and occurrence identity remain recorded;
no shipping viewport geometry was changed by this measurement correction.

Applicability of the focused-row changes:

| Consumer | Applicability / qualification |
|---|---|
| Discord history | Only shipping `OnBoundaryDemand` / `AdjacentPages` consumer found; directly affected by protected keys and scroll ownership. Focused compact/expanded delayed tests are required. |
| Discord conversations/pickers/participants, Spotify queue, Playnite achievements/related captures, PoB passive groups | Ordered occurrence updates can share in-place rebase/focus mechanics. No widget migration needed; native continuity, entry and end-following regressions must pass. Full live acceptance of these other widgets is not claimed. |
| Other indexed lists, grids, SDK examples/templates | Shared input lifecycle remains applicable; true source/view replacements still retire scroll ownership. No adjacent-page consumer found requiring domain changes. |
| Plain scroll viewers / browser content | No indexed-source rebase; existing input paths retained. |

The existing .35 live process remains unchanged. The prepared .36 shipping host
must be rebuilt after qualification; it predates the focused-row fixes. Do not
deploy the experimental validation hosts or describe the live loading problem
as fully resolved solely from the synthetic reproduction.

`held-scroll-focused-boundaries-1` reaches actual eviction and newer-page loading
with no cursor exception, but fails downward readability (two frames without an
anchor and a backward sample). `held-scroll-focused-reader-1` tests retaining
reading ownership while focus is parked: backward samples disappear, but four
frames lack an anchor and the append jumps from1099 to1120. Neither is a pass.
The trace identifies a distinct native anchoring error: applying
`KeepLastItemInView` to an append at the old bottom follows the newly appended
tail. The current host chooses bottom-up tracking only when the retained anchor
shifts to a higher index (insertion before it); other ordered updates use
`KeepItemsInView`. Tracking still remains active through the layout transaction.
This uses native list policy rather than replaying widget scroll offsets.

`focused-reader-shared-regressions-1` passes continuity16, end-following34 and
entry65 on host5 (115 checks), covering the parked-reader and scroll-ownership
changes before the append-mode correction. The first plain-server run failed
to deliver its native pointer click; the production-themed fixture used for prior
qualification succeeds. The final append correction still requires its own
focused scrolling and shared checks on `native-chat-focus-paging-host-6`.

`held-scroll-focused-edges-1` now passes22 compact and22 expanded native checks
on host6. Both runs cover actual bounded eviction, newer-page loading, immediate
up/down/up reversal,500ms replies, zero missing/retired/backward anchors, native
tracking retirement and passive focus settlement without moving the reader. No
cursor exception file was produced and no account mutations occurred. This
qualifies the focused controller-route scenario, not live Discord failures or
wheel input. The next fixture variant uses the existing owned-window
`NativeWheelProbe` through `-WheelHistoryScrolling`, preserving its foreground,
pointer-interference and process-ownership guards.

Wheel follow-up found the analogous focus replay: `wheel-scroll-focused-notches-1`
moves from1079 to1099 during held Up when the old focused page retires. Wheel
input had discarded controller scroll state without retaining its own owner, so
the next publication entered remembered item39. `WidgetViewPresenter.InputRoots`
now gives indexed wheel input the existing scroll-gesture ownership. Native
motion remains WinUI-owned; explicit pointer presses, Tab, focus departure,
source/scope retirement and subsequent controller input retain their authority.
The gesture records native-wheel origin only to retire that passive ownership
on an explicit focus departure. No widget input flags or extra motion loop.

`wheel-scroll-owned-1/minimum` passes21 checks, including actual page eviction
and return paging. Expanded has zero backward/retired-anchor samples but fails
readability during fast notch scrolling. `wheel-scroll-readiness-1` confirms
newly inserted native placeholders have no range data yet (Key=null,60-DIP
estimated rows, no obsolete containers, bounded40 slots, no failed loads).
This is distinct from the corrected capacity error and focus replay. The strict
missing-anchor assertion is retained. A fixture-only bounded listener now uses
the existing `WidgetRail-PresentationPipeline` events to separate range arrival,
dispatcher queue and publication delay. No arbitrary prefetch or cache growth
has been introduced; the previous rejected prefetch experiment remains rejected.

Shared final-source coverage so far: `wheel-owner-return-regression-1` passes52
return/input checks. `focused-edges-shared-regressions-1` passes129 checks before
the wheel-owner addition; `focused-history-lifecycle-1` passes15 hide/reopen
checks. After the addition, `wheel-owner-shared-regressions-1` passes continuity16
but times out in the early deferred-reveal setup of end-following (before wheel
input); that lane needs isolated follow-up and is not counted as a full pass.
Latest validation host is `native-chat-focus-paging-host-11`; it adds diagnostics
only over host9. Nothing has been installed into the live profile.

### Remaining row-readiness work (bounded prototype in qualification)

`wheel-scroll-pipeline-1` confirms missing anchors precede range arrival. Across
29 range operations, data acquisition is median50.54/max130.29ms; the measured
completion-to-UI queue is median2.63/max34.34ms; total range publication is
median92.94/max210.38ms. These are elapsed durations, not CPU times. For example,
the gap at9026ms precedes the32-row data arrival at9032ms and publication finishes
at9130ms. Current demand remains bounded, with no failed loads or retired
containers. Merely changing publication priority cannot eliminate an absence
of data; the range-pump start queue has not been separately measured.

Proposed owner: host collection/source admission, with protocol work only if an
existing range lease cannot safely prepare the next bounded window. Complexity:
medium/high, because preparation and commit must preserve authority, cancellation,
input and native anchoring together. The target behavior is to keep the current
readable window until the inserted range is ready, then reconcile once through
the existing native collection. No saved-offset replay, unbounded history, or
Discord-specific focus flags. This is a hypothesis requiring a separate bounded
implementation and rejection if it makes lifecycle ownership more brittle.

Before implementation, establish a staged-range contract: at most one pending
successor; exact worker/surface/query authority; cancellation on newer query,
hide, teardown and worker replacement; deterministic release of admitted ranges;
old paint never authorizes old actions; user input continues to update the reading
anchor; failed preparation leaves a retryable current surface without losing
focus. Reuse current range leases and bounded demand where possible. Test delayed
and out-of-order results, reentrant publication, duplicate occurrences, repeated
up/down, explicit focus departure, memory/lease bounds and native placeholders.
Check the ordered-window consumer matrix above, retaining ordinary large-list
virtualization. Do not substitute eager loading of every indexed collection.

Framework precedents reviewed: Android Paging separately exposes placeholder,
prefetch and memory-bound policy in [PagingConfig](https://developer.android.com/reference/androidx/paging/PagingConfig).
That is precedent for explicit presentation policy, not evidence that our bridge
can reuse Android's implementation. WinUI's [ItemsUpdatingScrollMode](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.itemsstackpanel.itemsupdatingscrollmode?view=windows-app-sdk-1.8)
defines first/last visible-item anchoring; it does not acquire widget data before
native insertion. The current correction uses that supported native policy.

Final wheel-owner shared checks: continuity16, entry65, preview14 and return52
pass in their named receipts. End-following passes34 on baseline host6 and again
on diagnostic host12. Two intervening runs timed out in deferred/cold reveal
before wheel input, at different checkpoints. Preserve those failures and record
the nondeterminism; do not claim a fix from the successful rerun. Host12 adds
navigation/layout/pending-offset facts on future failure. Latest live process
is still PID53636, with .35. No merge, install, restart, installer or release.

The final controller pass `final-focused-controller-scroll-36b/minimum` also
finds one missing-anchor frame on the first prepend (query3), while all three
directions have zero backward/retired-anchor samples and actual older/newer
requests succeed. Therefore the remaining preparation gap is not wheel-only.
Do not rerun until a pass and silently discard this receipt. The next step is
the bounded staged-range contract above, or a narrower evidence-backed mechanism
that preserves the same authority and lifecycle guarantees.

Prepared non-validation Release host: `prepared-release-host-36b`, paired with
the unchanged reviewed .36 widget archive. ReleaseVerifier passes317 references
and2/2 cleanup locks; the shipping assembly lacks the named native wheel,
messages, pipeline-listener and continuity-diagnostic hooks. It is prepared for
later acceptance, not a fully qualified candidate or an installed update. The
strict readiness tests remain failing and no user call was interrupted.

### Prepared-row implementation and initial evidence

The next continuation implements the bounded prototype using existing indexed
range leases; no widget API or protocol extension was needed. New
`Collections/PreparedIndexedWindow.cs` owns the successor's ranges until commit,
validates full bounded coverage and exact query/revision/occurrence identities,
and releases rejected/cancelled preparation. `IndexedItemsSource.Window.cs`
transfers those lifetimes before mutation and populates inserted slots before
their Add notifications. Surviving slot identity remains intact. Structural
callbacks now stop a superseded/disposed transaction rather than continuing
after reentrant retirement.

`WidgetIndexedRows.Window.cs` acquires one range at a time through existing
provider admission and session budgets. `WidgetIndexedCollectionView.PreparedWindow.cs`
coalesces to one pending successor, drains cancelled work before preparing the
latest, captures the reader at commit (not request start), and cancels/drains on
hide and teardown. Existing viewport recovery supersedes preparation. Real
preparation failures fall back to the existing range/retry path rather than
stranding retained paint; stale query authority is rechecked with the existing
protocol resolver. No old row gains current action authority while waiting.

The scope is deliberately **incremental paged updates**: it requires declared
boundary observation, the existing bounded ordered-window contract, an active
unchanged input surface, and no new explicit focus/reveal request or outstanding
navigation. Busy/disabled publications and intentional focus/Latest handoffs
keep the established immediate query path. Ordinary large virtualized lists,
reorder, unrelated membership and other consumers in the matrix are unchanged.
This narrows the proposal instead of adding a second focus/reveal queue.

Prepared-window source scenarios cover ready insertion notifications, retained
identity, complete new payloads, ownership transfer, malformed/incomplete
preparation rejection, exact release and reentrant teardown. They execute at the
start of the native chat fixture alongside the existing delayed-result,
retention, reentrant lifetime and bounded-publication scenarios.

`prepared-window-wheel-1/expanded` passes21 checks on prototype host1: actual
eviction/return paging, up/down/up with500ms provider replies, no backward,
removed or missing anchor samples. The oracle now compares against the native
source's presented query/occurrence map while separately logging the newest
published query and preparation state; a retained old window is intentional
until admission, whereas retired/incorrect slot identity remains invalid.

`prepared-window-hide-1` confirms cancellation after the provider succeeds,
drained preparation, no retry error, retained sidebar identity and reachable
composer after reopen. Its next-page assertion failed because90 pulses stopped
at offset1757 inside the now40-row window; it had not reached a boundary. The
runner now drives to the actual edge within a bounded1200-pulse budget. It is
not counted as a completed lifecycle pass yet. Host4 includes that test correction
and the explicit-intent/busy gating and still requires final qualification.
The .36b Release host predates this prototype; the real .35 process is unchanged.

### Prepared-row qualification and .36 deployment

On host4, `prepared-window-hide-2` passes17 checks. Controller scrolling passes22
checks per size in `prepared-window-controller-1`; native wheel scrolling passes21
per size in `prepared-window-wheel-2`. Every direction crosses actual page
boundaries, records preparation frames, and has zero backward, missing or retired
anchors. This is103 checks across the lifecycle/controller/wheel lanes.

The asynchronous commit now notifies the presenter's existing parked-focus policy
when required; it uses the same `NeedsCollectionEntry` decision as synchronous
Apply. New explicit focus choices and active scroll ownership still win. Commit
exceptions are reported rather than accidentally swallowed after the pending
successor is cleared. Prepared-source checks additionally reject complete ranges
from an old query and wrong occurrence maps, and verify abandoned ownership.
`prepared-window-source-final-1` runs that strengthened setup within a43-check
compact entry run. `prepared-window-authority-1` passes24 expanded native checks on host7,
including obsolete row authority remaining invalid while a non-prefix successor
is prepared. No shipping diagnostic hook is needed for this behavior.

Shared tests on host5 pass continuity16, end-following34, entry65, preview14 and
return52. Explicit navigation passes44 compact/43 expanded checks; the media/main
screen lane passes40/49. Inspected Servers, expanded chat, compact chat, inline
video and gallery captures retain the approved layout. Fixture artwork failure
is intentional, and those screenshots do not qualify actual decoding.

Consumer check for preparation: Discord history is the only shipping
`OnBoundaryDemand`/`AdjacentPages` consumer found in samples/first-party widgets
and templates. It adopts the host mechanism automatically. Other Discord lists,
Spotify/YouTube Music queues, Playnite, PoB, Capture Library, Settings and ordinary
SDK examples do not opt into boundary observation and keep their current lazy
loading path. The earlier shared focus/anchoring matrix still applies to them;
no full live acceptance of every widget is claimed.

Matched Release host `prepared-release-host-36c` passes317 dependency checks and
2/2 async cleanup locks; named fixture diagnostics are absent. The .36 package
SHA256 remains `0501501d6f9e3c4653b2f89dcbf1bbd0b8659e698fbb31e0b9c8dbbb12c7dcce`.
Fresh review/disable/apply/enable receipts are `candidate-package-36-*`. The
normal app startup/profile election can show an existing hidden instance; using
the same verified executable/config redirected and exited0, leaving PID53636
unchanged. Computer Use then verified an idle server picker before normal
Alt+F4 shutdown. The .36 candidate is now PID55416, default profile/isolated
catalog, with no validation switches or temporary startup diagnostics. Receipt:
`prepared-window-candidate-36/launch.json`.

Live smoke is read-only: TriggerFinger older/newer history loaded without a
visible retry state, and keyboard navigation resumed after wheel scrolling.
Animated media also appeared in the reading tree. No message, call, attachment
upload or microphone action was taken. These snapshots cannot certify every
transition frame; native frame-sampling remains the bounded motion evidence.

New visual follow-ups from the mandated capture review:

- Reproduced in live TriggerFinger: a long URL with a browser text-fragment
  suffix and its canonical embed render as both raw inline URL and link card.
  Investigate Discord content normalization/deduplication, preserve the intended
  full destination and meaningful fragments, and add a synthetic regression.
  This is widget/provider display policy, not a reason for arbitrary URL fetches.
- Observed during gallery/focus transitions: outgoing and incoming shortcut-guide
  labels can overlap briefly. Settled live captures show one correct guide.
  Investigate the shared host guide transition, not Discord-specific labels.
- The intentionally failed gallery fixture retains a large empty media area with
  its error message at the top. Review the shared media failure presentation for
  useful sizing/hierarchy while preserving attachment navigation and safe source
  authority; do not add an automatic external-open fallback.
- Cold Inbox-to-chat entry briefly shows an empty sidebar while changing from
  full-width list to split layout. Same-chat/same-scope switches have separate
  retained-source evidence. Check scope/layout ownership before changing this;
  do not weaken input isolation solely to preserve paint.

These are remaining goal work, not claims that the new paging implementation
regressed them. No merge, push, installer or public release occurred.

## Link destination fidelity and shared presentation polish

The live text-fragment duplicate is addressed by Discord domain display policy in
`Domain/DiscordMessageLinks.cs`. Exact authored URL matches win; otherwise only
one unique literal browser text directive can associate canonical embed metadata
with the full authored destination. Meaningful anchors, queries, hosts, paths and
ports stay distinct. Ambiguous selections keep their separate inline actions and
omit the extra canonical card. This performs no fetching, scraping, redirect
resolution or URL rewriting on invocation. Markdown labels remain readable and
spoiler suppression remains authoritative.

`link-dedup-tests-1` passes1014 custom widget checks (19 added), including exact
intent payloads, ambiguous fragments and malformed/credential-bearing targets.
`link-dedup-native-1` passes40 compact/49 expanded checks with an actual synthetic
text-fragment/canonical pair. Inspected main/chat/gallery screenshots confirm one
article card in the expanded history without the long duplicate raw URL.

The gallery transition screenshot also reproduces overlapping old/new controller
guide labels. This is host-owned in `ShellControllerGuide.Motion.cs`, using
`WidgetMotionPolicy.GuideFadeThrough`: the former140ms simultaneous crossfade is
now two70ms phases at normal speed. Existing bounded banks, input authority and
interruption semantics are preserved. This adopts the existing in-place page
fade-through pattern; it adds no public API or widget lifecycle plumbing.
Complexity is low. Pure motion tests pass28; the native shell suite passes70.
The initial native run stopped at a pre-existing opaque-chip color expectation;
the previous host reproduces the same failure. The fixture now explicitly checks
the standard guide's established translucent alpha while preserving RGB/border
checks. Both failure receipts remain. `guide-fade-through-media-1` passes40/49 and
its gallery transition capture no longer paints conflicting labels together.

Shared consumer matrix:

| Mechanism | Consumers | Applicability |
|---|---|---|
| Guide transition | Overlay tray/widget/modal guide | Automatic shared adoption across shipping widgets, samples and SDK templates |
| Guide transition | Pinned adjustment toolbar | Same shared class; source applicability confirmed, separate live pin check pending |
| Link association | Discord message rows | Migrated and tested; provider-specific policy |
| Link association | Other widgets/templates | Not applicable; no Discord embed-to-authored-link association |
| Media status alignment | Discord gallery/inline video, Capture Library previews, Game Help, SDK Gallery | Shared native player; no consumer migration needed |
| Media status alignment | EmbeddedMedia/YouTube media viewports, ordinary public-image avatars/thumbnails | Different presenter; unchanged |

The media status refinement retains allocated gallery geometry to avoid loading
and error resize jumps, centers the existing explanation and bounds its line
width. Native failed-item navigation/recovery and final screenshot qualification
are in progress. Cold Inbox-to-split-sidebar entry and remaining peer acceptance
are still open; this checkpoint does not declare goal completion.

Qualification finished for this bounded batch: `gallery-status-native-2` passes27
native checks with decoded generated PNG and bundled video, including centered
failure geometry, next-item recovery, fullscreen switching and decoder cleanup.
The first attempted launch used the wrong fixture flag and timed out without
running tests; it was closed and is not counted as evidence.
`link-gallery-native-1` passes40 compact/49 expanded checks, and
`link-gallery-accessibility-1` passes40 compact checks at150% text/high contrast.
Reviewed Servers, chat actions and compact/expanded/high-contrast gallery PNGs.
The explanation fits and remains readable, and the gallery does not resize on
failure. Empty synthetic thumbnails are intentional test-source failures.

Isolated package0.1.0.37 is installed through review/disable/apply/enable, with
unchanged full-trust identity and optional capture permission. SHA256:
`236d011c24b9b2a5d3790e09723f41460945b158207e53ca6c15e6112ed332f7`.
The matching `link-gallery-release-host-37` is trimmed, validation disabled, with
317 dependency references verified and2/2 async cleanup locks. Named fixture
switches/types are absent. Launch receipt: `link-gallery-candidate-37/launch.json`.
Current PID41260 uses the default profile and isolated candidate catalog.

Live smoke opened TriggerFinger, loaded older history and returned toward newer
content using native wheel input. No visible retry error appeared; the sidebar,
GIF and ordinary article card rendered. No outgoing messages, calls, links or
uploads were activated. Snapshot evidence is not continuous-frame proof.
Cold Inbox-to-chat sidebar blanking was observed again before rows loaded; this
remains the next bounded transition investigation. The live long text-fragment
case was not relocated during this smoke; its exact destination and native
layout are covered by the synthetic/presentation regressions above.

## Cold sidebar entry: shared paint reuse

Reproduced in live. `WidgetViewPresenter.ApplyCore` recreates the conversation
collection when Inbox (`discord.root`) changes to chat
(`discord.conversation.scope`). That scope separation is correct, but its newly
created source had no resident presentation while it acquired fresh row leases.
The provider's initial conversation page was already available. This is a host
presentation gap, not delayed Discord history or a widget cache requirement.

`WidgetIndexedCollectionView.Paint.cs` now seeds a new source before the old
native view retires. It requires the same session/surface, worker, package,
collection/source IDs, query generation, count and layout kind/axis, plus a
nondecreasing content revision. Grouped/discovery sources and changed queries are
excluded. The ordinary source's bounded resident slots are copied; no leases,
requests, focus/scroll intent or retained-index ownership transfer. Old values
keep their old row owner; normal demand acquires new input authority. Scope and
stale-action validation remain unchanged. No SDK/protocol or Discord change is
needed. Complexity: medium, because the visual fix must preserve lifecycle and
authority, despite a small implementation. This follows the native collection
separation of data presentation from view/input lifetime and the host's existing
same-occurrence paint seeding, rather than a widget-owned list reconstruction.

Validation on `sidebar-seed-host-3`:

- `sidebar-seed-native-2`:51 expanded checks; cold entry sampled6 frames with
  zero empty frames and6 frames of passive retained rows, with zero accepted
  stale input claims. Subsequent chat switching, gallery navigation/return and
  inline playback presentation checks pass.
- `sidebar-seed-compact-1`:41 compact/reduced-motion checks.
- `sidebar-seed-shared-1`:continuity16, end-following34, entry65.
- Source scenarios add12 checks for copied paint, independent fresh acquisition,
  release exactly once, and rejected query/instance/count/revision mismatches.
  The existing retention/reentrancy/publication checks also run.
- Main-screen screenshots inspected: conversation density, stable sidebar,
  composer, call dock and Servers retain their prior layout. Synthetic attachment
  failure is intentional and does not qualify live decoding.

An initial validation callback called `ClaimsInput` on a retired row and failed
to catch its expected `indexed_retired` rejection, crashing the fixture. The
failure stack proves it was the test observer, not the presentation path. The
observer now handles only the two expected rejection codes and counts any true
claim as a failure. The original receipt/dump is retained; it is not a passing
run. The first build also caught two WinRT-incompatible inferred collection
expressions in the new tests; explicit arrays corrected those analyzer errors.

Applicability matrix:

| Consumer | Result |
|---|---|
| Discord Inbox/sidebar | Automatically adopted; native cold-entry coverage above |
| Discord history/chat switch | Same-scope updates and changed channel queries keep existing paths; combined native checks pass |
| Playnite game lists/details and achievements | Existing modal/background retention or distinct collections; no widget migration. Same-query scope recreation can use the generic mechanism |
| Capture Library gallery/detail | Detail displays a player rather than the same collection; ordinary return/cache behavior remains |
| Spotify playlists/search/queue | Distinct sources/IDs and discovery paths; excluded where discovery applies, no widget migration |
| YouTube Music browse | Grouped and changed-query paths excluded; ordinary same-scope updates unchanged |
| PoB calculations/builds/items, Settings installed list | Ordinary shared collection mechanism; no author plumbing added; no separate live sweep claimed |
| SDK gallery/templates | No public API change; shared entry/continuity regression covers host paths |

Matched shipping host qualification and live cold-entry smoke remain next.

Shipping follow-up: `sidebar-release-host-37` passes317 dependency checks and
2/2 cleanup locks; named fixture switches/types are absent from its trimmed
assembly. The widget package stays0.1.0.37 because this batch changes only host
behavior. The running frontend is PID43288, default settings/isolated catalog;
`sidebar-candidate-37/launch.json` records the exact executable and package hash.

Live TriggerFinger cold entry now shows conversation text and status glyphs in
the first post-click capture while history is still loading, instead of an empty
sidebar. The settled chat loads normally and retains composer focus. No messages,
calls, uploads or external links were activated. This confirms live applicability;
continuous-frame evidence remains the native fixture, not the point snapshots.

The same first capture exposes the remaining photo-avatar gap: seeded row
declarations rebuild their image controls, but the retired artwork lease cannot
start a read. Photos return after new authority arrives. Do not describe this as
complete visual continuity. Investigate bounded transfer of already-decoded
static artwork using exact image identity, without transferring resolvers,
decoders, old control trees or action authority. `BorrowDecodedArtwork` in
`WidgetViewPresenter.Resources.cs` currently serves current handle-backed previews
and is not directly usable for this retired public-avatar case. `NativeArtworkDecode`
contains a BitmapImage; animated bitmap ownership must remain separate. Scope
any extension to genuine immutable/static paint, test changed image identity and
retirement, and reject the proposal if it requires unsafe lifetime coupling.

### Decoded static portrait continuity

The follow-up uses `WidgetViewPresenter.StaticArtwork.cs` to retain existing
static BitmapImage references. Exact element ID, source string and public-media
mode must match. A snapshot is admitted only when the displayed bitmap matches
the completed decode or an earlier verified static handoff. This prevents a
replacement URL from borrowing the previous image that is still visible while
replacement loading is pending. Opaque artwork handles and animated bitmaps are
excluded; their resource/animation lifetimes remain unchanged.

Realized row paint carries only these immutable image references, never a native
control tree, decoder, resolver or action lease. On fresh-scope rebinding, the
same exact-source check preserves pixels until current artwork arrives. Normal
unchanged publications allocate no snapshot: only replaced empty image bindings
are inspected. References retire with their binding/source. This is a bounded
host-only continuation of the preceding seeding fix; there is no widget cache,
protocol change or broader media transport admission.

`static-paint-native-2` passes9 generated-image checks for actual decoding,
handoff without authority/new decoding, old-view disposal, fresh-scope rebinding,
changed-source rejection, truthful pixel provenance, animated-image exclusion,
and worker replacement. `static-portrait-native-1` passes52 expanded checks with
the public Discord default avatar:4 sampled cold-entry frames,0 empty sidebar
frames,0 missing-portrait frames and0 accepted stale input claims. Its chat
screenshot was inspected; portrait/status placement and the approved layout are
preserved. `static-paint-animation-regression-1` passes37 existing native
animation, lifetime and artwork-authority checks.

Consumer applicability: the previous scope/query matrix remains authoritative.
All native static `Image` declarations can preserve exact-source paint across
control recreation within the same widget/session/worker/package. Resource-handle
artwork (including Playnite's captured artwork), media posters, animations,
WebViews, media viewports and package icons keep their established mechanisms.
No widget migration is needed. Separate live checks of every widget are not
claimed; the generated native tests cover the shared transfer and rejection paths.

Shipping build and actual-account avatar smoke remain next. This closes no live
peer/read-state/call acceptance gate by inference.

Qualified shipping host: `static-paint-release-host-37`, trimmed with validation
disabled; dependency verification passes317 references and2/2 cleanup locks,
and the new/previous named fixture diagnostics are absent. Package remains
0.1.0.37 with the unchanged reviewed hash. Current process41684 uses the default
settings and isolated catalog; receipt: `static-paint-candidate-37/launch.json`.

Live read-only TriggerFinger entry now retains actual photo avatars, text and
status glyphs in the first capture while history is loading. Settled chat loads
normally. Native Left focuses the selected conversation and Right returns to the
composer after its focus animation settles. No call, message, upload or external
link was activated. This is point-in-time live applicability evidence; the
public-portrait fixture supplies continuous-frame sampling for the transition.
The main approved UI still fits. No new visual defect was found in this bounded
pass; the overall live peer/call and longer-term acceptance gates remain open.

## Completion audit follow-up: dynamic call dock and live dismissal

Live .37 smoke opened the existing two-attachment message in the authorized Sanky
chat, waited for the actual image, then clicked outside the gallery. It dismissed
to the same visible attachment row with the row highlight restored. Both static
thumbnails rendered, including the video poster. This is point-in-time live
visual evidence, not a precise keyboard-focus or continuous-frame measurement.
No message, call, link, upload or permission grant was initiated. Idle process
41684 was then closed normally for isolated native tests.

The remaining dock audit found two bounded widget issues:

- `DiscordWebViewWidget.Navigation.cs:MessageCallDock` appended connection state
  to the title before using it to form an accessible summary that already began
  with the same state. `dock-state-tests-1-results.log` reproduces duplicate
  reconnect wording. The summary now derives once from the original title.
- `styles/default.wrss:.discord-dock-people` collapsed when the provider briefly
  supplied no participants. Compact native captures in `dock-state-native-1`
  show a 4-DIP dock/composer shift. Reserving the existing 28-DIP avatar-row height
  keeps the conversation geometry stable without inventing participants or adding
  focus targets. Existing WRSS/native minimum sizing is sufficient; no new SDK
  abstraction or host change is justified.

`MessagingTests.CallDock.cs` covers 0/1/7/2 participants, provider ordering,
overflow count, speaking suppression for mute/deafen, scope stability, reconnect
wording, summary navigation and disconnect. The focused private-call suite passes
55 checks; the full widget suite passes 1,033 (`dock-state-full-tests-2-results.log`).
No weakened assertion or retired safeguard was needed.

The native private-call fixture now changes participant state while Mute owns
focus. `dock-state-native-2/{minimum,expanded}/result.json` passes45 checks each:
same native Mute control/focus across updates, unchanged dock height/composer
position, truthful +3 overflow, all five dock actions inside the actual surface,
one Left to summary, full-call entry and RB return, plus existing synthetic
incoming/outgoing/notification/leave coverage. These are native controller-route
checks, not a physical gamepad or real Discord audio/peer call. Settled captures
were inspected in both sizes. Expanded retains useful chat area and labels;
compact keeps icon actions and its visible focus outline. An empty roster keeps
the row space but does not show fabricated avatars. Continuous-frame absence of
all possible state-transition defects is not claimed from these point captures.

Ownership/applicability: this is Discord-specific roster/title policy using
existing ActionSurface, native sizing and navigation contracts. No production
SDK/protocol/host behavior changed. Other shipping widgets, SDK examples and
templates do not use the Discord WRSS selector or title formatter; no migration
is needed. The host additions in this batch are validation-only.

The first .38 package in `candidate-package-38` predates the reserved row height;
do not install it. `candidate-package-38-final` is the reviewed replacement.
The existing trimmed/validation-disabled `static-paint-release-host-37` remains
the matching shipping host: all new frontend changes are confined to Validation.
Enlarged-text/high-contrast qualification and isolated update/live smoke follow.

Final dock qualification: `dock-state-accessibility-1` passes45 checks per size
at150% text/high contrast, for180 native checks across the standard/accessibility
runs. Inspected both sizes. The compact layout deliberately leaves less message
context at enlarged text; all call actions, composer, overflow and focus remain
visible. No font reduction or extra focus stops were introduced.

Applied the reviewed final .38 package to the isolated catalog using the existing
review/disable/apply/enable path. Hash:
`72bbb52554f672173d66ca108f3bddf2cb9ffa6b310fe21431a8b35abd45062b`.
Full-trust identity, empty required permissions and optional capture permission
are unchanged. Current host is `static-paint-release-host-37`, PID43640;
`dock-state-candidate-38/launch.json` records configuration and the reason for
reusing the verified trimmed host. There are no changed shipping host binaries
in this batch. All native fixture processes exited normally. No merge, push,
installer or release was created.

The completion audit now has a current requirement/evidence map ahead of the
historical checkpoints. It distinguishes implemented/fixture-qualified work
from the remaining real-peer unread, call/pin, physical-controller and longer-term
acceptance. Those gates are not closed by synthetic tests or source inspection.

Live .38 smoke completed (`dock-state-candidate-38/live-smoke.json`): signed-in
Servers and Messages list loaded, then TriggerFinger opened read-only. Actual
sidebar portraits/text remained present in the first loading capture; history
settled with composer focus and no visible retry error. Screenshots of the main
screens were inspected; no new layout defect was found. Current-process logging
contains only the successful controller startup health entry in the checked
window. No calls, messages, uploads, external links or privacy grants occurred.
The qualified candidate remains running; all owned fixtures are closed.

## Voice-pin acceptance follow-up: .39

The remaining native pin audit found a real enlarged-text defect. The existing
scenario passed111 geometry/input checks at ordinary text, but at150% text the
320-DIP pin visibly clipped Unmute/Undeafen because each equal-width button also
reserved icon space. Bounds alone had not caught the clipped labels.

`VoicePin` in `DiscordWebViewWidget.Views.cs` now uses the same three standard
buttons with complete text labels. Removing their redundant icons preserves
explicit action wording, selected/disabled state, IDs, focus order, commands and
pin dimensions. It neither reduces text size nor adds author-owned responsive
input logic. The host's standard Button is sufficient; no SDK/framework gap was
found. Other widgets and the main call dock do not use this private formatter,
so no consumer migration is needed.

`pin-label-standard-39` and `pin-label-accessibility-39` pass123 native checks each
(246 total), covering400/320-DIP pins, labels without native text trimming, exact
focus return, mute/deafen reconciliation, passive participant pins, management,
retirement and stale requests. The latter uses150% text/high contrast. Full
managed widget tests pass1,033 in `pin-label-tests-39-results.log`.

The first high-contrast test failed an obsolete assertion that expected a
transparent host background. `OverlayAppearancePolicy` deliberately selects the
opaque system background in high contrast. The test now asserts that exact color
there and still requires a transparent fill in ordinary mode; the failure receipt
is retained. `Test-VoicePin.ps1` accepts theme/text/contrast options and native
captures include both sizes and the participant surface.

Offscreen RenderTargetBitmap captures do not faithfully reproduce ordinary pin
material/alpha. High-contrast captures clearly demonstrate complete labels. The
actual synthetic desktop preview (`pin-label-desktop-39`) was inspected through
Computer Use after the user reauthorized it: dark material, labels and centered
speaker ring render correctly. Management opened and pointer Cancel dismissed
its toolbar. A keyboard Escape targeted at the main window did not dismiss that
separate toolbar in this smoke; this does not invalidate routed-controller B
coverage or establish the toolbar's own keyboard behavior. No real Discord call,
device, message or capture was used. The synthetic process6316 closed normally.

Reviewed/applied/enabled .39 only in the isolated catalog, unchanged permissions
and identity. SHA256:
`90cc2b30bf646aee9a13efca0167f58ea833c6ee4ed3031d1612d092ff103831`.
The qualified trimmed `static-paint-release-host-37` is reused because no shipping
host code changed. `pin-label-candidate-39/launch.json` records the current launch.
No merge, push, installer or release was created. Remaining real-peer/read-state,
call/pin continuity, physical-controller and longer-term acceptance are still
explicit in the completion audit.

Final .39 launch is PID49704 (worker42640), default profile and isolated catalog.
Signed-in Servers/activity and Messages/avatars were inspected on the real desktop;
`pin-label-candidate-39/live-smoke.json` records the bounded smoke. No real call,
message, upload or permission prompt was initiated. Only the qualified candidate
remains running. The user has been asked whether the second test account is
available for the remaining live acceptance; no response is assumed.

## Reopened scrolling investigation and shared media loading indicator

User reports: right-stick scrolling sometimes changes to the conversation list
after the second history-page load, and the load/retry error has appeared again.
The user subsequently authorized isolated native tests and Computer Use/UIA.

The focus-routing failure is reproduced in `sidebar-scroll-baseline-1`. During
row retirement, native focus can be parked on `WidgetIndexedCollectionView`.
`WidgetViewPresenter.ScrollBy` searched the wrapper's declaration ancestors only
for ordinary ScrollViewer bindings, then used the first scrollable collection as
a global fallback. With a long conversation sidebar that fallback is wrong.
Earlier synthetic paging fixtures had only two conversations, so the sidebar
was not scrollable and did not expose this failure mode.

The host now recognizes an indexed collection's existing native viewport in the
nearest-declaration search. No widget focus counters, collection replacement or
new scroll-owner lifetime were added. `sidebar-scroll-fixed-1` passes31 native
checks with120 conversations, parked history focus and delayed up/down/up paging.
All recorded directions have zero escaped-sidebar frames, wrong-direction frames
and missing anchors. The existing shared continuity16, end-following34 and entry65
checks pass in `sidebar-scroll-shared-1` (115 total).

The retry error is not assumed fixed by that result. No cursor exception appeared
in this regression. A bounded read-only live observation saw adapter exceptions,
but .39's exception message deliberately hides their provider reason. .40 adds
the standard opt-in `WidgetRail-Discord-Reads` EventSource with a fixed vocabulary
of read-operation and failure-reason codes. It emits no arguments, message text,
IDs, URLs or arbitrary provider strings and does not write a file by itself.
Three new diagnostic-contract checks pass in the1,036-check widget suite. The
temporary local observer in artifacts stores only whitelisted types/codes and
stops after90 seconds; it is not a shipped endpoint or permanent background tool.

Prefetch clarification: `CaptureBoundaryCrossing` has a half-viewport threshold,
but suppresses it while scrolling/layout is pending. Continuous held motion uses
the explicit edge-demand path. The SDK's existing `Prefetch` fetches **and commits**
a bounded page; it is not a separate fetch-only cache. Removing that guard is not
an equivalent fix for scroll ownership and can race protected-row eviction.
Earlier fetching remains a medium-complexity follow-up: separate bounded fetch
readiness from safe window commit, with account/query/cursor identity and delayed
reversal/eviction checks. Android Paging's `PagingConfig.prefetchDistance` and
WinUI incremental data virtualization are relevant precedents. No eager-loading
change is claimed in this batch.

The user also requested a loading indicator instead of Opening media text.
`MediaPlayerView.Loading.cs` owns one standard WinUI ProgressRing, centered in the
existing stage, with an accessible Loading media name and no input/focus stop.
It stops on suspension, unload, retirement and disposal; decoded content hides it
and errors/slot-exhaustion retain explanatory text. Posters and retained-image
handoffs keep their existing presentation policy. No widget changes are needed.
`media-loading-native-2` passes33 decoded gallery checks, including delayed-load
indicator geometry, suspension/resume, replacement, failure and recovery. The
first run measured before native layout settled; the assertion now awaits the
same centered geometry before checking it. It was not a production layout fix.

Applicability matrix:

| Consumer | Shared mechanism/result |
|---|---|
| Discord message history/sidebar | Directly reproduced and fixed; scrollable120-row sidebar/native regression |
| Playnite browse/favorites/achievements; Capture Library gallery; Settings installed widgets | Use indexed collections and inherit nearest-owner lookup; no widget migration or new lifecycle policy |
| Spotify queue/discovered lists, YtMusic standalone lists, PoB lists/calculations, YouTube search, SDK examples | Existing indexed/loaded collection consumers; inherit routing where native focus is parked. Their normal explicit focus choices remain authoritative |
| Discord galleries/inline video; Capture Library gallery/editor/preview; Game Help media; controller template media | Shared native player receives the indicator automatically; no new declarations or external media permissions |
| Web-session/browser-native players and legacy unrelated image controls | Not affected; they do not use MediaPlayerView status presentation |

Separate live checks of every consumer are not claimed. Native shared regressions
and media lifecycle tests qualify the affected host mechanisms.

Matched .40 is now deployed in the isolated catalog with the default profile:
`scroll-loading-release-host-40`, frontend54088/worker37756, validation disabled.
`scroll-loading-candidate-40/launch.json` records the launch. Trimmed Release
verification passes317 managed references and2 cleanup locks. Package SHA256:
`6d9ea38a5c1cc6e979c4975b1f7da6014e8fb0039823af19c9042ef69105e1fe`.

Live read-only TriggerFinger smoke exercised four upward wheel inputs through
older pages followed by two downward inputs through newer pages. The sidebar
remained stationary and no retry error was visible at the sampled states.
This is bounded wheel/snapshot evidence, not a physical held-stick test or
frame-by-frame proof of every live transition. The deterministic native fixture
provides the held-scroll and frame assertions above. No messages or calls were
sent. The intermittent loading error remains unresolved unless a matching
history failure is captured; unrelated exceptions are not treated as its cause.
The spinner's delayed appearance/lifecycle is qualified by the synthetic native
fixture; a slow live-media opening was not separately reproduced in this smoke.

`live-history-exceptions-3.jsonl` completed its90-second observation during this
live older/newer paging pass with no admitted exceptions or read-failure events.
The temporary observer exited normally. This absence does not close the user's
intermittent loading-error report; no speculative provider retry was added.

## Overlay attachment viewer and advance cursor preparation (November 19)

Authorized scope: preserve the existing shared gallery, present Discord attachments
in a transparent overlay-relative 16:9 stage, and fetch ahead for held right-stick
scrolling without premature row insertion or eviction. Impeccable polish/craft and
WinUI design guidance apply to native evidence. The Photos-style media canvas is
the precedent: content dominates, controls remain accessible, Escape/B/outside-click
return to the exact message. No new widget-owned gallery or manual focus history.

Protocol135 adds optional `OverlayModalPresentation` (maximum width1280 DIP and
16:9 by default; bounded validated options). SDK `WidgetModal.OverlayPresentation`
opts in. The existing modal portal uses the overlay viewport while retaining
widget input/lifetime/accessibility ownership. Ordinary modals retain their local
clamp. Discord removes only the opted-in frame's background/padding/border; the
separate dismissal scrim and native control backings remain. The requested aspect
is the viewer area; individual image/video aspect ratios remain preserved.

`WidgetCursorResourceOptions.PrepareAdjacentPages` is opt-in. The SDK holds at most
one unpublished page per available direction, each no larger than PageSize, after
a successful load. Demand joins an in-flight preparation or consumes a ready page;
the original protected-row merge/eviction path still owns publication. Preparation
does not change rows, focus, loading/error status, traversal history or reset identity.
Epoch, committed window, live mutation/boundary revisions and operation lifetime
fence reuse. Failed speculation is quiet; actual demand uses normal failure/retry.
This is intentionally separate from increasing the host's commit threshold.

Native held-scroll evidence exposed another host issue: an immediate cached page
can coalesce clearing the consumed entry/reveal request with the first prepend.
`WidgetIndexedCollectionView.PreparedWindow.cs` previously treated clearing like
a new navigation request and skipped prepared row handoff. The narrowed fix allows
an absent incoming request while retaining rejection for a different non-null
request. `prefetch-held-native-5` is the failing baseline (16 missing-anchor frames,
no wrong-direction or escaped-sidebar frames); fixed native qualification pending.

| Consumer | Applicability / decision |
|---|---|
| Discord message gallery | Migrated to overlay placement; same MediaGallery declaration, authorization and modal return |
| Discord history | Enables two-slot adjacent preparation; native delayed up/down/reversal/teardown validation required |
| Spotify queue | Existing cursor consumer; default remains off because its explicit queue workflow has no requirement for speculative provider requests |
| Capture Library, Marketplace, Settings, Playnite and Game Help dialogs | Existing local modal behavior retained; no automatic overlay migration of confirmations/forms |
| Game Capture/Game Help single-media previews, templates/SDK examples | Existing player/fullscreen presentation retained; overlay option reusable but no forced migration to multi-attachment gallery |
| Indexed scrolling in shipping widgets | Shared prepared-window eligibility fix only applies to compatible rebased windows; new non-null navigation intents retain their existing path |

SDK tests cover separate/bounded preparation, terminal edges, repeated traversal
and reversal, duplicate demand, reset with ignored cancellation, failures, protected
eviction, live edits and reversal during loading. Widget tests now distinguish
initial/around reads from speculative adjacent reads and verify committed row keys
instead of assuming provider-read count equals visible-page count. All1040 widget
checks passed in `overlay-prefetch-widget-tests6.log`. Native overlay geometry passes
its five new assertions; fixture size restoration before pointer checks is being
corrected. Runtime staging failures and mismatched binaries are not UI evidence.

`prefetch-held-fixed-1/expanded` passes 35 native checks with 120 conversations,
500ms provider delay, continuous up/down/up input, and parked-focus regression.
The fixed handoff has zero missing-anchor, wrong-direction or escaped-sidebar
frames. `overlay-modal-native-41` passes 56 checks including overlay geometry,
small-widget resizing, short-overlay fitting and existing real dismissal paths.
`overlay-gallery-native-41/expanded` passes 29 checks including gallery switching,
exact message return and reading-position preservation. The compact 1500ms-delay
run had zero geometry/focus failures but exposed an obsolete test assertion:
reversal consumed a prefetched page without another provider call. Tests now
assert committed windows, not new network calls; rerun pending.

Full SDK suite: 201/201; SDK compatibility: 17/17. Additional cursor checks cover
malformed speculative data, empty terminal pages, and ignored cancellation during
hidden-lifetime retirement. Published Release verification confirms 317 local
managed references and 2/2 async-cleanup locks. Discord .41 was reviewed/applied
in the isolated catalog, same permissions; package SHA256
`d64ebabf13cca4dc94f14b19791e512bee7404e5807bf94ee0f7192ba7154154`.
Main-screen native fixture snapshots were inspected. Their remote attachment
placeholders do not establish decoded media appearance; a generated-media overlay
fixture and real candidate visual check remain required before final delivery.

Final qualification: `prefetch-compact-slow-fixed-41/minimum` passes 32 checks
with 1500ms reads and up/down/up input. Every sampled direction has zero missing
anchors, wrong-direction frames or sidebar escapes; the downward leg commits a
previously prepared page with zero additional reads. `prefetch-hidden-41` passes
15 checks for suspension during row preparation and successful return/paging.
`overlay-decoded-native-41` passes 34 checks using a generated decoded PNG and a
bundled MP4 through the transparent overlay frame, including loading, switching,
fullscreen, failure, suspension and teardown. Its decoded image screenshot was
reviewed: no opaque nested card, preserved ratio, visible arrows and compact
controls. This supplements the remote-placeholder widget fixture screenshots.

The matched trimmed .41 is running as PID37832 with default settings and the
isolated catalog. `overlay-prefetch-candidate-41/launch.json` records the binaries,
config and package hash. Live Servers, Messages and test-peer history were
visually inspected, including section transition and media loading/return. An
existing received PNG opened larger than its widget in a transparent frame;
outside-click dismissed it and returned to the same message. No message, call,
upload or privacy grant was performed. Exact return pixels are asserted by the
native fixture, not inferred from this bounded live snapshot check. No live
physical-controller or new high-contrast visual sweep is claimed. All generated
test windows and bounded observers have exited; the isolated candidate remains.

## Reopened: moving-page insertion and real Playnite right-stick performance

User resumed the goal and reports pages arriving only after releasing the stick,
plus worse Playnite right-stick scrolling (not D-pad/mouse-wheel). The previous
prefetch qualification proved advance network reads and eventual page commits,
but did not prove insertion before the physical edge during continuous input.
`moving-boundary-baseline-42-test` now reproduces that gap:108 input pulses reach
15.4 DIP from the old edge without a new presented query. The narrowed host fix
requests the directional boundary at half a viewport while retaining lease/layout
guards. `moving-boundary-fixed-42-test` commits the next query after49 pulses at
189 DIP from the edge. Release build/update and broader regressions remain pending.

Early demand must also defer harmlessly when the opposite retained page is still
visible. The opt-in cursor preparation path now retains the fetched response and
restores the ready window instead of turning protected-capacity deferral into an
error. Later demand uses fresh visible keys. Explicit focus-moving operations and
non-opted-in consumers keep their existing behavior. This follow-up needs managed
and native qualification before shipment.

Playnite uses an indexed known-size grid and WinUI tracked offscreen ranges, with
32-item host data pages; it does not use Discord's cursor resource. Increasing its
prefetch budget is not assumed beneficial. Source hypotheses about clearing queued
scroll destinations on extent changes were rejected for the first fixture: its
extent did not change. No accumulator safeguards were removed. Instrumented input
calls cost0.24ms at p95 in that fixture, so an unmeasured scroll-owner rewrite is
not justified. The comparison against .40 was invalid because its host cannot
accept the current fixture's protocol135 declaration; it is not regression evidence.

The user observed that this fixture looks smoother than the real widget. It is now
limited to correctness evidence. The real .41 Release candidate was reopened using
the default profile, actual Playnite0.4.8 provider,183 games, ultrawide background,
translucent theme and0.9 scale. A bounded numeric-only EventPipe collector records
existing presentation-stage durations and GC pauses during physical right-stick
input. No screenshot/UIA polling or builds run during collection. PresentMon failed
to obtain its ETW session (access denied); no displayed-frame/GPU timing claim is
permitted from this collection. No machine security/group settings were changed.

### Fixture applicability correction

The user's smoother-fixture observation is accepted. Source and recorded run
provenance confirm material mismatches; the fixture is not acceptance evidence
for real Playnite right-stick performance.

| Condition | Recorded synthetic run | Required real reproduction |
|---|---|---|
| Presentation | Standalone presenter, 1360-DIP window; readiness callback only | Production overlay shell, actual theme/background, widget size and 0.9 scale |
| Input | `RightStickScroll.Sample` called by a 15ms async loop directly into `presenter.ScrollBy` | Physical controller through the shell's ordinary input routing |
| Library/artwork | 2,000 generated entries, eight repeated cover files, synthetic provider with 15ms delay | Actual 183-game captured query and distinct local artwork/cache state |
| Build | Validation frontend and fixture application | Matched Release frontend and shipping Playnite application |
| Timing | Initial artwork settled before the measured pass | Separate cold/revisited passes; retain actual acquisition/decoding and shell work |

Evidence locations: `Validation/PlaynitePerformanceValidationPage.cs:RunAsync`,
`Validation/PlaynitePerformanceValidationPage.Wheel.cs:CompareWheelAsync`, and
`playnite-stick-42/baseline-41/{provenance,result,comparison-ready}.json` under the
plan's artifact directory. Synthetic larger item counts do not compensate for
these differences. The numeric-only real observation completed with zero pipeline
records; it proves neither absence of scrolling nor absence of stalls. Existing
stage events record range/artwork work, not input or displayed-frame progress.

Next performance evidence must first establish that the actual Library moved
during the captured interval, then correlate demand, row publication, artwork
readiness, dispatcher delay and GC. Validate the observer against a known new-range
operation before interpreting silence. Keep UIA/screenshot polling and builds out
of the timed interval. If real performance cannot be reproduced, leave it open;
do not substitute another passing synthetic run or increase cache budgets blindly.

Playnite already has bounded offscreen data demand: `IndexedItemsSource.RangesChanged`
collects WinUI tracked ranges, and `RebuildDemand` prioritizes visible pages before
tracked pages (32 rows/page, four concurrent owned fetch/release slots). Its browse
rows project an already captured query; this is not Discord cursor/network paging.
Artwork starts separately through `NativeArtworkDemand` after native sizing. A
directional extension, if measurements show late demand, belongs to the shared host
and is medium complexity: preserve visible priority, cancellation, release budgets,
fast reversal and query replacement. A separate artwork prefetch/cache facility is
larger scope and must bound encoded/decoded memory and source authorization. Neither
extension is currently justified as the fix. Consumer checks would include all
indexed grids/rails (Playnite, Capture Library, Games & Apps, Settings and templates),
with Discord cursor history retaining its distinct SDK preparation mechanism.

### Protected prefetch qualification and trace admission (November 19)

New managed regressions reproduced a validation-order bug in early deferral:
an incoming duplicate key or noncontiguous indexed page was reported Ready while
the opposite page remained visible. `WidgetCursorResource.LoadCoreAsync` now uses
the existing `Merge` identity/continuity checks before treating protected capacity
as a deferred success. Removed the duplicate eviction simulation from
`WidgetCursorResource.Prepared.cs`; only valid pages blocked by protected rows are
remembered. Explicit focus-moving and non-opted-in operations retain their failure
semantics. Tests cover five repeated early demands in each direction, unchanged
rows/generation/anchor, no extra provider reads, and later commit using the saved
page once the protected page leaves view. The malformed-input regression failed
before the correction and passes afterward. Full SDK suite:201/201
(`deferred-validation-sdk-42-results.log`).

Consumer check: Discord history is the only shipping opt-in; Spotify queue uses
the same merge with deferral disabled. Playnite, Capture Library, other indexed
grids and templates do not use the cursor preparation API. No widget-local
capacity handling or speculative extra provider requests were added.

`deferred-prefetch-native-42/expanded` passes33 native checks with120 conversations,
500ms provider delay and continuous up/down/up input, including retained anchors,
no stale-current occurrences, no restoration against newer input, committed pages
and zero account mutations. The test-owned frontend exited normally. This is
chat paging correctness evidence, not Playnite performance acceptance.

The real Playnite recording `playnite-live-42/physical-stick-pipeline-1.jsonl`
ended normally at16:13:44 local time with zero pipeline events. The user then
reported completing the test and restarting the PC; boot time was16:22:09. No
observer or frontend survived that restart. Timing overlap with the user's
gesture is unproven, and existing range/artwork events cannot establish whether
a cached viewport moved. Do not infer either idle input or smooth performance.

The shared `PresentationPipelineTrace` now adds Verbose-only numeric scroll
request/viewport events from the existing gesture owner. There are no new timers,
UI-tree scans, normal logs or retained UI observers. Informational pipeline
collection and uninstrumented rendering retain their existing paths. All native
presenter scroll consumers inherit the diagnostics; browser scrolling remains
outside this native event and must not be claimed covered. The report script
accepts scroll-only recordings and rejects entirely empty recordings as
inconclusive. The2 managed event-contract checks pass; actual EventPipe roundtrips
verify combined, scroll-only and empty recordings in `scroll-trace-probe-42`.
These event timestamps are not display-present/GPU timing or FPS measurements.

`scroll-observation-host-42` is an optimized validation publication, not a deployed
production candidate. No live Playnite performance improvement is claimed. New
SDK behavior and diagnostics still need a matched production publication before
the next real-library recording. No candidate was restarted after the user's PC
restart; desktop automation and generated test processes are stopped.

### Physical Playnite reproduction and indexed lease correction

The user reproduced the pauses in the real Library on the Release host, using
the right stick. `scroll-candidate-42/playnite-physical-1.jsonl` records the
30-second motion interval. The viewport traversed 5,472 DIPs. In that interval
the host completed 708 artwork decodes and spent 3.18 seconds in 174 managed
runtime suspensions. The ten longest interior request gaps were 97–126 ms and
overlapped 55–68 ms of suspension each. These timings describe the frontend
process and native viewport. Displayed-frame timing remains unmeasured.

`OverlayShellPage.PlayniteLibraryMeasurement.cs` permits a repeatable, validation-
only traversal using the real shell input handler, actual Playnite package,
provider and default profile. Its only widget command opens the Library. The
two matched runs used all 183 games, the same 5,472-DIP extent and four timed
legs with alternating right-stick values. Both reached each end twice.

Allocation stacks in `library-replay-stacks-42` identified repeated full-snapshot
validation inside `IsIndexedLeaseCurrentLocked`. A poster can query the same
lease while laying out, opening artwork and publishing the decoded result.
The session now remembers the last committed publication revision for which it
resolved that particular lease's range and scope. `CommitStateLocked` advances
the revision and reconciles leases under the session gate before publishing new
state. Each check still examines registry ownership, retirement, visibility,
instance and session generations, and terminal state. Fresh demands, wire
responses and changed publications follow the original validation path.

| Real Library, matched 16-second validation traversal | Before | After |
|---|---:|---:|
| Frontend managed bytes allocated | 1,081,025,824 | 413,582,088 |
| Managed suspension during measured motion | 1.36 s | 1.00 s |
| 95th percentile between changing interior viewport observations | 20.0 ms | 17.5 ms |
| Longest interval between those observations | 119.4 ms | 130.6 ms |

`library-replay-base-42` and `library-replay-fixed-42` contain the results and
numeric observations. The allocation reduction is about 62%. Occasional long
gaps remain, and physical-controller acceptance on the corrected host is open.
The remaining allocation samples include byte buffers from the WinRT stream
adapter and WinRT/XAML objects. Changing file opening or ownership requires
preserving read-only access, replacement semantics and exact-handle package
root checks. Data prefetch and cache budgets stayed at their previous values.

| Consumer | Shared change and verification |
|---|---|
| Playnite Library | Real provider, real artwork, normal shell routing and matched traversal above |
| Discord history, conversation rows and attachment previews | Same indexed row and retained preview lease path. Compact 150% high-contrast media fixture passed 23 checks, including gallery return and modal navigation |
| Capture Library, Games & Apps, Settings, PoB, Spotify, YouTube, YtMusic, FullApplication sample | Indexed collections use the same session. Existing declarations automatically use the correction. No individual physical sweep for these consumers |
| Templates, ordinary non-indexed views and browser pages | No direct lease changes in widget code |

The presentation-session suite passed 387 checks, including a regression for
repeated readiness checks followed by query replacement. Separate end-to-end
checks passed with the real worker for indexed lease retention and modal input
scope changes. The compact fixture's gallery image was a remote placeholder,
so decoded-media appearance is covered by the earlier generated-media run.

The isolated catalog selected Discord 0.1.0.42 through the reviewed update.
Package SHA-256 `d2fec7cbeb33dfd61dec60c74f91022c2a86d1267a64fa1e09679eaef032eb15`.
The corrected Release publication is `scroll-release-host-42b`, with 317 managed
references and both async cleanup locks verified. `scroll-candidate-42b/launch.json`
records its default profile and isolated catalog. No installer or public release.

### Watch incoming call streams in the widget and fullscreen

In **Screens in this call**, activating a live stream opens **Watch** in the
Discord widget. Keep **Watch & pin** directly available beside it as a separate
action. Each action identifies the streamer to assistive technology. In the
viewer, expose **Fullscreen**, stream mute/volume and Back. Fullscreen expands
the selected incoming stream across the overlay and preserves its aspect ratio.
Escape/B returns to the in-widget viewer. Back again returns focus to the stream
that opened it, or to a surviving call control if the streamer has left.

`DiscordWebViewWidget.Views.cs` currently renders the receiving session with
`UI.WebView` in the stream pin. The watch action in `DiscordWebViewWidget.cs`
ends in `RequestPinAsync`. Give the main viewer its own navigation destination
and receiver lifetime. Resolve both watch actions against the current session,
account, call and stream key. Preserve the friend-activity shortcut and make
its destination clear. Use the existing host browser presentation and fullscreen
ownership where supported. Verify whether one live WebView can transfer between
hosts while keeping the receiver connected before designing any shared host
extension. A live WebRTC stream has different playback semantics from a file
attachment, including continuous playback and per-stream audio.

The host already has `OverlayShellPage.ExpandBrowserAsync` and
`BrowserOwner.Expanded.cs` for moving an existing browser into its fullscreen
layer and returning it to a valid source slot. Its pop-out regression checks that
expansion preserves live media and the document navigation count. Start with
that path for the incoming receiver. The specific integration to qualify is
main-view demand coexisting with a selected Discord pin and retaining audio
ownership across the transition. Estimated complexity is moderate for the
widget route and receiver lifecycle. Scope any host change after this check.

Acceptance covers Watch with no pin selected, existing Watch & pin behavior,
watching a stream that is already pinned, multiple simultaneous shares, switching
streamer, exiting fullscreen and closing the viewer while a pin remains. Verify
one audible stream per source, hidden-overlay behavior, aspect fit, predictable
controller focus, keyboard/mouse dismissal, source end/disconnect during each
transition, reconnect, and release of unused receiver resources. Use the private
test call for the live picture/audio pass. The widget owns stream selection,
audio intent and receiver demand. The host owns fullscreen layering, input and
focus restoration. The implementation and native qualification are recorded
below; playback with a real peer still needs acceptance.

### Attachment viewer accessibility follow-up

The generated PNG/MP4 gallery fixture now exercises 150% text with the host's
high-contrast palette. Inspection exposed a loading ring drawn in system ink
directly on the transparent stage. Shared `MediaPlayerView` now supplies the
paired system surface behind loading and error feedback in this mode. The
backing fits the feedback, accepts no input and collapses when content arrives.
Discord, Capture Library, Game Help and the SDK Gallery use this shared player.
WebView-based incoming streams have a separate receiver presentation.

`feedback-gallery-42-accessible` passes 40 native checks and the standard run
passes 35. The captured failure message wraps on its backing, the image retains
its aspect ratio and the enabled fullscreen arrow retains its own opaque button.
The fullscreen screenshot records XAML controls. Active MP4 playback was checked
through native player state. `feedback-chat-expanded-42` passes 29 Discord
navigation and return checks. The corrected host is staged in
`feedback-release-host-42`, with published dependency and cleanup verification.
The earlier .42b candidate continues running.

### Incoming stream viewer implementation and host qualification

Discord .43 now offers separate Watch and Watch & pin actions on shared-screen
rows. The viewer borrows an existing selected receiver for the same stream or
opens one of the three bounded slots. A viewer-only selection requests no host
pin. Navigation lifetime revokes its demand on Back and late completions are
discarded. Worker Background releases a viewer-only receiver and returning to
an active lifetime reacquires it. The existing friend-activity action still says
Watch & pin. The viewer uses the existing lease-scoped stream mute and volume.

One receiver owns one WebView. For the same stream, a visible main viewer takes
the picture while its pin remains selected. Hiding the overlay or leaving the
viewer gives the picture back to the pin. This preserves the subscription and
document across transitions. The provider retains its three-stream limit and
audio remains in the Discord session. Different streams retain separate slots.

The host's ordinary browser expansion requires a surface eligible for site
decisions, which deliberately excludes passive video. The host now admits an
explicit fullscreen presentation for a visible, live backend-video document.
Pointer, browsing and permission policies remain with the original passive
receiver. The native slot supplies a themed Fullscreen button and A/Back hints.
The shell's existing fullscreen layer owns return and Escape. Only Discord
currently opens `WebSessionSource.BackendVideo` in shipping widgets. Ordinary
browser pages, provider documents, hosted packages, pop-outs, and SDK examples
keep their prior placement precedence and permission paths. File media continues
to use the native media player.

`stream-view-lifecycle-43.log` passes 1,046 managed checks. The generated live
video run `stream-viewer-native-43b` passes using the packaged widget, private
profile, bridge, provider adapter and real WebView2 decoder. It verifies Watch
without a pin, A into fullscreen, two Back transitions with native focus on the
originating row, same-document pin borrowing, continued frames in a hidden-overlay
pin, restored main placement, all three concurrent streams, source end, and three
replacement cycles. Inspected `viewer.png`, `call.png` and the actual decoded
`generated-video.png` captured inside the owned synthetic video document. The
test has no Discord accounts. Live remote picture and subjective audio remain
acceptance items.

The .43 Release candidate passed dependency and cleanup verification. The
isolated catalog applied the reviewed package with SHA-256
`f078a85a1da637db6015484ecdd8270862dd40f5d9ffee4b1ba2338fceca808f`.
The prior candidate displayed the idle server picker before restart.
`stream-candidate-43/launch.json` records the replacement, and the running worker
was verified against the selected .43 package path. `stream-ui-main-screens-43`
passes 23 compact and 29 expanded native checks and includes screenshots of the
call, conversation, chat and gallery surfaces.

### Sender ends a fullscreen stream

The native receiver fixture now ends one of three generated streams while that
stream is fullscreen. The previous host sometimes left the ended viewer without
keyboard/controller focus. `stream-end-qualified-native-43/result.json` records
the failure after the sender ended. The shell now requests return focus for an
invalidated passive-video expansion when the main widget still owns input. The
existing fullscreen transition checks foreground and presentation ownership again
at completion. A surviving slot must still host the same video. Otherwise the
presenter restores entry focus, which places focus on **Back** in the ended viewer.
Ordinary browser and pop-out invalidation retains its previous focus policy.

`stream-end-focus-fixed-native-43` verifies the selected receiver closes, Back
has native focus, and one press returns to a surviving call control. Both sibling
pins received 11 more frames each when their generated canvases were explicitly
advanced. The control test also enabled mute, moved the volume slider from 50%
to 55%, observed the provider's stream-specific gain and the widget's displayed
55%, and entered fullscreen with that stream still muted. The generated provider
performs no remote calls. Subjective playback and remote account acceptance
remain user checks.

The independent repeat `stream-end-focus-confirm-native-43` again observed Back
focused and confirmed volume. Both sibling receivers advanced ten frames. The
updated host passed Release verification (317 local managed references and both
async lock releases). `stream-ended-candidate-43/launch.json` identifies the
running host paired with the same reviewed .43 widget and default profile.

### Hidden viewer and independently pinned streams

`viewer-visibility-native-43` opened one unpinned stream beside two different
pinned streams, hid the overlay, waited for the actual main browser slot to stop
displaying, and sampled the generated provider after three seconds. All three
subscriptions were still active. Returning and pressing Back ended only the
unpinned subscription. Existing checks separately cover background teardown when
there are no pins, and picture handoff when the main viewer and a pin share the
same stream.

`OverlayShellPage.LifecycleFor` combines the visible main surface and pins into
one worker lifecycle. `DiscordWebViewWidget.ViewerWanted` therefore sees the
worker as active in this mixed case. Choosing the Interactive state as a proxy
would also stop a visible stream on ordinary focus loss and could count input
to a different pin as a main viewer. The host knows the actual placement. A
consistent pause/resume policy across these cases would need a scoped main-view
visibility signal or equivalent host-owned demand observation. This is related
to F12 in `widget-authoring-plumbing-plan.md`. Keep explicit pin ownership and
the Discord subscription's original owner separate. The user has been asked
which hidden-audio behavior they prefer. The subsequent decision and .46
implementation are recorded below.

### Pause an unpinned incoming stream while the overlay is hidden

The user chose to pause an unpinned Watch stream and resume it on return.
The shell now publishes the identity of its visible, committed main widget to
the browser owner when it reconciles media placement. A sealed session backend
can observe its own main panel through `host.observeMainView()`. The native
host sends a boolean on the existing bounded session message channel at
registration and when placement changes. Registration ends with the adapter
document. No browser page controls this value or gains a permission from it.

Discord combines that signal with the viewer visit, session, and the selected
stream's own pin. Hiding retires the widget-owned unpinned receiver and
subscription while retaining the selected stream. Returning to the same visit
reacquires it. Closing the visit, a call change, or an ended stream prevents
resubscription. A selected stream pin continues independently. Voice and
outgoing sharing continue to follow their existing lifecycles.

| Consumer | Behavior |
| --- | --- |
| Discord incoming viewer | Opts into main panel visibility. `hidden-view-native-46` observes three subscriptions before hide, only the two pinned subscriptions while hidden, and a newly decoding unpinned viewer on return. Back releases that viewer. |
| Other hosted browser pages, webapps, and SDK examples | No visibility messages unless the sealed backend subscribes. Discord is the only shipping session backend using this opt-in. Page actions, browser permissions, and capture remain on their existing paths. |
| Native media, Game Help, and other widgets | Existing native presentation and widget lifecycles apply. No new broker operation, protocol version, or author obligation. |

Managed tests cover a delayed receiver opening after hide and leaving the
viewer before another visibility change. The generated stream test also
verifies fullscreen, pin handoff, stream-specific gain, and return focus.
Real-peer audiovisual acceptance remains open.

### Playnite poster reads during continuous right-stick scrolling

The host must keep reading the originally approved file across path replacement
and deny concurrent writers during the read. The native file-open variants in
`file-sharing-probe` failed one of these checks. Retain the checked file handle.

Copying bounded bytes from that handle to a native stream preserved the pixels.
In the same 183-game Library traversal it reduced managed allocations from
420 MB to 332 MB, but total GC pause time rose from 916 ms to 966 ms. The 95th
percentile interval between viewport updates rose from 16.3 ms to 19.5 ms,
and the longest interval rose from 88.8 ms to 100.2 ms. Rejected byte staging
for this scrolling issue and removed the experimental path. The paired results
are in `file-sharing-probe/comparison.json`.

### Observe ordinary image visibility when animation starts

The saved allocation trace placed WinUI effective-viewport event delivery on
10 of 22 induced collection trigger stacks. This implicates event delivery
across the native boundary, without identifying a single subscriber. Ordinary
image cells had subscribed at construction in case their source became a GIF.
The host now attaches a stretched, unpainted observer only to an image that
actually decodes as animated. Attaching before that child's first layout lets
WinUI deliver the current viewport after a late decode. Removal follows the
playback owner and leaves the original image geometry and automation peer intact.

| Consumer | Scope and evidence |
| --- | --- |
| Playnite ordinary posters | Shared host change. A matched four-leg right-stick replay reached both ends of the real 183-game Library with the same extent, scale and provider. |
| Discord conversation artwork and ordinary SDK images in other widgets, samples and templates | Existing image nodes use the same renderer. The native probe checks late source availability across three replacements, input transparency, bounds, original children after retirement, and pause/resume on motion-policy and viewport changes. No author migration. |
| Widget presentation surfaces, media viewer images, inline videos, and context indicators | Keep their existing observation and ownership. The image viewer passed its native policy and source-replacement checks. |

In `library-viewport-animated-43/comparison.json`, managed allocations fell
from 420 to 308 MB, and GC pause time during viewport updates from 916 to 488 ms.
Counting consecutive changing viewport positions inside the scrollable range,
the 95th percentile interval fell from 15.9 to 10.3 ms, gaps over 40 ms from
32 to 13, and the maximum from 88.8 to 86.4 ms. This is one pair of controlled
traversals. Physical scrolling still needs acceptance; long pauses remain.
The native artwork and static-paint fixtures passed 43 and 9 checks. The
120-frame GIF resource test passed 27 checks and collected all six retired
images. The trimmed `library-viewport-release-43` host passed dependency and
async cleanup verification and uses the same .43 Discord widget.

### Open a stream while its pin is still connecting

The .43 widget restarted a pending pin receiver when the user opened Watch
before the first picture arrived. Opening Watch, pressing Back, and opening
Watch again failed the one-request check in `pending-pin-baseline-43.log`.
The widget owns this decision because it knows whether a pin or viewer selected
the stream and whether that selection already failed. No new host or SDK API.

In .44, entering or reactivating the viewer leaves a selected pin's connection
in progress. An explicit Watch on a failed pin still retries. The delayed
receiver test covers Back, reentry and lifecycle suspension, and verifies a
single receiver request and provider watch command. The friend activity
shortcut still opens the pin. Its accessible name now includes the same
“Watch & pin” action shown visually.

`pending-pin-fixed-44.log` passes 1,049 widget checks. The packaged native
fixture `pending-pin-native-44` confirms fullscreen return focus, gain
confirmation and independent sibling playback. Both siblings advanced ten
frames after the selected stream ended. The call, viewer, and stream-ended
captures retain the existing layout. `pending-pin-candidate-44/launch.json`
identifies the running isolated candidate on the user's default profile.

### Unread state with the main chat hidden and a voice pin visible

The host reports a visible worker while an independent pin remains onscreen.
Message acknowledgment also checks the history collection's active viewport.
The .44 managed regression keeps the voice pin selected and the widget active,
retires the chat viewport during the 250 ms delay, and observes zero provider
read commands. Restoring the chat submits its unsent opening watermark once.
All 19 read-state checks passed in `pinned-chat-read-44.log`.

The native `Test-Messages.ps1 -PinnedReadOnly` scenario uses the real shell
pin command and a generated conversation. It holds the provider response until
the overlay hides, releases it while the call pin remains, waits past the read
delay, and reopens the chat. Both 420×360 and 1040×680 passed: the provider's
read marker stayed unchanged while hidden, then advanced to message 1120 in
the intended conversation. The reopened layouts show the composer and call bar
in both sizes, with the selected conversation alongside the expanded chat.
Evidence is in `pinned-chat-read-host-44/{minimum,expanded}`. The initial test
attempt used a pin-request capability missing from the synthetic worker. The
host tray pin command tests the same placement with the existing widget layout.

The same evidence directory contains 250 passing source provider/backend checks
and 186 passing minified and composed-backend checks, including startup of the
installed .44 backend asset. This change adds regression coverage and corrects
the user guide and widget README. Synchronization of the signed-in user's
unread marker across Discord clients and audio from another participant remain
live acceptance items.

### Keyboard dismissal of the pin adjustment toolbar

The earlier .39 smoke sent Escape to the main overlay. In `pin-escape-native-44`
the generated call opens participant pin management, waits for the toolbar
window to own the foreground, focuses its opacity slider and changes opacity.
One native Escape closes the toolbar, restores the original opacity, returns
the main window to the foreground and restores focus to the same participant
Manage control. The complete voice-pin run passed 127 checks. No production
change was needed for this interaction.

### Remaining Playnite pauses in the saved traversal

The host collector records both CLR suspension start/end and native viewport
update times on the same clock. Intersecting them in
`library-viewport-animated-43/long-gap-overlap.json` shows that eight of the
13 remaining interior viewport gaps over 40 ms spent at least half their time
in a managed suspension. The longest gap was 86.4 ms, including 54.7 ms
suspended. Three gaps had less than 1 ms of overlap. These samples distinguish
managed suspension from other delays; they cannot identify the thread or code
responsible for the rest of each interval. The comparable earlier traversal
had 23 of 32 gaps with at least half their duration in a suspension.

### Attachment continuity after an initial redirect

The transport used to revisit the original alias for every media range. HTTP
strong validators apply to a representation of a specific target resource.
Length and ETag alone could therefore accept a different redirect target in
the middle of a stream. The new generated regression failed on .44's earlier
host because the second block went back to the alias.

The shared host transport now binds range reads to the exact effective URI of
the first block. Seeking through an ordinary initial redirect keeps working.
If the resolved file starts redirecting, that open stream fails and its cached
blocks become unreadable. The conditional validator is never sent to the new
destination. A new viewer visit can open the source afresh. The whole-file
fallback still uses one bounded response and never joins unversioned responses.

| Consumer | Applicability |
| --- | --- |
| Discord inline video, video-backed GIFs, and gallery video attachments | All use checked public files through the shared native media reader. No widget migration. |
| Public images and posters | Whole-image downloads still follow admitted redirects. The native probe also decoded an image. |
| Capture Library, other first-party widgets, samples and templates | No other shipping consumer declares a checked public video source. Local grants, ordinary image URLs, WebViews and pinned WebRTC streams use separate paths. |

All 97 transport tests passed. The WinUI probe opened an 854×480 public MP4,
sought forward beyond its initial buffer and back, and retired the native reader
on source withdrawal. All 17 native checks passed. `ranged-attachment-44`
contains the failing baseline, passing tests, native playback result and launch
receipt. The updated trimmed Release host passed dependency and async cleanup
verification. Discord remains on package .44 with the user's default profile.

### Stream sender names on compact call screens

The 420×360 viewer at 150% text had room for a 380×198 DIP picture, Back,
Fullscreen, mute, the volume slider and its value. On returning to the call,
the earlier compact row truncated “Generated sender” to “Gener…”. The .45
widget puts the sender and Live/Pinned status on a dedicated compact heading.
Watch and Watch & pin remain adjacent. Expanded cards retain the inline name.
This uses the existing responsive visibility API; there are no new focus stops.

The native small-screen check verifies actual control bounds, untrimmed sender
text, and focus on the originating Watch action after fullscreen and Back.
Both sizes passed the generated three-stream playback and handoff checks.
The first harness configuration omitted the required backdrop opacity; it was
corrected before recording layout evidence. Comparison captures are in
`compact-stream-layout-44`, `compact-stream-fixed-45` and
`expanded-stream-fixed-45`. The .45 main-screen run passed 41 compact and 51
expanded checks. Servers, chat and gallery captures were reviewed; unavailable
fixture thumbnails used file placeholders. The managed widget suite passed
1,052 checks. The reviewed .45 package is running on the qualified
`range-release-host-44` in the isolated catalog with the user's default profile.

### Playnite shortcut-guide measurements

The context badge is created on demand in
`NativeComputedStyleAdapter.RefreshContextIndicator` after a control receives
focus and an available context command is admitted. Ordinary unfocused tiles
do not construct it merely because they offer game options. Retain the badge's
native visibility observation.

A fresh allocation trace of the corrected host on the real 183-game Library
placed `WidgetIndexedCollectionView.HasCurrentGuideRows` on two managed
memory-pressure collection trigger stacks. It measures realized row bounds to
determine whether any visible row still prevents shortcut-guide publication.
An experiment avoided transforming settled rows once a visible row was known.
Under matched 16-second right-stick traversals across the same four legs,
managed allocations fell from 341 MB to 333 MB and suspension time from
501 to 482 ms. Viewport intervals went the other way: p95 rose from 9.4 to
10.1 ms, gaps over 40 ms from 15 to 17, and the maximum from 104 to 114 ms.
This comparison did not establish smoother scrolling. The experimental
change was removed; the candidate retains the original readiness check.
`library-current-stacks-45` holds the sampled stacks and
`library-guide-viewport-45/comparison.json` holds the paired results.

### Keep the call page stationary under voice and connection dialogs

Opening Voice settings or Connection details pushed a dialog visit and also
changed the call section's transition key. The Home-only notification setup
control could disappear, moving the section beneath the modal. Both entry
points originate on Home. The widget now presents that same page for the
dialog's background and leaves the dialog's navigation visit, input scope,
focus restoration, and device/connection data lifetime intact. Participant
audio controls already use a separate focus preview. No host API change was
required.

A managed assertion for the background's transition identity failed before
the fix. The complete widget suite subsequently passed 1,054 checks. With
full motion enabled, the native voice-controls fixture sampled the call
section during opening and dismissal of both dialogs at 420×360 and 1040×680.
The same native section stayed loaded and stationary, no new section/page
playback started, and controller Back restored the invoking control. The final
run sampled 144 compact and 106 expanded rendering frames across those four
transitions. See `dialog-motion-46/modal-content` (42 checks per size). Dialog
captures show readable device selectors at both sizes. The compact connection
panel scrolls its longer details within the dialog. The
fixture now enters the participant preview with X and still exercises local
mute, slider adjustment and exact participant return.


### Confirmed sends from older Discord history

A confirmed outgoing message used the same passive update policy as incoming
messages. The .45 widget left a historical window in place even after the
sender chose Send. Discord now uses an exact SDK indexed reveal for the
confirmed message ID and returns focus to the composer within the originating
chat visit. A retained message costs no new history request. If the reading
window has a gap, one bounded latest-page request finds the sent item. If a
burst has already displaced it, a single bounded around lookup can locate the
exact ID. Pending delivery, failures, and late confirmation in another visit
do not request a scroll.

The native test found a separate shared host issue: during a query replacement,
passive recovery for the evicted historical reader could run before a new,
explicit indexed reveal. The failing compact trace fetched around old row
1039 even after receiving the latest page with sent row 1121. The owning
presenter now admits a current, exact reveal before starting or retaining
passive recovery. Existing query, input-scope, interrupted deferred-focus and
request-ID guards still determine admission.

| Consumer | Scope |
| --- | --- |
| Discord history | Confirmed sends opt into the indexed reveal. Unsolicited incoming messages retain their existing viewport and new-message policy. |
| YouTube Music queue | Its exact indexed queue-item reveal uses the same host priority. No widget change. |
| Game Help chat | It already reveals the submitted question's local element. The remote-history fetch and indexed-query recovery are specific to paged data. No corresponding lookup is needed for Game Help's transcript. |
| Other collections and ordinary scroll controls | The change only applies to an admitted exact indexed reveal in the owning presenter. Normal prepend/append, no-request updates, and non-indexed descendant reveals keep their paths. |

The .45 binary fails the exact-target test in `sent-reveal-baseline-45.log`.
The combined managed widget suite passed 1,062 checks in
`sent-message-reveal-tests.log`. `sent-reveal-native-46b` records the old
native reader taking precedence over the .46 widget's reveal.
`sent-reveal-host-fixed-46` passed 12 checks each at 420×360 and 1040×680.
The captured opening view shows the older unread boundary at 1040. After
one generated send, the complete 1121 row is visible above the focused
composer. The generated provider performed no sends to a real account.

### Confirmed sends from older history and the matched .46 candidate

Discord now resolves the exact provider-confirmed message in the current visit.
A retained row needs no fetch. Historical windows load bounded data to locate
that ID. Uncertain sends keep their nonce, files and position. A confirmation
for a departed visit cannot move the new chat. Game Help already reveals the
authored question in its 20-message local transcript. Its new Latest answer
shortcut uses the existing non-focusing reveal. The two-widget comparison and
evicted notification target case are in `discord-messaging-refinements-plan.md`.

The first native Discord test with the widget fix still saw the host recover
the old reading anchor. The presenter now prioritizes a newly admitted scoped
indexed reveal over passive viewport continuity. The same precedence applies
to YouTube Music's explicit queue occurrence. Ordinary passive paging,
stale query rejection, and user cancellation retain their existing guards.
Game Help and SDK Gallery use ordinary keyed scroll children, so this change
does not alter their reveal path. Other shipping widgets and templates do not
issue explicit indexed scroll-reveal requests.

The final send fixture passed 12 checks per size at compact and expanded widths.
It submitted one synthetic message per run and observed the confirmed row
inside the native viewport with composer focus. Shared indexed checks passed
10 for reveal, 16 for continuity and 34 for end following. Game Help passed
52 managed and five native checks, including sending a suggested question from
older history and returning to the latest answer.
The full Discord managed suite passed 1,062 checks. Source and minified/composed
backend checks passed 250 and 186 respectively. The generated fullscreen hide
test released the unpinned subscription, observed additional decoded frames on
both pinned streams and resumed a decoding viewer on return.

The reviewed Discord .46 and Game Help .9 packages are selected and enabled
in the isolated catalog. `send-dialog-stream-candidate-46/launch.json` records
the running default-profile host. Real-peer audiovisual acceptance remains open.

### Read-state warning after sending from older history

The original .46 widget issued a second read command for its own newly sent
row even when Discord's read-state store still reported the previous head.
The synthetic native reproduction in `send-read-old-native-47` confirmed one
send and one rejected write. Sending itself had succeeded.

The widget now uses the latest incoming row through the observed tail when
advancing a live read marker. A normal conversation entry still processes
Discord's exact opening watermark, including a chat manually marked unread.
In the adapter, a known target already covered by the read cursor can return
“already read” even if the store's head update has not arrived. Account,
channel, visit and target identity are checked first. An actual write ahead
of the provider head is still rejected.

`send-read-fixed-native-47` passed at compact and expanded widths, with one
generated send and zero writes ahead of the head per run. The sent row stayed
visible. All 26 focused read-state checks passed, including a following
unseen reply and a genuine read failure. These reproduce a cause of the
reported toast without requiring a real message.

### Media error flash during rapid section switching

The native player's refresh timer, media-open callback and asynchronous
resolution failure used the unsupported-format presentation after an indexed
row had lost its authority. A discarded Messages row could therefore still
display an error during the transition to Servers. The shared player now
retires that source, clears its pixels and decoder, and accepts a future
current binding of the same declaration. Open/failed events also verify the
native player they belong to. A decode failure for a current visible source
still presents its error.

In the generated native acquisition race the previous error path painted
the failure on 7 of 8 sampled frames. The fixed player had zero error frames
in 107 samples covering late resolution, rebind/play and withdrawal of active
playback. A generated invalid MP4 still displayed its error. The separate
Discord full-motion fixture performed three rapid Servers/Messages round trips
from a visible inline video; both compact and expanded runs sampled zero
failure frames, with 42 and 52 checks respectively. Evidence is in
`media-retirement/{before-b.json,after-b.json,section-switch}` under the common
artifact root. The WinUI compositor can coalesce rendering callbacks; these
counts cover the sampled frames.

The host owns failure and retired-resource presentation. Discord inline
video, animation and gallery, Game Help capture previews, Capture Library
viewers and SDK Gallery players share this path. No widget migration is
required. Browser/RTC streams use separate ownership and transport.

### Inline playback startup and transient format errors

A click-to-play probe uses the real host and Windows decoder with a public
4.4 MB MP4. It counts HTTP requests and timestamps source resolution, first
response block, metadata completion, playing and the first advancing playback
clock with nonzero video dimensions. It does not read account data or signed
attachment URLs. These are single-run observations of that media source.

| Qualification | Cold: requests / elapsed to advancing clock | Return after scrolling: requests / elapsed |
| --- | --- | --- |
| 64 KiB blocks, reacquire from scratch | 11 / 1,018 ms | 11 / 535 ms |
| 256 KiB media blocks | 3 / 578 ms | 3 / 267 ms |
| 256 KiB with one eligible warm source | 3 / 577 ms | 0 / 154 ms |

The returned row must still resolve the same valid grant. Checked compressed
blocks can be retained for 15 seconds for one player across the process.
No decoder stays active offscreen. The native timing fixture also rejected
changed grants, expired entries and replacement row owners for the same URL.
See `public-inline-{baseline,ranges,cache}-47.json`. The media range and
decode-admission suite passed 98 tests. Playback-clock timings exclude any
provider-specific delay on a real Discord attachment.

A stale row acquiring or playing a video previously called the same failure
path as a current unsupported file. The pre-fix delayed retirement painted
seven format-error frames in eight samples. The revised player released its
resources quietly during retirement and retained the error for a genuinely
undecodable current source. The controlled native cases had zero transient
errors in 107 samples. At compact and expanded sizes the synthetic Discord
widget repeatedly switched Servers/Messages; neither run showed an error in
the sampled transitions. See `media-retirement`.

### Fullscreen playback after resuming an inline chat video

The shell collapses its ordinary widget layout when fullscreen entry settles.
The native player was still observing the original chat row. That geometry
made entering fullscreen look like scrolling the video out of view, which
released the decoder and restored the central Play button. An indexed native
test reproduced the release with the original host.

Inline playback now observes the row during normal chat use and the native
fullscreen layer during the fullscreen visit. Brief detachment while moving
the card does not suspend playback. The final placement rechecks visibility;
retired, hidden or unauthorized sources retain their own teardown. No
widget-specific player, root-staged video plane, or permission change is needed.
Discord is the shipping `UI.InlineVideo` consumer. Other public media,
gallery videos, decorative GIFs, captured recordings and browser streams keep
their existing presentation and playback ownership.

`inline-fullscreen-46/placement-native` passed 17 checks with an actual
48-message indexed list and a generated MP4. The test played, scrolled the
source fully outside its viewport, returned, entered the row and pressed the
visible focused Play button. In fullscreen the playback clock advanced on
the same decoder, the central Play button was hidden and a displayed pixel
matched the green source. Unmuted audio remained set to 35%. A second
fullscreen visit while paused preserved position and paused state. Back
restored the inline action, then the original message. Teardown left zero
resident players. XAML captures show the focus ring and transport; the green
pixel assertion samples the displayed video, which XAML bitmap capture omits.

The .47 shipping host passed the dependency and async cleanup verifier. After
the approved restart, `inline-playback-candidate-47/launch.json` identifies the
running trimmed host with the .47 Discord worker and the default user profile.
Both the Discord and Game Help installations matched their reviewed archives.
The 31-check native inline return fixture confirms Play receives native
keyboard focus. The 17-check indexed fixture confirms offscreen pause, explicit
resume, decoded moving video with the chat collapsed in fullscreen, retained
volume/mute, paused playhead and Back focus return. The audio and passive
video-animation regressions passed nine and twelve checks respectively.
