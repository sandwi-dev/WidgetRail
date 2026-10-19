# Focus presentation retention

## Rich previews with optional interaction

Protocol 94 extends `PresentOnFocus` with optional host-managed interaction.
Put the complete content and its controls in the fragment, then set
`FocusPreviewOptions.EnterButton` to X, Y, LeftStick or RightStick. Do not add a
widget shortcut for entry or publish a second inspection view.

```csharp
var source = UI.Button("Select item", "select", "candidate")
    .PresentOnFocus(UI.Stack("details",
        UI.Text("Item details", "details.title"),
        UI.Button("Apply", "apply", "details.apply")));
var surface = UI.FocusPresentationSurface(source, UI.Text("", "empty"), "preview")
    with { Preview = new(480, 560) { EnterButton = ControllerButton.X, SizeToContentHeight = true } };
```

Focus/hover shows the complete fragment while focus stays on the source. X only
transfers focus into its first available control; re-entry remembers the last
control. Keyboard F6 does the same. Clicking a preview control enters directly.
A on the source retains its primary action. B/Escape closes a nested selector
first, then returns to the exact source while leaving the same panel visible.
Entry and return dispatch no widget action, and preserve controls, geometry and
reading position. Clicking another source changes the preview normally.

The host renders native controls once, gates their input until entry, and routes
actions through the original displayed frame. Indexed preview actions additionally
validate the exact lease, item key, control and declared action at the host and
worker. Source removal, scope/query retirement, hidden anchors and suspension
revoke the panel. No stale action is replayed against a replacement item.

Interactive fragments support bounded layout, rich text, images, buttons,
action surfaces, selectors and scrolling. Nested scopes, embedded services,
collections and nested previews are not supported. Existing fragment node/depth
limits apply to inline fragments; deferred content has its own bound below. Keep IDs stable across busy updates; mark
pending controls busy rather than removing or disabling them. For catalogs, resolve content on demand rather than attaching every preview to
the row batch.

Leave `EnterButton` unset for read-only previews such as Undo/Redo. Their passive
content validation and focus behavior remain unchanged. Ordinary blocking dialogs
continue to use `WithModal`; interactive previews do not wrap that API.

## On-demand preview sources

Protocol 95 lets ordinary and indexed rows carry a lightweight preview key.
Declare a `WidgetFocusPreviewSource<TContext>` once with `CreateFocusPreviewSource`.
Its `Resolve(context, key, cancellationToken)` returns a complete `WidgetElement`;
optional `OnAction` and `ResolveArtwork` callbacks receive that same captured
context and key. Use immutable context data. Call `Publish(newContext)` when the
build, query, selected target or other meaning of the data changes.

```csharp
// Create once in the widget; use your immutable domain model as context.
previews = CreateFocusPreviewSource("item-previews", catalog,
    new WidgetFocusPreviewOptions<Catalog>
    {
        Resolve = (context, key, token) => LoadPreviewAsync(context, key, token),
        OnAction = (context, key, action, token) => ApplyAsync(context, key, action, token),
    });
var row = UI.Button("Select item", "select", "candidate").PresentOnFocus(itemKey);
var surface = UI.FocusPresentationSurface(row, UI.Text("", "empty"), "preview")
    with
    {
        PreviewSource = previews.Descriptor,
        Preview = new(480, 560) { EnterButton = ControllerButton.X, SizeToContentHeight = true },
    };
```

After focus/hover settles, the host requests one key and renders only the resolved
panel. It never fetches a row batch's previews. X enters those existing controls;
B returns without requesting content again. Leave `EnterButton` unset for a
read-only summary. Both forms support rich text. Small inline previews such as
Undo/Redo can continue using `PresentOnFocus(WidgetElement)`.

The limits serve different purposes:

- Normal views retain their 2,048-node bound; inline preview fragments retain 64.
- A deferred preview has an independent 256-node bound. Nested scopes, lists,
  previews and service surfaces remain prohibited.
- The presenter retains at most four resolved semantic leases and one pending
  preview request. Cached data is reused only while its context and owner remain
  current. Native controls exist only for the active panel.
- Each source permits one actual provider operation. Cancellation-ignoring work
  holds that slot until it really completes; it cannot create unbounded work.
  The provider timeout defaults to five seconds, configurable from 250 ms to
  ten seconds. Honor cancellation and avoid blocking operations.

Source changes cancel pending requests. Late results are discarded; replacement
contexts, lifecycle suspension and disposal retire the cached leases. Errors leave
the ordinary page usable and do not repeatedly retry while focus remains unchanged.
Preview actions use the captured content lease and original displayed frame;
forged actions and retired contexts are rejected. This reuses indexed semantic
lease transport, but previews are not hidden collections or actionable fake rows.

## Anchored dialogs

Use the existing `WidgetModal` for a blocking contextual workflow. Its optional
`AnchoredPresentation` places the panel beside an element or an exact indexed item:

```csharp
view.WithModal(new WidgetModal("inspect", "Details", details,
    "inspect.confirm", "inspect.close")
{
    AnchoredPresentation = new(new PresentationAnchor(ElementId: "candidate.inspect")),
});
```

The default requested size is 360 × 280 logical DIPs. Width accepts 160–960 and
height 96–800; the host clamps both to the widget viewport. `Placement` can prefer
Right, Left, Below or Above; Auto starts on the right. The host tries other sides
when needed. Missing, offscreen or stale anchors use a bounded centered panel.
An indexed anchor uses `IndexedCollectionFocusTarget`, including its source,
query generation, occurrence key and index. Resolving it never fetches pages,
scrolls the list or selects an item.

This remains a modal: background controls cannot receive input, B/Close follows
the existing dismiss action, and focus returns to the remembered opener or a valid
fallback. It does not assign a new global Inspect shortcut. Authors expose an
ordinary action with a meaningful label, preserve its identity, and publish the
background view again on dismissal. Ignore asynchronous results whose inspection
request or widget lifetime has ended; a late result must not publish a new modal.

Anchored inspection requires presentation protocol 92. Omitting the option keeps
the existing centered modal and its existing protocol requirements.

Set `SizeToContentHeight = true` on `AnchoredPresentation` or `FocusPreviewOptions`
to fit the initial content instead of reserving the full preferred height. This
opt-in requires protocol 93; existing constructors and fixed-size defaults are unchanged:

```csharp
new AnchoredPresentation(new("candidate.inspect"), Width: 560, Height: 440)
{
    SizeToContentHeight = true,
};
```

Width stays at the bounded preferred width. Height is measured once on opening,
with a 96-DIP minimum and the authored `Height` as its upper bound, clamped to the
available widget space. Existing authored dialog minimums still apply. Changing
the viewport or text scale remeasures; late updates to the same open source do not
resize the panel. A new source or reopened inspection receives a new measurement.
Supply the useful summary before showing the surface; loading a large replacement
later will not expand it. Interactive inspection retains its ordinary scrolling;
passive content is clipped at the bound and still needs an explicit Inspect route.
The host uses bounded native measurement and retains the existing focus, lease and
lifetime authority. This sizing option does not fetch data or create another engine.

## Inline focus presentations

`BackgroundSurfaceElement` and `FocusPresentationSurfaceElement` share a native,
per-surface source resolver. Set `RetainLastPresentation` in C# to choose whether a
surface keeps its last valid focused source when focus moves elsewhere:

```csharp
UI.FocusPresentationSurface(content, defaultPresentation, "game-summary")
    with { RetainLastPresentation = true };
```

The default policies are: focused background artwork retains its
source; focus-presentation content uses the default when focus leaves it. Explicit
policy declarations require presentation protocol 56. Playnite enables retention
for its Home summary, matching its focused background artwork.

- A focused item with a declaration replaces that surface's selection.
- Tray, modal, and unrelated controls without a declaration retain the last source
  when enabled. Nested surfaces remain independent ownership boundaries.
- The host resolves the source from the current view, rather than copying old text
  or artwork declarations. Refreshed content is reflected immediately.
- A missing source, removed declaration, recycled item key, hidden responsive
  branch, or removed surface falls back to the current authored default. There is
  no resurrection when an evicted source later returns without receiving focus.
- Leaving the viewport does not invalidate an item still present in the view tree.
  Cursor overlap preserves identity; cursor eviction or filtering out the item
  clears it. Retention never fetches pages, changes scroll offsets, or restores a
  cursor position.
- Runtime/presentation authority changes and widget retirement cannot inherit old
  selections. Theme, scale, and render-target changes do not themselves change a
  valid semantic selection.

Retained presentation is view-only. It grants no input, action, focus, or navigation
authority. Measurement, painting, accessibility projection, image protection, and
incremental invalidation use the same selection. Compositor and fallback image
paths consume that selection; their bounded decode caches and crossfade textures
remain separate rendering concerns.

## WinUI background continuity

A retaining background keeps its last decoded image until another image is ready
for that same declared surface. Losing a source still retires its semantic selection,
lease and pending requests, as described above. An empty default, missing artwork
or a pending replacement does not clear the already painted background. A nonempty
authored default can replace it after decoding. Focus fragments continue to resolve
the current default; this pixel-retention rule applies only to backgrounds.

Surface identity is scoped to the widget/runtime/presentation owner, declared input
scope, element ID, collection-item ancestry and enclosing presentation surfaces.
Keep a shared background surface's ID and scope stable across sections; change its
foreground content underneath it. Playnite Home and Library declare the same
`playnite-library.cinematic` surface for this purpose. Different surface IDs are
independent, even when they occupy the same position. Nested surfaces never replace
their parent's retained image. Opening a modal with a separate input scope preserves
the declared parent surface; retiring the parent does not transfer its image to the
modal. Removing a surface or replacing its authority releases its retained image.

Set `RetainLastPresentation = false` when an empty selection should clear background
pixels. This is a declaration policy, not a global last-background cache; no input,
focus, data-fetch or navigation authority survives solely because pixels remain.

When a navigator supplies a different input scope per page, apply that scope to
the foreground subtree **inside** a shared background, not to its enclosing root.
Keeping the element ID alone does not preserve ownership when its inherited scope
changes. Playnite uses an explicit stable `playnite-library.presentation` scope
around the cinematic surface and keeps the navigator's page scopes underneath it.
This preserves separate page focus/scroll/action authority while artwork continues.

## Anchored passive previews

Existing `PresentOnFocus` content can appear beside its source instead of taking
space in an inline inspector. Set `Preview` on its existing enclosing surface:

```csharp
UI.FocusPresentationSurface(
    UI.Button("Audio setting", "inspect-audio", "audio")
        .PresentOnFocus(UI.Text("Current output and volume", "audio.preview")),
    UI.Text("", "preview.empty"),
    "settings.preview") with
{
    Preview = new FocusPreviewOptions(Width: 360, Height: 240)
}
```

The default is settled focus and mouse hover after 350 ms. `OnFocus` and `OnHover`
can independently disable either source; at least one must remain enabled.
`DelayMilliseconds` accepts 0–1500, width 160–960 DIP and height 96–800 DIP. The
host can shrink and flip the panel to keep it within the widget. Long content is
clipped; put complete information and controls in the optional interactive state
described above. Passive fragments retain their existing 64-node,
8-level limits and cannot contain focusable controls, actions or scrolling.

Preview activation does not invoke the widget or calculate data. Reuse content
already loaded for the source. Updating that content does not restart the settling
deadline. Only one preview is visible; the nearest enclosing surface owns it.
Pointer hover does not select a row or move focus. Controller input resumes the
focused source, including at a navigation boundary.

Anchored previews deliberately do not support `RetainLastPresentation`: they hide
when their source is removed, leaves the viewport or loses current occurrence
authority. Suspension, input handoff, modal entry and unloading revoke them and
cancel their pending deadline. Indexed previews inspect resident rows only, without
fetching, retaining or scrolling. They share placement geometry and scaled host
presentation layers with interactive inspection. Read-only previews never take
focus or pointer input; enterable previews accept a click to request entry.
Leaving `Preview` unset preserves existing inline presentation behaviour.

## Keyboard dismissal

Ordinary and anchored widget modals support keyboard Escape when their active
scope declares a pressed-B action. The host dispatches that same action through
the existing presentation admission path. Native child controls receive Escape
first; browser keyboard focus and transient native controls retain their own
handling. A repeated key or pending request cannot dismiss another modal instance,
and late completion cannot block or release a newer modal's dismissal guard.
Passive focus previews do not consume Escape or change the selected action.
