# WIDGE-92 pinned slider handoff

Status: **unresolved physical defect; stop further work in the current implementation lane**

Last updated: 2026-08-29 (America/Los_Angeles)

Plane item: `WIDGE-92` — `a8b343f3-83f1-4194-a9d3-e9b61d3966a1`

Related prerequisite: `WIDGE-60` — `5ae24d08-e270-433e-9f20-228734ca393a`

## User-visible defect

The Spotify custom pinned surface exposes a seek slider. Controller focus can
reach and select the slider, but pressing A and then D-pad Left or Right does
not change the value. The same slider behavior works in the ordinary full
widget.

The latest integrated candidate was physically rejected on 2026-08-29:

- The slider is focusable.
- The slider value cannot be adjusted.
- Repeated attempts have produced no visible improvement.
- The user requested that the issue be handed to another agent rather than
  continuing the current implementation loop.

Do not treat the green provider-neutral integrated test as product acceptance.
It did not reproduce the live Spotify failure.

## Current repository and runtime state

Main checkout and integrated production baseline:

```text
C:\Users\dwive\Projects\WidgetRail
branch: main
production HEAD before this handoff document: e4dc8bbd975ff71d5255213b9407f38bd3ef1729
tree: 0177e1e5ae0d0a0a366476a1e7f30b38796a9bae
```

The cumulative WIDGE-60/WIDGE-92 chain is already integrated into local main.
There is no push.

The exact main Release used for the final physical rejection was built with:

```powershell
pwsh -NoProfile -File src\OverlayHost\build.ps1 -Configuration Release -SkipTests
```

Current Release hashes after that main build:

```text
OverlayHost.exe
B86E99913A120D9080FFE612F67FDD508378D0FA9D7724CB70FE8A5CC5167B22

runtime\Bridge\WidgetBridge.exe
3DD5EE3A40BEFA3E9468F553AF83DA31BEE1319CA53EA9EAF345A5F0046A13EA
```

The rejected build was still running when this handoff was written:

```text
OverlayHost PID 60952
WidgetBridge PID 63848
WidgetWorkerHost PID 40808
SpotifyApplication PID 56168
Spotify package version 0.3.28
```

Do not assume those PIDs remain current. Resolve executable paths again before
signalling any process.

Main also contains unrelated user/reviewer document changes which must be
preserved:

```text
 M docs/overlay-host-code-findings.md
?? docs/controller-input-findings.md
?? docs/test-suite-findings.md
?? docs/widget-sdk-ui-findings.md
```

## Original evidence worktree

The original implementation evidence remains available and clean:

```text
worktree:
C:\Users\dwive\.codex\visualizations\2026\08\11\019fef3b-7e94-70f0-b329-3551f8dd805b\widge92

branch:
codex/widge-92-pinned-slider-activation

HEAD:
1bfe8e6220d2a1f19c62104f1bd4da7c9e5dfa5a

tree:
e2100f1dbc516dbb7fe4c24559d2adb3c62d16ca
```

Preserve that worktree as evidence. A new agent should normally create a fresh
branch/worktree from current main (including this handoff document, whose
production parent is `e4dc8bbd`), not continue building product
changes directly in the old evidence worktree.

## Commit chain

Original worktree commits and their integrated-main equivalents:

| Purpose | Original commit | Integrated main commit |
| --- | --- | --- |
| WIDGE-60 compatible pinned input authority | `00d6fda1912cf93e0545acc8dc0bbae18b74fe74` | `96f33e72` |
| WIDGE-60 native action binding guard | `9e66c17d34aab8fb1cc579ff567d5632765b8882` | `ccfbd029` |
| Reuse slider interaction on pinned surfaces | `8b7407abbccc683795d5d4f8550917d20b55ce8f` | `26861e83` |
| Preserve exact slider action authority | `129827c167e61ae48afaf5da6872360bd64d746d` | `5320ccca` |
| Retain compatible queued slider requests | `ff5cdbf4a53badd50a86d4a7af45e9b7945d4e19` | `3a573e76` |
| Integrated route fixture and focused selector | `1bfe8e6220d2a1f19c62104f1bd4da7c9e5dfa5a` | `e4dc8bbd` |

The final original commit touched seven files:

```text
src/OverlayHost/WidgetSurfaceCoordinator.cpp
src/OverlayHost/WidgetSurfaceCoordinatorTests.cpp
src/OverlayHost/WidgetSwitchHostTests.cpp
src/OverlayHost/build.ps1
src/OverlayHost/main.cpp
tests/WidgetBridge.Tests/BridgeClientRegistryScenarios.cs
tests/WidgetSwitchFixture/Program.cs
```

The cumulative production chain additionally changes:

```text
src/OverlayHost/WidgetSurfaceCoordinator.h
src/OverlayHost/WidgetBridgeClient.cpp
src/OverlayHost/WidgetBridgeClient.h
src/WidgetBridge/BridgeClientRegistry.cs
src/WidgetBridge/BridgeProtocol.cs
src/WidgetBridge/WidgetBridgeServer.cs
```

## Intended architecture

Pinned and full-widget surfaces already share the public Slider node, renderer,
range, value, role, and accessibility model. WIDGE-92 intentionally did not add
a second slider component.

The implementation gives `WidgetSurfaceCoordinator` its own short-lived
instance of the existing provider-neutral `WidgetInteractionSession` slider
state because the pinned projection has separate focus, lifecycle, layout, and
teardown authority.

Intended controller behavior:

1. Focus the pinned slider.
2. A enters activation-first adjustment mode.
3. D-pad Left/Right uses the shared min/max/step/current calculation and emits
   one absolute `valueChanged` action.
4. A or B exits adjustment mode.
5. Outside adjustment mode, D-pad resumes spatial focus navigation.

Authority is intended to remain exact across the native queue and Bridge:

- widget and instance;
- runtime and presentation generation;
- selected pinned layout;
- active input scope;
- focused node;
- node enabled/busy state;
- source element and `valueChanged` action ID;
- requested absolute value;
- compatible origin and successor snapshots.

There is no Spotify-specific host behavior and no retry/replay loop.

## Important source boundaries

Line numbers below refer to current main `e4dc8bbd` and may move after edits.

- `src/OverlayHost/main.cpp:8770-8816`
  - Physical pinned controller routing.
  - D-pad calls `MoveControllerFocus(..., true)`.
  - A/B call `HandleFocusedSliderModeButton` before generic pinned dispatch.
- `src/OverlayHost/WidgetSurfaceCoordinator.cpp:429`
  - `MoveControllerFocus`; resolves slider adjustment versus spatial navigation.
- `src/OverlayHost/WidgetSurfaceCoordinator.cpp:530`
  - `HandleFocusedSliderModeButton`; enters/exits activation-first adjustment.
- `src/OverlayHost/WidgetSurfaceCoordinator.cpp:649`
  - `QueueResolvedInput`; captures exact slider action request and requested value.
- `src/OverlayHost/WidgetSurfaceCoordinator.cpp:237-330`
  - `UpdateSnapshot`; reconciles slider state and retains compatible queued slider
    requests instead of clearing them blindly.
- `src/OverlayHost/WidgetSurfaceCoordinator.cpp:721`
  - `IsCurrentInputRequest`; native exact-current authority test.
- `src/OverlayHost/main.cpp:7491-7575`
  - Drains pinned input, performs host admission, calls Bridge, and rolls back on
    rejection.
- `src/WidgetBridge/BridgeClientRegistry.cs:675-719`
  - Bridge controller input admission.
- `src/WidgetBridge/BridgeClientRegistry.cs:1753`
  - `DemandPinnedSurfaceAuthority`; compares origin/current bindings and rewrites
    only the worker-facing snapshot sequence.
- `src/WidgetBridge/WidgetBridgeServer.cs:505-520`
  - Validates private `ExpectedActionId` use for pinned D-pad value changes.

## Physical attempt history

All candidates allowed focus to reach the slider but failed to change its value.

### Candidate `8b7407ab`

Added pinned ownership of the shared slider interaction state. Physical result:
A appeared to select the slider, but D-pad did not change the value.

### Candidate `129827c1`

Carried the exact `WidgetInteractionActionRequest` and private expected action ID
through native/Bridge authority. Physical result remained unchanged.

### Candidate `ff5cdbf4`

Replaced `UpdateSnapshot`'s unconditional request-queue clear with compatible
slider request reconciliation. Physical result remained unchanged.

### Final integrated candidate `e4dc8bbd`

Added the provider-neutral end-to-end fixture and durable focused selector, then
integrated all production commits into main. The test passed, but the user again
reported that the live Spotify slider could only be selected, not adjusted.

## Latest physical log evidence

Logs:

```text
C:\Users\dwive\AppData\Local\WidgetRail\action-correlation.log
C:\Users\dwive\AppData\Local\WidgetRail\overlay.log
```

For the final rejected run, the relevant action-correlation records were:

```text
2026-8-29 3:8:28.246 level=debug category=widget-action
stage=host-admission sequence=16 context=pinnedSurface button=b
widget=widgetrail.samples.spotify focus=spotify.seek.slider
scope=spotify.window native-snapshot=698 worker-snapshot=698
requested-runtime=06011c06959af65ff4aab1099a8947ac
current-runtime=06011c06959af65ff4aab1099a8947ac

2026-8-29 3:8:28.249 level=warning category=widget-action
stage=host-reply sequence=16 result=stale_pinned_input_authority
```

`overlay.log` immediately reported:

```text
2026-8-29 3:8:28.250 Pinned action transport failed for widgetrail.samples.spotify
```

Crucially, the same physical attempt produced **no pinned D-pad Left/Right host
admission record** and no Spotify `valueChanged` action. The B press being sent
as generic pinned input indicates that adjustment mode was not active by the
time B was pressed. This narrows the live loss to a boundary before ordinary
host/Bridge slider delivery:

- A did not actually establish adjustment mode; or
- adjustment mode was retired before the next D-pad sample; or
- D-pad was routed as navigation rather than slider adjustment; or
- `AdjustSlider` produced no action request; or
- the request was rejected/cleared before the existing host-admission logging.

The final build removed the temporary internal slider-stage diagnostics, so the
current logs cannot distinguish those cases.

## What the green test proves — and does not prove

Focused command:

```powershell
pwsh -NoProfile -File src\OverlayHost\build.ps1 `
  -Configuration Release -PinnedSliderRouteTestsOnly
```

The selector passed and proved, with a provider-neutral fixture and a
compile-time-only controller-frame injection seam:

- slider focus;
- A adjustment entry;
- Left absolute value 45 exactly once;
- compatible authoritative successor reconciliation;
- Right absolute value 50 exactly once;
- exact action ID `fixture.slider.changed` and source `pinned-slider` twice;
- A exit;
- outside-mode D-pad spatial navigation;
- coordinator and Bridge authority seams.

The seam is guarded by `WRAIL_PINNED_SLIDER_ROUTE_TESTING`; ordinary Release is
macro-free.

The test does **not** reproduce Spotify's real custom pinned layout, publication
cadence, focus identity, or rapid successor snapshots. Its passing result is
therefore insufficient and must not be used to close WIDGE-92.

## Recommended next-agent approach

Start from a fresh worktree at current main. The integrated production parent
for this handoff is `e4dc8bbd`; the following reviewer commit adds only this
document. Do not add another behavioral guess first.

1. Preserve the current implementation and add bounded diagnostic evidence at
   the physical pre-admission boundaries:
   - decoded physical controller frame and D-pad navigation event;
   - A route plus `TransitionSliderAdjustmentMode` result and active state after
     transition;
   - every `UpdateSnapshot` decision that retains or retires slider adjustment;
   - D-pad `RouteFocusedDirection` result;
   - `AdjustSlider` consumed/visual/action-request result;
   - queue insertion, compatible retention, rejection reason, and drain.
2. Launch that exact diagnostic Release and run one coordinated physical probe
   on Spotify: focus slider, A, Left, Right, B.
3. Stop at the first proven lost owner. Do not change Bridge or Spotify until a
   request is shown to reach those layers.
4. Compare the real Spotify selected projection against the fixture:
   - slider `sliderInteractionMode`;
   - focused node ID;
   - active input scope;
   - selected layout ID;
   - snapshot instance/runtime/presentation generation;
   - whether snapshot refresh changes any of those immediately after A.
5. Extend the integrated test with the proven real timing/state condition before
   accepting a correction.

Likely high-value question: does `WidgetInteractionSession::ReconcileAdmission`
retain activation mode when Spotify publishes a compatible successor whose
slider value or projection object changes immediately after A? The generic
fixture says yes for its sequence, but the physical evidence says the real mode
is absent before B and no D-pad request reaches host admission.

## Constraints for the next agent

- No Spotify identity special case in OverlayHost, WidgetBridge, protocol, or
  WidgetSdk.
- Reuse the shared slider interaction implementation; do not create a second
  pinned slider component or duplicate min/max/step math.
- Do not weaken exact runtime/layout/scope/focus/action authority.
- Do not add retries, input replay, timeout inflation, or an unbounded queue.
- Preserve queue/history cap 16 and exactly-once delivery.
- Preserve unrelated dirty documents on main.
- Do not push.
- Do not delete the original WIDGE-92 worktree.
- Treat physical acceptance as mandatory; the synthetic selector alone is not
  sufficient.

## Plane disposition

WIDGE-92 must remain open and blocked for handoff. The latest integrated code is
not accepted by the user. WIDGE-60 also remains physically unclosed because its
live pinned-action verdict is entangled with this path.
