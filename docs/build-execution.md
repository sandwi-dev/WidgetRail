# Build execution and NuGet auditing

WidgetRail treats every command that can restore a NuGet package as a
network-bearing security operation. Run such a command with supported network
access on its first attempt. NuGet auditing remains enabled, and an `NU1900`
failure is a failed build: do not retry the same command with auditing disabled,
pass `NuGetAudit=false`, downgrade or suppress `NU1900`, remove an audit/package
source, or change user or machine NuGet configuration. There is no silent retry
with a weaker execution policy.

The canonical coherent native host build is restore-bearing:

```powershell
pwsh -NoProfile -File src\OverlayHost\build.ps1 -Configuration Release -SkipTests
```

Run it in a network-capable execution environment from the outset. The script
owns each distinct `dotnet restore`, leaves NuGet auditing at its SDK defaults,
and then publishes with `--no-restore`; it never performs a hidden restricted
restore followed by a network retry.

After the exact project graph and native package assets have already been
restored successfully, a focused native selector may remain in restricted
execution by adding `-NoRestore`. That switch forbids implicit restore; missing
assets or project restore state fail the selector instead of falling back:

```powershell
pwsh -NoProfile -File src\OverlayHost\build.ps1 -Configuration Release -NoRestore -WidgetSurfaceTestsOnly
```

The same rule applies to managed and package commands. `dotnet build`, `run`,
`test`, `publish`, and `pack` are restore-bearing unless they carry the
appropriate `--no-restore` or `--no-build` contract and their exact inputs were
already restored. Run restore-bearing commands with supported network access on
the first attempt; already-restored commands may remain restricted.

`scripts\Verify.ps1` commonly selects restore-bearing managed steps. Run it in
the same network-capable execution environment unless every selected step is an
exact already-restored command carrying its own no-restore/no-build contract.

Credentials belong in supported external credential providers. Never put them
in repository files, command examples, NuGet sources, or user/machine
configuration as a workaround for a build.
