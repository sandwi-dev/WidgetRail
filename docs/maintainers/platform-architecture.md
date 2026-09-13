# Platform architecture

WidgetRail separates the native overlay from widget code and Windows integrations.
This lets widgets share one controller experience while running outside the
resident presentation process.

## The main parts

```text
OverlayHost ↔ WidgetBridge ↔ widget worker
                  ↕
             capability broker ↔ Windows providers
```

| Part | Responsibility |
|---|---|
| `OverlayHost` | Windows, native rendering, controller routing, layout, focus, media presentation |
| `WidgetBridge` | Coordinate catalogs, workers, snapshots, styles, and host services |
| `WidgetRuntime` | Worker startup, lifecycle, isolation, and message transport |
| `WidgetWorkerHost` | Load an ordinary packaged widget into its worker |
| `WidgetSdk` | Public authoring components and state/navigation helpers |
| `WidgetProtocol` | Shared manifest and presentation data contracts |
| `WidgetCatalog` | Package inspection, versions, enablement, and launch validation |
| `PlatformBroker` and `Windows*Provider` | Permission checks and native Windows operations |
| `WidgetStyling` | Parse and resolve WRSS |

The source folders under [`src`](../../src/) use these names.

## From widget state to pixels

The worker renders a view from its current state. The bridge validates the
presentation and resolves styles. The native host prepares layout and draws the
supported elements.

When state changes, invalidation tells the host that a newer view is available.
A new snapshot does not automatically mean every style, text layout, or pixel
must be rebuilt. The renderer compares changes and retains reusable preparation.

See [Renderer preparation](renderer-preparation-retention.md) and
[Performance](performance.md) for the update path.

## From input to an action

The host resolves input against the presented control and active scope. The
worker receives the semantic action. If presentation changed in between, action
admission must establish that the original target is still valid before delivery.

An input cannot be redirected to a different widget just because another view
now occupies the same screen position. See [Controller input](../reference/controller-input.md).

## Windows services

Ordinary widgets call typed `HostServices` APIs. The broker checks their identity,
declared permissions, consent, and current lifecycle before a provider performs
the operation. Provider-native handles and credentials remain on the trusted side.

Full-access application widgets use a separate public bootstrap and ordinary
Windows user authority. Do not describe them as capability-free sandbox workers.
See [Security boundaries](security-and-trust.md).

## Packaging and lifetime

The application release includes the native host, bridge, workers, built-in
packages, and runtime dependencies together. Independently installed widgets
have their own immutable package versions.

Workers start lazily and follow [residency policies](../reference/widget-residency.md).
Hiding the overlay does not necessarily terminate a worker. Pinning and media
also have explicit lifetimes; a hidden main window is not proof that no content
is still being presented.

When changing one boundary, verify the next consumer too. For a UI change, use
[Adding an element](adding-declarative-ui-elements.md). For a provider change,
use [Windows provider architecture](windows-provider-architecture.md).
