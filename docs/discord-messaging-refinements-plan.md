# Discord messaging refinements

Started November 18, 2026. Status: implementation and independent qualification
complete; matched Release candidate running for user acceptance. No merge,
push, installer or release performed.

## Final delivery and acceptance boundary

### User acceptance follow-up: navigation and preview fixes

The November 18 acceptance run found additional defects. The running follow-up
candidate uses `artifacts/discord-navigation-up/frontend-final`, the existing
matched protocol-131 service/native payload, default settings and the isolated
catalog, with Discord **0.1.0.29 / adapter 46**. Evidence and update/launch receipts
are under `artifacts/discord-navigation-up`. Earlier package versions below are
historical; no merge, push or release is authorized.

- **Collection Up:** reproduced natively (`up-repro/expanded/result.json`).
  `WidgetViewPresenter.DirectionalNavigation.cs` followed collection-level exits
  before item movement, so Discord's `FocusUp(search)` stole every upward move.
  The host now tries internal collection navigation first; per-row explicit
  neighbors keep priority. Native checks cover reverse traversal across 70 rows,
  keyboard Up, top-boundary exit and selected-conversation/composer return.
  Compact boundary checks explicitly distinguish remaining leading padding from
  an exhausted scroll boundary, retaining the existing reveal-before-exit rule.
- **Missing Call:** the new call factory was local to the SessionBackend wrapper
  while the adapter looked for it on `globalThis`. Top-level VM tests concealed
  this deployment error. The existing test fails inside the actual wrapper
  (`call-scope-repro.log`); exporting the factory like the other providers fixes
  that error. The live check then found a second incompatibility: the current
  loaded client exports `call/ring/stopRinging`, not `startCall`. Read-only runtime
  inspection established its `call(channel, video, ring, recipient, onSelected)`
  contract. The adapter now prefers that concrete module and preserves the native
  recipient preflight, with legacy startCall support retained. A void return does
  not end single-flight admission: the completion callback or bounded unconfirmed
  timeout does. No direct token HTTP or separate ring call was added. Tests cover
  recipient/group arguments, method binding, duplicate commands during a delayed
  callback and rejecting inherited Function.call. The final live widget visibly
  exposes Call in the reported chat; no real call was started. Temporary local
  debugging was removed before the final launch. Adapter tests now use the host's
  function wrapper, including minified runs. No microphone checks were weakened.
- **Call-page Down:** Mute, Deafen, Share/Stop and Leave explicitly target
  `discord.people.pin`, using existing authored focus APIs. All four paths are
  checked on the native production widget.
- **False New messages:** a delayed initial history test reproduced an upsert of
  an existing message being counted as new. The widget now compares with the
  exact visit's opening watermark as well as its loaded tail. Recovery/errors
  offer Latest without claiming a new arrival; actual messages arriving during
  loading and deferred live tails keep their indicators. Domain policy remains
  in Discord; SDK mutation `HasUnseenItems` is not an unread-message assertion.
- **Duplicate link actions:** Discord uses a URL set while authoring the preview,
  keeping a descriptive embed action once and retaining distinct text URLs.
  There is no host UI-tree inspection or visible-label parsing.
- **Call dock:** a clickable call summary, participant count and labeled
  Mute/Deafen/Share/Leave actions use existing action surfaces. Compact mode
  hides action text/count and stays on one row. The first two-row proposal failed
  the native minimum-history-space check and was replaced rather than relaxing
  that requirement. Unconnected states retain visible status text.
- **Capture chooser:** removed the requested introductory paragraph in
  `OverlayShellPage.WebCapturePicker.cs`; empty-source recovery stays intact.
- **Attachment presentation:** multiple attachments have an explicit count and
  at most three visual tiles, with video/file glyphs and additional-item count.
  All attachments remain in the gallery, including duplicate-source occurrences.
  Spoilers keep their URLs and titles concealed. A media-only preview uses the
  existing Capture-style aspect-ratio Star grid instead of fixed 280-DIP height
  plus an unnecessary outer scroller.
- **Gallery control/focus lifecycle:** the shared player puts gallery navigation
  above the canvas instead of over it. Horizontal edge inputs stay in that
  toolbar. `MediaPlayerView.ApplySource` parks current native-child focus on the
  stable player before teardown; ready controls regain focus only if it still
  owns it. This fixes the preview dismissal during LB/RB replacement rather
  than masking the problem with a Discord shortcut exception. Actual entered-
  preview tests cover delayed loads, reversal, both endpoints and intentional
  departure. An earlier claim that LB reached the parent section shortcut was
  incorrect; the user correctly distinguished focus loss from section activation.

| Shared mechanism / consumers | Applicability |
|---|---|
| Authored indexed collection exits | Discord conversation list is affected; history's cross-panel exit remains. Other shipping/sample/template collection declarations have no equivalent container-level exit to migrate. PoB row-authored detail links and existing Spotify row neighbors retain priority. |
| Media source focus / gallery layout | All shared media players receive the host fix: Discord, Capture Library, Game Help and SDK Gallery. Only Discord currently declares a multi-item gallery; other players keep collapsed gallery chrome. Browser-hosted streams use their separate WebView path. |
| Other preview replacement paths | Ordinary presenter application already captures/restores logical focus before subtree replacement (`WidgetViewPresenter.cs` / `.Focus.cs`). Select popup return has `OwnsSelectFocusReturn`; text entry retains exact focus authority. These are source-reviewed existing safeguards, not a runtime proof against every possible dismissal. Stale row authority may deliberately retire a preview through `PreparePreviewRefreshFocus`; no blanket ignore-focus-loss workaround was added. |
| Capture chooser copy | All host application-capture consumers get the same copy reduction. Source selection, quality eligibility, consent and application-audio controls are unchanged. |

Qualification: **920 widget checks**, **197 adapter/backend checks**; the focused
wrapped/minified adapter set passed **172/172**. Native production Messages suites
passed **88 minimum / 88 expanded**, and the large-conversation regression passed
**38**. Shared gallery controls passed **20**, including fullscreen image/video
switches; actual entered-preview focus/sizing passed **14** across wide/compact
and normal/150% text. Shipping dependency and cleanup verification passed for
**304 local managed references**. The candidate reached input-ready and catalog-
complete. Real peer call/audio acceptance remains a user check; no real message,
call, broadcast or capture mutation was performed by these tests.

This section supersedes the historical checkpoints below, including their
pending-build, pending-feature and older protocol/API-baseline statements.

- Matched shipping host: `artifacts/discord-messaging-refinements/shipping-1`,
  version `0.1.0-preview.41.local.1`, protocol **131**. Native inputs, managed host,
  Bridge, WorkerHost, Settings and bundled widgets were rebuilt together.
  Dependency verification checked 304 local managed references; catalog discovery
  passed. Candidate verification matched **144 deployed DLLs** and checked **22
  native capture/preview exports**. API baseline: **4158 symbols**, compatibility
  **2/2**; native/protocol parity: **221 constants**.
- Running candidate uses the default settings root with a separate catalog of
  the profile's 20 selected widgets. Packages: Discord **0.1.0.24**, Playnite
  **0.4.8**, Capture Library **0.2.1**, Network Controls **0.1.11**. Updates used
  CLI `update --apply` with the default `--settings-root`, preserving matching
  settings and permission decisions. The production catalog state hash is
  unchanged. Evidence: `candidate/launch-final.json`, `inventory-final.json`,
  `update-receipts.json`, `playnite-final-update.json`, `payload-verification.json`
  and `live-smoke.json` beneath the artifact root above.
- Live shipping smoke: Discord retained sign-in and loaded servers and the
  conversation list; Capture Library loaded real captures and exposed distinct
  Hide/Delete actions; Network Controls loaded status/tabs without a new consent
  prompt; Playnite loaded Home and Achievements without restarting Playnite.
  No real messages, calls, recordings, file hiding or deletion were performed.
- Shipping smoke found a packaging issue: fresh compilation of unchanged Playnite
  integration sources produced different binaries and triggered an unnecessary
  integration update. The optional explicit trusted-bundle reuse path in
  `samples/PlayniteLibraryWidget/Build-IntegrationPackage.ps1` preserves exact
  runtime binary verification. Its source/version/identity and archive/entry
  hash checks passed; mismatched-source and checksum cases were rejected.
  `playnite-final-build.log` verifies all six bundled files. Installed Playnite's
  integration DLL exactly matches the reused bundle. See the development notes
  for the trust/build-input boundary; this is not automatic source-based trust.

| Required outcome | Completion evidence / limits |
|---|---|
| 1. Stable chat updates and reading position | Messages native suites: 77 expanded / 76 minimum checks. Repeated section return: 34 expanded / 29 minimum, exact visible-message/clipped-fraction preservation. |
| 2–3. Reachable header, Server/Call labeling, conditional dock | Production widget/native fixtures and 913-check Discord suite; default shared tab behavior retained. |
| 4. Automatic bounded conversations and panel entry | 120-conversation native fixture: 24 checks, 70 D-pad moves, selected-key lookup, bounded loads and no eager history. |
| 5. GIF and GIF-like video viewing | Real native GIF pixels/lifecycle, 28 artwork rebind checks, 86 static-artwork regressions; 20 integrated passive-video checks through row/worker/bridge. Real Discord CDN attachment acceptance remains live. |
| 6–7. Attachment gallery and shared preview lifecycle | Native Messages preview/navigation tests, HTTPS image checks, SDK media 4/4 and bridge media 10/10. Separate bounded passive-video budget; capture custody retained. |
| 8. Private calls and alerts | 31 focused checks, 913 full Discord checks, 7 synthetic provider checks; 17 native call checks at each size including actual host inbox/banner/Open/Answer/Leave. Real peers/audio remain live acceptance. |
| 9. Capture LB/RB actions | 5/5 replay checks cover save-without-stop, cancellation, busy input and stale sessions. Hardware controller acceptance remains. |
| 10. Capture selection toolbar, Hide/Delete | 36 native layout/input checks at two widths and enlarged text; managed mixed-source, permission, cancel and recycle-dispatch checks. Hide remains linked-entry-only by deliberate scope decision below; no permanent-delete fallback. |
| 11. Playnite achievements utilities | 35/35 managed checks and 6 native production-snapshot checks cover traversal/activation. Shipping header is present; Sky's final generic-pane focus report does not independently prove hardware traversal. |

Consumer matrices and narrowed/rejected proposals remain recorded below. The
remaining user checks are real incoming/outgoing peer calls and audio devices,
physical controller behavior, actual Discord animated attachments/CDN delivery,
and continued everyday use. These are acceptance limits, not claims established
by synthetic fixtures. Leave the candidate running; wait for user acceptance
before merging or publishing.

## Historical implementation and qualification checkpoints

### Latest continuation: full Discord native qualification

- The expanded Messages fixture passed **77 native checks**
  (`messages-native-expanded-2/result.json`); the minimum 420×360-DIP fixture
  passed **76** (`messages-native-minimum-1/result.json`). These cover the real
  production widget/worker and host: header traversal, lazy histories, opening
  read acknowledgments, inline text, controller/keyboard/mouse preview entry,
  bounded history, composition, synthetic upload cancellation, Back and dashboard
  actions. A fixture-only hover target now waits for actual preview layout.
- New 120-conversation fixture passed **24 native checks**
  (`messages-native-large-2/result.json`), including 70 sequential D-pad moves,
  bounded automatic range requests without fetching the entire catalog, exact
  off-page lookup, no eager history, selected-row/composer entry and section
  return. The section-return check waits for actual native focus restoration
  rather than attempting focus as soon as a new control reports Loaded.
- The private-call provider fixture passes **7 state-contract checks**
  (`calls-contract-3/result.json`). It uses no Discord account, device, media or
  network. It validates its synthetic state and records command/microphone
  admission before the normal application bootstrap delivers notifications.
- Native call qualification found a **real widget manifest omission**:
  `samples/DiscordWebViewWidget/manifest.json` declared the call-open handler but
  not the outgoing request used by its notification. The bridge's existing
  `RegisterNotificationIntent`/`Declared` check correctly rejected it. Added the
  matching request declaration; this adopts the existing contract, with no host
  authorization change. The focused call/alert suite passes **31 checks** and
  the full Discord suite passes **913** (`discord-native-qualification-tests-1.log`).
  Game Help is the other shipping custom notification-intent consumer and already
  declares both request and handler; other shipping notification publishers have
  no custom OpenIntent to migrate.
- After that fix the private-call fixture passed **17 native checks at each
  size** (`calls-native-6/minimum/result.json` and `expanded/result.json`), with
  both frontend exits verified zero. This covers Decline without microphone
  admission, outgoing→active, Leave, a hidden-source incoming inbox/banner,
  notification Open focusing Answer without joining, explicit Answer after
  synthetic microphone admission, alert withdrawal, and final Leave. The fixture
  respects the existing quiet period after its initial invitation; its recorded
  expiry proves why the earlier immediate second-banner assertion was invalid.
- Repeated section returns passed **29 minimum / 34 expanded native checks**
  (`messages-section-return-1`), including three Call/Messages round trips
  preserving the exact visible message and clipped fraction, no history refetch,
  and selected-conversation/composer directional entry.

The native fixtures use protocol-131 managed host/CLI/worker payloads and a fresh
isolated catalog/profile, with the validation frontend's previously documented
native-activation DLL limitation. Matched shipping publication and candidate
installation remain pending; none of these fixtures modifies the user's profile.
Local candidate package versions are now Discord **0.1.0.24**, Playnite **0.4.7**,
Capture Library **0.2.1**, and Network Controls **0.1.11**. Version increments
permit immutable updates of the affected installed packages in the isolated
candidate catalog. They are not published releases or default-profile installs.

### Latest continuation: passive video animation

The earlier pending inline GIF-like video item now has an implementation, still
under integration qualification. Protocol 131 adds explicit passive animation
presentation, decorative-motion options for interactive previews, and bounded
URL posters. `UI.Animation` keeps focus/input with the message row. The host owns
viewport/hidden/reduced-motion policy, a separate two-decoder limit, and teardown;
the widget supplies the media URL, thumbnail and semantic animation intent.
Ordinary interactive media retains its existing four-slot limit and admission.

Latest evidence: `video-animation-integrated-3.json` passed 20 native checks,
including a real indexed presenter/worker/session/bridge path, inherited motion
preference, preserved row focus, the shell's resident media plane, overflow
slot recovery, suspend/resume and disposal. Only the final
native video URI is substituted with the existing synthetic MP4, after the real
pipes authorize the declared HTTPS source; this does not qualify actual Discord
CDN delivery. The preceding integrated run exposed a resume failure. The motion
observer now lives with its element, so replacing source/lease identity does not
discard known viewport geometry; the fixture also resolves the currently realized
row after resume rather than assuming the old control remains current.

`animation-image-regression-1.json` passed all 24 GIF checks after extracting the
common visibility policy. SDK media groups passed 4/4 and bridge media groups
passed 10/10, including end-to-end passive source resolution, forged/stale row
rejection and preserved data authority beneath a modal without interactive-media
admission. `animation-discord-1.log` passed 912 widget checks including production
GIF-like embed projection. Public API baseline is now 4158 symbols; compatibility
checks passed 2/2. Protocol/native-header parity passed for 221 constants at 131.
These validation runs reused the older native activation DLL; shipping still
requires a matched native build.

| Consumer of passive/decorative video contract | Status |
|---|---|
| Discord GIF-like embeds | Migrated inline and gallery presentation; synthetic widget checks and shared real-row native path pass. Full native Discord fixture still pending. |
| Capture Library saved/live previews | Existing interactive autoplay/loop/muted semantics retained; no animation inference. Bridge capture custody regressions pass. |
| Game Help and SDK Gallery | Existing interactive players retained; no decorative-video intent. |
| Other shipping widgets, first-party widgets, templates | No passive-video declaration to migrate. Shared GIF artwork receives the same visibility policy through `NativeMotionVisibility`; static artwork has no animation subscription. |

Review also found that indexed row presenters had not inherited their containing
presenter's motion preference. The host now passes it to realized rows, including
later appearance changes; the integrated native fixture covers both. This is
host-owned presentation policy, not widget state. The related retained-artwork
authority issue is also fixed in the host: `WidgetViewPresenter.Resources.cs`
retained a same-source demand while its callback could still refer to the
previous owner. A generated-PNG native fixture reproduced the blocked size
upgrade (`artwork-rebinding-repro-2.json`). An accepted same-source rebind now
replaces the callback along with its origin; source identity, handle-generation,
cancellation and stale-result checks remain. `artwork-rebinding-fix-1.json`
passed 28 checks, including the existing GIF suite, pixel retention, size upgrade
under the new owner and rejection after retirement. The failure was reproduced
at the shared retained-demand layer; a live Discord CDN/row replacement has not
been separately qualified. Other consumers receive this host fix without
widget-side state changes.
The final shared static-artwork regression run also passed all 86 checks
(`static-artwork-4.json`, verified fixture PID 50104), including cancellation,
source replacement, appearance-only updates, resource bounds and poster sizing.

Still outstanding: updated Discord
conversation/call fixture checks, matched shipping build and fresh packages, documented candidate update,
and the final goal audit. Real peer calls/audio/controller acceptance remains
explicitly separate from synthetic tests.

Continue the uncommitted `codex/discord-navigation-selection` worktree. Preserve
the previous navigation/selection work and its accepted behavior. The last user
candidate was Discord 0.1.0.22 with the qualified `shipping-4` host; it is closed.
The 0.1.0.23 package predates the later media changes and is not a current candidate. No merge, push,
installer or release is authorized. The user is away and permits closing and
restarting the overlay for testing. Live contact ringing, messages and broadcasts
must not be initiated without a designated authorized test; use synthetic/local
fixtures and leave real peer/device acceptance clearly identified.

## Required outcomes

1. **Conversation changes update the chat panel.** The section transition key
   identifies the account's Call/Server or Messages destination, not the selected
   conversation. Keep source/query/channel identity separate for data authority.
   Preserve the recently fixed reading position across section changes.
2. **All header controls are reachable.** D-pad/keyboard movement traverses
   Call/Server, Messages, Refresh and Open Discord. Segmented tabs must support
   authored exits without widget-side traversal/rebuilding of generated buttons.
   Default behavior for other tab consumers remains compatible.
3. **No empty call bar.** When no call is ongoing, hide the Messages call dock and
   label the main destination and its controller hint **Server**. A known ongoing
   or connecting call retains its existing call controls and Call label.
4. **Automatic conversation loading.** Replace Next/Previous controls with
   automatic loading through a supported shared collection. Preserve search,
   provider rank/order and catalog version, bounded resources, selection, drafts,
   read-head provenance and account/session boundaries. Entering the left panel
   must still focus the selected conversation; entering the right focuses the
   composer. Do not fetch all histories or treat displayed names as identity.
5. **Received GIF viewing.** Render real GIFs and Discord GIF-like video embeds
   inline; pause offscreen/hidden; honor reduced motion; provide a larger preview
   with playback controls. No sending picker is requested. Retain spoiler privacy
   and bounded decode/network/media demand. Source audit alone is not playback
   evidence: verify actual animated output and lifecycle behavior natively.
6. **Multiple attachments.** Every supported image/video in a message must be
   reachable in the preview, with controller and mouse navigation and stable
   selection. Do not offer external-open actions for media the app can preview.
   Keep appropriate actions for unsupported files/links. Switching attachment
   must retire obsolete loads, media sessions and input authority correctly.
7. **Reuse applicable Capture Library preview behavior.** Audit its media source,
   rendering, sizing, decode budgets, lazy demand, current-frame retention,
   playback controls and teardown. Reuse supported SDK/host contracts; extend
   shared contracts only for genuine gaps, with consumer checks. Do not copy its
   capture-file permission/custody semantics into Discord URL handling.
8. **Incoming and outgoing calls.** Research existing open-source Discord clients
   and use the resident client's supported internal action/store pathways where
   available. Provide explicit outgoing call, incoming answer/decline and normal
   end-call behavior. Distinguish ringing, connecting, active, ended and failure.
   Preserve microphone consent, current voice ownership, mute/deafen, sharing and
   pins. No automatic acceptance, unbounded ringing retries, credential extraction
   or direct token-based HTTP client. Treat cancellation after dispatch as an
   uncertain external result until the provider state reconciles it.
9. **Capture Library recording shortcuts.** Add controller actions for Save replay
   and Start/Stop recording. Provisional main-library bindings: LB Save replay,
   RB Start recording / Stop recording, with dynamic guide labels and the same
   availability, busy, permission/source-picker and session-authority checks as
   the existing buttons. Inspect actual recording semantics: the current X
   Record clip action is distinct from the replay panel's ongoing recording and
   must not be silently repurposed. Detail view already uses LB/RB for previous/
   next capture (`CaptureLibraryWidget.cs:257`); preserve those bindings and
   preview/media/dialog input ownership. These are widget-local shortcuts, not
   new global controller interception. Verify saving without stopping, start
   cancellation, stop state, repeated input and stale recording-session rejection.
10. **Capture multi-select action bar.** Fix the excessive empty space allocated
    around the first/share icon. Keep the icon compact and align the text actions
    without an oversized empty column. Rename the existing Remove action to
    **Hide…** if its operation only hides library entries. Add a distinct
    **Delete…** action that moves the selected underlying files to the Windows
    Recycle Bin, never permanent deletion. Reuse existing library/linked-source
    custody, permission and confirmation contracts. Make hide versus delete clear
    in review text; cover mixed sources, unavailable/stale files, partial results,
    cancellation and selection retention. Verify controller focus/guide behavior
    and actual native layout. The screenshot is user-reported evidence of spacing
    and misleading labeling; do not count it as a native reproduction by the agent.
11. **Playnite achievements header navigation.** Investigate the reported inability
    to move from the achievements header to Capture settings. Apply the shared
    tab-exit mechanism if it is the same failure, or correct the actual authored
    neighbor/host navigation owner. Preserve achievement search/L3 and existing
    section controls. Verify the real production entry point and native
    controller/keyboard traversal; do not infer this consumer uses SegmentedTabs
    merely because its header looks similar.

The standalone wide inbox and compact layout were discussed, but the user has not
requested a wider minimum size or removal of compact support. Keep existing
responsive sizing while implementing the confirmed requirements above.

## Current evidence and implementation

- Section key now remains `messages.<account>` across inbox/conversation changes.
  Managed regression checks prove the section declaration is unchanged; the
  existing native section-return fixture passed 31 checks with the corrected
  widget, including three exact-anchor round trips and cross-panel focus.
- `SegmentedTab.Focus` is an additive SDK property implementing explicit neighbor
  overrides with existing protocol fields. Discord uses it for toolbar exits.
  Settings adopts it in place of generated-child focus rewriting. Network
  Controls adopts it for Up/Down; its separate transition decoration remains.
  SDK composite tests passed. Native expanded Discord toolbar/section return
  passed **34 checks** (`artifacts/discord-messaging-refinements/toolbar-native-1/result.json`),
  including both directions through Refresh/Open Discord and exact reading anchors.
- No-call dock removal and Server label/hint are implemented. Latest Discord
  managed suite: **869 checks passed** (`toolbar-managed-2.log`).
- All artifacts remain under `artifacts/discord-navigation-selection`; start a
  fresh scenario/output name rather than overwriting failed evidence.

## Collection investigation and proposed ownership

Current inbox: `DiscordWebViewWidget.Messages.cs` uses a query resource for one
40-entry page and a regular vertical scroll. Explicit Next/Previous mutate offset
and catalog version. Message history already uses cursor/loaded-collection
boundary demand, so its scrolling behavior is different by implementation.

The provider `messaging-provider.js::conversations` supplies exact bounded
`totalCount`, offset and a catalog version. Its ordering can change when private
channels or unread state change; later pages reject a mismatched version. Compare
the SDK's finite indexed collection against discovered collection before choosing
the migration. Finite indexed range loading can use the known count, while a
discovered collection retains an append-only prefix. Do not force a prefix model
if provider reordering makes it brittle. Search/refresh/account changes must retire
old range leases. Selected-conversation directional entry must remain declarative
or use an existing exact-occurrence focus contract, not UI-tree inspection or
widget-managed direction/scroll counters.

## GIF and attachment findings to verify

- Attachments already recognize `image/gif` as an image, but only the first
  previewable attachment is admitted into the rich preview (`previewMediaCount<1`).
- The embed adapter currently projects only image/thumbnail URLs. It discards
  embed type and video source, so GIF-like video embeds cannot animate through
  the current projection.
- The URL artwork path preserves encoded bytes and uses WinUI `BitmapImage`;
  it does not transcode every source to a static PNG. Native GIF behavior must be
  measured before claiming it is missing. The SDK's separate encoded-artwork
  enum currently admits PNG/JPEG/WebP; do not conflate that path with URL images.
- Native artwork currently bounds encoded size and single-frame source pixels.
  Inspect animation frame budgets and hidden/retained bitmap playback. Prefer
  platform playback APIs and shared lifecycle ownership over per-widget timers.
- Microsoft's [WinUI BitmapImage documentation](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.imaging.bitmapimage?view=windows-app-sdk-2.0)
  documents GIF playback, `AutoPlay`, `IsAnimatedBitmap`, `Play`/`Stop`, and
  reduced-motion guidance. `NativeArtworkDecoder` preserves streams and creates
  BitmapImage without overriding AutoPlay. `WidgetArtworkView` paints through
  ImageBrush, with an off-tree Image used only for measurement. Verify that actual
  path, rather than assuming the documented Image example proves its behavior.

## Immediate next steps at this checkpoint

- Source includes additive segmented-tab exits and migrations for Discord,
  Settings and Network Controls. Discord managed tests869 pass; run the focused
  SDK composite tests, relevant Settings/Network checks, regenerate public API
  baseline, then publish the validation frontend containing the newly added
  native header traversal checks. Latest published native fixture binary is
  still build21 and does not contain those new checks.
- Capture Library shortcuts are now item9 in this plan; implementation has not
  started. LB/RB already mean previous/next in capture details, so scope the new
  commands to the main library and preserve child input ownership.
- The native fixture process41416 was closed. User candidate14212 may still be
  running from `shipping-4` with `candidate/shell-22.json`; revalidate current
  process state before closure. User now explicitly permits closure as needed.
- Finish the small toolbar/no-call changes coherently, then migrate automatic
  conversations and implement GIF/multi-attachment/call work. Preserve the entire
  goal and all original uncommitted edits; do not mark it complete at a partial
  candidate milestone.

## Consumer matrix

| Shared area | Consumers and intended handling |
|---|---|
| Segmented-tab exits | Discord adopts horizontal exits; Settings and Network Controls adopt equivalent existing vertical links; Catalogue keeps its default isolated tab behavior; SDK fixture verifies compatibility. |
| Automatic collection loading/focus | Discord migration; inspect Spotify/YouTube discovery, Playnite/Catalogue/Capture Library finite collections and relevant templates/tests for any shared addition. No unrelated forced migration. |
| Animated/preview media lifecycle | Discord and Capture Library are representative consumers; inspect Game Help, Browser, Playnite artwork, Spotify/YouTube artwork and SDK examples if shared decode/lifecycle changes affect them. |
| Notifications/call attention | Reuse existing host notification/action/permission routes; preserve background-account and stale-action rejection for other consumers. |
| Capture recording shortcuts | Capture Library main view adopts existing replay command paths; detail previous/next and media/preview/dialog bindings retain ownership. No capture engine or global input changes are implied. |
| Capture batch Hide/Delete | Reuse existing capture-library batch, source-custody and Recycle Bin contracts; check per-source capabilities and keep hide separate from file recycling. UI tests use synthetic temporary captures, not user files. |

Update this matrix with reproduced/source-inferred findings and final decisions.
Reject or narrow abstractions that weaken existing behavior or make authoring more
brittle. Existing safeguards take priority over fewer lines of widget code.

## November 18 continuation: header and capture work

The earlier immediate-next-steps section above records the previous checkpoint.
Current source/test state supersedes it as follows; no updated shipping package
has been installed yet.

- Playnite's reported boundary is in `PlayniteLibraryPresentation.NavigationTabs`,
  which uses `NavigationShellParts`, not SegmentedTabs. Its default horizontal
  wrapping bypassed the adjacent utilities. Added additive `CompactFocus` and
  `RailFocus` destination neighbors and `parts.CompactFocusId(destinationId)` for
  supported return edges. Ownership is SDK composition; no host input interception
  or protocol addition. Playnite's Achievements → Capture settings → Refresh and
  return path now uses this contract. Its existing cosmetic class decoration is
  unrelated and remains. Compact-only layout still hides the utilities as before;
  the host ignores hidden exits. No claim is made that Capture settings is newly
  available in the compact layout.
- Evidence: SDK navigation **10/10**, including frozen legacy output; Playnite
  achievement/navigation **35/35**; actual production achievement snapshot in the
  native host **6 checks**, including utility traversal and A activation
  (`playnite-header-2.json.native.json`). The first native fixture incorrectly
  sent D-pad as action buttons; corrected it to the presenter's production
  directional-navigation entry point. That initial failed fixture is retained.
- Capture Library main-page LB Save replay / RB Start or Stop recording are
  implemented using existing session-scoped actions. They are absent in selection
  mode, preserve X Record clip, and do not replace detail LB/RB previous/next.
  **5/5 replay checks** cover cancelled starts, busy/repeated commands, saving
  without stopping, stale sessions and stopped state. Physical controller input
  remains user acceptance; synthetic command routing does not prove hardware.
- Batch actions now distinguish Hide/Unhide from Delete. Delete invokes the
  existing owned or linked-original Recycle Bin APIs, obtaining linked-original
  permission before any mutation; it never falls back to hiding/removing an entry.
  Unavailable entries stay in the library and appear in partial-result feedback.
  The equal-column grid is replaced by a horizontal action strip, so Share has
  its natural icon width. The actual production action subtree passed **36 native
  checks** at 1040 and 420 DIPs, including 150% text: compact Share width, no empty
  column, all controls revealed/reachable and A dispatch. This standalone fixture
  isolates the action subtree, not the bridge-backed gallery rows. Evidence:
  `artifacts/discord-messaging-refinements/capture-batches/native.json`.
- Narrowed Hide semantics deliberately: `CaptureLibraryStore.RemoveLinkAsync`
  and SDK `HideAsync` support **linked entries only**. Extending canonical hidden
  state to owned captures would change storage recovery, index rebuilding and
  physical-file identity semantics. This UI fix does not attempt that refactor.
  Mixed Hide review explains the owned originals that will be kept; no Delete
  operation runs from Hide. Existing selection is retained on cancel and denied
  permission. After completed mutations the existing query refresh clears positional
  selection; failed items remain available through the failure report and can be
  selected again. Automatic failed-item reselection would require a separate
  supported cross-query identity/selection contract and is not faked with stale
  indices.
- Capture managed evidence so far: **18/18 Gallery**, **2/2 Hidden gallery**,
  **5/5 Replay widget**, **2/2 Detail actions**, **2/2 Sharing selection** and
  **2/2 Linked recycling widget**. Final batch-specific checks: **4/4**, including
  owned-only Hide retaining selection without requesting permissions. Tests
  exercise mixed Hide/Delete, unavailable originals,
  explicit linked permission denial before mutation, cancel, existing stale-result
  safeguards and synthetic Recycle Bin command dispatch. These tests do not delete
  the user's captures.
- Public SDK API baseline regenerated: **4150 symbols**.
  Current-baseline compatibility test: **1/1 passed**.

Additional consumer applicability for navigation-shell neighbors:

| Consumer | Decision |
|---|---|
| Playnite achievements header | Migrated; native boundary and activation checked. |
| Playnite detail tabs | Default isolated horizontal row; no adjacent utility exit needed. |
| Spotify, YouTube Music, YouTube | Existing shell content/rail edges and separate header controls; no equivalent generated-neighbor rewrite found in affected shell entry points. Defaults retained. |
| Games & Apps | Existing shell destination/content behavior; defaults retained. Its other child rewriting is cosmetic, outside this fix. |
| SDK Gallery and templates | Defaults retained; frozen SDK trees verify compatibility. |

Remaining goal work is still active: automatic conversation loading and exact
selected-row entry, GIF lifecycle/video embeds, shared typed-image previews,
complete attachment navigation, incoming/outgoing calls, matched candidate and
final native qualification. Do not mark the goal complete at this checkpoint.

### Animated artwork lifecycle: implemented and natively checked

`NativeArtworkDemand` now controls native GIF Play/Stop using the presentation's
admission, reduced-motion policy, loaded state, effective viewport and page-cache
suspension. The decoder accepts an optional autoplay argument; demands explicitly
disable it so retired loads never start playing. Direct decoder consumers such
as `NotificationCard` retain their existing default. Static images do not register
effective-viewport events. No widget timer, decoder replacement, duplicate byte
cache or protocol change was added.

The actual WinUI ImageBrush displays both generated GIF frames. The extended
native fixture passed **13 checks**, including reduced motion, presentation and
cached-page suspension, scrolling offscreen, resumption without another resolve/
decode, and retirement (`gif-lifecycle-1.json`). These tests exercise the demand's
policy callback and native viewport; presenter wiring is also source reviewed.
The existing native static-artwork suite passed **86 checks** in process36312
(`static-artwork-1.json`), including decode sizing, scale changes, stale-result
cancellation and bounded streams. This is not a performance benchmark or proof
of live Discord attachments. Bitmap animation frame-count/aggregate resource
bounds still need assessment before declaring the entire GIF item complete.

| Shared animated-artwork consumer | Status |
|---|---|
| Discord message GIFs and other image artwork | Uses shared demand; lifecycle mechanism implemented. Provider GIF-like video projection and larger-view controls remain. |
| Playnite posters, Spotify/YouTube cover art, Browser/Game Help and SDK image consumers | Static sources retain sizing/cache paths and avoid animated viewport subscriptions; native static regressions pass. Animated sources receive the same policy. |
| Capture Library thumbnails/media posters | Same demand semantics; captured PNG image viewer is a separate path and unchanged. |
| NotificationCard | Direct decoder consumer; optional autoplay defaults preserve existing behavior. No notification playback refactor. |

### Next media boundary

`ViewSnapshotValidator.ValidateFocusPresentationContent` explicitly limits a rich
preview to **one** native media player (`preview_media_limit`). Therefore removing
Discord's `previewMediaCount < 1` without a replacement contract would create an
invalid view. Preserve one active player and add bounded attachment selection;
do not relax the limit to instantiate every attached video at once. The shared
viewer already provides image zoom/pan/fit/fullscreen for capture images, but
`MediaPlayerSourceResolver` treats every WebUrl as audio/video and the native image
load path insists on a host-authorized local file. Typed remote-image support is
a genuine protocol/host gap. Reuse the bounded HTTPS artwork decoder for that
addition; preserve local capture custody and stale-load checks. No typed remote
source or gallery API has been added at this checkpoint.

### Subsequent media implementation (qualification in progress)

- Added protocol129 `MediaPlayerSource.WebImage` and a bridge/session/native
  resolution path. It requires HTTPS, validates source identity across both pipes,
  uses the existing 8MiB encoded/4096-dimension/16,777,216-pixel artwork limits,
  and reuses capture-viewer zoom, fit, pan, fullscreen and stale-load rejection.
  Native qualification against public GitHub project artwork passed **9 checks**
  (`remote-image-native-1.json`), including actual HTTPS decode, no video-player
  slot, zoom/fit, fullscreen return and replacement/failure authority. Local
  capture permission/custody semantics remain separate.
- Added bounded `MediaPlayerGalleryItem` declarations and `UI.MediaGallery`, with
  at most16 stable occurrences, optional per-item playback intent and value
  equality across JSON transport. A native gallery selects one effective source;
  the bridge authorizes its key against the full exact declaration before using
  the existing source/lease machinery. The complete list never starts all decoders.
  SDK media contracts **3/3** and main/pinned bridge media checks **3/3** pass.
  Updated indexed-gallery admission checks still need their new execution.
- Native gallery selection, mouse buttons, LB/RB, label/order continuity,
  delayed-read cancellation and mixed fullscreen media are implemented but
  **not yet natively qualified**. The validation frontend is currently publishing
  as `native-gallery-1` (exec session9346 at this checkpoint). Once built, run
  `--validate-styles --media-gallery-output=<fresh directory>` with
  `--capture-mp4=<absolute bundled template sample.mp4>` and normal validation
  activation. The fixture uses generated PNG and the bundled sample, not user
  captures. Any failure must be fixed before packaging.
- Discord attachment/embedded-image/sticker previews now supply one gallery;
  previewable files no longer also produce external-open buttons. Unsupported
  documents retain their normal action. Managed suite: **872 checks passed**
  (`discord-gallery-tests-1.log`). No matched package installed yet. Current
  sources have since gained optional embed video metadata and need another
  managed run after its rendering integration.
- Provider projection now preserves both API `proxy_url` and resident-store
  `proxyURL`, plus GIF-like video metadata. Exact `media.tenor.com` is admitted
  only for embed media; attachment/avatar origins remain unchanged. Provider
  tests **51/51 passed** (`provider-gif-metadata-1.log`); adapter build41. Metadata
  is not yet wired to inline video or animation playback controls.
- The public SDK baseline is now4151 symbols. Re-run parity/API checks after
  remaining additions. Shipping must rebuild native inputs against protocol129;
  validation currently reuses the earlier native platform binary for window/input
  activation, so it is not a matched shipping payload.

Source references for media investigation:

- Vencord's [Embed types](https://github.com/Vendicated/Vencord/blob/e75ac9ace41f6212cfaa2d5f3670830be392da4e/packages/discord-types/src/common/messages/Embed.d.ts)
  confirm resident `proxyURL` and `gifv` semantics. This is a source reference,
  not evidence that a particular live Discord message was viewed here.
- WinUI's [WicAnimatedGifDecoder](https://github.com/microsoft/microsoft-ui-xaml/blob/354bf6017e6443023214d34309dfdf0fe2d526b1/dxaml/xcp/components/imaging/WicAnimatedGifDecoder.cpp)
  and [AsyncImageDecoder state](https://github.com/microsoft/microsoft-ui-xaml/blob/354bf6017e6443023214d34309dfdf0fe2d526b1/dxaml/xcp/components/imaging/inc/AsyncImageDecoder.h)
  retain current/saved composition and next-frame state, not a decoded bitmap
  for every frame. Reject an invented frame-count-times-full-image memory rule:
  it would unnecessarily reduce ordinary GIF quality. Continue assessing actual
  per-frame/canvas metadata bounds, including malicious oversized delta frames;
  do not treat this source audit as a process-memory benchmark.

## Validation and delivery

### Latest media qualification checkpoint

- SDK public baseline/determinism: **2 passed, 0 failed, 0 skipped** using native
  MTP's `--project`/`--filter` syntax (`gallery-api-compatibility-2.binlog`). The
  earlier attempt that reported zero tests was an invocation failure, not a pass.
- Static artwork after animation extraction and final GIF bounds/sizing changes:
  **86 checks passed** in process27080 (`static-artwork-3.json`). GIF lifecycle/viewer: **24 checks
  passed** (`gif-canvas-native-1.json`), including actual red/blue native pixels,
  reduced motion, manual Play/Pause, offscreen/hidden pause and no re-resolve on
  resume and logical-canvas sizing when the first frame is smaller.
- Gallery: **10 checks passed** (`gallery-pointer-1/result.json`), including an
  actual mouse press/click, LB/RB, duplicate occurrence identity, delayed-source
  retirement and image/video switching in fullscreen with one resident decoder.
  The failing fixture used the raw gesture helper before mouse admission and
  layout had settled. It now uses the existing `ClickWindowElementAsync`, which
  wakes pointer admission and reacquires current control bounds. Recorded click
  coordinates changed from the earlier stale position, and `pressed=1/clicks=1`.
  No production input workaround or new shared pointer guard was needed. The
  fixture also supplies the production theme owner. This is fixture evidence,
  not a claim that every live Discord attachment has been checked.
- Existing media evidence remains: HTTPS image viewer **9 checks**, indexed
  gallery admission **1/1**, Discord managed **874 checks**, and provider/adapter/
  backend **228/228** (`remote-image-native-2.json`, `discord-gallery-tests-2.log`,
  `provider-gif-integration-1.log`).
- Found and resolved a host decoder gap in
  `Presentation/NativeArtworkDecoder.cs`: first-frame WIC dimensions alone do
  not validate a GIF composition canvas or later image descriptors. New
  `NativeGifBounds.cs` validates the logical canvas, each delta frame and encoded
  block boundaries before native pixel decoding, off the UI thread, using the
  already-owned stream. It neither decompresses pixels nor copies the encoded
  payload; normal non-GIF artwork gets no extra worker hop. Logical canvas size
  supplies decode sizing when the first frame is smaller. Pure bounds tests:
  **11 passed, 0 failed, 0 skipped** (`gif-bounds-3.binlog`), including truncation,
  cancellation, 10,000 small frames and exact existing dimension limits. Native
  rejection of oversized canvas/later frame passed before pixel decode. This
  closes the bounds audit without inventing aggregate frame-memory limits.
- Applicability: all shared artwork/GIF consumers and WebImage previews receive
  the decoder fix. Static Playnite/Spotify/YouTube images retain their existing
  path. Capture's local PNG viewer remains separate. No widget-specific parsing
  or focus/animation counters were added.

Remaining implementation is still substantial: inline GIF-like video playback
needs explicit animation semantics and bounded viewport lifecycle; conversation
automatic loading now has the implementation described below but still needs
integrated native Discord qualification; private calls have provider/widget
implementation below, including background alerts, but still need native qualification.
Then build matched shipping inputs and install
the candidate through the reviewed update path. These are not completed by the
media checks above. No new combined candidate has been installed or launched.

### Automatic conversations and preferred entry

Implemented in `DiscordWebViewWidget.Conversations.cs`: the existing query
resource acquires the first bounded provider page; the existing finite indexed
source supplies subsequent host-demanded ranges. Each read retains the same
account, search, provider version and total count. Larger native ranges split
into provider requests of at most50; the supplied first40 rows are reused.
Catalogue replacement revokes old leases and uses `PublishRefresh` only within
the same account/search. A different query or account uses `PublishQuery`.
No widget-owned viewport listener, page-selection model or focus counter was
added. Next/Previous actions and their offset/version UI state were removed.

The genuine shared gap was expressing a preferred selected occurrence whose
position is not loaded. Protocol130 adds `IndexedPreferredEntryKey`; SDK
`PreferredEntry(key)` opts into host-owned default/directional lookup. Existing
`FindIndex`, range admission and exact-key focus realization remain the authority.
Missing keys fall back to remembered/default entry. Explicit focus/reveal/return
requests retain their own policy. Departure, scrolling, hide, replacement and a
changed preference revoke pending work. Existing native collection memory avoids
a lookup when it already has the exact key in the current query. This is a small
additive presentation contract, comparable to selected-item entry in master/detail
list controls, not a new navigation framework.

Discord's new `messageLocateConversation` provider action searches the same
bounded ordered catalogue and validates its version. It never opens a DM or
fetches message history. Provider build42 minifies to92,878 bytes, below the
existing128KiB sealed-backend bound. A selected conversation and shared collection
exit replace generated row-ID neighbors; widget rows only declare domain content
and their primary action. Message drafts and incoming-share custody remain in
their existing domain transactions.

Validation at this checkpoint:

- Native shared preferred entry: **15 checks passed** through the real bridge/
  worker (`preferred-native-2/result.json`), including native keyboard entry to
  item7500, controller routing, explicit exact entry, missing-key fallback and
  delayed lookup cancellation scenarios. This is not the integrated Discord UI
  test; that fixture's row locators have been updated but need a fresh native run.
- Managed Discord suite: **879 checks passed**, including the final removal of
  redundant per-row neighbors and strengthened old-share-lease assertion
  (`conversation-indexed-tests-2.log`).
- Provider lookup tests **52/52**; adapter/backend **177/177**, all synthetic.
- Synthetic production-widget Messages contract fixture **10/10**
  (`conversation-synthetic-1/result.json`).
- SDK directional-entry and indexed-author-host groups **1/1 each**; SDK API
  compatibility **2 passed, 0 failed, 0 skipped**. Baseline4154 symbols; native
  parity220 constants. `WidgetTestHost.WaitForActionsAsync` is a bounded test-only
  observer of the existing serial queue, not a production action scheduling change.

| Consumer | Applicability |
|---|---|
| Discord conversation list | Migrated to lazy finite ranges, provider lookup and preferred selected key. Integrated native qualification pending. |
| Discord history | Already uses visible-edge entry and cursor resources; unchanged policy. |
| Capture Library | Existing `FindIndex` supports query continuity; multiselect does not identify one preferred item. Default entry retained. |
| Playnite | Existing explicit detail-return/search focus and ordinary browsing remain; no selected-master-row requirement identified for this opt-in change. |
| Spotify / YouTube Music | Playing/highlighted state is not necessarily the user's desired browsing focus. Existing remembered/explicit entry retained. |
| PoB library / sets | Potential later adoption for master/detail entry (`PobWidget.Collections.cs`, `PobWidget.Sets.cs`); existing one-time open requests have different semantics. Deferred rather than replacing them with a sticky preference. |
| Settings, Network Controls, Games & Apps, other first-party widgets | No use of the new property; existing navigation is unchanged. No blanket mapping from `.Selected()` to focus. |
| SDK examples / CLI templates | Default behavior remains compatible; SDK contract fixture covers opt-in serialization, validation and deltas. |

The current validation native platform DLL is still reused only for fixture
window/input activation. Shipping must rebuild native inputs against130 and
prepare a matched host, CLI and widget payload; no combined candidate is installed.

### Call implementation handoff

Provider/widget implementation now exists in `web/provider/call-provider.js`,
`Domain/DiscordCalls.cs` and `DiscordWebViewWidget.Calls.cs` (adapter43):

- Snapshot reads expose bounded private-call invitations, active channel IDs and
  the current ringing/active/waiting/unavailable phase. A retained CallStore entry
  alone never implies an active peer. Private-call names populate existing dock
  and pin titles when Discord's channel has no explicit name.
- Invitation IDs track account, CallStore instance and observed ringing lifetime.
  A provider-owned CallStore listener records cancellation/re-ringing even between
  host snapshots. This is domain event identity, not widget-authored focus or
  transport counters. Answer/Decline fail closed without a usable listener.
- New calls use `startCall`; joining an already-active private call and Answer use
  `selectVoiceChannel`. Decline uses `stopRinging(target, [self])`. A bounded command
  ledger deduplicates exact requests and refuses changed intent or simultaneous
  call dispatch. Ambiguous dispatch is never automatically repeated.
- Native controls offer Call/Join/Answer in the chat header and Answer/Decline in
  a bounded incoming-invitations strip. The existing command operation owns
  single-flight behavior. Host microphone consent runs before Call/Answer, and
  account/session/current-call/invitation checks run again afterward. Decline
  does not request microphone access. Call state is observed, not optimistically
  asserted. Command failures use the existing toast publisher.
- Existing Leave, audio, sharing and pins remain on their existing paths.
  Steamcord's [disconnect handler](https://github.com/Necrosiak/Steamcord/blob/47dd1212d6c742ee8ab5fdc0d17287cbc74f027e/defaults/discord_client/event_handler.py)
  uses the same `VOICE_CHANNEL_SELECT` with null channel/guild as the current
  adapter. No additional ring-all or stream teardown shortcut was introduced.

Evidence: the full managed Discord suite passed895 checks before the last
unavailable-phase display check; the final focused private-call suite passed
**17 checks** (`private-calls-managed-2.log`). Provider/adapter/backend passed
**192/192** (`private-calls-provider-2.log`); the subsequent focused call-provider
suite passed **13/13**, including unavailable status, failed listener installation,
cancel/re-ring, duplicate dispatch, source replacement and account change.
All contacts, store transitions and microphone results in these tests are
synthetic; no real call, message, permission grant or broadcast was initiated.

Remaining: native layout/controller/notification checks for the new call controls
and actual peer/device acceptance. Background alerts are implemented below.

### Background call alerts and publisher withdrawal

The initial concern about ordering was narrowed by source evidence:
`WidgetOperations.RunLatest` already waits for an incumbent delegate to actually
finish before its replacement starts. The publisher needed a withdrawal intent,
not another semaphore, work queue or widget-owned notification generation.
`WidgetNotificationPublisher.Dismiss()` now shares that lane, skips debounce,
and prevents late update-only metadata from undoing withdrawal. A subsequent
new-event Publish can show the same key again. Failed/rejected optional delivery
still has the documented retry-on-next-publication behavior; no retry loop was
added. SDK baseline4155 symbols; no new wire protocol for withdrawal.

Discord now consumes backend invalidations in the background. The existing host
already coalesces them for100ms and permits at most one outstanding invalidation
until a consumer read (`WebSessionPageAdapterHost.Invalidations.cs`). The widget
uses its existing resource/operation lane; the5s periodic fallback is unchanged.
This establishes bounded/event-driven design, not a measured background CPU claim.
Read completion also rejects a changed session before publishing domain effects.

Calls use a declared **Incoming calls** Pending notification category, respecting
host preferences. Account/session/invitation membership drives event eligibility:
repeated snapshots are quiet, name/count changes update existing content, a newly
arrived caller is a new event, and ended/unavailable/account-replaced invitations
withdraw their alert. Only a bounded current invitation set is retained.
The `community.widgetrail.discord.call.open` intent refreshes current state and
navigates to native controls, using the SDK exact-focus request API. It never
answers or requests microphone access, and preserves pending file offers/drafts.
An expired or foreign-account/session alert is rejected. An unavailable action
focuses Open Discord rather than a disabled Answer button.

Evidence:

- Publisher test group **1/1** passed, covering old Show ignoring cancellation,
  Dismiss before debounce, late metadata, new events, destruction and existing
  metadata/receipt semantics.
- Full Discord suite **909 checks passed** (`call-notifications-managed-final.log`),
  including background alerts with the polling timer held, manifest/intent schema
  agreement, quiet repeated snapshots, second-caller events, navigation-only open,
  expiry/account withdrawal and the prior microphone/dispatch tests.
- Spotify notification and background-event groups **1/1 each**; shipping YouTube
  Music standalone suite **44/44** (`notification-ytm-tests-1.log`).
- SDK API compatibility **2 passed, 0 failed, 0 skipped**
  (`notification-api-compatibility-1.binlog`).
- Directly previewable ordinary embed URLs no longer also produce external-open
  buttons; the final combined suite passed **911 checks** (`call-alert-media-final.log`).

| Notification consumer | Status |
|---|---|
| Discord incoming calls | Adopts Dismiss and existing publisher; native host/visual acceptance pending. |
| Discord and Capture Library action-result toasts | Existing short BannerOnly policy retained; no withdrawal event is needed. |
| Spotify and shipping YouTube Music | Existing track metadata/new-event policy retained and regressions passed. |
| Game Help | Completed-result one-shot notice, not an expiring invitation; direct service API retained. |
| SDK Gallery | Intentional raw Show/Update/Dismiss examples remain, separately admitted by its existing command flow. |
| Other widgets / templates | No publisher withdrawal workflow found; no forced migration. |

Next media work still needs explicit semantics for GIF-like video. Source evidence:
indexed rows use a presentation-only presenter, and `BridgeClientRegistry.MediaPlayerAdmission.cs`
currently authorizes native indexed media only inside an opted-in focus preview.
Simply placing a regular MediaPlayer in a message body would bypass neither this
admission boundary nor its focus/decoder rules. Inspect a declarative noninteractive
motion-artwork path (poster plus HTTPS animation source) against the existing
artwork lifetime/row authority and media-plane placement mechanisms before choosing
the implementation. Preserve focus on the message, reduced-motion policy, bounded
decoder residency and disposal; larger previews must retain explicit playback
controls. This remains an investigation/implementation item, not a completed feature.

Provider behavior is grounded in existing clients, not a new token-based API:

- [Steamcord's resident client](https://github.com/Necrosiak/Steamcord/blob/47dd1212d6c742ee8ab5fdc0d17287cbc74f027e/defaults/steamcord_client.js)
  uses `startCall(channelId)` for a new DM call and voice-channel selection when
  joining an existing call. Its incoming check is
  `CallStore.getCall(channelId).ringing.includes(currentUser.id)`. Decline invokes
  `stopRinging(channelId, [currentUser.id])`, stopping only the local recipient's
  ring. Do not pass all recipients or treat the ringing array as caller identity.
- [Vencord's CallStore declaration](https://github.com/Vendicated/Vencord/blob/e75ac9ace41f6212cfaa2d5f3670830be392da4e/packages/discord-types/src/stores/CallStore.d.ts)
  exposes `getCalls`, `getCall`, message identity, unavailability and Flux-store
  observation. A retained CallStore entry is not proof that a call remains live;
  reconcile participants through VoiceStateStore and selected voice channel.
- Adapt these semantics within the Discord provider; the host should continue
  owning microphone consent, operation admission and presentation. The widget
  supplies explicit call/answer/decline intent. Add CallStore to existing observer
  subscriptions, not another widget polling loop. Capability-probe each action;
  do not claim missing internals mean the user is signed out.
- Fence invitations by account/session, channel and observed invitation lifetime.
  Reject a stale Answer/Decline after ringing ends or an account changes. Reconcile
  outgoing dispatch against actual voice/ringing state; do not retry a ring after
  an ambiguous result. Joining an existing call must not ring participants again.
  Preserve the existing current-call/source permission safeguards and stream/pin
  lifetimes. Snapshot reads must never initiate calls or grant permissions.
- Before live acceptance, exercise incoming→answer, incoming→decline, caller
  cancellation, outgoing→connected/ended, duplicate commands, missing internals,
  account replacement and dispatch-completion races through synthetic stores.
  Actual peer ringing, device/audio and notification delivery remain live checks.

- Managed/protocol: stable section keys, optional tab exits/default wrap,
  connected/disconnected call presentation, stale catalog/search/account responses,
  exact selection identity and bounds, multiple attachment/animation selection,
  spoilers, call state transitions and stale/duplicate command rejection.
- Adapter: authentic API discovery/capability fallback, catalog reordering,
  GIF/video metadata URL constraints, incoming/outgoing call transitions, account
  switch, disconnect and ambiguous action outcomes. Compare behavior with cited
  open-source implementations; do not claim undocumented modules are stable APIs.
- Native: keyboard/controller header traversal, selected-row/composer panel entry,
  repeated section return, automatic list loading and focus across delayed ranges;
  all attachments reachable; actual GIF/video playback and pause on hide/offscreen,
  reduced motion, preview switches and teardown with bounded resource ownership.
  Use local/synthetic media and contacts where possible.
- Run proportional shared-consumer regressions. SDK/protocol additions need public
  baseline/version/header checks as applicable. Preserve low-impact defaults.
- Build a matched Release candidate and widget packages. Use the reviewed CLI
  update path in the separate candidate catalog, preserving IDs/settings/matching
  permission decisions. Smoke-test actual shipping startup, record payload proof,
  and launch for user acceptance. Keep production catalog unchanged.
- Record remaining physical/live acceptance (real incoming/outgoing peer calls,
  audio, controller hardware, real animated attachments) without claiming those
  were tested by source inspection or synthetic scenarios.

### Game Help and Discord transcript behavior

| Behavior | Game Help | Discord |
| --- | --- | --- |
| Sending from older history | `GameHelpWidget.AddQuestion` appends locally and issues a scroll request for that question. Suggested replies use the same path. | A confirmed send can arrive outside the retained history window. An exact message ID and the conversation's cursor publication are needed to reveal it safely. |
| Receiving while reading | An asynchronous Gemini answer preserves the user's reading position. Game Help 0.1.9 offers **RB → Latest answer** after a result exists. The existing SDK reveal scrolls the transcript without moving composer focus. | Incoming chat messages already follow the end while the user is reading there. Readers of earlier messages can choose **Latest**. Discord's server-backed unread and pagination semantics belong in Discord. |
| Focus and retention | **R3** focuses the fixed composer. The local transcript retains 20 messages. Opening an answer notification and then evicting its target during a delayed reply previously invalidated the whole snapshot. `GameHelpWidget.Presentation.cs` now resolves each requested ID against the retained transcript before publishing a reveal. | **X** focuses the composer. Discord's split-pane navigation, provider-watch lifetime, and indexed occurrence validation remain necessary when conversations change or history pages arrive. |

These consumers share the SDK's scroll-reveal and focus APIs. Neither chat
requires a new shared controller binding. Game Help's session-only history has
no remote pages to prefetch or read acknowledgments to send.

### Inline video return after scrolling

Discord's single-video message uses the SDK's native inline player. Its
remembered playback control could precede the poster Play button in the focus
order after scrolling offscreen. The resulting native target could sit inside
a collapsed transport bar. Returning to poster mode now prioritizes its visible
Play button. The reference capture and corrected focus outline are in
`inline-return-47/{baseline,qualified-focus}/return-focus.png` under the
call/media artifact root.

Returning still requires explicit Play and a new resolution of the current
row's source. For a still-realized inline public-video control the host can
transfer its checked compressed blocks and validated metadata across decoder
retirement. One process-wide entry expires after 15 seconds. A changed grant,
changed source/row owner, page departure, unload, or disposal discards it.
A read already in flight skips retention. Range continuations still require
the original resource URI, validator and remaining transfer budget. Native
cursors, decoders and audio are released offscreen.

Only Discord declares inline video in shipping widgets. Capture Library and Game
Help use ordinary native preview players; GIF animations follow their own
visibility and resource policy. No SDK or widget migration is required.
The generated local test passed 31 checks, including 12 hide/reopen cycles
and actual first-frame pixel samples of 223 ms cold and 164 ms on explicit
return. The native test left zero resident decoders. The checked transport
suite passed 98 tests, including revocation of the retiring source and
one-shot continuation with an independent cancellation lifetime.
