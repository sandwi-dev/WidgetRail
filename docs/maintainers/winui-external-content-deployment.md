# WinUI identity package with external content

Sparse package identity is a concrete deployment candidate for WidgetRail. The
identity package is protected by Windows, while the frontend, Bridge, workers and
bundled runtime stay in an installer-owned external directory. This preserves the
existing sandbox admission path: `WidgetProcessClient` grants each AppContainer
read/execute access by updating DACLs on owned worker/runtime directories. Moving
those directories into a full MSIX's protected storage is not equivalent.

This assessment uses WinApp 0.7's documented sparse workflow. It does not change
`WindowsPackageType`, retry the rejected unpackaged publish, replace the installed
overlay, or claim that an unsigned package is deployable.

## Reproducible offline probe

`scripts/New-WinUiExternalContentStage.ps1` accepts three existing inputs:

- A frontend-only build/publish directory containing the executable, assembly,
  runtimeconfig and resources.pri.
- A separate installation containing `widget-catalog.json` and `runtime/Bridge`.
- The matching **generated** AppxManifest.xml, including resolved Windows App
  Runtime dependencies and WebView2 activation registration.

Use a fresh output directory. The script refuses overlapping/existing output,
reparse-point inputs, escaping catalog references, reserved payload collisions,
unresolved manifests and reuse of the normal frontend identity. It does not build
or publish any project.

This evaluation stager copies the installation's `runtime/` and optional
`dotnet/` trees. Catalog references outside those trees are rejected before
staging rather than silently omitted. A future installer with another payload
layout needs an explicit copy/ownership contract for those paths.

The output contains:

| Path | Purpose |
| --- | --- |
| `external/` | Copied frontend and trusted Bridge/worker payload |
| `identity/Package.appxmanifest` | Distinct sparse identity with external content |
| `*.msix` | Unsigned identity-only package |
| `embedded-app.manifest` | Manifest extracted from the staged executable |
| `stage.json` | Source hashes, runtime requirements and isolated probe arguments |
| `profile/` | Empty, isolated future-test profile |
| `logs/` | WinApp manifest, embedding, extraction and packaging output |

The script invokes `winapp manifest generate --template sparse`,
`winapp embed-identity`, `winapp tool mt` for extraction, and
`winapp package <manifest-file> --no-sign`. It removes the template's unnecessary
`allowElevation` capability. The documented `runFullTrust` and
`unvirtualizedResources` capabilities remain; they describe the trusted host,
not community worker trust. Identity assets are copied to the external location,
where sparse manifests resolve them. No certificate, registration, startup or
launch command is run.

`scripts/Test-WinUiExternalContentStage.ps1 -Receipt <stage.json>` verifies the
result without launching it. Seventeen checks passed against the existing tested
trimmed frontend and an isolated Bridge/Media Sessions installation. The MSIX
contains exactly `AppxManifest.xml`, `AppxBlockMap.xml` and `[Content_Types].xml`;
it contains no executable, DLL or signature. Executable identity matches the
package's name, publisher and application ID. Frontend assembly bytes and the
source executable are unchanged. Negative checks reject existing/overlapping
output. Evidence is under `artifacts/winui-deployment-evaluation/stage-02/`.

The stage records the supplied binary hashes; it does not infer that arbitrary
build output matches the current source commit. It is a deployment-layout probe,
not a newly built or release-qualified candidate.

## What is established, and what is still open

The identity/package construction is feasible with supported tools. The generated
identity requires Windows 10 build 19041 or later and retains the frontend's
`Microsoft.WindowsAppRuntime.2` dependency (2.5.1.0 in the tested input). The copied
frontend includes .NET 10.0.10; the supplied Bridge and generic worker still require
.NET 8. Sparse identity does not provision either runtime automatically. An existing
bundled `dotnet/` directory is preserved if supplied; this probe does not download
or install one.

Native activation and AppContainer admission are **not yet proven** under this
identity. The current artifact is unsigned and has not been registered. A later
serialized native probe must use an approved signing/development-registration
path without silently changing trust stores. The documented production
registration is `Add-AppxPackage -Path <signed identity.msix> -ExternalLocation
<external directory>`; the unsigned artifact must not be substituted into that
command. Embedding identity changes executable bytes and must precede production
code signing.

That native probe must verify:

1. The running package identity and external location match the staged artifacts;
   `AppContext.BaseDirectory` and the Bridge child resolve to this external payload.
2. Activation passes the two isolated profile arguments from `stage.json`.
   A default launch must not accidentally touch the installed overlay's profile.
3. XAML/PRI assets, fonts, artwork and WebView2 activation work from external content.
4. A sandboxed community worker actually starts in its expected AppContainer and
   its per-profile read/execute grants target external worker/runtime directories.
5. Startup, Guide ownership, process cleanup and failed activation retain the
   existing production authority rules. No physical controller question is needed
   for the initial automated deployment gate.

## Installer direction

A future installer can stage a complete versioned per-user payload, verify all
signatures and package seals, provision required runtimes, and then register the
signed identity against that exact directory. Package identity, external-location
binding and the selected payload must be committed together. Updating only an
installer pointer would leave Windows activation bound to the old directory.

Rollback needs a deliberate package-version/external-location transaction, retained
previous payload, settings compatibility and failed-activation recovery. Uninstall
must unregister only the owned identity before removing its payload. None of these
transactions, signing/publisher choices, clean-machine runtime provisioning or
startup registration is implemented by this offline probe.

References:

- [Microsoft external-location identity guidance](https://learn.microsoft.com/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)
- [WinApp sparse packaging guide](https://github.com/microsoft/WinAppCli/blob/main/docs/guides/sparse.md)
- Installed WinApp 0.7 help for `manifest generate`, `embed-identity` and `package`.
