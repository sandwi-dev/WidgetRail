# SDK reference

Use this index to find one API family at a time. For a first project, start with
[Your first widget](../developers/widget-quickstart.md).

## Views and interaction

| Topic | Start here |
|---|---|
| Describe a view | [UI elements](declarative-ui.md) |
| Compose a navigation shell or custom header | [Presentation composition](presentation-composition.md) |
| Buttons, sliders, settings rows, and menus | [Controller components](controller-ui-components.md) |
| Actions, shortcuts, and Back | [Controller input](controller-input.md) |
| Stable IDs, focus lookup, text entry | [Identity and input helpers](identity-and-input.md) |
| Scrolling and responsive collections | [Collections](collections.md) |
| Artwork, toasts, and backgrounds | [Visual content](visual-content.md) |
| Window size and display scale | [Display and layout](display-and-resolution.md) |
| Screen-reader behavior | [Accessibility](accessibility.md) |
| CSS-like appearance rules | [WRSS](wrss.md) |

## State and work

| API | Responsibility |
|---|---|
| [`WidgetModel<TState>`](widget-model-reference.md) | Store related immutable UI state |
| [`WidgetResource<TValue>`](async-work.md) | Load and cache one result |
| [`WidgetCursorResource<TItem>`](collections.md) | Fetch a continuous window of adjacent pages |
| [`WidgetPagedResource<TItem>`](collections.md) | Load and cache one offset-based page at a time |
| [`WidgetNavigator<TRoute>`](routes.md) | Keep the route stack and route lifetimes |
| [`WidgetOperations`](async-work.md) | Coordinate asynchronous work by key |
| [`WidgetOptimisticCommand`](async-work.md) | Show a provisional change and reconcile its result |

Hiding the overlay and destroying a widget are different events. See
[Lifecycle and residency](widget-residency.md) before keeping work alive in the background.

## Integrations and distribution

- [Capabilities](capabilities.md): permission-based host services.
- [Private state](private-widget-state.md): package-scoped saved data.
- [Local companions](community-companion-services.md): exact-port JSON and private secrets.
- [Media and pinning](../developers/media-and-pinning.md): compact layouts and players.
- [Pinned layout handles](pinned-layouts.md): stable layout identity and selection lifetimes.
- [Widget tests](widget-tests.md): fake services, scenarios, and operation barriers.
- [Full-access applications](application-widgets.md): the separate public application bootstrap.
- [Live window previews](window-previews.md): view-only application content.
- [Package format](widget-packaging.md) and [CLI workflows](cli-workflows.md).
- [SDK compatibility](../developers/widget-sdk-compatibility.md) and [versioning](../maintainers/versioning.md).

## Find an exact signature

The SDK source includes XML comments for public APIs. Common entry points are
[`Widget`](../../src/WidgetSdk/Widget.cs), [`UI`](../../src/WidgetSdk/UI.cs),
[`Elements`](../../src/WidgetSdk/Elements.cs), and the helper files linked by each topic.
[`ProtocolConstants`](../../src/WidgetProtocol/ProtocolConstants.cs) lists feature versions.
