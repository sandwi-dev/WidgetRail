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

### Subsequent native development proof

WinApp 0.7 `create-debug-identity --keep-identity` successfully registered the
isolated external-content probe in Developer Mode, without certificate creation
or trust-store changes. Run it from the stage directory so its `.winapp/debug`
metadata stays with the probe. Direct PowerShell `Add-AppxPackage -Register
-ExternalLocation` returned immediate `E_INVALIDARG` on this machine, including
with a trailing directory separator; the precise cmdlet/API difference is not
established. The supported CLI path succeeded.

The newer trimmed frontend launched through Windows' registered application
activation API. `GetPackageFullName` verified its actual process identity and its
loaded frontend assembly hash matched the staged publish. With no installation
root override, Settings and the bundled Media Sessions widget both reached their
committed native presentation. The Bridge and worker executable paths were under
the external payload. The bundled generic worker's actual `TokenIsAppContainer`
was true; read/execute AppContainer ACL entries were present on its runtime and
package directories. The static, trusted Settings worker is intentionally
job-isolated and was not used as proof of the community-widget sandbox.

Evidence: `artifacts/winui-deployment-evaluation/native-current-05/`, particularly
`runtime-result.json`, `identity-and-grants.json`, `register-cli.log` and
`cleanup.json`. The first native probe used an older supplied publish that
predated config-free bootstrap; its installation-recovery screen is retained in
`native-cli-04` and is not a failure of the newer bootstrap. All owned frontend/
worker processes exited normally and both temporary package registrations were
removed. No installed native overlay or startup entry was replaced.

This proves development identity, native XAML startup, default payload resolution
and an actual sandboxed bundled worker under external content. It does not yet
qualify a signed installer, clean-machine runtime provisioning, embedded media,
all asset/font pixels, update/rollback or physical controller behavior. The probe
used an installed Windows App Runtime and .NET 8 worker runtime.

The durable regression driver is `scripts/Test-WinUiExternalContentRuntime.ps1`.
Supply a stage receipt and a fresh output directory; the default sandboxed widget
is `media-sessions`. It refuses existing registrations, uses the supported WinApp
development-registration path, creates an isolated profile, activates through
Windows, verifies process identity/assembly/worker token, then checks process exit
and removes only its own development registration. It requires an already staged
frontend with config-free bootstrap and the selected bundled sandboxed widget.
It neither provisions runtimes nor changes certificate trust. The complete driver
passes in `artifacts/winui-deployment-evaluation/runtime-driver-06/result.json`.

### Production delivery still required

The private .NET service-runtime gap is now corrected and tested. A staged,
checksum/signature-verified .NET 8.0.31 payload hosted both Bridge and the actual
sandboxed generic worker; the runtime driver verified their loaded CLR module
paths, not just environment settings. The frontend remained its own trimmed
.NET 10 publish. See `private-runtime-check-08/result.json`. The driver now
requires private runtime modules whenever the stage contains `dotnet/`.
Windows App Runtime provisioning and signed identity installation remain separate
open requirements; this test used the existing Windows App Runtime installation.

### Verified offline Windows App Runtime payload

`Stage-WinUiRuntimePrerequisites.ps1` takes the restored frontend assets file,
its generated manifest and a fresh output directory. It stages the six signed
Microsoft packages required on x64 Windows: x64/x86 Framework and DDLM, plus
x64 Main and Singleton. It uses the actual manifests and version metadata;
the restored `MSIX.inventory` misstates the DDLM names and Singleton version.
For runtime 2.5.1, Singleton is 8002.5.1.0; the others are 2.5.1.0.

The stager computes NuGet's signed-package content hash from the archive to tie
it to the resolved build graph, separately verifies the raw archive SHA-512,
verifies NuGet signatures and each MSIX's Microsoft signature, and preserves
the complete Framework packages with embedded Main/Singleton license payloads.
Its receipt records exact identities, architectures, versions, dependencies,
license hashes and signer information. Framework prerequisites are ordered first.

`Test-WinUiRuntimePrerequisites.ps1` independently checks the signed manifests
against role, identity, dependency and license expectations. Receipt paths must
remain inside the staged payload without reparse points; duplicate paths and
identities are rejected. `Test-WinUiRuntimePrerequisitesAdversarial.ps1` covers
substituted roles/packages, escaped paths, junctions, omitted dependencies and
licenses, and forged NuGet cache/graph metadata. The fresh stage passes 95 checks
and 14 adversarial cases in `runtime-prerequisites-13/` and
`runtime-prerequisites-adversarial-14/` under the deployment evaluation artifacts.

This is verified offline payload construction, not runtime provisioning. The
official installer/deployment manager handles licensing as well as package
installation. The receipt explicitly leaves installation, license deployment
and clean-machine qualification false. Native ARM64 coverage is not included.
An installer still needs that provisioning step before committing app identity.

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
