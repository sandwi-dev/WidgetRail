# Displayed ordinary input admission

## Defect and deterministic proof

A presenter captures the displayed frame, then waits for the worker's Interactive lifecycle acknowledgment. An unrelated publication can arrive before that acknowledgment. The previous authority-only `SendActionAsync` and `SendControllerInputAsync` both require equality with the latest admitted authority, so they reject the unchanged control with `presentation_stale`.

`AuthorityOnlyInputRejectsUnchangedControlAfterSnapshotDuringLifecycleAcknowledgment` reproduces that exact order over the real session transport: frame 1, pending lifecycle request, frame 2 with only unrelated text changed, lifecycle acknowledgment, attempted action/controller input. The existing strict overloads intentionally retain their original behavior.

## Additive session API

```csharp
Task<WidgetOperationAdmission> SendActionAsync(
    WidgetPresentationFrame displayed, WidgetActionEvent action,
    CancellationToken cancellationToken = default);
Task<bool> SendControllerInputAsync(
    WidgetPresentationFrame displayed, ControllerInputEvent input,
    CancellationToken cancellationToken = default);
```

The new overloads require the exact frame object published by this session. Copying its record/authority does not establish provenance. Current widget instance/runtime/presentation/local session identity and active input scope must match. Original and current bindings must both resolve to the same actionable owner, kind, keyed occurrence path and focused occurrence. Changed or unavailable controls cannot acquire input from old pixels.

Actions compare their relevant contract: direct activation; the nearest declared shortcut; slider range and step; text input kind and maximum length; Select option identity/content/availability; or contextual action and menu trigger. Unrelated text/layout changes and slider values do not retarget a captured explicit command. The existing shared shortcut resolver and slider validation math are reused.

Open-widget controller input preserves the origin snapshot sequence, focused element, button, phase, input sequence, timestamp and origin. The bridge continues to validate that origin against its bounded history and current binding. No retry uses a newer sequence. Raw open-widget handlers remain supported, but adding/changing a shortcut or changing the raw focus owner cannot silently redirect a delayed event. Other controller contexts retain the prior APIs.

Typed action transport remains the existing `BridgeActionRequest` with the original action object. Its protocol has no snapshot-origin field. This change validates action provenance and the original/current binding at session admission; it does not add a different bridge action protocol or replace existing runtime action admission. Controller input still receives the bridge's independent origin/current validation.

## Frontend wiring

The integration frontend now carries the displayed frame through ordinary actions,
Select, text commit, slider commit and context actions. Controller dispatch keeps
its origin sequence through the Interactive barrier. The production shell consumes
stale-input rejections without showing a widget failure or replaying the input.
The following rules also apply to new control adapters:

1. Carry the captured `WidgetPresentationFrame` in `WidgetActionRequest` instead of only its authority. An `Authority` convenience property can return `Displayed.Authority`.
2. Capture the frame before any lifecycle await, popup dismissal or focus restoration in ordinary activation, Select, text commit, slider commit and ordinary context-action paths. Keep the captured action values and focused ID.
3. In shell `InvokeAsync`, keep the existing Interactive barrier, then call `Session.SendActionAsync(request.Displayed, request.Action, ...)`.
4. In ordinary controller routing, capture the frame before the barrier, construct input against its authority, then call the displayed-frame overload. Never read new native focus after waiting.
5. Treat `ordinary_input_stale` as a consumed/dropped interaction, like indexed/snapshot/scope retirement. It is not a widget runtime failure and must not become host Back navigation or show Retry.

The barrier still owns visibility, foreground and selection intent cancellation. The new session overloads do not promote the widget or replace that barrier. Existing indexed input and resource/media APIs are unchanged.

## Validation

Tests exercise the lifecycle race, original payload preservation on the wire, forged frames, worker retirement, changed scope/action/kind/key/availability, raw/shortcut ownership, exact slider/text/Select/context contracts, and cancellation. These are deterministic session regressions; native tray-to-button and tray-to-popup checks remain part of frontend integration.
