# Discord native prototype

Base: main 30b77a63. Separate development package; no catalogue or production installer changes.

## Acceptance gates

- [x] Voice control path: native channel selection, join/leave, mute/deafen, authoritative participants/state.
- [x] Incoming video: direct track relay into a pinned packaged WebView; audio path stays in Vesktop, not physically verified.
- [x] Outgoing video: explicit user start, Vesktop source/audio picker, actual stream state and stop; audio unverified.
- [ ] Overlay hide/reopen, pin changes, source ending, adapter reload, account changes, teardown.
- [ ] Native navigation and measured media latency/resource use.

The prototype uses a dedicated portable Vesktop profile and loopback CDP. This is a development
transport, not the proposed production authenticated companion. Never read/export Discord tokens.
Do not automatically join calls, broadcast a screen, or repeat an uncertain mutation after timeout.
Source/audio permission decisions remain with the user. Existing Discord installation is untouched.

## Ownership / applicability

| Concern | Existing owner / consumer | Decision |
| --- | --- | --- |
| Loading and stale read publication | SDK WidgetResource | Adopt in prototype |
| User operation single-flight/lifetime | SDK WidgetOperations | Adopt; no manual UI request counters |
| Native navigation/theme/focus | Host standard controls | Reuse |
| Pin selection/cancellation | SDK PinnedLayoutHandle | Reuse for media demand |
| Packaged video receiver / messages | SDK WebSessions, already used by Spotify | Reuse; WebRTC viability requires runtime proof |
| Discord stores, commands, media mapping | Adapter | Provider-specific; keep out of host |
| CDP connection and uncertain command outcome | Adapter transport | Bounded, serialized, no automatic mutation replay |
| Other shipping widgets / templates | No shared contract changes currently | Unaffected |

Vesktop 1.6.8 is downloaded into ignored artifacts with upstream asset digest verification.
Prototype limitations: Windows x64; one incoming screen stream; no camera relay, text chat,
automatic installation/update, or native capture-source picker yet. System loopback audio
must not be described as isolated game audio until verified. Existing host media player
does not accept WebRTC directly; packaged WebView is the bounded first experiment.

## Evidence (2026-10-25)

Implemented and locally installed separate package `widgetrail.prototype.discord` 0.0.4.
No shared host, SDK or protocol files changed. No release or catalogue publication.

Automated:
- C# build/publish: zero warnings/errors, binary logs under ignored artifacts/discord-prototype.
- 12 managed behavior checks: native declaration validation, distinct pin IDs, read-only startup,
  stale channel rejection, search, single-flight mutations, disabled busy controls, no uncertain
  mutation replay, background call preservation, failed-read stale control removal, teardown,
  bounded transport response.
- 19 JavaScript checks: account/call boundaries, desired-state mute, missing APIs, channel validation,
  duplicate capture, late capture cancellation/timeout, account/call change during picker,
  broadcast state, stream descriptor/lease checks, reinjection, unknown commands, capture owner
  heartbeat/teardown, and packaged SDK messaging startup.
- 30 successive live-state declarations passed protocol and WinUI support validation.

Native/live evidence:
- User signed into isolated portable Vesktop and designated a private test call with a second account.
- Adapter enumerated ~289 channels, two participants and one incoming screen stream.
- Native Mute action changed Discord state and button to Unmute.
- Native Deafen action changed the button to Undeafen; restored to Deafen afterward.
- Native search committed "Test server" and reduced the visible channel list to its two channels.
- Incoming video was visibly rendered in a real WidgetRail pin. Relay reported `connected`.
- Native guide Close hid overlay (`visible:false`) while pin selection and relay remained connected.
- User started outgoing sharing through the prototype and Vesktop picker; Discord reported active
  broadcast alongside connected incoming relay. User confirmed picture works on the other account.
- User could not check audio. End-to-end audio delivery, game-only isolation, controller-only source
  selection, exclusive-fullscreen games, hardware swaps and prolonged media recovery remain unverified.
- Updating only this prototype through the documented package update path preserved the live call
  and outgoing broadcast in Vesktop. The prototype's transient stream selection is reset on update.
- Native Stop sharing returned Discord to sharing=false. Native Disconnect returned the widget to
  "Not in a voice channel". Clicking the designated Lobby channel rejoined and restored two
  participants. Left the call joined, microphone muted, undeafened, with broadcasting stopped.

Corrections from runtime evidence:
- Discord STREAM_WATCH takes a descriptor including streamType, not a stream key. Corrected and
  covered by adapter tests. Direct live probe then acquired the video track and produced an offer.
- Packaged receiver needed the existing SDK WebSessionRuntime.js explicitly staged and loaded.
  Adopted the Spotify packaging precedent; no new framework API was needed.
- Avoid resending/parsing the entire adapter on every poll; install once per page/build and issue
  small commands thereafter. No failed mutation replay; only a proven missing-adapter branch installs.
- Added capture owner heartbeat and explicit pending-capture cancellation on widget teardown;
  late source selection cannot start a new share after the requesting widget has gone away.
- Corrected inline-video baseline overflow in receiver CSS; scrollbar fix not separately requalified
  with an incoming stream after the final package update.

Performance observations (not a controlled benchmark):
- Prototype worker: ~66 MiB private / 117 MiB working set during simultaneous incoming/outgoing video.
- Overlay host: ~340 MiB private / 373 MiB working set at that sample (other widgets also resident).
  WebView2 child-process memory was not isolated in that host measurement.
- Vesktop: six processes, ~1,627 MiB private / 1,646 MiB summed working set during two-stream test.
  Summed working sets may double-count shared pages. Earlier pre-relay snapshot was about 1,206 MiB
  private across Vesktop processes. Workload differences prevent attribution solely to the relay.
- Media latency, decoded-frame timing, CPU/GPU cost and long-run memory stability are not qualified.
  Do not promote this prototype as a low-memory production integration yet.

Production follow-up: authenticated companion transport, automatic setup/version compatibility,
native capture picker/audio policy, event-driven bounded state updates, live receiver statistics,
recoverable stream end/restart UX and measured performance. Current minimal native controls are
functional prototype UI, not the agreed final product design.

## Reproduce checks

From the repository root (use the local dotnet/node executable paths if not on PATH):

```powershell
dotnet build tests/DiscordNativePrototype.Tests/DiscordNativePrototype.Tests.csproj "-bl:artifacts/discord-prototype/check-{}.binlog"
dotnet tests/DiscordNativePrototype.Tests/bin/Debug/net8.0/DiscordNativePrototype.Tests.dll
node --test samples/DiscordNativePrototype/adapter.test.mjs
dotnet samples/DiscordNativePrototype/bin/Debug/net8.0/DiscordNativePrototype.dll --probe
dotnet samples/DiscordNativePrototype/bin/Debug/net8.0/DiscordNativePrototype.dll --validate-live
```

`--probe-watch` is an explicit developer media test: requires exactly one incoming test stream,
acquires an offer and releases its relay. Do not run it while using the widget's pin, because the
prototype intentionally supports one viewing relay at a time.

Final package 0.0.4 SHA-256: `2a618f7bde8c2c1e825710913c4b8effcac6c4a5f17d8d570580d449662fddae`.
Vesktop 1.6.8 official portable ZIP SHA-256 verified against GitHub asset metadata:
`8ffa8d3778c287551ee0b302b3396acd2fac45931282071048bb881b67c88286`.
