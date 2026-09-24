# Settings and themes

Settings is the built-in entry point for configuring the overlay, reviewing
permissions, and managing widgets. It stays enabled so users can recover from
an unwanted configuration. Other built-in widgets can be disabled.

For everyday instructions, use [Customization](../users/customization.md).
This page explains the relationships an author or platform contributor needs.

## Widget management

The Widgets section distinguishes bundled widgets from separately installed
packages. Bundled widgets update with WidgetRail. Community packages have their
own versions, permissions, and update files.

Installing a package shows a toast; it does not automatically move the user
to permission review or enable the widget. A manual update validates the new
package before replacing the selection, then requires review and enablement.
The older version remains available until it is removed.

Uninstall and unused-version removal require confirmation. Cancelling an
uninstall does not disable a working widget. The selected version cannot be
removed as an unused version. Saved widget data is preserved by widget removal.
See [Package and share](../developers/publishing-and-installation.md).

## Controller settings

The user chooses one opening shortcut: View + Menu or Guide. View + Menu is the
default. Optional Exclusive control has its own status below the toggle, and
its changes may require restarting affected applications. It can produce
duplicate input in some Windows surfaces; see [Compatibility](../users/known-limitations.md).

## Application settings

Quit and Restart in the header affect WidgetRail. Sign-in startup uses the
installed application's per-user registration and launches hidden. Windows can
disable that entry through Startup Apps; WidgetRail respects that state.

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

Widget context menus, tray menus, and Select dropdowns share native popup styling:
vertically centered labels, inset selection rows with a short leading accent,
and theme-derived depth. A subdued outer border keeps emphasis on the selected row. Their
surface, selection, selected text, and focus colors follow the existing host
theme styles. Shadows and decorative shading are omitted in high-contrast mode.
Dropdowns retain their checkmarks, icons, and value-selection behavior.

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
