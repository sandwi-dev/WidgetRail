# Widget status badges

## Acceptance and integration — 2026-10-15

The user accepted all current changes and authorized merging this work to local
`main`. This supersedes the earlier no-merge delivery boundary and pending
physical-acceptance notes below. The YouTube Music 0.3.38 → 0.3.39 settings-update
test was reported working. Candidate/build evidence remains in the relevant
sections; older candidate paths and PIDs are historical. Remote publication and
release are outside this integration request. Main's unrelated
`docs/implementation-agent-goal.md` edit is preserved.


Implementation in the existing marketplace-widget-lifecycle checkout. Preserve prior changes and the normal profile; no merge, release or installers.

## Design

Shared host-owned semantic status, shared rendering in rail and radial. Pending notification counts exclude banner-only entries. No page DOM scraping. WebView2 audio events aggregate main and hosted child pages. Native playback uses player state/mute/volume. A bounded package-scoped SDK activity service lets ordinary widgets report local audio, remote playback and recording without choosing artwork or asserting host lifecycle states.

Rail has a count at the upper right and one activity symbol at the lower right. Normal radial uses both badges after visual review. Below 320 DIPs it collapses to one: stopped, recording, failure, pending count, audio, remote playback. All statuses remain in accessible names/tooltips; no extra focus targets or continuous animation. Visual review must include eight populated radial slots, rail overflow, small/large scaling and high contrast.

## Validation

Policy/lifecycle tests, capability admission and validation, synthetic WebView audio and native visual fixture, review, complete updated candidate. Account-dependent acceptance remains the user step.

## Decisions and evidence

- SDK is source-scoped (`SetAsync` / `ClearAsync`), not an author-managed whole-widget snapshot. At most 16 contributions, one in-flight and one replaceable pending aggregate; duplicate suppression and 100 ms spacing live in the SDK.
- Host observations remain independent. Reporting false cannot hide actual host-managed audio. No status admission wakes a worker or retains residency.
- Added exact worker-run retirement events because idle unload does not necessarily replace catalog identity. Session tombstones reject late reports; old retirement cannot clear a replacement run.
- Supported WebView2 SDK XML exposes screen capture request/start consent but no matching public ended/state event. Automatic website sharing badge is deferred rather than incorrectly latching on permission/chooser activity.
- Native fixture uses synthetic SVGs, browser pages/oscillator audio and generated WAV; no personal page or window captures. Nine image combinations cover dark, light, high contrast and 0.8/1.0/1.4 scale. Standard radial shows two badges; reduced 280-DIP radial shows one. Rail includes overflow-arrow spacing, dense 44-DIP tiles, and 22/24/34-DIP icons. Status refresh preserves native focus.
- Earlier fixture issues corrected: nonexistent light theme resource, missing BrowserInteractionMode, missing native fixture import. A Python default-codepage roundtrip affected a few edited strings; they were restored and scanned. These were fixture/tooling errors, not product regressions.

## Completed validation and delivery

- Native badge/media fixture: **174/174** (`native05/native-result.json`), including browser start/stop/mute/hidden audio, hosted child aggregation and retirement, and native player mute/zero-volume/pause/disposal.
- SDK: **158/158**, including a 500-update burst converging to two publications with bounded pending tasks, independent source clearing, rapid stop/resume, source limits and cancellation before submission.
- Presentation session: **344/344**, including exact worker-run retirement, stale runtime/replay rejection, restart admission and late old-run publications/retirement.
- Shell: **237/237**; broker focused authority/domain checks: **4/4**.
- Real full-trust broker/worker: **1/1**; intrinsic capability admission: **1/1**.
- YouTube Music standalone: **32/32**; Capture Library: **27/27**.
- Existing native production/radial navigation: **114/114** (`radial-result02.json`). First run failed a stationary-rail assertion after a fixed 30 ms layout delay (`radial-result01.json`). Added observed bounds to diagnostics; unchanged production code passed on rerun. Cause is not established; retain this as timing-sensitive fixture evidence rather than claiming a confirmed product fix.
- Nine synthetic rasters generated; reviewed standard and compact dark/light layouts and high-contrast focus clearance. The first high-scale screenshot clipped the system focus visual because the fixture window was smaller than its scaled content; sizing the fixture client to contain the full board corrected the capture without changing production focus rendering.
- Code review covered stale authority, source ownership, teardown, aggregate deduplication, background residency, focus, theme/high-contrast behavior and bounded queues. UTF-8 and diff whitespace checks passed.

Complete candidate: `artifacts/widget-status/candidate01/OverlayFrontend.WinUI.exe`, launched with the normal profile (PID **2336** at delivery). Payload verification checked **1,121 files**, **136 frontend DLL hashes**, and **15 bundled widgets**; frontend asynchronous-cleanup verification passed. Hidden startup remained responsive with **zero child services**. Opening started the bridge normally; current candidate had no nonzero error headers. Its tray UIA probe occurred while Guide had hidden the overlay; that probe timed out and is not counted as a UI pass.

Discord **0.1.1** and YouTube Music **0.3.38** were built/validated and selected in the existing catalog. CLI installation required temporary disablement, followed by restoration of the prior enabled flags. Platform settings and stopped-state file hashes were unchanged through the update. No sign-in/configuration data was inspected or replaced. Previous package versions remain available.

No merge, push, release or application installer was created. Main retains its unrelated goal-document edit. The candidate includes the earlier pending Marketplace/lifecycle changes and description cleanup.

## Physical checklist

1. Play/pause/mute Twitch or another webapp. Check speaker status in rail and radial, including a hosted/pinned additional page; close the page and check it clears.
2. With pending notifications and playback together, check the count above and speaker below. Opening the widget alone should not clear the count; opening/dismissing notifications should. Song-change banners must not increase the count.
3. Stop a widget, then reload it. Check the stopped badge replaces activity and reload clears it. Test switching/hiding without losing the status of a playing neighbor.
4. Play/pause YouTube Music and, if desired, record a Capture Library clip. Verify the respective activity badges and cleanup. Real account and personal-capture acceptance remains the user's step.
5. Check your preferred theme/interface scale and a crowded rail/radial. Normal layouts show two badges; very compact layouts reduce to one rather than overlap.

Automatic website screen-sharing indication remains deferred for the documented missing reliable stop signal. Native media's audio indication describes playback/mute/volume state, not measured audible samples.
