# Widget notifications

Implement the agreed shared notification service for sandboxed and full-trust widgets. Retention (banner only / pending), interaction (informational / open widget), rich artwork/text/metadata, icon visibility, duration, background opacity, replacement keys, and overlay visibility are independent host-validated options. Browser notifications keep their current defaults and callbacks.

YouTube Music is the first consumer: opt-in during setup, off by default, editable afterward; thumbnail, song, artist and total duration; no widget icon; banner only; available with overlay open or hidden; no sound/focus theft; four seconds and the existing quiet interval. Track transitions, not playback ticks/pause/resume, publish. Metadata updates replace without extending the deadline.

One placement policy avoids visible pins and, while open, widget, Guide, rail and dialog surfaces. No separate rendering path for open/hidden overlay. If no safe corner is available, suppress the banner rather than cover controls.

## Work

- Implemented: bounded SDK/protocol; authenticated background-capable broker service; bridge event with widget/runtime authority; per-widget sequence checks and retirement boundary; `ShowAsync`, `UpdateAsync` and `DismissAsync`.
- Shared coordinator owns pending/transient entries and per-widget preferences. Browser owner is an adapter using the same coordinator. Metadata-only updates preserve entry identity; banner-only entries never enter the bell count. Optional artwork reuses the bounded native decoder. Background alpha is separate from content opacity.
- Shared corner placement avoids pin and visible overlay surfaces; active dialogs/fullscreen leave no eligible position when they occupy the available space. Visible/hidden modes use the same passive window, timer and geometry policy.
- YouTube Music: setup discovery, default-off persisted toggle, conditional opacity control; debounced track transition publishing during the widget lifetime; pause/resume/ticks stay quiet. Updates cannot resurrect expired banners.
- Final managed checks: 232/232 shell; 32/32 standalone music, including setup discovery, background sending, saved opt-in/opacity and dismissal retry; full-trust process broker test passes; SDK 157/157; PlatformBroker 80/80. Broker vocabulary counts were extended for the service. The suite also exposed a pre-existing mismatch: main already allows the exact capture-library open-folder continuation, but its vocabulary assertion omitted it. The assertion now explicitly checks that existing operation; capture behavior was not changed.
- Native `artifacts/widget-notifications/native-01.json`: 86/86 checks passed, including real worker -> broker -> bridge -> shell delivery while visible/hidden, update/expiry, pending activation, existing web notifications, popup/media recovery and shutdown. Song raster reviewed (synthetic artwork URL intentionally unavailable, fallback visible).
- Final native `native-03.json`: 133/133 passed, including ordinary and web notifications, stable metadata-update identity/deadline, informational pending controls, visible/hidden delivery, popup/pin placement, retained media, renderer recovery and shutdown. Frontend builds pass without warnings/errors. Optional artwork uses the already-tested shared bounded decoder; the synthetic URL is intentionally unavailable and the fallback was reviewed.
- Review completed: authenticated producer identity and generation checks; background lifecycle admission; per-worker bounds; transient cleanup/replacement; stable pending identity; preference precedence; native focus/layering; bounded metadata layout; DPI-aware artwork decode; disposal and settings debounce. No blocking findings remain. SDK API baseline and author/user documentation updated.
- Community package `artifacts/widget-notifications/ytmusic-0.3.36/widgetrail.samples.ytmusic-0.3.36.wrwidget` built and validated. No external account probes. Pending: complete candidate build, package installation and launch for physical acceptance.
- Preserve normal profile, sign-ins, shortcut and main's unrelated documentation edit. Synthetic test data only; no credentials, real messages/calls or personal captures. No merge/push/release/installers requested.

## Candidate delivery

- Implementation commit: `bdc70bde` on `codex/widget-notifications`.
- Complete candidate: `artifacts/widget-notifications/candidate-01/0.1.0-preview.31`; Production/Developer catalogs, package integrity and icon admission passed (11 / 15 bundled widgets). No installers or release created.
- YouTube Music 0.3.36 installed, selected and re-enabled through the CLI's normal disabled-update-review flow. Version 0.3.35 remains installed for rollback. No account data was read or changed by this work.
- Developer candidate launched on SDK Gallery (PID 30168). Native UI Automation confirms the bell and clock/connectivity area, and the process is responsive. Normal platform settings SHA-256 remains `7D939734F705FE10F1685A6EC800809309D3D8A5667A73F8A7D8F556F3CC516B`; main's unrelated goal-document edit remains untouched.
- Rendered synthetic song banner background alpha was measured at 204/255 for the requested 80%; text/artwork opacity is independent.
- Autonomous implementation and validation complete. Physical acceptance: enable Song-change banners during setup or Y → Settings; start/skip tracks with overlay open/closed; check real thumbnail/artist/duration, unchanged bell count, opacity adjustment, pause/resume staying quiet, rapid replacement/quiet interval, and disabling the feature.
- Changes remain unmerged and unpushed.

## Banner layer correction

- Physical testing found that a visible-overlay banner inherited passive-pin ordering and could land behind the desktop backdrop. Corner placement was correct; native window order was not.
- Shared peer windows now declare a presentation layer independently of interaction. Notifications sit immediately above the main overlay, remain click-through/nonactivating, and preserve the order of already-raised peers. Ordinary pin interaction and adjustment rules are unchanged. Backdrop/activation/appearance reconciliation now includes both pins and notifications.
- The ordering helper moves only the banner, never lowers the main window (which could put its backdrop above it). Existing no-activate/no-owner-order flags are retained; no per-frame polling or new focus path is added.
- Native regression coverage deliberately reproduces the bad stack and checks repair through three cycles, passive pins, placement adjustment, already-raised siblings, banner hide/show, foreground, controller focus and passive window styles.
- Debug build: zero warnings/errors. Shell suite: 232 passed, zero failed/skipped. Native `native-04.json` passed 146 checks including all new ordering checks and existing pin/dialog/pop-out/media checks, then timed out awaiting the intentional renderer crash recovery state. A bounded unchanged retry is in progress; this run is not recorded as a complete pass.
- Review: shared nonactivating insertion preserves other siblings' relative order, default pin behavior, existing backdrop reconciliation and native handle lifetime. No layout, focus-admission or WebView ownership changes.
- The unchanged retry `native-05.json` passed all 149 checks, including renderer recovery and shutdown. No layering regressions were observed. The initial timeout remains recorded above rather than discarded.
- Correction commit `3f23b090`; complete `candidate-02/0.1.0-preview.31` payloads verified (11 Production / 15 Developer widgets). Developer candidate relaunched on SDK Gallery, PID 29976, using the normal profile and existing YouTube Music 0.3.36 package. Platform settings hash remains `7D939734F705FE10F1685A6EC800809309D3D8A5667A73F8A7D8F556F3CC516B`. No merge/push/installers/release performed.
- Physical check: with song banners enabled, skip a track while the overlay is open; verify the banner is above the backdrop, then open/hide the overlay during a banner and exercise pin interaction/move/resize/opacity.
