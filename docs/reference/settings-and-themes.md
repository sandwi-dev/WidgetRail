# Settings and themes

Settings is the built-in entry point for configuring the overlay, reviewing
permissions, and managing widgets. It stays enabled so users can recover from
an unwanted configuration. Other built-in widgets can be disabled.

For everyday instructions, use [Customization](../users/customization.md).
This page explains the relationships an author or platform contributor needs.

## Widget management

The Widgets section has **All**, **Built-in**, and **Community** filters over a
virtualized list. Rows have stable package identities, and returning from details
restores and reveals the selected row. Refreshes preserve the filter and stable
selection. Bundled widgets update with WidgetRail. Community packages have their
own versions, permissions, and update files. The ordinary list has no pagination;
version-management and catalog-recovery pages keep their bounded paging.

Install and Update from file use a Windows file picker followed by themed
overlay dialogs for full-trust approval, progress and completion. The picker
requires mouse/keyboard; the overlay dialogs support controller navigation.
Completion refreshes the widget and permission lists. Fresh installations through
Settings and Marketplace start enabled and request permissions when needed.
Updates validate the new package before replacing the selection and preserve
enabled state, permissions and data; the older version remains available until
removed. CLI staging remains a separate disabled-install workflow.

Uninstall and unused-version removal require confirmation. Cancelling an
uninstall does not disable a working widget. The selected version cannot be
removed as an unused version. Saved widget data is preserved by widget removal. **Clear local data** is a
separate confirmed operation for overlay-owned private state, with worker restart;
it excludes credentials, provider data and ordinary user files.
See [Package and share](../developers/publishing-and-installation.md).

## Controller settings

The user chooses one opening shortcut: View + Menu or Guide. View + Menu is the
default. Controllers links to a dedicated Exclusive control page containing the
toggle, status, driver setup and help. Changes may require restarting affected
applications. Exclusive control can produce
duplicate input in some Windows surfaces; see [Compatibility](../users/known-limitations.md).

## Application settings

Quit and Restart in the header affect WidgetRail. Sign-in startup uses the
installed application's per-user registration and launches hidden. Windows can
disable that entry through Startup Apps; WidgetRail respects that state. Inno
exposes startup selection. The current WinUI session does not advertise the
Settings startup control even though the trusted registration backend exists.

The installed edition and source development copies share application data.
The installer controls which installed executable the registration points to.
See the [installer contract](../../eng/installer/README.md).

## How widget styles fit a global theme

The host provides default styles. A selected theme supplies global values, and
explicit widget styles can override them. Users' accessibility choices are
applied as a final presentation policy where appropriate.

Use shared semantic tokens for colors and focus. A widget that hard-codes all
its colors can override the user's intended appearance. Begin with
[Styling and themes](../developers/styling.md), then use the [WRSS reference](wrss.md)
for exact selectors, properties, variables, and cascade behavior.

Controller hints and common components use shared theme classes. Widget-specific
artwork can retain its own colors; do not assume a brand logo is monochrome.

Tile and PosterTile roots own their outer content shape. With `overflow: clip`,
their resolved corner radius also clips descendants; zero gives square corners.
Poster artwork uses that shared boundary instead of independent rounding on its
generated image layer. `overflow: visible` permits decorative overflow, while
focus outlines and interactive bounds remain separate from content clipping.

Widget context menus, tray menus, and Select dropdowns share native popup styling:
vertically centered labels, inset selection rows with a short leading accent,
and theme-derived depth. A subdued outer border keeps emphasis on the selected row. Their
surface, selection, selected text, and focus colors follow the existing host
theme styles. Shadows and decorative shading are omitted in high-contrast mode.
Dropdowns retain their checkmarks, icons, and value-selection behavior.
Menus measure all labels when opened and keep that width while navigating. The
width ranges from 220 to 360 logical pixels, shrinking further if the viewport
requires it; long labels use an ellipsis. The visible left border aligns with the
opener when it fits, otherwise the right border aligns, with viewport clamping as
a final fallback. Shadow space is excluded from the visual edge alignment.

## Framework dialog colors and placement

Global themes can style native framework dialogs independently of the tray.
Existing themes retain derived defaults; these roles are optional. They apply to
host choices, bookmarks/history, installation dialogs and controller keyboards.
They do not restyle Windows file pickers or WebView-native screen-share choosers.

| WRSS role | Supported dialog color properties |
| --- | --- |
| `dialog` | `background`, `color`, `border-color` |
| `dialog-title` | `color` for the native dialog title |
| `dialog-description` | `color` for choice-dialog descriptive text |
| `dialog-footer` | `background` (defaults to the dialog surface) |
| `dialog-choice` | `background`, `color` |
| `dialog-choice:selected` | `background`, `color` for selection/hover/press |
| `dialog-choice:focused` | `outline-color` for dialog focus cues |
| `dialog-button` | `background`, `color` for action buttons |
| `dialog-button:focused` | `background`, `color` for focused/active action buttons |

Use semantic role selectors, such as `dialog { background: #20182d; color: #f5efff; }`.
These are host theme roles, not new widget node kinds or SDK parameters. Typography
continues to follow the shared body font, text scale and interface scale. High
contrast overrides authored colors. Native modal behavior, controller routing,
and specialized keyboard/install flows remain separate from visual styling.

Dialogs anchor over their requesting widget rather than the fullscreen host's
center. They can extend beyond a small main widget but are clamped to the native
window. Live browser editing stays in place; the notification inbox stays anchored
to its bell, and widget-authored modals keep their existing local viewport.

## Theme packages and updates

A theme is a data-only `.wrtheme` package. It has its own manifest and immutable
version. Installation validates the package before it becomes selectable.
Selecting a theme pins the selected version rather than following an unreviewed
future replacement.

A failed theme load should retain the last usable appearance. Validate theme
files with the CLI and test focused, disabled, busy, and error states at several
scales. See [Theme packaging](../developers/theme-packaging.md).

## Source map

- [Settings widget](../../src/FirstPartyWidgets/SettingsWidget/) builds the UI.
- [Platform settings](../../src/PlatformSettings/) owns application settings and startup registration.
- [Widget catalog](../../src/WidgetCatalog/) validates package/version operations.
- [Widget styling](../../src/WidgetStyling/) parses and resolves WRSS.

The trusted Settings integration does not grant every community widget access
to installation or startup controls. These are host-owned management operations.

## Catalog icons

Settings' indexed widget rows use host-resolved catalog icons, including disabled
widgets. Protocol 82 adds a typed package ID, version, source (built-in or installed)
and optional sealed content digest to an icon reference. Only trusted Settings and
Marketplace views may use this authority; package manifests and ordinary widgets
cannot borrow another package's assets. Only the target's presentation icon is
available. Workers are never started to obtain icons.

The host verifies files with the existing package resolver. These references use
the same bounded session demand pool, byte cache and native SVG/tint renderer as
ordinary icons, with semantic glyph fallback. Built-in and installed copies remain
distinct even when their package and asset IDs match. Catalog replacement retires
cached management references.

Widget stop state lives in `stopped-widgets.json` under the profile root, separate
from the main settings schema so older app versions can still read ordinary
preferences during rollback. The host atomically writes a bounded set of package
IDs under a cross-process lock. Package operations and Settings resets do not
clear it. Stop/start publishes a new runtime authority immediately, supersedes
older catalog discovery and drains only that widget's resources. Settings rows
distinguish Stopped from Enabled and Disabled.
