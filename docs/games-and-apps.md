# Games & Apps reference

Status: implemented local first-party slice; packaged hands-on verification and
broader catalog sources remain

Games & Apps replaces Recent Apps in the bundled overlay catalog. It is a
manifest-backed first-party package that uses the public SDK, generic
`WidgetWorkerHost`, package-specific capability-free AppContainer, authenticated
broker, normal lifecycle, and declarative renderer. It is not a privileged
native shell panel.

The old Recent Apps project remains useful historical coverage for the
read-only foreground-activity API, but it is no longer one of the packaged
dashboard widgets. Games & Apps lists installed launch registrations rather
than applications the user happened to foreground.

## Current user experience

- Entering Visible or Interactive starts one bounded first-page load.
- Applications render in a horizontal controller Scroll with stable hashed UI
  IDs; widget snapshots contain only display names, conservative kinds, and
  broker-issued opaque app IDs.
- Left/Right selects a card. A launches that exact card only while the widget
  is Interactive.
- A successful launch currently leaves the overlay open. The planned product
  behavior is to close only after the trusted provider reports success for that
  exact request; enqueueing, timeout, denial, or failure must leave the overlay
  visible with focus and an actionable status.
- `Load more` requests another bounded page and moves selection to the first
  newly appended item. The widget retains at most 512 items.
- Permission denied, lifecycle denied, provider unavailable, healthy empty,
  and generic failure states remain controller reachable and provide an
  explicit retry action.
- Moving to Background cancels the active load lifetime. The manifest uses
  `unload-after-idle` with a 120-second idle interval; the bridge can recreate
  the worker from its last validated snapshot when it is needed again.

## Public SDK contract

Authors declare the read grant when the library is core functionality and the
launch grant only when opening a selected app is an optional feature:

```json
{
  "permissions": ["system.apps.library.read.v1"],
  "optionalPermissions": ["system.apps.library.launch.v1"]
}
```

The corresponding public service is `HostServices.AppLibrary`:

```csharp
var page = await HostServices.AppLibrary.GetPageAsync(
    offset: 0,
    limit: 32,
    cancellationToken);

await HostServices.AppLibrary.LaunchAsync(
    page.Items[0].AppId,
    cancellationToken);
```

`GetPageAsync` accepts offsets from 0 through the bounded library and a page
size of 1–64. It returns `WidgetAppLibraryPage`, containing sanitized
`WidgetAppLibraryItem` records and an optional next offset. `LaunchAsync`
accepts only the opaque `AppId` returned by a current page. Widgets never
receive a path, `.lnk` filename, target executable, command line, AUMID,
package identity, launcher identity, PID, or window handle.

Read requires Visible or Interactive. Launch is a control operation and
requires Interactive; it cannot use dashboard-gesture authority. Declaration,
user consent, authenticated package/publisher/instance identity, current
lifecycle, payload validation, and provider validation are independent gates.

## Trusted Windows provider boundary

The current provider scans only the current-user and all-user Start Menu
Programs folders. It:

- enumerates at most 4,096 `.lnk` candidates, to a maximum directory depth of
  16, without following reparse points;
- accepts bounded shortcuts whose resolved target is an `.exe` or `.com`;
- sanitizes display names, deduplicates the same trusted target identity, and
  publishes at most 512 entries;
- assigns random opaque IDs that stay stable only while that registration
  remains in the current provider snapshot; and
- keeps shortcut paths, target identities, arguments, and file fingerprints
  inside the trusted provider.

Launch does not trust a stale opaque-ID lookup by itself. Immediately before
calling the Windows Shell, the provider re-enumerates and requires exactly one
registration with the same scope, target identity, full shortcut path, and
shortcut-content fingerprint. It then invokes only the Shell `open` verb on
that exact fully qualified `.lnk`, with no supplied arguments, working
directory, elevation verb, or window delegation. Missing, moved, changed,
duplicated, or unknown registrations fail as `app_not_found`; platform and
Shell failures are sanitized before returning to widget code.

## Honest limitations

- Discovery is Start Menu `.lnk`-only. AppsFolder/UWP registrations, Steam,
  Xbox, Epic, GOG, and other launcher libraries are not integrated.
- A curated combined Games & Apps library—deduplication across launchers,
  authoritative game classification, favorites/order, and close-after-
  correlated-success—is tracked as [GBA-033](known-issues.md).
- The provider does not extract or publish application artwork or icons. The
  current card uses a host semantic Play glyph.
- The public kind enum supports Unknown, Application, and Game, but the real
  Start Menu provider deliberately reports every current entry as Application.
  Filename/path guessing is not authoritative game classification.
- There is no search, favorites, grouping, install/uninstall, game history,
  foreground switching, or arbitrary executable/path launch.
- Catalog refresh is currently lazy/cached for the provider lifetime; broader
  source-change observation and packaged performance evidence remain open.

## Executable evidence

Focused tests cover first-page lifecycle loading, horizontal controller focus,
opaque selected launch, bounded paging, sanitized failure states, and manifest/
GBSS validation. Provider tests cover lazy refresh, sanitization and bounds,
opaque-ID lifetime, payload privacy, exact shortcut revalidation, constrained
Shell invocation, sanitized errors, cancellation, and a non-mutating real Start
Menu scan. Broker, SDK, bridge, Settings, and first-party conformance suites
cover separate read/launch consent, lifecycle denial, invalid payload/backend
data, transport mapping, permission copy, packaged AppContainer startup, render,
and a simulated launch.

This is local automated evidence, not a public-release claim. A packaged
controller/visual playtest, broader Windows catalog matrix, icon pipeline, and
authoritative game sources remain required.
