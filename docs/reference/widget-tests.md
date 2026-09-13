# Widget testing helpers

Test widget behavior with known state and fake services before involving live
accounts, controllers, or Windows devices.

## Define a scenario

`WidgetScenarioDefinition` pairs a widget with test host services. The generated
template includes a `ready` definition that can be used by its tests and CLI preview.

`WidgetTestHostServicesBuilder` supplies typed fake host services. It lets a test
provide device lists, values, or failures without using the real Windows provider.

## Describe interactions

`WidgetScenario` builds a sequence of steps. It can activate/deactivate the widget,
invoke actions, wait for work, and check text, busy state, or focus. `RunAsync`
returns a result with the individual step outcomes.

Keep cases small enough that a failure explains which behavior broke. For example,
check that a loading update preserves the selected game before adding provider
failure, filtering, and page eviction to the same scenario.

## Control asynchronous completion

Use `WidgetScenarioOperationBarrier<TRequest,TResponse>` when a test needs to
pause a fake request deliberately. The recorded invocation exposes the request
and cancellation/completion signals; the test can complete it or fail it explicitly.

This is more reliable than sleeping for an arbitrary duration and hoping a
request is still pending. It can reproduce late results and cancellation races.

## Use the right evidence

Scenarios and snapshot/replay checks establish semantic behavior. They do not
prove native pixels, screen-reader timing, real playback, or game compatibility.
Keep those checks separate and state their limits.

Continue with [CLI scenarios](cli-scenarios.md). Source:
[`WidgetTesting.cs`](../../src/WidgetSdk/WidgetTesting.cs),
[`WidgetScenarios.cs`](../../src/WidgetSdk/WidgetScenarios.cs), and
[`WidgetScenarioTesting.cs`](../../src/WidgetSdk/WidgetScenarioTesting.cs).
