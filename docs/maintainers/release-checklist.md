# Prepare a public release

Use this checklist for the exact commit and artifacts being offered to users.
The project has accepted local installers, version stamping, and initial
branding. Publishing those artifacts is a separate step.

## Prepare the public repository

- Review the README, user guides, and product screenshots in `docs/images`.
- Check the MIT license and third-party license/branding notices.
- Review the Git history being published for secrets and personal information. Deleting old notes from the current tree does not erase history.
- Establish the reporting channel described in [SECURITY.md](../../SECURITY.md).
- Run CI on the exact proposed main commit after an authorized push.

The repository retains three active planning documents at the owner's request:
`docs/delivery-plan.md`, `docs/review-planner-goal.md`, and
`docs/implementation-agent-goal.md`. They are working records, not user guides
or the public API contract. Older research and archived progress notes are removed;
their previous revisions remain available through Git.

## Build the editions

From a clean checkout with the [development tools](building.md):

```powershell
$env:RUSTUP_TOOLCHAIN = '1.97.1'
pwsh -NoProfile -File .\scripts\Build-Release.ps1
pwsh -NoProfile -File .\scripts\Get-InstallerCompiler.ps1
pwsh -NoProfile -File .\scripts\Build-Installer.ps1 `
  -ReleaseRoot .\artifacts\releases\0.1.0-preview.3 `
  -CompilerPath .\artifacts\tools\installer-compiler\package\tools\ISCC.exe
```

Use the actual version from the build output when it changes. Existing release
destinations are not overwritten. The build records source and packaging revisions
and validates the catalogs and file inventory for both editions.

Read the [installer contract](../../eng/installer/README.md) for runtime provisioning,
startup ownership, uninstall behavior, and the supported GameInput minimum.

## Check the user journey

- Fresh installation on supported Windows without developer tools.
- Missing-runtime prompts, cancellation, failure messages, and retry after restart.
- Upgrade and edition switch with settings and add-ons preserved.
- Startup enabled/disabled, including a Windows Startup Apps override.
- Widget installation, permission review, manual update, and removal.
- Uninstall with data kept and with explicit data deletion.
- Representative controllers, display scales, media, window previews, and games.

Full-access add-ons may save data outside the host's directories, including
credentials in Windows Credential Manager. The host uninstaller does not
generically erase arbitrary add-on-owned stores. Review and document those
cleanup boundaries before describing uninstall as removing every possible sign-in.

## Publish deliberately

Choose the signing and distribution policy. Test the download/install experience
on a normal user account, including the warnings an unsigned preview may show.
Review third-party redistribution terms for the actual bundle and retain notices.

Advance versions according to [Versioning](versioning.md), publish immutable
artifacts with checksums and release notes, and describe remaining compatibility
limits. Clean-machine coverage and signing are release work, not something a
documentation pass can certify.
