# WidgetStyling / GBSS

`WidgetStyling` is the shared parser, validator, package loader, typed property catalog, variable resolver, and deterministic cascade for GBSS. Developer tooling and the future renderer should consume this assembly instead of maintaining separate CSS-like parsers.

## Integration API

```csharp
var package = GbssPackageLoader.LoadFile(
    entryFile: @"C:\Widget\styles\default.gbss",
    packageRoot: @"C:\Widget");

var compiled = GbssThemeCompiler.Compile(package);
if (compiled.IsValid)
{
    var style = compiled.Theme!.Resolve(new GbssElement(
        Role: "button",
        Id: "play",
        StyleClasses: new HashSet<string> { "primary-action" },
        PseudoStates: new HashSet<GbssPseudoState> { GbssPseudoState.Focused }));

    var scale = style.Get("scale");
}
```

Parse diagnostics carry source, one-based line and column, severity, stable code, and a controller-readable message. Parse and compile off the render thread; atomically swap only a valid `GbssTheme`, retaining the last valid theme on errors.

## Supported language

- `:root` custom properties such as `--accent`.
- `var(--name)` and `var(--name, fallback)`, including cycle detection.
- Comma-separated simple semantic selectors: role, `#stable-id`, `.style-class`, and `:focused`, `:pressed`, `:selected`, or `:disabled`.
- Quoted package-relative `.gbss` imports before rules, with depth/cycle/missing-file checks.
- Deterministic cascade: ID specificity, then class/pseudo-state specificity, then semantic role, then document/rule/declaration order.
- Typed, bounded values. Scale, opacity, dimensions, spacing, blur, border/outline width, and transition duration are clamped with warnings.
- Explicit limits for source size, imports/depth, statements, selectors, declarations, raw values, and expanded variable values.

The allowlist is available as `GbssPropertyCatalog.AllowedProperties`. It covers layout, typography, colors, borders/outlines, opacity, scale, blur/shadow geometry, and transition duration; unsupported properties are errors.

Presentation transforms support bounded `translate-x` and `translate-y`
lengths. Percentages resolve against the corresponding parent axis, viewport
units resolve against the viewport, and the native boundary clamps each final
offset to +/-4096 device-independent pixels. Translation changes painting and
hit-test geometry without becoming a general layout or arbitrary transform
escape hatch.

Media layouts additionally have bounded `vw`/`vh` lengths, `aspect-ratio`,
`object-fit`, `object-position`, `shape`, image tint/scrim colors, line height,
line limits/ellipsis, outline offset, and named transition easing. A small
deterministic flex subset—`flex-grow`, `flex-shrink`, `flex-basis`, and
row-only `flex-wrap`—lets a row allocate fixed artwork beside flexible metadata
or form additional lines without renderer-specific widget hacks. `flex-wrap`
accepts only `nowrap` and `wrap`; wrapped line spacing comes from the first
value of a two-value `gap`. Image source selection remains outside GBSS and
must come from verified host content.

## Deliberate safety boundary

GBSS is not CSS and has no browser escape hatch. It rejects `url()`, `http:`, `https:`, `file:`, `data:`, JavaScript/VBScript, `expression()`, script/eval functions, declaration-level `@` statements, selector combinators, attribute selectors, and unknown functions/values. Imports cannot be absolute, contain backslashes/schemes, or use `.`/`..` segments. There is no shader, command, native-extension, or arbitrary filesystem syntax.

Assets are intentionally not a style value in this milestone. A future asset type must use package-relative identifiers resolved by the host's verified asset broker with decoded-size limits; it must not reintroduce `url()`.

Accessibility rules are not represented as GBSS. The host applies text scaling, high contrast, focus visibility, reduced motion, and other mandatory accessibility overrides after resolving the theme, so author styles cannot defeat them.
