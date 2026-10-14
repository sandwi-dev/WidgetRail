# Hidden webapp memory policy

User authorized measuring WebView2 Low memory mode with synthetic calls and screen sharing before adopting it. Low is a best-effort memory target, not suspension; scripts and network connections continue. Do not add fragile call detection or Discord-specific scripts. Task Manager process naming stays unchanged. Also correct YouTube Music's missing visible banner-opacity label.

## Validation and decision

- Compare hidden Normal / Low / Normal / Low on the same document and connection, using only fake microphone/camera devices and a uniquely named animated synthetic sharing window. No real accounts, devices, messages, calls, or personal capture.
- Record working set, private bytes, cumulative process CPU, renderer round-trip time, sent/received bytes, encoded/decoded frames, packet loss and concealed audio samples. Preserve raw observations; do not interpret working-set trimming as a guaranteed reduction in committed memory or gaming overhead.
- Verify return to Normal, visibility, page identity and connection continuity. Use the existing shell regressions for pins, pop-outs, dialogs and shutdown if a production policy is adopted.
- Prefer delayed hidden-only application and immediate restoration before showing. No sleep/suspend API, arbitrary-site DOM detection, repeated trimming loop or per-frame polling.
- Preserve the existing user profile and main's unrelated goal-document edit. Work on `codex/webapp-memory-policy`; no merge, push, release, installers or worktree removal.

## Progress

- Added reusable native alternating-target measurement fixture and animated synthetic sharing source. Initial and policy-integrated trials completed.
- YouTube Music 0.3.37 adds a visible `Banner opacity · N%` label above the existing slider; accessibility semantics and control behavior are unchanged.
- Initial `call-01.json` passes 17 checks and `share-01.json` passes 11. Both alternate Normal/Low twice, retain the same connection, advance decoded frames, and show zero packet loss (also zero concealed audio samples for the fake call). Raw phase/process samples are in adjacent `.memory.json` files.
- First call phase working sets: 476.7 / 415.7 / 427.4 / 194.2 MiB. CPU consumption was not consistently tied to target level (453 / 875 / 969 / 891 ms over each nine-second sampled interval), so no CPU improvement is claimed. These are short local synthetic trials, not real Discord/game performance guarantees.
- Adopted event-driven per-surface policy: one nonrepeating timer after 15 seconds hidden, immediate Normal before visibility, no timer restart on repeated hidden reconciliation, no suspension and no call/media heuristics. An unavailable optimization logs once and does not fault the page. Timers retire with controller disposal/failure. Calls, sharing and visible pins use the same placement-derived policy as other content.
- Added native policy checks for brief-hide cancellation, sustained hiding through repeated layout updates, live media continuity and immediate Normal restoration without navigation. Added full-shell retained-page target and hidden notification delivery checks.

## Measured follow-up

Policy-integrated `call-02.json` passes 22 checks; `share-02.json` passes 16. Each repeats the alternating manual-target comparison with the automatic policy temporarily disabled, then tests the real 15-second policy. The separate sharing fixture now supplies its missing host picker-preparation callback; automatic selection is scoped to the generated synthetic window title, never the desktop. The source animates at 10 Hz so decoded-frame progress is meaningful.

| Fixture | Phase | Average browser-group working set (MiB) | Average private bytes (MiB) | CPU ms over sampled 9-second interval | Average renderer reply ms | Decoded frames over phase |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Fake call | Normal 1 | 446.0 | 237.8 | 672 | 1.11 | 204 |
| Fake call | Low 1 | 385.1 | 241.4 | 547 | 1.15 | 205 |
| Fake call | Normal 2 | 397.6 | 245.3 | 844 | 1.11 | 205 |
| Fake call | Low 2 | 178.3 | 248.4 | 656 | 1.22 | 203 |
| Window share | Normal 1 | 450.5 | 309.1 | 375 | 1.03 | 101 |
| Window share | Low 1 | 372.3 | 293.3 | 266 | 0.92 | 96 |
| Window share | Normal 2 | 381.0 | 294.4 | 375 | 0.99 | 105 |
| Window share | Low 2 | 171.4 | 295.2 | 453 | 1.07 | 97 |

All phases: zero observed packet loss; fake-call phases also have zero concealed audio samples. Return-to-visible renderer round trips were 6.3 / 6.7 ms. Those are script-response measurements, not display-latency or perceptual smoothness guarantees. The tests have no STUN/TURN servers or remote peer, use fake microphone/camera devices, and save statistics only. Working-set totals sum the environment's browser/GPU/utility/renderer processes and can include shared resident pages. Private bytes are reported separately; lower working set is not freed commit. No CPU benefit is claimed, and real Discord calls under game load remain a physical acceptance check.

- Final Debug frontend build: zero warnings/errors. Managed tests: 232/232 shell and 32/32 standalone music, including visible opacity label/value assertions. Community package 0.3.37 built and validated (316 files).
- Review: UI-thread-only target changes, one nonrepeating timer per hidden surface, immediate normal-target restoration before native visibility, guarded failure/disposal, existing pin/popup placement ownership, no widget-specific media heuristics and no new author-facing settings. No blocking findings.
- Full shell `shell-native-01.json`: 152/152 checks pass, including hidden Low-target notification delivery, retained live media, immediate Normal restoration, pins/pop-outs, dialogs, banner layering, recovery and shutdown.

To reproduce with a Debug frontend: launch `--validate-webapp-background=<absolute-result.json> --webapp-profile=<isolated-profile> --webapp-memory --shell-no-controller`. For sharing use `--validate-webapp-sharing=<absolute-result.json>` and add `--webapp-sharing-source=<built tests/WebAppSharingSource/WebAppSharingSource.exe>`. Both write the result plus `.memory.json` raw phase samples. Run separately without concurrent builds for meaningful process measurements.

## Candidate delivery

- Implementation commit `5aadc4df` on `codex/webapp-memory-policy`. Complete candidate at `artifacts/webapp-memory/candidate-01/0.1.0-preview.31`; catalog/package/icon checks pass for 11 Production and 15 Developer widgets. No installers created.
- YouTube Music 0.3.37 installed, selected and enabled; 0.3.36 remains available for rollback. No saved private state, sign-ins or credentials were read or changed. The explicit full-trust acceptance flag was supplied for both install and enable, as required by the CLI.
- Developer candidate launched on SDK Gallery, PID 32520. Platform settings hash before/after delivery: `C17EE2EE30C4C8E96E23E1C3CF61DF362BE5C1DBAC42F7802DC1A41C9DAA19D3`. Main's unrelated `docs/implementation-agent-goal.md` edit remains untouched.
- Physical checklist: hide Discord for at least 20 seconds during your own call/share, reopen and check continuity/responsiveness; try quick switches, a visible pin and a pinned pop-out; check YouTube Music Settings shows the banner-opacity label and percentage. Synthetic validation is complete; account-dependent acceptance remains with the user.
- No merge, push, release or worktree removal.

## Acceptance

User accepted the memory-policy and opacity-label changes on 2026-10-14 and authorized merging them to main before the separate native chooser investigation. The preceding unmerged status records candidate delivery. Native chooser controller/lifetime issues are tracked as the next task, not attributed to Low memory mode by these measurements.
