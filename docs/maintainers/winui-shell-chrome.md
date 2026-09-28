# Native shell theme roles and controller guide

`ShellChromeStyles` applies the existing bridge-resolved shell palette through
the same `NativeComputedStyleAdapter` used by widgets. It does not create a
second renderer, replace native ListView templates or handle controller input.

The page inherits the `body` role. Header/status/guide backing surfaces and the
native tray use `tray`; title, status, body actions and guide typography use
their named roles. Native tray containers combine `tray-item` or
`tray-item:selected` as Base with `tray-item:focused` or
`tray-item:selected:focused` as Focused. Pressed falls back to that focused map,
because the existing bridge inventory supplies no separate pressed tray role.
Selection changes update only the affected native item. Container recycling
retires style ownership and restores native local values/resources; a reused
container receives the current palette. Theme changes reuse the controls.

The shared adapter preserves native templates and owns authored focus outline,
brushes, typography and depth decoration. Existing High Contrast, reduced
transparency, bold text, motion preferences and text scaling remain authoritative.
The widget's host-owned surface appearance and desktop backdrop remain separate
from chrome fills, so transparent widgets do not gain an opaque outer card.

`ShellControllerGuide` declares controller prompts with `ControllerPrompt` and
uses `WidgetGlyphs`/the installed Kenney fonts. The existing normalized input
frame updates `WidgetControllerPrompts` even before an active widget presenter
exists. Unknown frames retain the last known family. No additional input reader
or action path is introduced.

Normal and reorder guides for both Xbox and PlayStation remain measured in one
native Grid. Only their presentation opacity changes. The inactive guide still
reserves its full slot when entering widget controls. Native wrapping/text-scale
changes may resize the slot, but focus, reorder or controller-family changes at
the same constraints do not. Glyphs use minimum bounds rather than a fixed
font-height clip. The accessibility name describes only the active family/mode;
measured inactive glyph/text variants are excluded from the control view.

Existing tray commands retain their semantics: A opens, tap Y reorders, hold Y
restarts, Menu opens commands, B closes; reorder uses Left/Right and A/B/Y to
finish. Native menus and captured quick-action authority are unchanged.

`--validate-shell-chrome` uses compiled built-in Default + Neon Circuit/Redline
WRSS roles on a real native ListView. It checks selected/focused distinction,
theme replacement, native template retention, rapid focus movement, controller
family semantics, invariant guide height, narrow scaled reflow, High Contrast
and recycled containers. Actual integrated widget/tray commands remain a
separate production-shell gate; this fixture does not invoke provider commands.
