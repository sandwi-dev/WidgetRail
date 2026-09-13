# Controller components

Use shared components when they already express the interaction you need.
They provide consistent focus, actions, and theme classes across widgets.

## Choose a component

| Component | Good use |
|---|---|
| Button | One named action |
| Slider | A bounded numeric adjustment |
| Settings row | A setting's label, explanation, and control |
| Action surface | A custom card with one interaction owner |
| Poster tile | Artwork and a primary content action |
| Action sheet | Secondary actions without cluttering the main page |
| Controller hint | Explain a shortcut without adding a focus stop |

The complete factories live in [`ModernComponents.cs`](../../src/WidgetSdk/ModernComponents.cs),
[`TileComponents.cs`](../../src/WidgetSdk/TileComponents.cs), and
[`UI.cs`](../../src/WidgetSdk/UI.cs). SDK Gallery shows them in the overlay.

## Keep one interaction owner

An action surface can contain an icon, artwork, and labels. Its children describe
the card; they are not extra buttons hidden inside it. Give a secondary action
its own control or menu instead.

Generated composites use derived child IDs. Keep the parent ID stable and avoid
reusing it for unrelated controls. This protects navigation and update matching.

## Sliders

Define the value, minimum, maximum, and step. Use a label and visible value so
the control remains understandable without relying on its thumb position alone.

Let the shared slider own adjustment and its presented value. Do not implement
a competing left/right handler or a second optimistic thumb state in the widget.
The host reconciles the displayed adjustment with the acknowledged value.

## State and appearance

Focus means “this is the current target”. Selection means “this is chosen”.
Busy means work is pending; disabled means activation is unavailable. Do not
represent these four states with one boolean.

Use the shared theme tokens and test focus against bright artwork. Controller
hints should remain readable without depending on the background image's color.

See [Styling](../developers/styling.md), [Controller input](controller-input.md),
and [Accessibility](accessibility.md). The [Audio Mixer source](../../src/FirstPartyWidgets/AudioMixerWidget/)
is an example of settings rows and value controls in a real widget.
