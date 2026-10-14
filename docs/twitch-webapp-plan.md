# Twitch webapp

## Scope and decisions

- Branch `codex/twitch-webapp`, based on the accepted native-dialog changes at `ffa69d3c`. Reuse the existing platform-feature-roadmap worktree; preserve main's unrelated goal-document edit and the normal user profile.
- Generate through `wrail new widget --template webapp`. Keep its public browser declaration unchanged: on-demand chrome, persistent isolated profile, hosted pop-outs, bounded background residency and interaction on focus.
- Community package only, with a curated marketplace listing and the existing package builder. No installer inclusion, publication, merge or push requested. A listing requires a published archive before remote download is available.
- Reuse the accepted host for local installation. No account access, messages, subscriptions or website-specific scripts. Account and physical controller acceptance remain the user's.

## Validation

- Public CLI scaffolding succeeded using the accepted candidate's CLI. The repository source matches the generated browser declaration; only the SDK project reference, packaging and icon differ.
- Generated MSTest browser-contract check redirected to the actual repository Twitch project: Passed: 1, Failed: 0, Skipped: 0. Evidence: `artifacts/twitch-webapp/template-tests.log` and binlogs.
- Release package builder, SVG validation and archive validation passed. Archive: `artifacts/twitch-webapp/package-02/widgetrail.samples.twitch-0.1.0.wrwidget`, with SHA-256 sidecar. Initial icon validation rejected unsupported stroke attributes; the final icon uses the supported SVG subset.
- CLI `preview --scenario ready` succeeded in an isolated worker with zero view diagnostics. Evidence: `artifacts/twitch-webapp/ready.scenario.json`.
- Release packaging self-test passed all 10 checks, including marketplace-only exclusion in both editions. Evidence: `artifacts/twitch-webapp/release-packaging.log`.
- Installed and enabled `widgetrail.samples.twitch` 0.1.0 in the ordinary local catalog. Restarted the accepted host candidate and opened Twitch (PID 30724 at delivery). The host is responsive, the tray entry and controller guide are present, and the public logged-out Twitch home page renders with a video preview. Reviewed page-only raster: `artifacts/twitch-webapp/home.png`. No sign-in, cookie-choice changes or chat submission performed.
- Source review: stable browser identity/navigation generation, inherited shared session/popup policies, no widget capabilities requested, no site-specific injection, no new native layout or input code. Main's unrelated goal edit remains untouched.
- Remaining physical checks: controller browsing/search, playback controls/audio, pinning and hidden playback, Twitch player/chat pop-out placement, and optional user sign-in persistence. Package/preview checks do not prove those site-dependent behaviors.
- The running accepted host already supports this widget and required no rebuild. The marketplace catalog addition is source-only until the next host build; remote installation also requires publication. No merge, push, release or installer creation performed.
