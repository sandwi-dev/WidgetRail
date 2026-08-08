# Widget SDK developer testing

Widgets access platform providers through the protected `HostServices` property.
Production services are attached exactly once by `WidgetWorkerBootstrap` before
`OnCreatedAsync`; a widget cannot replace them after creation.

For dense controller surfaces, use `UI.VerticalScroll` or
`UI.HorizontalScroll`; the host owns clipping, focus-follow, and restored
offsets. A view may also provide bounded `WidgetSurfaceHints` so compact and
wide widgets communicate a useful shape without assuming a monitor or fixed
window. Both are optional snapshot protocol-v2 features. Widgets that use
neither continue emitting the package-API-1-compatible protocol-v1 snapshot.
See [Declarative UI](../../docs/declarative-ui.md) and
[Display and resolution](../../docs/display-and-resolution.md).

For settings and contextual commands, prefer the public controller composites
over custom focus routing. `UI.SettingsRow` keeps long supporting copy separate
from its one stable `id.action` target. `UI.ActionSheet` accepts 1–32 stable
items, owns a vertical focus-follow Scroll, and binds B on its nested scope;
publish that scope as `WidgetView.ActiveInputScopeId` while it is open. Disabled
and Busy actions stay focusable and are suppressed by the standard router.
Use `UI.Picker` for bounded single selection rather than disguising choices as
commands: it exposes one stable option ID per row, explicit selected state,
Up/Down neighbors, focus-safe unavailable state, and the same scope-owned B.
Use `UI.Scrubber` for media seeking instead of pairing a private Slider with
duplicate progress text. Its only focus stop is `id.slider`; Left/Right uses
the native Slider contract and the action receives an absolute requested
position in milliseconds. Elapsed and duration labels are formatted by the SDK
unless localized labels are supplied.

Use `UI.ResponsiveGrid(id, minimumColumnWidth, maximumColumns?, children)` for
a bounded set of peer cards/categories that should reflow across compact and
wide logical viewports. It is protocol v8: the host derives row-major columns
from final DIP width, authored gap, a 44–1600 DIP minimum column width, and an
optional 1–32 column cap. The Grid is not focusable; children keep their stable
IDs and normal focus graph. Put unbounded collections in a host-owned Scroll.
Settings uses this same public helper for its root category surface.

Use `element.VisibleWhen(ResponsiveVisibility.CompactOnly)` and
`ExpandedOnly` when the same semantic view needs substantially different
composition below the host's compact breakpoint (less than 960 DIPs wide or
540 DIPs high). `UI.ResponsiveBranch(...)` is the equivalent non-fluent form.
This protocol-v9 modifier adds no layout container and preserves the wrapped
element's stable ID. The inactive element and its complete subtree are absent
from native layout, paint, pointer hit testing, controller focus, shortcuts,
and accessibility. Keep the root unconditional and give mutually exclusive
branches distinct stable IDs; use ordinary responsive Grid/Row wrapping when
the content hierarchy itself does not need to change.

Use `UI.CodeText(text, id, accessibilityLabel?)` for bounded diagnostics or
commands. It emits one nonfocusable Text node with `.gbar-code-text`, preserves
whitespace, and caps content/accessibility text at 4,096 characters. It does
not create selection, a copy command, scope, shortcut, or background work;
provide a separate explicit Button when copying matters. The built-in theme
uses single-family `Consolas` with up to eight wrapped lines. Native GBSS does
not yet implement CSS font fallback stacks or packaged font loading.

For indeterminate work that lasts long enough to be visible, use
`UI.LoadingIndicator(id, accessibilityLabel, size)`. It is a protocol-v5,
host-rendered primitive with bounded Compact, Standard, and Large sizes. It
never enters controller focus, accepts no action or interaction state, and
does not require widget polling. The native host animates its lightweight arc
only while it is visible; reduced-motion mode keeps the same accessible status
as a static indeterminate arc. Keep fast cached transitions visually quiet
instead of flashing a spinner for a single frame.

For rich media/application rows, use `UI.MediaTile` or `UI.AppTile`. Both are
protocol-v7 `ActionSurface` compositions: the complete tile is the sole focus,
pointer, pressed, and A-action target, while generated artwork/copy children are
presentation only. `TileArtwork` accepts one semantic glyph, credential-free
HTTPS image, or bounded inline PNG. Custom rich actions may use
`UI.ActionSurface`, but its subtree is limited to 8 direct children, 32 total
descendants, and four relative levels and cannot contain actions, focus,
scopes, shortcuts, scrolling, interaction state, Buttons, Sliders, or another
ActionSurface. Preserve generated `id.artwork`, `id.content`, `id.title`,
`id.subtitle`, `id.metadata`, and `id.state` suffixes and `gbar-action-surface`/
`gbar-tile` classes when adding widget-specific classes.

Use `UI.Toast` for brief feedback that must not steal focus. Tone is paired
with visible text, duration is bounded to 2–30 seconds (five by default), and
the widget—not the host or component—owns removal through normal lifecycle-
aware state. Do not create a timer or hidden worker solely for Toast animation;
themes must suppress or shorten motion when reduced motion is active.

Unit tests can use the supported transport-free fake instead of reflection,
internal APIs, named pipes, or hand-written JSON:

```csharp
var services = new WidgetTestHostServicesBuilder()
    .WithResponse(
        WidgetAudioCapabilities.GetSessions,
        (IReadOnlyList<WidgetAudioSession>)[
            new("game", "Game", 0.75, false, true)])
    .WithEvents(
        WidgetAudioCapabilities.SessionsChanged,
        [new WidgetAudioSessionsChanged([])])
    .Build();

var widget = WidgetTestHost.Attach(new MyWidget(), services);
await WidgetTestHost.InitializeAsync(widget);
await WidgetTestHost.SetLifecycleStateAsync(
    widget, WidgetLifecycleState.Interactive);
// ...assert UI and behavior...
await WidgetTestHost.DestroyAsync(widget);
```

Pass `IsAvailable: false` in `WidgetAudioSessionsChanged` to simulate a live
provider outage. This is not equivalent to an available event with an empty
session list; widgets should render distinct recovery and empty states.

Use `WithHandler` for request-dependent or asynchronous responses and
`WithEventStream` for a live deterministic stream. The builder takes an
immutable snapshot at `Build`. Missing operations/events fail with
`WidgetCapabilityException` and `test_handler_missing`; mismatched generic
contracts fail with `test_contract_mismatch`. Duplicate handlers are rejected.
`WidgetTestHost.Attach` uses the same one-time pre-creation gate as production,
so double attachment and attachment after `OnCreatedAsync` remain invalid.
Its lifecycle helpers call the same host-validated transitions as production;
`Created` and `Destroying` cannot be requested through `SetLifecycleStateAsync`.

When a widget combines a current snapshot with future events, open the typed
acknowledged subscription first (`OpenSessionsSubscriptionAsync` or
`OpenStatusSubscriptionAsync`), fetch current state second, then consume
`ReadAllAsync`. A successful open means host registration is complete, so the
coalesced full-snapshot event buffer closes the fetch/subscription race.
`WatchSessionsAsync` and `WatchStatusAsync` remain compatibility helpers for
event-only consumers.
