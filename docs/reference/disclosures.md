# Controlled disclosures

Use a disclosure for optional content within the current page. The widget owns
expanded state and domain context; the host owns accessibility and focus recovery.
It is not a modal, a new navigation route, or a persisted host preference.

```csharp
UI.Disclosure("service-details", "Service details", state.DetailsExpanded,
    "details.expand", "details.collapse",
    () => new WidgetElement[]
    {
        UI.Text(state.ServiceSummary, "service-summary"),
        UI.Button("Refresh service", "service.refresh", "service-refresh"),
    });
```

The helper creates stable `service-details.header` and `service-details.content`
IDs. It calls the content factory only when expanded; collapsed children are
absent from the snapshot, layout, navigation, and accessibility tree. Add classes
to the returned stack or use the separate header form when existing layout needs
more control. Keep content spacing explicit when moving rows from another parent.

Handle transitions by setting a value, not toggling it:

```csharp
case "details.expand":
    model.Update(value => value with { DetailsExpanded = true });
    break;
case "details.collapse":
    model.Update(value => value with { DetailsExpanded = false });
    break;
```

Repeated input may arrive before the next snapshot. Explicit idempotent actions
cannot accidentally reverse the first request. Keep domain guards such as a
message ID, selected build, context token, or busy transaction check in these
handlers. The two action IDs must be distinct.

## Separate header and content

`UI.DisclosureHeader(id, label, expanded, contentId, expandActionId,
collapseActionId)` returns a normal `ButtonElement` with disclosure metadata.
It supports ordinary button styling, disabled/busy state, and leading icons.
The host adds an expansion chevron. This permits a header in an existing action
grid with content elsewhere in the same presentation, without rearranging the
page into a new container.

When expanded, declare the referenced content subtree exactly once. When
collapsed, omit it. Header and content must belong to the same input scope and
presentation fragment. Content cannot contain the header, and two headers cannot
claim the same content ID. Use the stack helper unless separate composition is
needed. Multiple noncontiguous regions and virtualized group expansion remain
separate domain contracts.

## Interaction and accessibility

Controller A, pointer activation, and UI Automation use the current admitted
button action. UIA exposes `ExpandCollapse`, with idempotent `Expand()` and
`Collapse()`. Expanded state changes only after the widget publishes it; the host
does not optimistically change state. Accepted state changes raise the native
accessibility property-change event. Disabled, busy, inactive, and retired
headers retain the ordinary action-authority safeguards.

Expansion keeps focus where it is. Collapse returns focus to the retained header
only when its disappearing content contained the focused control. A focused
control that survives elsewhere, focus outside the content, and ordinary refreshes
are preserved. A new explicit focus request takes priority. The same rule applies
inside an entered interactive preview; collapsing does not leave that preview.
Widget authors do not need a focus counter or a collapse-specific focus request.

This declaration requires protocol 100. Normal controls and older snapshots keep
their existing version requirements. See [SDK source](../../src/WidgetSdk/Disclosure.cs)
and [native header](../../src/OverlayFrontend.WinUI/Presentation/WidgetDisclosureButton.cs).
