# Discord voice, sharing and pins

This is the maintained Discord SDK-widget integration. It owns no external
client or debugging port. WidgetRail owns the persistent Discord WebView2
session, its permissions, the direct video view and pinned window.

Package: `widgetrail.samples.discord-controls` (x64, full access).
Read the [user setup guide](../../docs/users/discord.md). This replaces the
Catalogue listing for the older sandboxed Discord webapp; it has a separate
identity because reviewed updates cannot change a package's execution trust.
Existing profiles are not copied or silently migrated. For local testing, install
the supplied package file after the matching host installer. Public Catalogue
download remains unavailable until this package is published.

> **Discord Terms of Service warning:** this unofficial integration modifies
> Discord's hosted web client and uses private APIs. It may violate
> [Discord's Terms of Service](https://discord.com/terms), with a risk of account
> restrictions or termination. Use at your own risk. WidgetRail is not affiliated
> with or endorsed by Discord. It does not patch the installed desktop client.

The native view has persistent **Servers / Messages** destinations. Servers combines
participants, incoming screens, sharing and friend activity. Without a call, it
shows the server/channel picker. LB/RB switches destinations; open child controls
retain their own shortcuts. Messages keeps a compact call-control bar visible.
X toggles your microphone, Y starts sharing when available, and L3 opens the
server picker while in a call on Servers. In Messages, contextual L3 focuses the
chat header's Call/Join/Answer action without activating it. B restores the invoking control or indexed row.
Search and keyboard suggestions include both servers and voice channels, ordered
by prefix then substring matches against their full names. Server selection opens
its channels; choosing a channel joins voice. Indexed lists remain bounded at
compact sizes; the host owns scrolling, focus and dropdown behavior.
The unfiltered channel list puts favorites first and recently connected channels
next, without duplicate rows. Use a channel's Menu action to add/remove a
favorite; A still joins it directly. Search keeps its existing relevance order.
Favorites and up to eight recent channels are stored per account through the
SDK's private state service, with revision checks and bounded data. Unavailable
channels remain hidden. A failed or malformed settings read leaves ordinary
channel navigation available and offers Refresh to retry.
Sign-in opens the same persistent Discord
session at a size that permits QR sign-in; authentication stays in that page.

## Native messages and capture sharing

Messages shows a conversation sidebar beside history at expanded width and a
list-to-conversation flow at compact width. Switching destinations retains the
current conversation, unsent draft and reading position. Right from the conversation
list enters the composer; Left from the composer or history returns to the selected
conversation. Search uses the short label; the top-level Refresh also refreshes
the conversation list. **B** opens the radial directly from a conversation.
Use the conversation-list button to return to the inbox. In an attachment
gallery or picker, **B** closes that view first.

Messages uses bounded native conversation/history lists. Gateway changes update
retained text locally; opening history, paging and exceptional event-gap recovery
can request history. Ordinary incoming messages do not cause one HTTP read each.
Account/session changes retire the previous account's data and file grants.
Conversation snippets reuse the exact latest message when it is already available
in Discord's resident store or the current conversation's bounded history cache.
Listing conversations does not fetch each chat's history; unavailable snippets
keep their generic description.

Incoming and outgoing messages are left aligned. The native
timeline renders bounded Markdown emphasis/code/quotes, Unicode and custom emoji,
mentions, replies and attachment metadata. Spoilers require an
explicit reveal before requesting their media. Ordinary text stays inline;
attachments use a single action containing up to two static thumbnail tiles, with
`+N` for additional media, to open a native modal gallery. Filenames remain in
accessible labels rather than a redundant visible card. URLs use inline
links or compact text cards, with one action per destination. Rich previews do
not open automatically on focus or hover. Reactions and polls are read-only summaries; voting,
reaction changes and unsupported animated stickers remain in Discord. This is
not a complete renderer for every Discord message type.
Article/link embed artwork is treated as decoration rather than a second
attachment when the provider identifies it as an ordinary page link.

GIF-like video embeds, uploaded GIFs and Discord's image animation flags carry
animation intent into the host's passive inline artwork and explicit gallery.
The selected image's own flag is used; an animated thumbnail does not make an
ordinary video autoplay. The host owns visibility, reduced-motion handling,
resource limits and decoding. Recognition of a flag does not guarantee Windows
can animate every image format. Native pixel tests on the current Windows build
verify GIF animation; animated PNG and WebP currently display a static frame.
GIF-like video embeds use the native video player instead. Embed media uses exact admitted Discord CDN/proxy
and Tenor origins through the host's checked anonymous transport; a provider page
link alone never authorizes scraping or loading an arbitrary media origin.

An ordinary message containing exactly one video attachment uses protocol134's
shared `UI.InlineVideo` player. Poster artwork remains separate; video acquisition
waits for explicit Play. The host owns controller transport, fullscreen, per-widget
audio preferences and visible-only playback lifetime. Mixed attachments retain
the explicit gallery. Discord does not implement a second decoder or playback
timer for message videos.

Inline video uses the existing explicit-grid aspect-ratio contract and a480-DIP
width cap. It is larger in expanded chat without filling that column; compact
layout shrinks through native stretch measurement. Gallery dialogs opt out of
the SDK's standard header with `ShowHeader=false`, retaining an accessible title
and ordinary modal dismissal/return semantics. The host owns the compact gallery
counter/arrows and fitted-image control footer.

Live arrivals follow the end only while the host considers the current viewport
to be reading the end. Reading older content preserves position and offers
**New messages**. Initial and paged history remains bounded to a 40-message window.
When the current client exposes its read-state API, opening a conversation marks
its captured opening watermark read after native history is visibly presented.
New arrivals advance only at an observed newest end. Historical notification/reply
jumps do not acknowledge newer unseen history. Hidden pages, inbox loading and
pins do not mark conversations read. This changes the user's own unread position,
not a recipient-facing read receipt. Discord owns network batching; local read
state is not proof of server confirmation. Open Discord if its private API is
unavailable. The primary unread dot uses a bounded provider summary without
loading message history; unavailable/truncated summaries remain unknown.

Completed action feedback uses host toasts. Sending/upload progress, field
validation and unconfirmed delivery remain beside the relevant controls.

Every external file share opens recipient selection. Incoming files remain separate
until that choice; Cancel or Back releases only the incoming batch. Choosing the
same recipient adds unique files to its unsent draft; another recipient gets its
own draft. Deduplication uses the broker's opaque content identity when available,
otherwise only exact grant IDs—not matching filenames or file sizes. Up to 16
conversation drafts remain in this widget lifetime. Their attachment references
retain the shared service's expiry and owner limits; they are not persistent uploads.

File controls sit outside the history scroller. Remove handles one attachment;
Clear attachments preserves the draft text. Provider count/size limits appear in
recipient selection with removal available. An existing send can continue while a
new incoming share waits. Unconfirmed sends retain their exact nonce and files for
Check delivery; incoming files cannot modify that unresolved submission. Nothing
uploads or sends until the user explicitly chooses Send.

Friend activity uses read-only, feature-probed provider stores and does not require
Discord's own Home panel to stay open. It includes at most 50 visible friend rows
and distinguishes no current activity from unavailable provider APIs. Join checks
the current account, call, friendship and channel permissions again after microphone
consent. Watch is offered only for a current, discoverable stream in the same call.
No messages, invitations, activity secrets or arbitrary activity URLs are used.

Hover or focus another call participant to preview local voice mute/volume controls.
Click a control directly, or press X (F6 on keyboard) to enter the preview. These
change only what you hear, independently of the participant's stream audio and
their own microphone state. **Menu → Voice settings** provides microphone and
output-device selection plus device refresh in a native dialog. Input gain is
explicitly unavailable on the current Discord browser engine because its setter
does not process the microphone signal. Connection text uses the actual RTC
phase, distinguishing connecting and reconnecting from a completed connection.

On the call screen, **Watch** opens the stream in the widget. Focus the picture
and press A to enter fullscreen. B or Escape returns to the viewer. **Watch &
pin** opens a floating pin directly, including when chosen from a friend's
activity. The viewer and a pin for the same stream share one receiver, even
when the pin is still connecting. Closing the viewer returns the picture to the
pin. A viewer-only receiver is released on Back and while the main panel is
hidden. Returning to the same viewer restores its subscription. A separate
visible participant or stream pin can keep the worker active during that time.
Account and call changes also
retire the old receiver.

The receiver displays Discord's decoded video track. Discord's source session
owns voice and stream audio, including while the picture moves between the pin,
viewer and fullscreen. Hiding the overlay keeps the call and admitted capture
running.

The separate voice-controls pin keeps its channel header at the top, shows up to
four passive participant rows with speaking rings and an overflow count, and
uses equal-width Mute/Deafen/Leave controls. Entering that pin focuses the first
available call action. Speaking updates do not reorder participants or add focus
stops. Saved pin dimensions remain host-owned. Voice controls use a 340-DIP
minimum height; the transparent participant-only layout uses 224 DIPs. The voice pin does not claim the host's X/Y pin-management shortcuts.

While watching, the shared-screen card exposes **Mute stream** and a
0–100% **Stream volume** slider. These use Discord's stream-specific audio context
and its displayed volume scale; they do not change the participant's voice volume,
your microphone, or deafen state. Volume changes preserve stream mute. If deafened,
the controls explain why received audio remains silent. Unsupported provider APIs
show an unavailable message instead of silently falling back to voice controls.
Saved desktop boosts display at the browser's 100% endpoint without rewriting
the saved preference. Only a deliberate change writes a new level. The screen pin
remains video-only; audio controls stay in the main widget until the host can
show them only during pin interaction. The slider thumb aligns to its 5%
steps while the visible percentage retains the observed value, so Discord's
default-volume snapping cannot trap successive controller presses at that level.
Mute/volume commands return the checked provider state directly; the widget
publishes that acknowledgment without waiting for a full channel/status refresh.
The receiver remains video-only so there is no second copy of stream audio.

The active receiver also retains video demand on its exact Discord stream
connection while the Discord page is hidden. This avoids Discord's 30-second
hidden-page video shutdown without changing global visibility or other streams.
Demand is released on unwatch, account/call changes, subscription loss and lease
expiry. This adapter uses feature-probed private provider APIs; unsupported
versions report Watch unavailable rather than pretending to keep receiving video.

Use **Enable notifications** on the Discord controls page to grant website
notifications for the native widget’s Discord profile. The host offers a
temporary or remembered choice and delivers browser notifications to WidgetRail’s
notification centre. Website settings can change a saved block.

An exact notification-to-message association can open the native conversation and
reveal that message. A retained target needs no history read; a cold target loads
one bounded page around it. Drafts remain attached to their original recipient.
Missing or stale associations use the original Discord browser activation, as do
clicks during an unresolved recipient selection or active send. The integration
does not match notification titles, create duplicate notifications or fetch history
for every notification.

Outgoing sharing uses the permission-bound host capture service. Its native
chooser selects one application, optional application audio, and supported
480p and 720p at 15, 30, or 60 FPS, and 1080p at 15 or 30 FPS, according to the
current Discord account’s eligibility. The default is 720p30. Saved 1440p
selections map to 1080p. A saved 60 FPS selection at either 1080p or 1440p maps
to 1080p30 when eligible. The provider remembers the confirmed resolution and
frame rate. The picker defaults to **Prefer smoothness** for game motion.
Choose **Prefer clarity** when preserving fine detail matters more than cadence.
The adapter preserves the selected hint on the outgoing track during attachment.
If the provider cannot confirm the preference, sharing remains available and
the widget shows a warning.
The widget passes confirmed settings to Discord and waits for
both native capture and provider sharing state before advertising an active
broadcast. These settings bound local capture; they do not prove the resolution
or FPS Discord actually encodes or the receiver sustains. Higher-quality live
delivery still requires verification. The widget retains an unconfirmed stop for
explicit retry and never retries an uncertain broadcast start. No fallback opens
the browser capture picker or silently broadcasts a source.

During an owned outgoing share, **Menu → Stream details** reads captured pixel
dimensions, actual outbound video dimensions/FPS and Discord's advertised quality
separately. Discord's receiver quality badge uses advertised metadata; it is not
proof of the actual encoded resolution. Missing measurements display as
unavailable. Reading details does not change quality, restart capture or add
background polling. A smaller application window is not enlarged to the selected
maximum resolution.
When available, details also shows the codec, reported adaptation reason and
effective sender policy, including Smoothness/Clarity, bitrate ceiling and scale.
These observations help distinguish a capture limit from encoder adaptation;
unknown provider values are not presented as verified settings.
**Measure stream cadence** on the active stream card samples 12 seconds of
submitted, encoded, and sent frames. It keeps sampling while the overlay is
closed so you can measure motion in the game.

If local capture ends while Discord still reports a broadcast, the widget keeps
that distinction and offers explicit Stop. It cannot safely stop an unknown newer
provider broadcast automatically: Discord's current stream key is reused across
restarts. Report unexpected capture termination with the Stream details; a
synthetic pass is not a guarantee against every device or provider failure.

A separate reproduced failure is corrected: brief renderer stalls could overflow
the live audio queue and end video as well. The live worker now discards stale
queued PCM within a 200 ms transport budget and the existing 192,000-byte cap.
Retained packets keep their timestamps; the in-flight buffer is never overwritten.
The native fixture verifies audio/video recovery after a 1.5-second stall and
normal watchdog shutdown after a four-second stall. This is live-stream behavior;
the recording/WASAPI policy is unchanged. Remote audio still needs user acceptance.

Build a distributable archive with `./Build-CommunityPackage.ps1`. This uses the
shared validate/pack/checksum flow and does not install or publish. For fixture
development, `./Build-Package.ps1` prepares a directory containing the package manifest,
styles and sealed resources. The composed backend reads the widget-owned provider
from `web/provider/{activity-provider.js,adapter.js}` and
`../../tools/DiscordWebViewPrototype/{webpack.js,discord-commands.js}`; do not
copy/fork their domain logic. Compilation alone does not generate that sealed
backend asset, so launch/install only a directory produced by this script and
validated/packed by the normal package tooling.
The build requires Node/npm as well as .NET. It restores the lockfile-pinned
Terser build tool with lifecycle scripts disabled and minifies the composed
backend within the existing sealed-script size limit. Readable source remains
authoritative; no dynamic script loader or Node runtime is shipped to the widget.
CI runs provider/backend behavior tests against the minified form as well.
It also executes the final composed asset with synthetic Discord modules to
check startup without Vencord, late module discovery and request restrictions.

`./New-IsolatedWorkspace.ps1 -AcceptFullTrust` publishes current service workers,
seals Settings, and validates, packs, installs and enables this widget in a new
catalog and settings profile under `artifacts/discord-live-<id>`.
It does not start an app or copy any account data. After qualifying the matching
frontend build, use the emitted `Launch.ps1 -FrontendExecutable <absolute-path>`.
The explicit full-trust switch accepts ordinary current-user execution of this
reviewed development widget; microphone and application-capture consent still
use the normal user-facing flows.

Requires a host with SessionBackend, 1,000-item backend response batching,
microphone admission, native web capture and widget pin requests. Sign-in and permission decisions
belong to the user. Do not copy the standalone prototype's browser credentials
into this package profile.

Automated tests are in `tests/DiscordWebViewWidget.Tests` and
`web/session-backend.test.mjs`. These do not log in, join a real call or capture
any actual application. Passing them is not evidence of native navigation or
end-to-end picture/audio delivery; those are integration gates in the main plan.

The synthetic UI fixture uses fake account, call and activity data without opening
Discord or accessing a microphone. Native capture probes use generated windows and
process audio. Use `tools/DiscordWebViewPrototype/Measure-WebCapture.ps1` for bounded
motion/static runs and CPU, private memory and optional per-process GPU counters.
The GPU measurement excludes work attributed to DWM/system processes.

The user accepted the merged behavior for continued use on 2026-10-28 and will
report issues as they use it. This does not establish exhaustive audio-device,
controller, game or sustained-frame-rate coverage. Motion quality/FPS limitations
remain documented. The accepted main baseline is being packaged for local
installers; higher-bitrate/codec experiments and the cancelled desktop-client
adapter are excluded. See the [verification record](../../docs/discord-webview-prototype-plan.md).
