# WinUI widget transport and real Playnite connection

Status: implementation guidance based on current source, 2026-09-27. No provider,
worker or hardware process was launched for this investigation.

## Reuse the service boundary, not the native renderer

The smallest legitimate connection already exists:

```text
WinUI frontend
  -> WidgetPresentationSession (managed asynchronous client)
  -> WidgetBridge.exe (owned child process, private pipe)
  -> WidgetProcessClient / broker companion
  -> admitted widget worker/application
```

`src/WidgetPresentationSession` already implements catalog discovery, presentation
establishment, lifecycle, action/controller admission, asynchronous refresh,
artwork resolution and typed failure state using the existing bridge framing.
`OwnedBridgeProcess` now owns bridge launch and bounded shutdown. Consume this
facade; do not spawn `OverlayHost.exe` or connect
directly to a worker to bypass catalog/consent/admission.

### Important trust correction

The current `samples/PlayniteLibraryWidget/manifest.json` declares
`full-trust-application-v1` with `payload/PlayniteLibraryApplication.exe` and no
broker permissions. `Application/Program.cs` uses `WidgetApplicationBootstrap`
with `PlayniteLibraryApplicationService`, its existing private state and
`PlayniteBridgeClient.CreateDefault()`. It is **not presently a sandboxed worker**.
Do not change its trust model simply to label this milestone sandboxed.

Prove the real Playnite UI through its actual full-trust out-of-process service,
and prove sandbox parity separately using an admitted `dotnet-worker` package
(for example, the existing Clock manifest) and a paged sandbox fixture. Both
arrive at WinUI as the same validated declarations and semantic actions.

## What can be connected immediately

1. Resolve the candidate's trusted installation root. The current backend layout
   is `runtime/Bridge/WidgetBridge.exe`, root `widget-catalog.json`, and generic
   worker host `runtime/WidgetWorkerHost/WidgetWorkerHost.exe`.
2. Launch the bridge with an independently random per-process pipe name, using
   `ProcessStartInfo.ArgumentList`, `UseShellExecute=false`, `CreateNoWindow=true`,
   an explicit working directory, and continuously drained bounded diagnostics.
   Required arguments are `--host-pipe` and `--catalog`; also supply the owned
   bridge session generation, connection deadline, and deliberately chosen
   `--settings-root` / `--installed-catalog-root` for the candidate.
3. Retain the child process handle and observe exit alongside the connection
   attempt. `WidgetPresentationSession.ConnectAsync` performs hello correlation
   and begins the receive loop. Do not attach to the running native overlay's
   bridge: the server supports one client and session disposal sends Stop.
4. Subscribe to state/diagnostic events, then `ListWidgetsAsync()`. Select the
   catalog descriptor by the actual Playnite ID
   `widgetrail.samples.playnite-library`, not by UI display text. If absent, show
   the normal not-installed/admission state; do not substitute a generated grid.
5. `GetTarget(id)` then `EstablishPresentationAsync(target, lifecycle)` returns a
   `WidgetPresentationFrame`: descriptor, validated `ViewSnapshot`, resolved
   style data and exact presentation authority. Apply view-model/control updates
   on WinUI's dispatcher. Event callbacks can occur on transport/background
   threads; subscribers must post and return promptly.
6. Request artwork for realized items and active presentation slots through
   `ResolveArtworkAsync`, retain the returned authority, decode to WinUI image
   objects at appropriate dimensions, and bind only to the current logical item.
7. Send semantic activation with `SendActionAsync(authority, WidgetActionEvent)`
   or widget controller shortcuts with `SendControllerInputAsync(authority,
   ControllerInputEvent)`. Host directional focus should stay in WinUI rather
   than turning every focus move into worker UI regeneration.
8. On hide/switch, send the appropriate lifecycle without disposing the global
   bridge. On application shutdown, dispose the session (Stop handshake), await
   child exit with a deadline, and retire only the owned child if necessary.
   Never kill another host's bridge or unrelated widget processes by name.

The native launch reference is `OverlayHost/WidgetBridgeClient.cpp::Launch`.
It currently constructs a PID/tick-count pipe name; use actual randomness for
the new launcher rather than repeating the README's unsupported claim that this
old name is random. `WidgetBridge/Program.cs` owns catalog/settings/provider
composition and `WidgetBridgeServer.RunAsync` owns the server endpoint.

## Authority and isolation to retain

`WidgetPresentationAuthority` includes widget ID, runtime generation,
presentation generation, local session generation, instance ID, snapshot
sequence and active input-scope ID. `ValidateAuthority` requires exact current
authority; `ValidateTarget` requires the current catalog descriptor/revision.
Recycled controls must not retain action closures for an older authority.
Reject stale actions rather than relabeling an old click as coming from a new
snapshot. For a legitimate current gesture, resolve the current logical item and
its current action from the latest committed frontend model.

`SendActionAsync` returns `WidgetOperationAdmission`; accepted admission is not
proof the asynchronous operation finished successfully. Observe subsequent
failure/state/host-effect publications. `SendControllerInputAsync` also checks
snapshot sequence and active scope. The visible WinUI modal/page scope must match
the authority sent to the service; hiding a control does not revoke an already
captured action on its own.

Keep `BridgeCatalog` package integrity, execution-trust derivation, runtime and
presentation fingerprints. `WidgetBridgeServer.CreateWidgetClient` derives the
worker isolation policy from trusted configured metadata. It instantiates
`WidgetProcessClient` with bounded startup/request deadlines, residency leases,
read-only content authority, and a broker companion for sandboxed workers.
`WidgetRuntime/WidgetProcessClient.cs` creates the AppContainer when required,
uses its scoped pipe, verifies the connected worker PID, and retains teardown
ownership. Full-trust community applications use the corresponding explicit
policy and do not receive the sandbox broker companion. Keep these decisions out
of XAML and widget-supplied UI declarations.

The bridge's frontend pipe uses `PipeOptions.CurrentUserOnly` and a correlated
hello. Current source does **not** prove peer PID binding/authentication between
frontend and bridge; hello's client name is not a credential. Do not describe
this as cryptographic authentication. The new owned launcher should pin the
expected bridge process identity when connecting and the service should reject
an unexpected frontend where the trust model requires it. This is distinct from
the existing verified worker/broker process boundary.

## Facade gaps that must not be mistaken for finished migration

| Current code | Required direction |
|---|---|
| Establishment uses `PresentationUpdateCapabilities.None`, sequence zero and ordinary checkpoint | Useful for first real connection, not the final lazy collection/update contract. Introduce direct logical data/template/change contracts and a clean negotiated version. |
| Snapshot carries `BridgeNodeRenderStyles` and old `ViewNode` graph | Feed a deliberately bounded initial adapter; replace renderer-specific style/layout semantics with semantic WinUI resources and components. Do not recreate Taffy, native preparation or damage painting. |
| `HostEffect`, `CatalogChanged`, `AppearanceChanged` become diagnostic strings | Add typed authoritative event APIs and appropriate refresh/dispatch handling. Otherwise launching a game, theme changes and catalog updates can silently lose visible behavior. |
| No managed embedded-media/window-preview/package-icon or settings control APIs | Extend the existing managed facade with the already supported bridge request contracts as each feature is ported. Do not send raw JSON from individual controls. |
| Hello does not advertise `WindowPreviews` | Negotiate this when actual supported preview presentation exists; do not advertise a feature merely because transport types compile. |
| Artwork request omits runtime/presentation generations and demand ID | Use the existing generation/demand fields; correlate completion to a unique demand, not a FIFO keyed only by widget/handle. |
| Caller cancellation removes local pending artwork but does not cancel server-side work | Define demand withdrawal/consumer lifetime explicitly for realized items and retained presentation slots. Late completion must not satisfy a replacement demand. |
| Shared contracts formerly resided in the `WidgetBridge` executable | Extracted to `WidgetBridge.Contracts`; the frontend client now references only protocol/SDK/style contracts. Keep executable providers behind the process boundary. |

The artwork issue is concrete: `ResolveArtworkAsync` sends
`new BridgeArtworkRequest(widgetId, artworkHandle)` and stores a pending queue
under `(widgetId, artworkHandle)`. `HandleArtwork` dequeues that queue and checks
the pending authority, but does not correlate the completion to an echoed unique
demand. An old canceled request and a replacement for the same handle need stronger
identity. `BridgeProtocol.BridgeArtworkRequest` and the server already support
paired runtime/presentation generations and `DemandId`; reuse that direction.

`BridgePresentationTransport.RequestAsync` intentionally retains sent requests
until their correlated response even if the caller stops waiting. Cancellation
does not mean cancellation of a provider operation. For the new collection API,
document that distinction and implement real demand cancellation where needed
instead of depending on local task cancellation to relieve backend work.

## New WinUI collection contract versus reused services

Keep the worker/service boundary and provider/business logic. Redesign collection
declarations around stable keys, logical item data, templates, directional page
requests, revisions and explicit lifetime. WinUI receives an observable data
source and realizes UI through standard virtualized controls. The cursor producer
remains responsible for provider pagination; the frontend owns viewport/focus
policy and requests data from current demand. Preparation of old renderer trees
must not be the new collection's admission protocol.

Do not convert the real Playnite response to just title/artwork and call the
port complete. Preserve action availability, completion status, game options,
details pages, input scopes, focus group policy, background/presentation slots,
search/category/sort state, shortcuts, hints, theme behavior and launch effects.
Retained background metadata must come from logical item identity independent of
the realized item container. This is UI contract work, not a reason to rewrite
the Playnite REST client or credential/state store.

## First integrated evidence

- Log the owned bridge PID, package identity/trust, worker PID, presentation
  generations and runtime startup success without logging secrets or raw bodies.
- Render the actual Playnite worker response and artwork, open actual details,
  close back to the same item and viewport, and exercise one non-destructive
  semantic action and its response. A live launch action needs its normal user
  trigger; transport tests can use safe controlled actions.
- Exercise a real sandbox package and a delayed paged sandbox fixture through
  the same frontend facade. Assert actual AppContainer/PID admission rather than
  inferring sandbox status from an API name or out-of-process execution.
- Cover stale actions, worker restart, catalog replacement, hide/reopen, late
  artwork, rapid focus changes, modal scope changes and provider failure.
- Measure request-to-visible timelines through worker, bridge, dispatcher and
  actual WinUI frame presentation. Fixture snapshot agreement does not prove
  responsive controller behavior.

Existing evidence locations: `tests/WidgetPresentationSession.Tests` covers the
managed facade and transport; the managed-session parity cases in
`tests/WidgetBridge.Tests` cover real production bridge and sandbox/full-trust
supervisors. Inspect and run the relevant cases when implementing changes; this
research did not rerun them or claim they cover WinUI.

## Source map

- `src/WidgetPresentationSession/PresentationContracts.cs`
- `src/WidgetPresentationSession/WidgetPresentationSession.cs`
- `src/WidgetPresentationSession/BridgePresentationTransport.cs`
- `src/WidgetBridge/Program.cs`, `BridgeProtocol.cs`, `BridgeCatalog.cs`
- `src/WidgetBridge/WidgetBridgeServer.cs`, `BridgeClientRegistry.cs`
- `src/WidgetRuntime/WidgetProcessClient.cs`, `WindowsAppContainer.cs`
- `src/WidgetRuntime/WidgetProcessOptions.cs`, `WidgetWorkerServer.cs`
- `src/WidgetSdk/Widget.cs`, `WidgetCursorResource.cs`
- `samples/PlayniteLibraryWidget/manifest.json`, `Application/Program.cs`
- `src/OverlayHost/WidgetBridgeClient.cpp`
