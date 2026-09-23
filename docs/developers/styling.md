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

Shared tokens include `--accent`, `--surface`, `--text`, `--text-muted`, and
`--focus`. If your widget leaves a token undefined, it can inherit the global
theme or host default. An explicit package definition overrides that default.

Scroll indicators have separate `--scrollbar-track` and `--scrollbar-thumb`
tokens. Theme them independently of text and container backgrounds; see
[Scrollbar styling](../reference/wrss.md#scrollbars).

Try both light and dark backgrounds. Avoid conveying an error or disabled state
through color alone; use a useful message as well.

## WRSS is not all of CSS

WRSS supports a defined set of selectors and properties. Browser-only features
and arbitrary selector combinations are not supported. Run `wrail validate` to
catch unsupported styles before packaging.

Use the [WRSS reference](../reference/wrss.md) for the full property list and
[theme packaging](theme-packaging.md) to create a theme for the whole overlay.
