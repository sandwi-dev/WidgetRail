# Discord UI fixture

This worker renders the real Discord widget with synthetic friends, voice channels,
participants and stream rows. It never opens a Discord page, accesses an account,
captures an application or broadcasts media. Mute, deafen, leave and join mutate
only its local fake state. Watch can exercise pin admission, but the synthetic
receiver deliberately reports unavailable; there is no real media stream. Outgoing
capture capability is absent by default. `Stage.ps1 -SharePreferences` exposes
the native Bandwidth and Encoder controls for keyboard/layout checks, but still
has no registered browser session, capture permission or real media source.
Use it only for preference controls; it cannot establish a real broadcast.

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

Check call controls across every tab, participant speaking/mute labels, Home action
ordering and destination text, channel search and clearing, empty states, and the
compact voice pin. At minimum size, all four sections must retain reachable content.
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
