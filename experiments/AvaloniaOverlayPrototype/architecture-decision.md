# AVP-003 architecture responsibility and composition decision

## Responsibility map

| Concern | AVP-002 owner | AVP-003 owner | Boundary result |
| --- | --- | --- | --- |
| Window visibility and controller activation | `MainWindow` | `MainWindow` | Presentation lifecycle remains outside view models. |
| Keyboard/controller semantic routing | `SemanticInputRouter` | `SemanticInputRouter` | One accepted route still owns Slider, Button, tray, and virtualized-item behavior. |
| Spatial focus and remembered focus | `PrototypeShellView` / `FocusNavigator` | Same presentation services, plus narrow `GameLauncherPage` container realization | View models never inspect controls or focus. |
| Route admission/history/loading | `NavigationCoordinator` / `PrototypeShellView` | Same services | `PrototypeShellViewModel` exposes immutable selected/loading state and route-request commands but cannot admit a page. |
| Page transition | Custom opacity children in `PageTransitionPresenter` | Avalonia `TransitioningContentControl` / `CrossFade` wrapper | Avalonia owns the actual transition; the wrapper owns cancellation and diagnostic timing only. |
| Representative page state/actions | Mostly AXAML literals and code-behind | CommunityToolkit view models over immutable records | State and commands are independently testable without a Window or controller. |
| Launcher collection | Sixteen code-created Buttons | Bound 10,000-item `ListBox` / `VirtualizingStackPanel` | Avalonia owns selection, recycling, scrolling, and standard UIA containers. |
| Remote snapshot/action lifecycle | None | UI-neutral `RemoteWidgetProjection` and `IRemoteWidgetEndpoint` | Latest-wins, last-good failure, exact action, and deactivation cancellation do not reference Avalonia. |
| Object construction | Ad-hoc field construction | `PrototypeComposition` | One explicit manual root owns singleton projection/view models and transient pages. |

The result deliberately does not move focus, controller, window, transition,
navigation, or UIA policy into view models. The launcher page contains only the
presentation-specific bridge needed to realize/focus standard recycled
containers and translate their stable item identity into the shared semantic
router.

## Manual composition versus direct Microsoft DI

Candidate: `Microsoft.Extensions.DependencyInjection` 10.0.10 with one
singleton remote source, one singleton launcher state, and one transient page
factory. Comparison fixture:
`tests/fixtures/CompositionComparison`; bounded runner:
`scripts/Compare-Composition.ps1`.

Seven framework-dependent console process samples were retained on 2026-08-13.
This is a composition microcomparison, not an Avalonia startup benchmark:

| Metric (median unless stated) | Manual | Direct Microsoft DI | DI delta |
| --- | ---: | ---: | ---: |
| Process launch through result | 57.4457 ms | 75.0394 ms | +17.5937 ms |
| Composition call | 0.1519 ms | 20.8152 ms | +20.6633 ms |
| Private memory | 6.4609 MiB | 6.2188 MiB | -0.2422 MiB (noise-sized) |
| Framework-dependent published bytes | 184,424 | 348,190 | +163,766 bytes |

Decision: retain manual composition. The slice has one application lifetime,
one remote projection, one shared page-view-model set, and transient page
controls; it has no request scope or ownership ambiguity that a container would
resolve. The small memory difference is below the credibility of this
microcomparison, while startup/composition and published-footprint deltas are
observable. Direct DI can be reevaluated only when a real scoped lifecycle or
substitution boundary benefits from it and an actual Avalonia comparison is
authorized.

The tracked summary is
[`evidence/composition-comparison.json`](evidence/composition-comparison.json);
raw samples remain in the ignored `artifacts/avp003/composition-comparison`
directory.
