# WinUI process lifecycle

One settings profile has one resident renderer across executable paths, package
versions, and the original native/WinUI frontends. The shared
`OverlayProcessOwner` remains the only election/activation authority. Windows App
SDK `AppInstance` is used only to read packaged activation arguments; its
version/path-scoped instance registration is not a second election.

`Program.Main` elects before creating XAML windows, input, or Bridge services.
Normal secondary launch sends the existing authenticated Show request and exits.
`--hidden` elects a hidden first owner or returns `AlreadyRunning` without sending
Show to an existing owner. This also works against native versions that only know
the original Show protocol. A repeated launch never changes the resident's
installation roots or selected widget from new command-line arguments.

The default settings root maps to native-compatible `production`; other absolute
settings roots map to a 64-character SHA-256 identity. Case, slash spelling,
trailing separators, relative path segments and extended path prefixes normalize.
Installation/package paths do not affect identity. Profiles should use one
canonical settings directory path; filesystem junction aliases are not resolved.
The existing user SID/logon security boundary is unchanged.

The independent additive process ABI lives in `OverlayPlatformInterop.dll` and
does not create a controller session. Its managed lease is deliberately
thread-affine, with no finalizer releasing a mutex on the wrong thread. Custom
`Main` holds the lease around `Application.Start`, disposing it only after native
window shutdown and the host's asynchronous child/input cleanup finish.
Both frontends share the installer Running marker and Setup exclusion logic.

## App integration hook

After constructing `MainWindow`, before initial activation/input, the application
must attach the process notification endpoint:

```csharp
ProcessLifecycle.Attach(main, main.ShowOverlay);
```

`ShowOverlay` must be idempotent with respect to visibility, use the existing
foreground acquisition route, and ignore activation after cleanup begins. It must
not toggle. `ProcessLifecycle` registers its native message monitor before binding
the HWND, so Show arriving before window creation remains pending and is delivered
on the UI thread. Explicit validation pages bypass production election only in
builds containing validation code; production replay/switch diagnostics do not.

The integration branch attaches that endpoint before initial presentation and
passes the same resolved launch configuration from election to the shell.
The native DLL must be rebuilt and packaged with the frontend; a missing/newer
process entry point fails startup closed instead of starting a second input owner.

## Validation

- Managed platform-client checks cover owner/client outcomes, invalid profiles,
  idempotent disposal, and rejection of cross-thread release.
- Managed shell checks cover stable production, isolated, UNC, and path spelling
  identities.
- Original native-owner tests include quiet duplicate behavior in addition to
  simultaneous clients, early Show, malformed clients, shutdown, stale owners,
  endpoint squatting and bounded timeouts.
- `Build-OverlayPlatform.ps1 -TestProcessLifecycle` builds/runs a bounded, invisible
  native probe exercising the actual exported ABI and original owner in both
  directions. It uses isolated profiles and a message-only HWND; it never creates
  controller/Bridge sessions or changes installer state.

The packaged activation probe now passes 17 checks through Windows' registered
application activation API: a hidden duplicate does not show/start workers;
normal duplicates show the same resident without toggling or replacing its one
Bridge; clients and the final owner exit normally. The first launch still uses
project-mode WinApp. Evidence: `artifacts/winui-shell/process-activation-integrated-01`.
Foreground acquisition and physical controller acceptance remain separate.
