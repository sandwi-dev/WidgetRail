# Controls and everyday use

The controller guide at the bottom of the overlay shows actions for your
current focus. A widget can provide its own shortcuts.

| Input | Usual action |
|---|---|
| View + Menu | Show or hide the overlay; configurable to Guide in Settings |
| D-pad / left stick | Move focus |
| A | Activate the focused control |
| B | Go back; at a widget's top level, return to the tray |
| Right stick | Scroll a scrollable area |
| Menu on the tray | Open widget/pinning options |
| Y on the tray | Reorder widgets; hold to reload the selected widget |

With a keyboard, use arrows, Enter and Escape to navigate. F1 toggles the overlay.
Text fields can use the built-in controller keyboard.

## Choose a widget switcher

In **Settings → Overlay → Widget switcher**, choose **Rail** (the default) or
**Radial**. With Radial, returning to the tray opens a wheel over the widget.
Point the left stick toward a widget, then press A to open it. D-pad or keyboard
arrows move between slots. B returns to the current widget.

The wheel holds eight widget icons per page, with the selected widget's name in
the center. Its preferred diameter is 400 logical pixels, scaled by your display
and interface settings and reduced when screen space is limited.
Hold the right stick left or right to
change pages with the same repeat timing as ordinary navigation. Browsing the
wheel does not open each highlighted widget. Menu and Y retain their tray actions.
Widget-specific shortcuts become available after you open the widget with A.

View keeps its pinned-view focus shortcut. Inside a widget, the right stick
still scrolls; release it to neutral when moving between the wheel and a widget.
If Windows composition is unavailable, WidgetRail uses the rail as a fallback.

## Set it up for you

Settings controls appearance, scale, themes, widget order and enablement,
permissions and controller behavior. Settings itself remains enabled.
The Quit and Restart buttons in its header affect **WidgetRail**, not Windows.

The **Power** widget controls the PC. Sleep is immediate. Restart and Shut down
ask for confirmation, with Cancel selected first.

The tray clock shows local time. Its internet indicator reflects connectivity
over Wi-Fi or Ethernet; the Bluetooth indicator reflects radio power.

## Pin a supported view

Use Menu on a tray icon to manage pinning. Pin controls also let you adjust or
remove an existing pin without finding the icon that originally created it.
A widget decides which views it supports for pinning; the full widget is not
automatically a pin option.

## Review permissions

Built-in widgets and sandboxed community widgets request specific permissions
for Windows operations. Review them under Widgets.

A **full-trust application** widget is different: it runs with your ordinary
Windows user access. Only enable one from a source you trust. See
[Security and trust](../maintainers/security-and-trust.md).
