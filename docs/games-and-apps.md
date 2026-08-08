# Games & Apps reference

Status: durable authority-scoped curation and trusted close-on-confirmed-launch
implemented. Packaged hands-on verification and broader sources remain.

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

- Entering Visible or Interactive reads package-private state and resolves only
  the SavedIds the user already added. It does not enumerate the broad catalog.
  The bounded first catalog page loads only after **Add applications**.
- The default Library contains only entries the user has added. **Add
  applications** opens a nested Catalog backed by a horizontal controller
  Scroll; A toggles the focused entry in/out of the Library and B returns.
  Library X removes the focused entry.
- Both surfaces use stable hashed UI IDs; widget snapshots contain only display
  names, conservative kinds, broker-issued short-lived AppIds, and
  authority-scoped SavedIds. Provider launch tokens stay inside the host and
  last only for the current provider snapshot.
- Library Left/Right selects a card. A launches that exact card only while the
  widget is Interactive. After confirmed provider success, that item moves to
  the front and the new order is persisted; failure preserves order and
  actionable focus.
- Curation, recent-first order, and selected SavedId are stored through
  `HostServices.PrivateState` with compare-and-swap conflict handling. On a new
  worker or host run, the widget resolves the authority-scoped SavedIds to fresh
  short-lived AppIds and silently drops registrations that no longer exist.
- A successful launch requests `CloseOnConfirmedSuccess`. The broker emits the
  host effect only after the exact trusted provider call succeeds, and the
  native host accepts it only for the current widget and runtime generation.
  Enqueueing, timeout, stale generation, denial, or failure leaves the overlay
  visible with focus and an actionable status.
- Catalog `Load more` requests another bounded page and moves selection to the
  first newly appended item. The widget retains at most 512 items.
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

// Save SavedId—not AppId—in package-private state.
var savedIds = page.Items.Select(item => item.SavedId).Take(64).ToArray();

// On the next worker/host run, reconcile durable IDs to current launch tokens.
var currentItems = await HostServices.AppLibrary.ResolveSavedAsync(
    savedIds,
    cancellationToken);

await HostServices.AppLibrary.LaunchAsync(
    currentItems[0].AppId,
    WidgetAppLaunchOverlayBehavior.CloseOnConfirmedSuccess,
    cancellationToken);
```

`GetPageAsync` accepts offsets from 0 through the bounded library and a page
size of 1–64. It returns `WidgetAppLibraryPage`, containing sanitized
`WidgetAppLibraryItem` records and an optional next offset. Each item has a
short-lived `AppId` for launch and a durable `SavedId` for private state.
`ResolveSavedAsync` accepts at most 64 unique SavedIds, refreshes the provider,
preserves request order, and omits apps that are no longer available. A
SavedId is scoped by a persisted host key plus the authenticated publisher and
package IDs; it remains stable across worker/host restarts and package updates,
but it cannot be correlated or reused by another widget package. The widget
instance ID is intentionally not part of this durable authority.

`LaunchAsync` accepts only the short-lived opaque `AppId` returned by a current
page or resolution. Widgets must never persist AppId. Widgets never
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
- exposes a separate stable private provider fingerprint only to the host
  broker, which derives non-reversible authority-scoped SavedIds with HMAC; and
- keeps shortcut paths, raw provider identities, arguments, host key, and file
  fingerprints out of widget IPC.

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
- Curation is durable for the package, but deduplication across launchers,
  authoritative game classification, source-aware grouping, and broader source
  reconciliation remain tracked as [GBA-033](known-issues.md).
- The provider does not extract or publish application artwork or icons. The
  current card uses a host semantic Play glyph.
- The public kind enum supports Unknown, Application, and Game, but the real
  Start Menu provider deliberately reports every current entry as Application.
  Filename/path guessing is not authoritative game classification.
- There is no search, grouping, install/uninstall,
  game history, foreground switching, running-program capture, file picker, or
  arbitrary executable/path launch.
- Catalog refresh is currently lazy/cached for the provider lifetime; broader
  source-change observation and packaged performance evidence remain open.
- Responsive intrinsic-height and Catalog-only loading regressions are green;
  the refreshed packaged visual/controller pass remains tracked as
  [GBA-038](known-issues.md).

## Executable evidence

Focused tests cover empty curated Library, Catalog-on-demand and empty-Catalog
recovery, nested Catalog add/remove, B return, horizontal controller focus,
confirmed recent-first ordering, failed-launch order retention, durable
SavedIds across fresh worker instances, opaque selected launch, bounded paging,
sanitized failure states, and manifest/GBSS validation. Provider tests cover lazy refresh,
sanitization and bounds,
opaque-ID lifetime, payload privacy, exact shortcut revalidation, constrained
Shell invocation, sanitized errors, cancellation, and a non-mutating real Start
Menu scan. Broker, SDK, bridge, Settings, and first-party conformance suites
cover separate read/launch consent, lifecycle denial, invalid payload/backend
data, transport mapping, permission copy, packaged AppContainer startup, render,
and a simulated launch.

This is local automated evidence, not a public-release claim. A packaged
controller/visual playtest, broader Windows catalog matrix, icon pipeline, and
authoritative game sources remain required.
