# Discord Desktop adapter foundations

Phase A source for the full [desktop integration plan](../../docs/discord-desktop-integration-plan.md).
This is not an installed adapter, a finished connection protocol or a replacement widget.

The initial custom Vencord plugin is a **read-only capability probe** for the official
desktop client. On enable it takes one observation and one delayed observation after
three seconds, to distinguish immediate from later-loaded interfaces. It never joins,
mutes, captures, shares, watches, asks for permissions, changes windows or opens a socket.

Only fixed boolean/interface fields cross its native IPC boundary. The native module
accepts only the unique eligible top-level Discord application window/frame and writes bounded evidence
under `%LOCALAPPDATA%/WidgetRail/DiscordDesktop/diagnostics/capabilities-<instance>.json`.
Names, account/channel IDs, messages, credentials, SDP and device lists are not exported.
At most one write is in flight and two writes are attempted per desktop process;
there is no write queue. Stop/replacement cancels delayed observations, and async
startup failures are caught because Vencord does not await plugin startup.
Unknown state remains null. Interface presence does not establish runtime support or
hardware encoding. Self/effective mute flags are observations, not persisted user intent.

Build only through `tools/DiscordDesktopIntegration/Stage-Vencord.ps1` when available.
Its staged Vencord source and provenance must disable automatic updater/repatch behavior
and unrelated plugins. This does not install anything. Installation into an existing
Discord client requires an exact reversible operation and user approval first.

The browser adapter is deliberately not imported: native desktop capture and video
bindings differ, including a native `getDesktopSource` implementation that rejects
browser-style stream acquisition. The named-pipe control plane, mutations, source picker,
surfaces, setup workflow and complete UI migration remain later implementation work.

Run deterministic provider and schema checks with:

```powershell
node --test samples/DiscordDesktopAdapter/plugin/*.test.mjs
```

These fixtures establish schema/query behavior only, not successful loading in the
official client. Vencord and any combined custom build retain their upstream licensing;
stage corresponding source, changes and license notices with build provenance.

`voice-intent.mjs` is a separately tested provider translator, not imported or exposed
by the probe. It uses the observed explicit mute setter and raw saved preference,
keeps effective permission/server/deafen state distinct, and does not retry uncertain
mutations. Deafen uses a source-proven synchronous compare-and-toggle only at a
verified idle dispatcher boundary; missing or busy private contracts fail closed.
Neither command is exposed by this probe or yet qualified against a live call.
