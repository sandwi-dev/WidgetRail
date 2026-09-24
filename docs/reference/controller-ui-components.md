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
| Controller glyph | Show a button or stick symbol beside navigation or custom content |

The complete factories live in [`ModernComponents.cs`](../../src/WidgetSdk/ModernComponents.cs),
[`TileComponents.cs`](../../src/WidgetSdk/TileComponents.cs),
[`UI.cs`](../../src/WidgetSdk/UI.cs), and
[`ControllerGlyph.cs`](../../src/WidgetSdk/ControllerGlyph.cs). SDK Gallery shows them in the overlay.

## Controller symbols and hints

```csharp
UI.ControllerHint(ControllerButton.Y, "Refresh", "refresh.hint");
UI.ControllerGlyph(ControllerButton.LeftTrigger, "section.previous", "Previous section");
UI.ControllerHint(ControllerPrompt.RightStickMove, "Scroll", "scroll.hint");
```

The native host draws bundled Kenney Input Prompts and selects Xbox or PlayStation
symbols from the last active controller family. Symbols remain non-focusable:
these components document controls without registering shortcuts. A hint may
still own a container context menu, or be presentational content inside an
action surface. No font files need to be included in widget packages.

Use `ControllerButton` for actual buttons. `LeftStick` and `RightStick` in that
enum mean **pressing** the stick. Use `ControllerPrompt.LeftStickMove` or
`RightStickMove` for movement, and `DPad`, `DPadHorizontal`, or `DPadVertical`
for directional navigation. `ControllerPrompt` is presentation-only and also
supports `Guide`; it does not extend the set of widget-bindable buttons.

The glyph-only factory accepts optional accessible context, such as "Previous
section". Without an override, the host supplies the controller-specific name
(for example, "A button" or "Cross button"). Action labels stay ordinary text.

Style `.wrail-controller-glyph` for all symbols, `.wrail-controller-hint__key`
for symbols in labeled hints, and `.wrail-controller-hint__label` for labels.
The default glyph size is 24 logical pixels; a theme can set
`--controller-glyph-size` or override `font-size` on a specific glyph. Color,
opacity, padding, and container decoration are themeable. Set
`--controller-glyph-color` to change symbol ink independently of labels; the
built-in themes use a subdued accent. A stretched layout box keeps the symbol
centered at its font size instead of enlarging it to fill the row. Glyphs honor text
scaling and high contrast. Font family, weight, letter spacing, and line height
do not reshape symbols; those properties continue to apply normally to labels.
The host centers each symbol's visible ink and provides a readable drawn fallback
if a font cannot be loaded. Kenney's outlined buttons and triggers are used by
default; the PS-logo button uses PromptFont because it is absent from Kenney's set.

Glyph nodes require snapshot protocol 54. Rebuild packages to adopt the new
`ControllerHint` rendering; old packages continue emitting their old text nodes.
Do not infer controls from strings such as "X" or from style-class names.

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
