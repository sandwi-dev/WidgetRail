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

Run `gbar validate <style.gbss>`. Check that:

- imports precede rules and stay package-relative;
- every property appears in the safe allowlist;
- values include required units and are within documented bounds;
- there are no URLs, paths, scripts, `calc`, `expression`, or unknown
  functions; and
- variables resolve without a missing value or cycle.

The bridge publishes typed `base` and `focused` maps today. The language can
parse `:pressed`, `:selected`, and `:disabled`, but complete native rendering
of all state maps is still under integration. See [GBSS](gbss.md).

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
- `gbar list`, `enable`, and `disable` must use the same `--catalog` value as
  install. The default is `%LOCALAPPDATA%\GameBarAlternative\widgets`.
- The prototype native host does not yet discover this user catalog. A
  successful install followed by no new dashboard card is currently expected.
- Newly discovered widget IDs are disabled. A remote update refuses to replace
  an enabled ID; run `gbar disable <widget-id>`, retry installation, review the
  result, then run `gbar enable <widget-id>` with the same `--catalog`.

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
