# Discord WebView widget

This is the real SDK-widget integration under development. It owns no external
client or debugging port. WidgetRail owns the persistent Discord WebView2
session, its permissions, the packaged stream receiver and pinned window.

The native view combines the current call, participants, incoming screens,
sharing and friend activity. Without a call, it shows the server/channel picker.
X toggles your microphone, Y starts sharing when available, and L3 opens the
server picker while in a call. B restores the invoking control or indexed row.
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

Friend activity uses read-only, feature-probed provider stores and does not require
Discord's own Home panel to stay open. It includes at most 50 visible friend rows
and distinguishes no current activity from unavailable provider APIs. Join checks
the current account, call, friendship and channel permissions again after microphone
consent. Watch is offered only for a current, discoverable stream in the same call.
No messages, invitations, activity secrets or arbitrary activity URLs are used.

Select another call participant to open local voice mute/volume controls. These
change only what you hear, independently of the participant's stream audio and
their own microphone state. **Menu → Voice settings** provides microphone and
output-device selection plus device refresh in a native dialog. Input gain is
explicitly unavailable on the current Discord browser engine because its setter
does not process the microphone signal. Connection text uses the actual RTC
phase, distinguishing connecting and reconnecting from a completed connection.

Watch opens the widget's incoming-stream pin directly. The receiver is acquired
only while its layout is selected and is retired on account/call/selection changes.
The incoming receiver is video-only; Discord's backend session remains the
voice/stream audio owner. Hiding the overlay does not leave the call or stop an
admitted capture.

The separate voice-controls pin keeps its channel header at the top, shows up to
three passive participant avatars with speaking rings and an overflow count, and
uses equal-width Mute/Deafen/Leave controls. Entering that pin focuses the first
available call action. Speaking updates do not reorder participants or add focus
stops. Saved pin dimensions remain host-owned; the current minimum height is180
DIPs. The voice pin does not claim the host's X/Y pin-management shortcuts.

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

Outgoing sharing uses the permission-bound host capture service. Its native
chooser selects one application, optional application audio, and supported
480p/720p/1080p/1440p at 15/30/60 FPS. Options are restricted to the current
Discord account/call's reported eligibility. The default remains 720p30;
the provider's persisted streaming settings remember the chosen resolution/FPS.
Cancelled choices do not change the preference. The host chooser also offers **Prefer smoothness** and
**Prefer clarity**, using the browser track's supported content hint. Discord
defaults to smoothness; the confirmed choice is remembered per account. Other
capture consumers retain their existing clarity default. Neither mode promises
the selected resolution/FPS when source size or encoder adaptation limits it.
The adapter preserves that hint when Discord's motion experiment overwrites it
during the supported synchronous attachment path. If a changed provider cannot
confirm the preference, a warning appears while sharing remains available.
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

If local capture ends while Discord still reports a broadcast, the widget keeps
that distinction and offers explicit Stop. It cannot safely stop an unknown newer
provider broadcast automatically: Discord's current stream key is reused across
restarts. The observed live capture termination remains under investigation; a
synthetic pass is not proof that this failure is fixed.

A separate reproduced failure is corrected: brief renderer stalls could overflow
the live audio queue and end video as well. The live worker now discards stale
queued PCM within a 200 ms transport budget and the existing 192,000-byte cap.
Retained packets keep their timestamps; the in-flight buffer is never overwritten.
The native fixture verifies audio/video recovery after a 1.5-second stall and
normal watchdog shutdown after a four-second stall. This is live-stream behavior;
the recording/WASAPI policy is unchanged. Remote audio still needs user acceptance.

Build with `./Build-Package.ps1`. Its fresh output includes the package manifest,
styles and sealed resources. The composed backend reads shared provider sources
from `../DiscordNativePrototype/{activity-provider.js,adapter.js}` and
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

Pending physical acceptance: receiver-side application audio, microphone/device
behavior, real controller navigation, real-game sharing stability and live friend
action execution. Loaded friend activity and avatars have been observed in the
current signed-in client without invoking friend actions. The candidate stays isolated until those checks and
the remaining integration gates are resolved. This development package is not yet
part of the shipping catalogue.
