# Discord UI fixture

`Stage.ps1 -Messages -RichConversations` stages six synthetic conversation rows
for visual review: online/idle/do-not-disturb/offline/unknown presence, unread
state, long names, a group snippet, cached message timestamps and missing cache
metadata. It performs no Discord/network access and does not change the normal
two-conversation native navigation scenario. It cannot be combined with
`-LargeConversations`.

`Stage.ps1 -Messages -LargeConversations` supplies 120 synthetic conversations.
Run the validation frontend with `--validate-discord-messages=<result.json>` and
`--discord-large-conversations` at the expanded size. This checks automatic
bounded range loading, provider order, off-page selected-key lookup, composer
entry, and selected-conversation restoration after changing sections. The audit
records range/lookup arguments separately from message-history reads.

`Stage.ps1 -Messages -PrivateCalls` enables a separate fake private-call scenario.
Its published worker supports `--calls-contract-check <fresh-directory>` before
native testing. The frontend uses `--discord-private-calls` alongside the normal
Messages validation output flag. Decline, outgoing connection, Leave, a delayed
background invitation, notification Open, Answer and final Leave affect only
the synthetic provider. Microphone admission returns a fixture receipt; no
microphone, Discord account, call, media capture or network exists. This mode
uses the normal application bootstrap so notifications traverse the real broker,
bridge and native inbox/banner. It does not use the Messages upload-service stub.

`Stage.ps1 -Messages` and `Test-Messages.ps1` add an opt-in native Messages case.
It runs the production widget with two synthetic conversations and 120 synthetic
long messages, served in bounded cursor pages. The worker writes only a provider
audit in that fixture's isolated result directory and rejects every send/DM
creation request. Native checks cover controller inbox/conversation entry, lazy
history, Older/Newer window bounds, A/F6/B/Escape long-text preview entry/return,
mouse transit, the actual composer viewport and mouse/keyboard draft editing.
The upload case selects only `messages-upload-fixture.txt` through deterministic
SDK test services, using the same worker bootstrap seam as CaptureLibraryWidgetFixture.
The injected uploader reads half of the synthetic stream, reports50%, and waits for native
controller Cancel. It has no HTTP client and cannot transmit bytes. The native
checks verify busy-row bounds, cancellation and retained draft/file selection.
This does not qualify Windows chooser selection or broker file authorization;
those boundaries have separate broker and picker-lifetime fixtures.
No Discord page, credentials, external messages or network uploads are involved. Incoming
file-intent routing requires a second authorized sender and is qualified separately;
this single-widget fixture does not bypass the broker to fabricate that authority.
The synthetic provider also exposes a validated channel-watch/read-state contract.
Only IDs actually returned by its bounded history can be acknowledged; reads
update a local audit and unread flag without any network. Native checks verify
that visible history triggers the opening acknowledgment and clears the primary
unread indicator. This qualifies presentation timing, not Discord server delivery.
The published fixture's `--messages-contract-check <fresh-directory>` command runs
nine non-UI checks against these exact synthetic replies and production declarations
before desktop qualification, including both history cursor directions and full
preview text. It writes its own bounded provider audit and never launches a window.

This worker renders the real Discord widget with synthetic friends, voice channels,
participants and stream rows. It never opens a Discord page, accesses an account,
captures an application or broadcasts media. Mute, deafen, leave and join mutate
only its local fake state. Watch can exercise pin admission, but the synthetic
receiver deliberately reports unavailable; there is no real media stream. Outgoing
capture capability is absent.

`Stage.ps1 -Audio` selects the separate `widgetrail.tests.discord-audio` fixture.
It places an opt-in marker in the staged payload because the production manifest
does not support arbitrary worker arguments. This mode uses an unregistered,
protocol-shaped receiver to render the real main audio controls and video pin. Its mute
and volume commands update only the fake provider state after checking the fake
account, call, stream, user and receiver lease. It opens no WebView, microphone,
audio device or real media connection. The default mode still deliberately fails
receiver creation and retains the existing recovery regression.

`Test-Audio.ps1` stages that opt-in mode and runs `--validate-discord-audio` through
the actual native shell. Initial volume is 100%. It tests the first directional
entry from Mute into the slider before any pointer/slider focus, controller
adjustment and mute confirmation in the main widget, a passive video-only pin at
300 DIPs, and focus continuity through periodic publications.
The sentinel browser view may show an unavailable message; that is intentional.
Only native controls/layout and synthetic provider mutations are tested.
It writes bounded geometry/focus evidence to the fresh output directory and
closes only its own spawned frontend process.

Publish `DiscordWebViewWidget.Fixture.csproj` for `win-x64`, then run `Stage.ps1`
with that output as `-PublishedPayload`, an existing qualified `wrail.exe` as
`-CliPath`, a matching service payload as `-InstallationRoot`, and a fresh
`-OutputDirectory`. The script validates, packs and enables the fixture in its own
catalog/profile. `-ThemeId widgetrail.builtin.mineral-light` selects the light case;
`-Contrast High` selects the host's high-contrast policy. It does not change Windows
or the user's WidgetRail settings.

`Test-Messages.ps1 -HistoryContinuityOnly` runs three delayed older-history loads
at each requested surface size, including bounded opposite-edge trimming. It
samples every native rendering frame from before the response through publication
and asserts stable source/container identity, no empty painted list and less than
two DIPs of anchor movement. Before/after screenshots and per-load measurements
are written beside `result.json`. This mode isolates loading from the test's
deliberate large scroll to reach the edge. The normal expanded Messages run also
checks the first prepend and the surrounding navigation/composer flows.

Launch the matching frontend with `--shell-config=<output>/shell.json` and
`--shell-no-controller`. The existing validation option
`--validation-viewport-dips=468x548` gives exactly 420×360 DIPs of widget content
with the standard 48×188-DIP shell allowance. Confirm the resolved dimensions in
`layout.json`; do not infer size from monitor pixels. Omit the viewport option for
the normal 780×680-DIP widget. The account page retains its separate 1040×740
declaration. The fixture exposes an intentionally unregistered session reference
so Account can test input-scope entry and Back restoration; the broker cannot
resolve it to any page. This does not test account browser rendering or sign-in.

Run the published fixture with `--snapshots <fresh-directory>` to export validated
Home, Voice, Channels and Streams snapshots without launching a native window.
Adding `--audio` also exports `streams-audio.json` after selecting the fake stream
pin, including the real compact footer and main audio controls.
These semantic snapshots complement native screenshots/layout measurements; they
do not prove native focus, controller routing or remote audio/video delivery.

Check Call/Messages destinations, retained drafts, the conversation sidebar,
participant state, call action ordering, channel search and clearing, empty states,
and the compact voice pin. At minimum size, both destinations must retain reachable content.
Use only the synthetic profile, and close its exact process before changing a
fixture package or starting the next case.

`Test-Navigation.ps1` stages a fresh fixture and launches a validation-enabled
frontend at minimum size. It uses the actual shell's controller event routes and
D-pad samples, actual worker publications and indexed row leases. It verifies
populated/empty section navigation, shared controls, Account Back, passive Watch
pin admission, failed-receiver guidance and Stop watching recovery. It records
native focus traces and visible/hidden package icon preparation separately.
This is synthetic controller-routing evidence, not physical hardware validation.
The runner closes only its own frontend process and never installs into the
user's profile. The fake Leave action clears activities as well as the call to
exercise empty pages deterministically.

The native scenario checks the actual foreground HWND before input and after
passive pin creation. If Windows denies the fixture's activation request, activate
that fixture window yourself (or with the computer-use tool). A background-window
run cannot qualify native focus behavior.

The shared presenter regression uses `--validate-focus-policy --shell-no-controller
--focus-publication-output=<result.json>` on the same validation-enabled frontend.
It covers consecutive publications, newer native host focus before and after the
last publication, directional cancellation, target retirement, newer group entry,
scope changes and worker replacement. It also requires actual Windows foreground.

For safe example images, `Stage.ps1 -Showcase` enables full-widget pinning only in
the synthetic manifest and uses the host's Solid surface override in that isolated
profile. Capture the resulting full-widget pin window: it contains only fake data,
has its own exact window bounds and does not reveal the user's desktop through the
widget surface. This is capture scaffolding, not a change to Discord's production
pinning declaration or the user's appearance settings.

`Test-Messages.ps1 -MediaMessages` runs the explicit attachment-modal scenario
at both minimum and expanded sizes (or the requested `-Surface`). It stages rich
conversation metadata, inline URLs, text-only link cards and a two-attachment
message, then checks modal input ownership, RB switching and exact visible
message-position restoration on Back. Screenshots cover the main screens and
the modal. Its unavailable synthetic CDN URLs intentionally exercise media
failure; this scenario does not qualify decoded images or playback.

Add `-PointerMediaClick` to open the first gallery through an actual native
mouse click while focus remains in the composer, then exercise repeated
controller opening/return. `-PointerMediaOpen` invokes the same native Button
command directly to separate dispatch from pointer delivery. These scenarios
record input admission sequence facts and pointer hit-test ownership. Retained
row content must remain usable as its parent publishes newer snapshots; the
tests do not extend the bridge's bounded input-origin history or bypass leases.

`Test-Messages.ps1` also accepts `-ThemeId`, `-Contrast Standard|High`, and
`-TextScale 1..2`. They change only the isolated fixture profile. Use the same
matched host/worker payload for comparisons, with a fresh output directory per
run. Add `-SolidSurface` for visual comparisons: the native validation host
captures the whole Page with its actual solid host material, then crops to the
widget surface in the same coordinate space. It waits for visible package icons
to finish preparation and checks their set remains stable through the pixel read.
Each image has a `.capture.json` sidecar recording bounds and icon readiness.
Use these captures to review solid-material colors, layout and icon stability.
They do not qualify desktop acrylic or decoded native video-plane pixels.
Older presenter-only captures exclude the sibling host backdrop and cannot
establish light-theme/high-contrast appearance.

`-HeldScrollOnly -ReverseWhileMoving -FocusHistoryBeforeScrolling` exercises
continuous up/down/up input with native focus initially on the latest message.
Use `-HistoryDelayMilliseconds 500 -Motion Full` for delayed page publication
during animated scrolling. The initial upward leg continues through two page
requests so the bounded window must evict a page; the downward leg must fetch
newer messages. A maximum input budget prevents a stuck load from running forever.
The runner checks current occurrence identity, reading-direction continuity,
native anchoring retirement, and visible focus settlement without moving the
reading position. The composer-focused variant remains useful as a separate
entry case. This is synthetic controller-route evidence, not physical hardware
or live Discord network evidence.

The fixture does not inject the generic message-loading error. Its optional
`cursor-exceptions.jsonl` records bounded first-chance cursor/history exceptions
before user-facing error mapping; cancellation is distinguished from failures.
These diagnostics run only in the isolated synthetic worker, not the shipping
widget. A focused-row regression exposed offscreen focus preventing bounded-page
eviction. A passing composer-only scenario does not cover that failure mode.

Add `-WheelHistoryScrolling` to drive the same boundary scenario through actual
native wheel notches, using `NativeWheelProbe` and its owned-window/input guards.
This exercises wheel ownership separately from controller `ScrollBy`. The bounded
pipeline listener records range data/queue/publication timings, while missing
anchors include native loading/layout state. Those diagnostics are excluded from
shipping builds. Keep failed readiness receipts: zero backward motion does not
by itself establish that all sampled frames contain readable message data.

`-HistoryLifecycleOnly -SuspendDuringPreparation` hides the overlay after the
provider page has succeeded but while the host is preparing its row leases. It
checks drained preparation, retained sidebar identity, no mapped cancellation
error and a successful later page read. Without that switch, the same scenario
suspends the earlier provider request. The two cancellation phases are distinct.

The expanded `-MediaMessages` lane also samples cold Inbox-to-chat entry. It
requires painted sidebar rows on every sampled native frame and verifies that
retained rows cannot claim new-scope input. Its trace distinguishes those passive
rows from freshly acquired ranges. Source checks cover identical-query paint
seeding, independent range ownership, rejection of foreign queries/instances,
changed counts and backwards revisions, and exact cleanup. This is separate
from the existing same-scope chat-switch and paging checks.

Opt in to `-PublicPortrait -Surface Expanded -MediaMessages` to test actual
portrait pixels across cold entry. This fetches Discord's public default avatar
at `https://cdn.discordapp.com/embed/avatars/0.png`; it uses no account media or
credentials. The lane waits for decoded initial pixels, then requires no missing
portrait on sampled entry frames. Ordinary fixture runs remain network-free
apart from their deliberately unavailable synthetic media URLs. The separate
`--static-artwork-paint-output=<json>` native style scenario uses generated PNG/GIF
bytes to check source identity, retirement, worker replacement and animation
exclusion without the network.

The private-call lane also drives `call-dock-phase.txt` inside its isolated result
directory through four fixed synthetic states: seven participants, reversed order
while reconnecting, one participant, and an empty participant snapshot. It keeps
native Mute focused, verifies the same control identity, unchanged dock/composer
geometry, bounded truthful overflow, and one-step Left access to the call summary.
Activating that summary opens the existing call screen; RB returns to Messages.
The file is consumed only by the credential-free test provider. No corresponding
command or diagnostic endpoint exists in the shipped Discord adapter.

`Test-VoicePin.ps1` accepts `-ThemeId`, `-TextScale` and `-Contrast` for the existing
400/320-DIP native pin scenario. It checks full action labels, not only button
bounds, and writes `voice-400.png`, `voice-320.png` and `participants.png`.
High contrast must use the system's opaque background; ordinary mode still
requires the authored transparent participant background. RenderTargetBitmap
cannot faithfully capture all desktop material/alpha effects, so assess ordinary
pin contrast on the desktop as well as using these geometry/label receipts.
