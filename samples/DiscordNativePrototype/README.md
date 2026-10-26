# Discord Native Prototype

Development experiment, not a catalogue replacement. Uses native WidgetRail controls,
a separate portable Vesktop session, and an injected v1 adapter. No Discord credentials
are exported. The main Discord installation and existing web widget are unchanged.

## Run

1. Obtain the Windows x64 portable ZIP from the official Vesktop release, verify its
   GitHub release asset digest, and extract it outside this source directory.
2. Run `./Start-Prototype.ps1 -VesktopExe <absolute-path-to-vesktop.exe>`.
3. Sign in yourself in the separate Vesktop window.
4. Run `./Build-Prototype.ps1`. Install the emitted package using WidgetRail Settings
   or the documented CLI install/enable flow with full-trust acceptance.
5. Open **Discord Native Prototype**. Search and choose a voice channel. Mute/deafen
   operate on the currently observed call. Disconnect leaves that call.
6. To watch, select a participant's stream and use the tray Pin command with **Stream**.
   **Voice chat** is a separate compact pin layout. Stream audio stays in Vesktop.
7. **Share game…** opens Vesktop's own capture source/audio chooser. Choose explicitly.
   Status says sharing only when Discord reports an active stream. Stop sharing is
   independent from disconnecting. Capture audio is system/client loopback; isolated
   game audio is NOT established by this prototype.

`dotnet <build-output>/DiscordNativePrototype.dll --probe` checks read-only connectivity
and prints capabilities/counts without account or channel names. The --validate-live
mode additionally validates native declarations using live state, without joining a call.

Close overlay: call/broadcast continue in Vesktop, selected pin remains. Unpin: viewing
relay is released. Stop widget: receiver closes; voice/broadcast remain in Vesktop and
must be stopped there. Quit Vesktop: all media and the development endpoint end.

## Limits and authority

This transport uses a loopback DevTools endpoint and is for development only; other
processes under the same user may access it. No LAN listener, token copying, arbitrary
remote evaluation service, or automatic call/share actions are exposed by the widget.
A production adapter requires authenticated local commands and a supported setup/update
mechanism. Vencord/Discord internal interfaces can change; this is not an official API.

One incoming screen stream, no cameras or text chat. Up to 1,000 channels are read;
native search shows at most 32 matches. No auto-replay of failed commands. An active
relay has a bounded lease so a crashed widget does not leave a relay encoding forever.
The relay can add video encoding cost; no performance claim until measured in a call.

Architecture and validation evidence: ../../docs/discord-native-prototype-plan.md.

## Checks

`node --test samples/DiscordNativePrototype/adapter.test.mjs` from the repository root
runs deterministic adapter and receiver bootstrap checks, without Discord or a microphone.
Build `tests/DiscordNativePrototype.Tests/DiscordNativePrototype.Tests.csproj` with a binary
log, then execute its test DLL for the repository-style managed behavior checks.

Live testing confirmed native mute/deafen, an incoming screen in a real pin, continuation
when the overlay hides, and outgoing picture on a second account. Audio is unverified.
The companion's memory use during simultaneous watching/sharing needs optimization and
a controlled benchmark before production. No claim of low overhead or full Discord compatibility.
