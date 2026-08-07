# Troubleshooting

## Start with a bounded verification run

From the repository root:

```powershell
.\scripts\Verify.ps1 -Configuration Release -SkipNative
```

For native verification, install Visual Studio's Desktop development with C++
workload and run:

```powershell
.\scripts\Verify.ps1 -Configuration Release
```

Do not claim a native pass when using `-SkipNative`.

## `gbar` is not found

Build and invoke the repository-local executable:

```powershell
dotnet build .\tools\GbarCli\GbarCli.csproj -c Release
.\tools\GbarCli\bin\Release\net8.0\gbar.exe help
```

The repository does not add `gbar` to PATH; invoke the built executable or add
that directory to your development shell explicitly.

## `gbar new` cannot find the template

Run it from this checkout, use the built executable with its copied templates,
or set `GBAR_TEMPLATE_ROOT` to the directory containing
`templates/ControllerWidget/template.json`.

If scaffolding outside the checkout produces a preview `PackageReference`,
remember that the SDK package is not currently published. Replace it with a
valid local `ProjectReference` or develop inside the repository.

## Manifest validation fails

- JSON property names and casing are strict; unknown members fail.
- Use reverse-DNS lowercase `id` and `publisher` values.
- The only supported runtime is `dotnet-worker`.
- Entrypoint assembly paths use forward slashes and cannot contain `.` or `..`.
- Resource requests are 16–256 MB and 1–60 Hz.
- Architectures are `x64` and/or `arm64`.

Run `gbar validate <manifest.json>` for the precise JSON path and diagnostic.

## GBSS fails or a theme does not appear

For widget-local GBSS, run `gbar validate <style.gbss>`. For a global theme,
run:

```powershell
gbar theme validate <directory-or-file.gbartheme>
gbar theme preview <directory-or-file.gbartheme>
```

Check that:

- imports precede rules and stay package-relative;
- every property appears in the safe allowlist;
- values include required units and are within documented bounds;
- there are no URLs, paths, scripts, `calc`, `expression`, or unknown
  functions; and
- variables resolve without a missing value or cycle.

The bridge publishes typed `base` and `focused` maps today. Static snapshot
`selected` and `disabled` state participates in those maps; complete separate
maps for transient pressed/busy/dynamic state are still under integration. See
[GBSS](gbss.md).

The first-party Settings widget, managed appearance store, immutable versioned
themes, and bridge watcher are connected. Check these boundaries:

- Settings is a catalog widget. If its card is missing, rebuild/package
  `OverlayHost` and verify `runtime\Settings\SettingsWidget.Worker.exe`, its
  manifest/style/payload files, and the `settings` entry in
  `widget-catalog.json`.
- Settings loads on each new Visible/Interactive lifetime, not on a timer. A
  stale page after an external edit should refresh when the widget next enters
  a new active lifetime.
- The store is
  `%LOCALAPPDATA%\GameBarAlternative\platform-settings.json`. Invalid JSON
  displays safe defaults and an error; use the confirmed Reset page to replace
  it safely rather than editing while the overlay is open.
- Installed themes live under
  `%LOCALAPPDATA%\GameBarAlternative\themes\<id>\<version>\` with exact-case
  `theme.json` and its package-relative GBSS entry. The manifest ID/version must
  exactly match both directories. Invalid themes remain visible but disabled
  in the picker and diagnostics.
- The bridge uses file notifications with a 200 ms debounce, retains the
  last-good revision after an invalid edit, and does not poll. Correct the
  reported manifest/GBSS diagnostic; do not repeatedly touch files to force a
  fallback.
- Theme shell styles and appearance/accessibility preferences should update
  after the bridge publishes a valid newer revision; the host ignores stale
  revisions and retains the last good appearance after an invalid edit.
  `textScale`, contrast, bold text, reduced transparency, and motion are applied
  after GBSS; globally layered widget styles update when a new snapshot is
  requested. Windows setting changes also reapply System contrast/motion.
- If a valid shell change does not appear, inspect the host diagnostic log for
  `Applied platform appearance revision` or a retained-last-good refresh error,
  then verify the settings/theme diagnostic rather than restarting workers.
  The host log is `%LOCALAPPDATA%\GameBarAlternative\overlay.log`.

If install fails, run `gbar theme inspect <file.gbartheme>` and compare the
reported digest. Remote installs require `--sha256`, existing versions are not
overwritten, and the installed catalog is capped at 128 user-theme versions.
Publisher signing is not implemented. Track exact commands and limits in
[theme packaging and distribution](theme-packaging.md).

## Snapshot validation fails

Common causes are duplicate/unstable IDs, an initial focus ID that is not a
button, focus neighbors that name missing/non-focusable nodes, invalid
progress ranges, more than three quick actions, or a reserved dashboard
button. Images require absolute HTTPS URLs and accessible labels.

Shortcut bindings must also be unique for the same button and event phase
inside one input scope. The root is the default scope. Use `.InputScope(id)` on
a Stack or Row only when a nested surface needs to reuse bindings independently.

Use `gbar render <snapshot.json>` to preview an existing snapshot. Use DLL
rendering only for code you trust.

## Package, download, or catalog command fails

- `gbar pack` requires a clean directory with exact-case root `manifest.json`
  and the manifest's `entrypoint.assembly` at that exact relative path/casing.
- Pack recursively includes the directory. Stage only intended release files;
  do not package a project tree containing source, `obj`, or secrets.
- Output must end in `.gbarwidget`.
- Installed versions are immutable. Bump the canonical dotted manifest version
  instead of reinstalling or overwriting the same `<id>/<version>`.
- `gbar install` accepts a local `.gbarwidget`, an absolute HTTPS URL, or
  `github:<owner>/<repository>@<tag>/<asset.gbarwidget>`.
- GitHub shorthand identifies one exact Release asset. It cannot use `latest`,
  query the GitHub API, clone a repository, or build a widget from source.
- Remote URLs cannot contain embedded credentials or fragments. HTTP and any
  redirect to HTTP are rejected. HTTPS must use port 443. Localhost names and
  private, loopback, unspecified, or link-local IP literals are rejected.
- A remote response may redirect at most five times and may transfer at most
  72 MiB of compressed package bytes. Connection, response-header, and overall
  time limits are 10, 20, and 120 seconds respectively. Encoded HTTP responses
  are rejected; release servers must return the asset with identity encoding.
- Every remote source requires `--sha256`, with exactly 64 hexadecimal
  characters. A mismatch means the downloaded bytes are not the pinned asset;
  verify the release, tag, asset name, and independently published digest
  instead of bypassing the check.
- The CLI prints the actual SHA-256 after every successful remote install. A
  mismatch diagnostic also reports the received digest. For other failures,
  inspect the network/redirect diagnostic. Temporary download files are removed
  on success and failure.
- `gbar list`, `enable`, `disable`, and every `gbar version` command must use
  the same `--catalog` value as install. The default is
  `%LOCALAPPDATA%\GameBarAlternative\widgets`.
- The packaged native host and Settings widget discover and watch the default
  current-user catalog. A custom `--catalog` path is an isolated CLI/test
  catalog and does not appear in the packaged overlay.
- Newly discovered widget IDs are disabled. Local and remote updates refuse to
  add a version while that ID is enabled; run `gbar disable <widget-id>`, retry
  installation, run `gbar version list <widget-id>` and `gbar version select
  <widget-id> <version>`, review the result, then run `gbar enable <widget-id>`
  with the same `--catalog`.
- Version selection and rollback are disabled-only. `gbar version rollback
  <widget-id>` chooses the greatest installed version older than the active
  version; `--to <version>` must name a specific installed older version. Use
  `version select` to move forward.
- An `active_version_missing` diagnostic means schema-2 catalog state pins an
  immutable version directory that is absent. Discovery intentionally fails
  closed rather than running another version. Reinstall the exact pinned
  package first; after discovery succeeds, an explicitly selected installed
  version can replace the pin while the widget is disabled. Do not hand-edit
  the state to an arbitrary version.

Remote acquisition does not launch the widget. A successful download therefore
cannot create lifecycle logs by itself; lifecycle begins only when a configured
host launches the installed worker.

## Controller input is not handled

- Guide/Home is never delivered to widgets.
- Dashboard A, Y, and D-pad are host-owned.
- Dashboard quick actions support B, X, bumpers, triggers, stick clicks, Menu,
  and View.
- Open-widget shortcuts check the focused node, then the explicitly published
  active input scope. They do not infer a scope from focus or fall through to a
  parent or sibling scope.
- Every open-widget event must echo the latest snapshot's
  `ActiveInputScopeId` and `SnapshotSequence`. A stale sequence, wrong scope,
  or focus ID outside that scope is deliberately unhandled.
- The MVP supports only Pressed bindings. A is reserved for focused activation;
  D-pad is reserved for focus navigation.
- Disabled and busy buttons do not activate.
- An unhandled B returns to the dashboard only from the widget's root scope. A
  nested scope does not bubble B or get dismissed by the host.

If a modal has no focusable controls, bind B directly on its Stack/Row with
`.InputScope("modal").Shortcut(ControllerButton.B, "dismiss")`, publish
`ActiveInputScopeId: "modal"`, and leave `InitialFocusId` null. This is a valid
focusless surface.

If directional focus does not move, first inspect the explicit neighbor. A
missing, disabled, or busy target uses geometric fallback; the fallback needs
focus rectangles from a completed render and never wraps. For analog tests,
return the stick below the release threshold before expecting a fresh direction
and remember that the dashboard consumes only horizontal movement.

## Guide works in one controller mode but not another

GameInput is the primary Guide source. The native host also includes an
isolated compatibility adapter for an observed Xbox-360-class driver gap. It
uses undocumented behavior from the system `xinput1_4.dll`, so it is not a
guarantee for all 8BitDo models, modes, firmware versions, USB/Bluetooth paths,
or remapping software.

- Run `tools/InputProbe` in each actual controller mode and transport.
- Test with Xbox Game Bar and Steam enabled and disabled; they may also claim or
  react to Guide.
- Use `--show` or F1 to separate Guide discovery from overlay rendering.
- Inspect `%LOCALAPPDATA%\GameBarAlternative\overlay.log` for the reported Guide
  source and compatibility-adapter availability.

For Game Bar, Steam, game, or device conflicts, run the bounded
`tools/InputProbe` matrix. Background Raw Input/HID can still reach a game.

## Worker or bridge failure

The runtime starts workers lazily. Check that the configured executable and
arguments are trusted and package-relative, the .NET runtime is installed,
and the worker uses the pipe/instance/message-size values supplied by the
runtime. Request timeouts terminate a hung worker; restart attempts are
bounded.

The bridge catalog is strict trusted configuration. Invalid worker paths,
duplicate IDs/actions, invalid styles, malformed JSON, or message ceilings can
prevent startup. `src/WidgetBridge/README.md` documents its protocol and
catalog shape.

Installed/community workers never fall back to the desktop token. A startup
failure can therefore mean the host could not open/create the stable package
AppContainer profile, grant read/execute access to the generic runtime and
exact package root, verify the Low-integrity exact-SID/zero-capability token, or
establish the SID/Low-label/PID-bound main or broker pipe. Inspect the bounded
worker failure and `%LOCALAPPDATA%\GameBarAlternative\overlay.log`; do not work
around the failure by launching the package DLL directly.

Direct sockets and arbitrary desktop-user files are intentionally unavailable
to installed/community code. Use declared typed `HostServices` audio/network
operations and grant them separately in Settings. A broker request still fails
closed on missing declaration/consent, Background lifecycle, wrong PID, nonce,
or package/publisher/instance identity. Bundled Settings and YT Music are a
temporary trusted Job-only exception, not evidence that packages can opt out.
Win32k disable is not enabled because its tested configuration caused CoreCLR
DLL initialization failure (`0xC0000142`); Job Object UI restrictions are the
active UI containment control.

## A widget keeps polling while in Background

Lifecycle state is explicit; worker process lifetime is not visibility. The
current host publishes `Visible` for a selected dashboard card,
`Interactive` for an open widget, and `Background` when selection changes or
the overlay hides. Once launched, the process remains resident in Background by
default.

Bind work that may run in both Visible and Interactive to the visible-lifetime
token received by legacy `OnActivatedAsync`, preferably through
`RunPeriodicUpdatesWhileActiveAsync` or
`InvalidatePeriodicallyWhileActiveAsync`. Do not use an uncanceled process-wide
timer. Use the token passed to `OnLifecycleStateChangedAsync` for work exclusive
to one state. Retain and observe ticker tasks: visible-lifetime cancellation
completes normally, but callback errors fault the task. Lifecycle hooks must
start work and return promptly.

Do not diagnose process residency itself as a leak. First determine whether the
work is UI-bound or explicitly permitted background work. UI rendering,
animation, controller polling, and ordinary refresh must stop with the
appropriate state/visible token. Legitimate widget-lifetime background work may
continue in Background and should be governed by permissions and resource
reporting. Those controls, plus opt-in `suspend-when-hidden`/
`unload-after-idle` policies, are not yet enforced by the prototype.

## Network Controls cannot show or switch Wi-Fi

Network Controls has provider, widget, worker, catalog, and Release packaging
wiring, but it is not yet a production-supported feature. If the card is
missing, rebuild the complete Release package before treating it as a runtime
failure; an older `out/Release` directory will not contain newly wired assets.
Check the current [implementation status](implementation-status.md).

When the package/provider path is available, diagnose its two independent
permission layers separately:

- In **Settings → Permissions & capabilities**, grant
  `system.network.read.v1` to show status/profiles and, separately,
  `system.network.saved-profile.switch.v1` to connect. Required capabilities
  are not auto-granted. Switching is denied unless the widget is Interactive.
- Version 1 deliberately does not query location-sensitive active Wi-Fi
  profile/signal automatically. Expect `PrivacyRestricted` with those fields
  omitted even after an overlay read grant; Ethernet/aggregate connectivity and
  saved-profile enumeration can remain available. A future Windows access
  request cannot be implied by overlay consent.
- Only profiles already saved by Windows are eligible. An absent network cannot
  be scanned, created, or supplied with a password through version 1.
- WLAN connection is asynchronous. An accepted command should display bounded
  busy feedback until a native status event confirms success or reports a
  terminal failure/timeout; acknowledgement alone is not “connected.”
- A missing WLAN adapter, disabled service/radio, device removal, access denial,
  and policy restriction are ordinary unavailable states. Do not retry them on
  a timer or ask the user to elevate the overlay.

Do not put profile names, SSIDs, interface identifiers, addresses, profile XML,
or keys in an issue or log. Report the stable error code, lifecycle state,
Windows version, `Transport`, `WirelessAvailability`, `DetailsAccess`, and
`ConnectionAttemptState`. See the [Network Controls
reference](network-controls.md) for the full privacy and test contract.

## Overlay does not open or render

- Build with `src/OverlayHost/build.ps1`; CMake/MSBuild metadata may lag the
  primary prototype script.
- Run the host with `--show` to bypass Guide discovery during diagnostics.
- F1 is the developer visibility fallback.
- Read `%LOCALAPPDATA%\GameBarAlternative\startup-error.log` for the latest
  initialization failure.
- `%LOCALAPPDATA%\GameBarAlternative\overlay-state.ini` is state, not a log.

True Fullscreen Exclusive is not a current target. Test in a windowed or
borderless presentation mode first.

The host uses ordinary topmost DWM windows, so secure desktop/UAC, elevated
foreground apps, exclusive-render paths, or Windows foreground restrictions can
still keep it behind the target. The dimming backdrop covers only the monitor
that contained the previously foreground app. Clicking that backdrop closes
the overlay; it is not forwarded to a widget or evidence of general mouse UI.

## YT Music state snaps back or stops updating

The reference widget works only while the host marks it active. It locally
interpolates progress at four Hz and requests authoritative YTMDesktop2 state
every two seconds. Transport and rating commands are optimistic for at most two
seconds; a stale companion response is ignored during that window, then the
companion wins. Refresh requests immediate authoritative reconciliation.

A failed command restores the prior snapshot and keeps connected controls
available with a bounded error status. Next/previous set visible progress to
zero but retain old metadata until the companion reports a changed track. The
progress control is display-only; seeking and scrubbing are not implemented.

## Remote artwork is missing

The source must be HTTPS and credential-free. The native cache applies
redirect, response-size, decoded-dimension, MIME, timeout, and cache limits.
Failure should leave a semantic icon/placeholder rather than execute widget
content. Verify the same URL is reachable outside the game and inspect host
diagnostics without copying private tokens into issues.
