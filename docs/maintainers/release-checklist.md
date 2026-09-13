# Release checklist

Source publication and a downloadable desktop release are separate milestones.
Use the [readiness audit](release-readiness.md) for current evidence; this page
describes the gates.

## Publish the source repository

- [ ] Review the root README, license split and contribution instructions.
- [ ] Confirm ownership/redistribution terms for third-party assets and service branding.
- [ ] Review a redacted secret scan of the exact Git history being made public.
      Removing a file from the current tree does not remove it from history.
- [ ] Review historical personal information, old paths and copyright identities.
- [ ] Establish a private security-reporting channel and update `SECURITY.md`.
- [ ] Push the reviewed branch and obtain CI results for the exact proposed main commit.
- [ ] Review known limitations and use an honest preview label.
- [ ] Owner explicitly approves changing repository visibility.

## Publish a desktop preview

- [ ] Choose and stamp an application version; see [Versioning](versioning.md).
- [ ] Produce a complete relocatable artifact from a clean checkout.
- [ ] Verify supported Windows versions and runtime provisioning on a clean PC.
- [ ] Verify launch from a normal user account with no development tools installed.
- [ ] Verify installation, upgrade, rollback, removal and retention of user settings.
- [ ] Define optional driver installation/removal and recovery behavior; never
      silently install controller drivers as part of testing.
- [ ] Run current managed/native gates, keep exact-commit evidence, and resolve
      or explicitly document any waived failures.
- [ ] Test controller disconnect/reconnect and wired/wireless changes.
- [ ] Test sleep/resume, audio/network changes, prolonged sessions and bounded
      cache growth. Power tests must be explicitly performed by a person.
- [ ] Test standard and elevated applications, window previews, foreground behavior,
      multiple monitors, DPI/text scale, themes and reduced-motion settings.
- [ ] Test expired/revoked authentication, provider rate limits, offline use,
      unavailable services, and full/denied/read-only storage.
- [ ] Review redistribution terms for separately licensed runtimes such as GameInput; do not apply the project license to vendor code.
- [ ] Publish required license notices and corresponding source for every shipped component.
- [ ] Review signing, checksums, runtime redistribution and SmartScreen experience.
- [ ] Add real screenshots or a short demonstration from the accepted build.
- [ ] Owner approves the final release artifacts and publication.

These are release gates, not a claim that each check has already passed.
