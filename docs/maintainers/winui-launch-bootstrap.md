# WinUI launch and payload resolution

Debug and Release now choose the production shell by default. The development
gallery requires `--gallery`; named validation routes remain Debug-only. A normal
launch no longer depends on a generated shell-options JSON file.

The default payload is `AppContext.BaseDirectory`. The frontend never searches
the current working directory, ancestor folders or the installed native overlay's
registry entry for a different Bridge. It requires `widget-catalog.json` and
`runtime/Bridge/WidgetBridge.exe` in that selected payload before creating the
shell or reading a profile. Missing or invalid launch configuration shows a
friendly installation-recovery message; no Bridge starts.

The default profile retains the product's `%LOCALAPPDATA%/WidgetRail` convention,
with its installed catalog under `widgets`. Explicit launcher/development options
are `--installation-root=...`, `--settings-root=...`,
`--installed-catalog-root=...` and optional `--widget=...`. Roots must be absolute.
An isolated settings root also isolates its catalog unless a separate catalog is
explicitly supplied. Conflicting/empty arguments are rejected. The existing
`--shell-config=...` diagnostic route remains available but cannot be combined
with root/widget overrides.

Resolution and preflight have no filesystem writes. Native tests use the existing
isolated payload/profile, never the installed native overlay or its settings.
The config-free startup test verified the requested Playnite widget, four-widget
catalog and exact Bridge executable path. The incomplete default-payload test
verified recovery and absence of a Bridge child. Its first run exposed an uncaught
`InvalidDataException`; WinUI crash triage identified the exception, and the
corrected recovery filter passes the native check. All 78 managed shell checks
and the analyzer-enabled frontend build pass.

Evidence: `artifacts/winui-shell/config-free-startup-02/`,
`bootstrap-recovery-fixed-native/`, and `bootstrap-managed.log`. The earlier
`config-free-launch-01` attempted the full retention replay while Windows retained
another process in foreground; the adapter recorded failed acquisition and correctly
denied interaction. That attempt is not a passing replay or a bootstrap diagnosis.

## Current deployment

Inno installs a coherent versioned unpackaged payload with the WinUI frontend,
native libraries, Bridge, workers and application-local runtimes. Start menu,
CLI and startup launch the executable directly. There is no identity package,
MSIX registration or startup extraction cache. See
[release preparation](release-checklist.md) and
[installer ownership](../../eng/installer/README.md).

`WidgetProcessClient` and `WindowsAppContainer` still require the installed
sandbox runtime/worker access boundary. The previous protected-WindowsApps and
external-content identity experiments are historical; they are not alternatives
that current installers need to implement. Validate installation, runtime
provisioning and rollback separately from publication/inventory checks.

## Quiet startup and one configuration per process

`--hidden` does not activate then hide a window. It starts the existing Guide
adapter with visible navigation polling disabled. Both overlay/backdrop stay
hidden, and no Bridge, widget initialization or profile writes occur until Show.
WinUI may load a hidden tree, so worker initialization is guarded by presentation
visibility independently of `Loaded`. A failed Guide adapter surfaces recovery
instead of leaving an unreachable resident. Missing/invalid installation still
shows recovery even when hidden startup was requested.

`Program` parses launch arguments and resolves configuration once. The elected
settings profile and the shell share those immutable options; neither activation
argument differences nor a rewritten diagnostic JSON file can change the profile
after election. Configuration errors take the service-free recovery route;
ownership failures still stop before creating services. The process activation
endpoint is attached before initial presentation. See `winui-process-lifecycle.md`.

Validation passes seven hidden-start/show/reopen checks and 17 packaged process
activation checks, including hidden duplicate/no-show, normal duplicate/show,
show-not-toggle, unchanged Bridge identity, child cleanup and process exit.
Malformed-root recovery is also verified with `--hidden`. Evidence:
`artifacts/winui-shell/{hidden-startup-integrated-02,process-activation-integrated-01,lifecycle-invalid-config-01}`.
These checks do not replace physical Guide/foreground acceptance. All test
profiles and payloads are isolated from the installed overlay.

## Private service runtime

When the selected installation contains `dotnet/`, `BridgeProcessOptions` selects
it through the Bridge child's `DOTNET_ROOT`, `DOTNET_ROOT_X64` and
`DOTNET_MULTILEVEL_LOOKUP` environment. The self-contained .NET 10 frontend and
the user's environment are unchanged. An incomplete private runtime directory
without `dotnet.exe` fails explicitly. Development payloads without `dotnet/`
retain their installed-runtime behavior.

The Bridge's existing sandbox runtime admission uses the runtime actually
hosting the Bridge, then grants its AppContainer worker access to that owned
runtime. It does not grant access to an arbitrary inherited/global runtime.
Historical runtime evidence: the 251-test checkpoint and trimmed external-content
probe verified the loaded
`coreclr.dll` module of both the real Bridge and its AppContainer worker under
`external/dotnet/shared/Microsoft.NETCore.App/8.0.31/`. Source archives/hashes and
Microsoft signatures were verified by `Get-ReleaseRuntimes.ps1`; nothing was
installed globally. Evidence:
`artifacts/winui-deployment-evaluation/private-runtime-check-08/result.json`.

## Development payload consistency

When switching build output layouts, do not reuse a --no-build deployment merely
because its executable exists. OutDir-only builds share intermediate files, and
AppX staging can combine a new intermediate frontend with old output dependencies.
Rebuild the chosen normal output before deployment (or give truly independent
builds their own intermediate trees). After launch, run
`scripts/Assert-WinUiCandidatePayload.ps1` against that qualified build directory
and inspect real visible widget controls plus frontend errors. Process liveness
and Bridge lifecycle success alone do not prove a usable widget.

Historical tooling caution: do not use project-mode winapp --manifest with another identity as a way to run
fixtures alongside the resident: the September 29 attempt updated the original
registration despite returning the requested alternate AUMID. The live candidate
was interrupted. Normal project-mode deployment needs an intentionally released
frontend slot.
