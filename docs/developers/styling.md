# Styling and themes

WidgetRail uses **WRSS**, a CSS-like style language, for native widget controls.
It lets your widget fit the user's theme without depending on a browser.

## Start with the shared appearance

SDK components already have a default appearance. Build a usable layout first,
then add only the styles that make your widget clearer.

Keep text readable and leave room for the focused state. A control that looks
good with a mouse pointer may still need a stronger focus outline for someone
sitting across the room with a controller.

## Style a specific control

The basic template has a control with the ID `primary`. A rule in
`styles/default.wrss` can target it:

```css
#primary {
  corner-radius: 12;
}

#primary:focused {
  outline-color: var(--accent);
}
```

`#primary` selects that element by ID. `:focused` applies while it has controller
focus. `var(--accent)` uses a theme value instead of fixing the highlight to one color.

For a reusable visual treatment, apply the same style class to several elements
and use a `.class-name` selector. Roles such as `button` can style a kind of control.

## Let the theme supply colors

For a translucent surface with opaque labels and artwork, use
`background: alpha(var(--surface), .84)` and, if needed,
`border-color: alpha(var(--border, var(--surface-muted)), .84)`. This multiplies
the theme color's existing alpha without dimming child content. Avoid container
`opacity` for that effect; WinUI applies opacity to the whole subtree. See the
[color transparency contract](../reference/wrss.md#color-transparency) for
supported arguments and limits.

Shared tokens include `--accent`, `--surface`, `--text`, `--text-muted`, and
`--focus`. If your widget leaves a token undefined, it can inherit the global
theme or host default. An explicit package definition overrides that default.

Scroll indicators have separate `--scrollbar-track` and `--scrollbar-thumb`
tokens. Theme them independently of text and container backgrounds; see
[Scrollbar styling](../reference/wrss.md#scrollbars).

Try both light and dark backgrounds. Avoid conveying an error or disabled state
through color alone; use a useful message as well.

## Use shared surface depth

Cards, navigation, buttons and dialogs receive subtle theme shading and shadows.
For a custom panel, add `wrail-surface-raised` to its classes; use
`wrail-surface-inset` for a recessed track or `wrail-surface-flat` to remove depth.
Keep the widget canvas flat so raised controls remain distinct.

This appearance comes from WRSS, with native paint and compositor transitions.
There is no separate C# 3D-control API. The [surface-depth reference](../reference/wrss.md#surface-depth)
lists properties, theme tokens, clipping and accessibility behavior.

## WRSS is not all of CSS

WRSS supports a defined set of selectors and properties. Browser-only features
and arbitrary selector combinations are not supported. Run `wrail validate` to
catch unsupported styles before packaging.

Use the [WRSS reference](../reference/wrss.md) for the full property list and
[theme packaging](theme-packaging.md) to create a theme for the whole overlay.
