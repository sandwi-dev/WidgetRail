# Reviewed widget settings continuity

## Acceptance and integration — 2026-10-15

The user accepted all current changes and authorized merging this work to local
`main`. This supersedes the earlier no-merge delivery boundary and pending
physical-acceptance notes below. The YouTube Music 0.3.38 → 0.3.39 settings-update
test was reported working. Candidate/build evidence remains in the relevant
sections; older candidate paths and PIDs are historical. Remote publication and
release are outside this integration request. Main's unrelated
`docs/implementation-agent-goal.md` edit is preserved.


Status: implemented and validated, 2026-10-15. Updated candidate physically accepted; local integration authorized.

## Problem

Unsigned package authority contains its exact content digest. YouTube Music's
banner Enabled, BackgroundOpacity and SetupSeen preferences consequently had
separate private-state documents in 0.3.37 and 0.3.38. Ordinary settings need
intentional update continuity without extending consent or secret authority.

## Implemented contract

- Update review says that widget settings will be kept. Confirming the reviewed
  update authorizes that copy, without a second technical permission prompt.
- Marketplace individual/batch updates, Settings Update from file, GitHub CLI
  updates and the new CLI `update --file` use the same settings transfer helper.
- Old identity comes from the verified current installation; new identity comes
  from the exact sealed staging tree. Marketplace and local reviews reject a
  changed source baseline. Catalog mutation serializes admission and selection.
- The host blocks new worker admission, retires the old worker, copies settings,
  publishes/selects the package, reloads the catalog, then releases admission.
  Restart is rejected during this window. Cancellation releases admission.
- CLI stays disabled-only; an exclusive old-manifest handle rejects a still-live
  worker and prevents relaunch through publication. Profile selection is explicit
  with `--settings-root` when the profile is not the catalog's parent directory.
- One shared portable store implementation now serves the Windows backend and
  trusted package maintenance. Existing envelopes and paths are unchanged.
- Copy only the canonical bounded private-state document. Ordered cross-process
  locks, five-second transfer timeout, existing quotas, reparse/corruption checks
  and atomic publication apply. Destination data and clear markers take priority.
  Source data remains available for exact-byte rollback.
- No transfer IPC or widget capability was added. Permission/secret identities,
  cookies, AppContainer profiles and arbitrary files are not copied. Raw install,
  version selection and changed-byte reinstall after uninstall have no implicit
  settings continuity grant. No retroactive namespace migration occurs.
- A successful final preparation finishes package publication despite a late
  cancellation. A storage/preparation failure leaves the prior version selected.

## Validation

Evidence: `artifacts/widget-settings/`.

- Catalog: 39/39, including exact-authority copy, no copy on unapproved/raw
  installation, preparation failure, preserved destination values/clear markers,
  cross-widget rejection, cancellation and corruption.
- Existing Windows private-state tests: 4/4, including cross-process CAS,
  persistence, corruption/reparse protection, quotas and rate/cancellation rules.
  Credential tests were not run; all exercised documents were synthetic.
- Registry update admission: 2/2 (retirement, blocked navigation/restart,
  replacement admission, cancellation cleanup).
- Local import/update: 5/5, including explicit review, stale authority,
  declined/cancelled review and preserving enabled state.
- Marketplace service: 15/15, with a real synthetic private-state transfer plus
  batch cancellation, review expiry, changed baseline and partial failure tests.
- CLI: local-file and GitHub update tests passed, including profile routing,
  reviewed checksum, active-file refusal and retained preferences/security identity.
- Settings: 91/91. Marketplace widget: 7/7.
- Normal Release WinUI build passed with zero warnings/errors. An initial build
  accidentally combined validation fixtures with Release trimming; it failed on
  existing fixture IL2026 diagnostics. The normal Release configuration passed.
- Documentation and source review completed; no credentials, personal windows or
  account-dependent actions were used. The running candidate/profile are unchanged.

## Separate follow-ups

Full-trust widgets may still use stable application-managed files for volume,
large application data or caches. This change does not migrate those files.
YouTube Music's independent debounced-save/read-error handling is not a confirmed
cause of the package-identity reset and was not changed in this fix.

## Physical test preparation

- Complete candidate: `artifacts/widget-settings/candidate01`; 1,121 inventory
  files, 15 verified bundled widgets, native cleanup verification and deployed
  frontend DLL hash check passed. Normal existing profile retained.
- User baseline: YouTube Music 0.3.38, banners enabled, opacity 65%, setup seen.
  Baseline state and profile/stop-state hashes remained unchanged through restart.
- Prepared `artifacts/widget-settings/widgetrail.samples.ytmusic-0.3.39.wrwidget`
  from the accepted 0.3.38 package, changing only manifest version. This is a local
  test package, not a published release or source-version bump. It passed CLI
  archive inspection and remains uninstalled for the user's Update from file test.
- Physical acceptance remains pending: approve the update review, confirm settings
  and setup behavior, then restart WidgetRail and confirm persistence again.
