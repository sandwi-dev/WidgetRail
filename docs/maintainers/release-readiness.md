# Public release readiness audit

Audit date: 2026-09-12. Product baseline: `1e5a46b2`.

**Assessment:** the implemented feature set is suitable for a source preview.
A public desktop binary release still needs packaging, exact-commit CI and
clean-machine validation. Local feature acceptance does not establish these.

## Findings

| Priority | Finding | Evidence / disposition |
|---|---|---|
| High | No top-level project license at baseline | Owner selected GPLv3 platform/tools, MPL 2.0 SDK/protocol/application-bootstrap/shared files, MIT first-party widgets/samples/templates. Added explicit path/file licenses, SDK package metadata and generated-boilerplate notices. License metadata participates in the local package content version. |
| High | No release artifact or canonical app version | GitHub has no releases; no application tag existed at audit time. SDK is `0.3.0-dev`; only SDK/CLI share canonical version properties. Documented a separate app/SDK/package strategy; version stamping and artifact automation remain open. |
| High | No current green remote release evidence | The latest remote workflow is a failed run on `2dd8331d` from August 29, not the accepted current baseline. Managed failure involved YouTube tests; native failure involved WidgetSurfaceCoordinatorTests. These historical failures are not a finding that current code fails. New CI evidence requires a later authorized push/run. |
| High | Clean-machine prerequisites are not packaged | Native build and framework-dependent managed outputs exist; no checked-in application installer/release workflow was found. Documented .NET 8 runtime, SDK 10 for builds, C++/Rust, GameInput and WebView2 distinctions. Validate optional drivers separately. |
| Medium | Rust toolchain selection depends on invocation context | The toolchain file is nested in `taffy_bridge`; invoking Cargo with `--manifest-path` from the root does not select that nested rustup toolchain. Build instructions explicitly select it; CI now installs/selects it. A future build-script guard should enforce it outside those entry points. |
| Medium | User entry points were obsolete and overly technical | Root README called the product a Phase 0 prototype and described Guide as the only shortcut. Replaced it with user benefits, current controls, developer entry points and honest source-preview setup. |
| Medium | Public docs mixed reference and development history | Organized 96 existing files into audience/subject folders, separated historical plans, rewrote the index, and updated local links and path-based doc contracts. |
| Medium | Task Switcher tests were missing from aggregate verification | Runner self-test identified the unregistered existing test project. Added build/test steps so CI includes it, and updated the explicit aggregate inventory for Task Switcher and the already-added Power steps (55 defaults). |
| Medium | Documentation gate had two baseline false failures | Its source-text checks expected individual cleanup calls, while the build now loops over generated runtime directories. Updated the checks to verify the current cleanup block and ordering, retaining the cleanup requirements. |
| Medium | Trust language was stale | Corrected SVG, full-trust application, pinning and local-data guidance. Package hashes do not authenticate publishers; full-trust widgets remain ordinary Windows applications. |
| High before binaries | Proprietary runtime redistribution needs a license review | GameInput 3.5.262 carries Microsoft redistribution terms, including limits on imposing source-disclosure obligations on Microsoft code. The GPL project license does not relicense that runtime. Review the intended binary bundle and any needed linking permission before shipping it. |
| Medium | Third-party branding/redistribution review remains open | Dependency notices exist, but icon provenance explicitly includes provider-branded assets. Project licensing cannot grant trademark rights. Removed duplicate ViGEmClient notice; complete binary notices remain a release gate. |

## Repository hygiene

A locally downloaded Gitleaks **8.30.1** binary was checked against the publisher's
release checksum and run with redaction against reachable `HEAD` history.
It reported scanning **2,171 commits / 37.28 MB** and nine matches. All nine
were inspected at the reported historical revisions: three were ordinary prose
and six were explicit synthetic hexadecimal test fixtures. No real credential
was identified by that scan.

This is evidence from one scanner and the selected history, not a guarantee
that no sensitive information exists. It does not cover unrelated refs,
GitHub issue content, or all personal information. Do not publish local runtime
state, credentials or diagnostic archives. Build logs, package outputs,
test results and local agent data have ignore rules.

The existing main-checkout personal documents and the accepted running
candidate were not changed by this audit. The prior tracked implementation
note is archived; the old local-note paths are ignored so personal working
copies can be retained during integration.

## Validation scope

The baseline documentation executable reproduced the two cleanup-check failures
listed above. The prepared documentation contract passes (74 Markdown files). The CLI suite
passes 64/64, including generated profiles, the external author workflow and
the SDK package license/content-version contract. SDK compatibility passes
14/14. The audited-build guidance check passes. Task Switcher passes 6/6 and the verification-runner self-test passes with
the updated default inventory.

An initial CLI invocation used `dotnet WrailCli.Tests.dll`, which broke fixtures
that relaunch their own executable. It was stopped and corrected to the test
apphost executable; the completed 64/64 result is from that invocation.

The audit does not repeat the earlier feature-by-feature physical acceptance,
shut down/restart/suspend the PC, install drivers, or claim a clean-machine
performance/security certification. It does not publish a release or change
GitHub visibility.

## Next release decisions

1. Review and integrate this source-publication preparation.
2. Resolve the [release checklist](release-checklist.md) for the intended milestone.
3. Obtain current CI and clean-machine evidence before offering an end-user download.

Feature completeness is a scope decision. Release readiness is evidence about
the exact build and the environments in which it is offered.
