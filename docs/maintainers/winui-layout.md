# Native widget layout mapping

The trusted presenter reconciles semantic widget identities into WinUI controls.
It does not use the native renderer's layout tree or run a frame scheduler.

`Row` and `Stack` use native `Grid` tracks. Fixed gaps occupy explicit tracks;
positive flex-grow values map to star tracks. Start, center, end and space-between
justification map to native auto/star tracks. Cross-axis alignment maps to native
element alignment. Collapsed responsive branches create no tracks or gaps.

`Grid` uses `WidgetResponsiveGrid`, which adjusts native star columns from the
available content width, authored minimum column width, maximum column count and
gaps. Native Grid still measures and arranges the controls. This bounded static
primitive is distinct from indexed collections, whose ListView/GridView control
owns virtualization. A static grid must not replace an indexed collection for
large content.

`VisibleWhen` follows the SDK's existing viewport rule: less than 960 DIPs wide
or 540 DIPs tall uses compact composition. Collapsed state propagates through the
whole branch. Resizing preserves retained controls and uses `FocusPersistenceId`
to transfer native focus between mutually exclusive presentations. A row or focus
fragment uses its enclosing widget viewport, not its own dimensions.

Lengths support px/unitless DIPs and widget viewport vw/vh units. Native stretch
handles ordinary full-width/full-height composition. General percentage lengths,
flex shrink/basis parity and wrapping rows are not yet implemented; they are not
silently translated into a second custom layout engine.

Poster ActionSurface declarations use a single `WidgetPosterPanel` cell: Cover
artwork underneath bottom-aligned copy. A bounded portrait desired size prevents
intrinsic image dimensions from defining collection item extent. Native image
UniformToFill may expose an ActualWidth wider than the layout slot; the slot and
the containing clip are the relevant geometric boundaries. Current clipping is
rectangular; shared rounded clipping/depth/scale still needs completion.

`WidgetStylesValidationPage` includes real native checks for responsive branches,
focus continuity, static grid reflow and poster artwork/copy placement. The
33-check checkpoint is fixture correctness, not production Playnite performance
or complete WRSS compatibility.
