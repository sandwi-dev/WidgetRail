# WinUI authoring continuation

Updated 2026-09-29. Scope: SDK/protocol/author guidance audit on
`codex/winui3-frontend`. No worker actions, system settings, provider calls or
candidate restarts were performed for this audit.

## Completed

- Added [the current WinUI authoring guide](../reference/winui-authoring.md),
  with actual native-control mapping, ordinary and indexed C# examples, retained
  state responsibilities and explicit unsupported contracts.
- Extracted both C# examples directly from the Markdown and built them against
  the existing Debug SDK/protocol assemblies. Corrected generic type inference
  in the indexed example during that check. Final build: zero warnings/errors;
  both generated snapshots pass `ViewSnapshotValidator`. All relative links in the new guide resolve.
- Evidence: `artifacts/winui-shell/authoring-audit-20260929/`, including extracted
  source, the small validation executable and `examples-02.binlog`. This proves
  example syntax and protocol validity, not native rendering or physical parity.

## Confirmed contract gaps

1. **Explicit Grid authoring: SDK/protocol work completed in this continuation.**
   The new `UI.Grid`, `GridTrack.Auto/Pixel/Star`, `.Spacing(row,column)` and
   `.InGrid(row,column,rowSpan,columnSpan)` publish native track/cell intent.
   `UI.ResponsiveGrid` remains a separate adaptive-column primitive. Frontend
   geometry and Gallery adoption are owned by the integration lanes; SDK tests
   alone do not establish their readiness.
2. **Some accepted WRSS is not mapped.** `WidgetViewPresenter.Layout.cs` consumes
   flex-grow, bounded lengths and cross-axis `100%`, but no flex-shrink/basis or
   general wrapping flow mapping exists. First-party Games & Apps and Audio
   Mixer still author flex-shrink; other package styles do too. Their mere
   presence does not prove a visible defect, but WRSS validation can pass an
   ineffectual declaration. Prefer porting intended layout to native tracks and
   explicit bounds, then removing obsolete style properties. Do not recreate
   the old flex layout engine. Native-frontend diagnostics for such styles are
   still needed before claiming authoring parity.
3. **Legacy collection diagnostics: shared preflight added 2026-09-30.**
   `WidgetViewPresenter.Plan` rejects CollectionLayout on ordinary Scroll,
   VirtualCollectionWindow, cursor anchors/generations, and near-edge pagination
   actions. The SDK and shared protocol validator still allow these declarations.
   `WinUiPresentationContract` now owns the frontend's existing admission rules.
   `WidgetTestHost.ValidateWinUiPresentation` combines protocol validation with
   those rules for parent/pinned/focus trees. Authors can validate acquired row
   roots separately without eager range demand. Errors identify structural paths,
   safe element IDs and the replacement contract; native rejection preserves the
   current controls/focus. This improves diagnostics without emulating the legacy
   renderer or claiming to validate WRSS/pixels/provider effects. SDK148 and native425
   pass; see `artifacts/winui-shell/author-preflight-20260930/`.
4. **Embedded media plus the modal helper is not general composition.**
   `WidgetView.WithModal` explicitly rejects any view declaring
   EmbeddedMediaSession, and rejects nested modals. This is an existing SDK
   boundary, not an observed regression from this audit. Future media settings
   or confirmation workflows should be designed with the host media owner,
   without removing a session declaration just to bypass validation.
5. **Public collection documentation still describes the native cursor path.**
   `docs/reference/collections.md` originally recommended CursorResource for
   continuous browsing without distinguishing rendering contracts. A WinUI
   branch note now routes readers to the new guide. Resource/provider behavior
   and frontend UI admission must remain clearly distinguished.

## Production collection inventory

Source search covered `src/FirstPartyWidgets`, `samples`, and `templates`.

| Widget | Actual WinUI data/declaration path |
| --- | --- |
| Playnite Home/Library browse | `PlayniteLibraryWidget.RenderCore` passes `_indexedHome` on Library and `_indexedBrowse` on Browse into the presenter. Nonempty live routes select `PlayniteLibraryIndexedHome.Rail` / `PlayniteLibraryIndexedBrowse.Grid`. |
| Spotify queue | A provider-side CursorResource still holds the bounded observation; `SpotifyIndexedQueue.Prepare` freezes it and publishes an indexed source. The normal ready render path uses its IndexedQueue. Removing the resource solely because its name contains Cursor would remove real provider behavior. |
| Spotify search/playlists/details | Discovered-prefix sources in `SpotifyWidget.cs`; no fabricated total. |
| YouTube Video search | `_searchResults` discovered collection in `YouTubeVideoWidget.Search.cs`. |
| YouTube Music browse | `_browse.Collection` is a WidgetIndexedCollection passed to indexed list/grid overloads in StandaloneMusicWidget.Presentation. |
| Games & Apps | Provider cursor acquisition remains data logic; `GamesAppsWidget.Indexed.cs` publishes the visible indexed grid. |
| Full Application reference | Captured immutable document query and indexed ListView source. |

Only Playnite's older fallback presenter functions still declare `.Paginate`
and cursor-anchor metadata in these directories. They are guarded by absent
indexed sources in `PlayniteLibraryPresentation.Render`; ordinary Home/Library
production entry supplies the sources above. This audit did **not** establish a
current first-party live route feeding rejected cursor declarations to WinUI.
Do not report those search hits alone as production crashes. The useful cleanup
is to remove the duplicate fallback presentation after converting its old pure
presenter fixtures to indexed source-backed fixtures, retaining provider state
types until their actual consumers have been reviewed.

## What stays, what can shrink

- Keep the serializable UI boundary for both trust levels, action authority,
  cancellation, source-scoped background retention, logical item identity and
  media lifetime. WinUI controls do not replace these worker/host responsibilities.
- Keep normalized controller ownership and product-specific root exits while
  using native focus/scroll APIs. Default WinUI controller routing alone does
  not implement the shell's Guide/View, pin or dashboard semantics.
- Consolidate remaining legacy layout declarations and duplicated presenter
  fallback branches through deliberate SDK/package migration. Avoid new generic
  compatibility layers for Taffy/renderer-specific geometry.
- A future SDK can expose familiar native Grid and control semantics without
  making workers instantiate WinUI controls or supplying arbitrary XAML.

## Open acceptance

No physical checks were claimed. Remaining acceptance includes per-widget
controller flows and state restoration, real artwork/list/grid frame pacing,
theme/high-contrast and text scaling, media/modal interplay, pinned interaction
and system-provider workflows performed by the user. Native fixtures and compiled
author examples cannot establish absence of visible stutter.

## Explicit Grid implementation checkpoint

- Protocol 64 adds immutable `GridLayoutDefinition` and `GridCellPlacement` to
  existing Grid/child nodes. Track lists copy their input arrays into read-only
  owned lists, so snapshots, fragments, pinned layouts and indexed leases cannot
  retain mutable caller lists. No additional frozen-copy layer is needed.
- Limits: 64 tracks per axis, 0..16384 DIPs for pixel tracks/minimum/maximum/gaps,
  star weights greater than zero through 1024, Auto value exactly 1. Empty axes
  mean one implicit star track. Default placement is row/column 0, span 1;
  overlaps are allowed. Explicit/adaptive mixing and non-direct placement fail.
- Property updates carry layout/placement, including null removal, through the
  existing atomic update validator/materializer. Modifiers preserve responsive
  visibility, action/focus metadata, transitions and theme classes.
- Deferred indexed templates require advertising capabilities before their row
  trees exist. SDK snapshots containing indexed sources now advertise the current
  protocol, including pinned sources and fragment traversal. Shared validation
  still accepts valid older indexed protocols. No eager template rendering or
  manual protocol knob was added.
- Source-generated JSON overwrote omitted init-only defaults. Track/cell JSON
  constructors now supply their specified defaults. Missing axis arrays use the
  existing bounded snapshot-omission restoration path; explicit null remains an
  error. Tests include sparse raw JSON, not only SDK-produced full payloads.
- Validation: 5 new Grid cases (many assertions) plus all existing SDK cases,
  **144/144 passed**, zero build warnings/errors. This includes lazy Grid item
  acquisition, protocol gates, bounds, array ownership, pins/focus fragments,
  modifier composition, and update/reset/removal. Three existing exact-version
  expectations were intentionally updated while adding older-wire acceptance
  assertions. Evidence: `grid-sdk08.binlog`, `grid-sdk-all04.txt` in the audit
  artifact directory. Public API baseline regenerated and reviewed: additive
  Grid authoring symbols only.
