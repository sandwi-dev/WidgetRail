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

## Deployment work still required

This resolves launch roots; it does not implement quiet startup, cross-version
single-instance activation, installer registration, update/rollback or provisioning.
The default-root path still needs a complete, staged payload qualification.

Do not simply move every runtime into a protected full MSIX installation directory.
`WidgetProcessClient` calls `WindowsAppContainer.GrantReadAndExecute` on worker and
runtime directories, and that implementation writes per-profile DACL entries. The
deployment choice must preserve sandboxed community workers and those ownership
rules. The existing per-user mutable versioned payload is a useful constraint.

The next executable deployment comparison should evaluate self-contained
unpackaged WinUI against an identity package with external content, preserving
that payload boundary. A fully protected MSIX layout would require a separate
runtime/permission design. Microsoft's
[external-location identity guidance](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)
documents the identity-package alternative. No packaging model, certificate,
installed startup entry or installed native application was changed by this work.
