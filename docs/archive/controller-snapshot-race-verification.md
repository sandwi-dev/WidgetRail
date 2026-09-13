# WIDGE-239 controller snapshot admission verification

Base: 55231775. Worktree: codex/controller-snapshot-race.

The reproduction was three Spotify Playlists B inputs rejected while playback
published the next snapshot between native ingress and bridge admission. The
shared bridge/runtime fix adopts its current published snapshot under the
presentation gate, verifies the original binding, and dispatches once. Standard
unbound input retains ordinary host fallback; stale/unverifiable input retires
quietly. Widget implementations are unchanged. Spotify 0.3.56 only republishes
the shared runtime; its manifest version is bumped for immutable installation.

## Automated checks

- WidgetBridge.Tests: 128/128, including a blocked publication/input race and
  101 compatible admissions under repeated snapshot publication, each delivered
  exactly once. Changed bindings, disabled/removed focus, scope, retained-history
  expiry, pinned projection and runtime authority remain guarded.
- WidgetRuntime.Tests: 96/96. Covers standard unbound and declared input,
  one action execution, stale worker sequence, no lazy runtime restart, inherited
  overrides, nonvirtual hidden handlers, declared override bookkeeping, and
  strict frozen-v2 application compatibility. Unknown legacy revalidation does
  not dispatch raw input; verified declared input uses one legacy delivery.
- WidgetSdk.Tests: 115/115, including the unchanged public SDK contract.
- WidgetBridgeCatalogTests: passed, including stale-result classification and
  13 embedded-media boundary cases.
- Complete Release native host and managed runtime publication: passed.
- Spotify 0.3.56 package: validated and packed using the normal package script
  with unique build binlogs and the version override; no widget code changes.

The initial broader runs exposed a pre-existing Terminate double-read race
against session disposal. Capture the Job and Process once before terminating.
The startup test also needed to tolerate its intentional crash beating queue
acknowledgement and to exclude disposal's Background transition from startup-token
counting. These are covered by the final passing runtime suite.

An intermediate bridge run correctly rejected a stale runtime DLL in candidate
output. The final coherent build resolved it; the full bridge suite then passed.

## Physical acceptance and integration

The user accepted candidate b4ca64f81a4d36f8b79b73c35a54653fb8900025 on
2026-09-12 and requested integration and closure. Spotify 0.3.56 was the installed
package, containing the shared runtime update and unchanged widget logic.

The accepted production is unchanged. This documentation-only successor records
acceptance and accompanies fast-forward integration into main and WIDGE-239
closure. Automated results above remain the accepted candidate's verification;
no tests were rerun for this documentation-only change. No push was requested.
