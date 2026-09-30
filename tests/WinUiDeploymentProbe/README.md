# Isolated OS external-content deployment probe

This console uses the Windows SDK projection only. It has no WinUI or Windows
App SDK dependency and does not initialize XAML. It tests **unsigned development
manifest registration**, never signed production package deployment.

Supply an existing receipt from `New-WinUiExternalContentStage.ps1` and a new
absolute result path whose parent already exists:

```text
WinUiDeploymentProbe.exe validate --stage <V1/stage.json> --output <validate.json>
WinUiDeploymentProbe.exe inspect --stage <V1/stage.json> --output <before.json>
WinUiDeploymentProbe.exe register --stage <V1/stage.json> --output <register-v1.json>
WinUiDeploymentProbe.exe register --stage <V2/stage.json> --expected-current-stage <V1/stage.json> --output <update-v2.json>
WinUiDeploymentProbe.exe register --stage <V1/stage.json> --expected-current-stage <V2/stage.json> --allow-downgrade --output <rollback-v1.json>
WinUiDeploymentProbe.exe remove --stage <V1/stage.json> --expected-full-name <FullName from readback> --output <remove-v1.json>
```

`validate` performs offline receipt/manifest/path/hash checks. `inspect` reads only
the exact supplied identity for the current user. `register` calls
`PackageManager.RegisterPackageByUriAsync` with `DeveloperMode=true` and an
explicit `ExternalLocationUri`. It never forces application shutdown, defers
registration, installs dependencies, enables Developer Mode, changes signing
trust or activates an application. `remove` requires matching development mode,
publisher, version, effective external location and an explicitly supplied exact
full name; it preserves package application data and uses no all-user operation.

Only names matching `WidgetRail.WinUI.External[A-Za-z0-9]*Probe` are accepted;
the supplied manifest, receipt and frontend hashes must agree. Their owned layout
must be `identity/AppxManifest.xml` and `external/` underneath that receipt.
Initial registration requires an absent identity. Updating or rebinding requires
the exact prior development stage (publisher, version and external directory).
`--allow-downgrade` is admitted only for a lower target version and sets
`ForceUpdateFromAnyVersion=true`. Without that flag, the API receives `false`:
the probe deliberately records whether Windows itself rejects a lower development
version. This observes OS behavior rather than pretending to implement a shipping
update policy.

The JSON result reserves its output before mutation, reports the explicit mode,
requested API, before/after full identities and effective external locations,
and whether an OS operation actually started. A failure can still have changed
registration; inspect the recorded after-state before any recovery. Retain both
stages and coordinate native deployment tests and exact-owned cleanup separately.
