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

With a keyboard, use arrows, Enter and Escape to navigate. To toggle the overlay
with F1, enable **Settings → Controllers → F1 keyboard shortcut**. This is off by
default, leaving F1 available to other applications until enabled.
Text fields can use the built-in controller keyboard.

## Controller keyboard

The D-pad and left stick wrap around the keyboard's rows and top/bottom edges.
Use A/Cross to type the selected character or activate an action.

| Input | Keyboard action |
|---|---|
| Hold A/Cross on a character or Space | Repeat that character |
| X/Square (hold to repeat) | Delete before the caret |
| LB/RB (L1/R1; hold to repeat) | Move the caret left/right |
| LT/L2 | Toggle uppercase letters |
| Y/Triangle | Clear the draft |
| RT/R2 | Submit the draft |
| B/Circle | Cancel |
| R3, for private text | Show/hide the password |

Repeats begin after 400 ms and continue every 90 ms. Moving to another key or
changing the character layer cancels a held character. Actions such as Clear,
Done, Cancel, Shift and Show/Hide never repeat.

The caret remains visible while controller keys have focus. Private text starts
hidden whenever an editor opens. Switching from native mouse/keyboard editing
of a password back to controller editing starts the controller caret at the end;
use LB/RB to position it. Ordinary text retains its native selection.

Physical keyboard paste remains available, including Ctrl+V and Shift+Insert.
When a virtual key has focus, physical Left/Right move the text caret; Enter and
Space activate the selected key. Enter submits when the text editor has focus.
Hiding the overlay or leaving the widget cancels an unsubmitted edit.

## Choose a widget switcher

In **Settings → Overlay → Widget switcher**, choose **Radial + rail** (the default
for new installations) or **Rail**. Existing saved choices stay unchanged.
Radial + rail combines the wheel with the horizontal tray:

- Press B at a widget's top level to open the wheel. Nested pages and menus
  still handle Back first.
- Move down past the widget's bottom row to enter the horizontal tray. Moving
  left or right previews each widget and enables its dashboard shortcuts.
  Press A or move up to enter it. B closes the overlay from the horizontal tray.

The wheel opens over the current widget without changing its layout.
Point the left stick toward a widget to preview it behind the wheel and use its
dashboard shortcuts. Press A to enter it. D-pad or keyboard arrows move between
slots. B returns to the selected preview.

The wheel holds eight widget icons per page, with the selected widget's name in
the center. Its preferred diameter is 400 logical pixels, scaled by your display
and interface settings and reduced when screen space is limited.
Hold the right stick left or right to
change pages with the same repeat timing as ordinary navigation. Changing pages
keeps the selected widget, preview and shortcuts until you choose another icon.
Selection is shared across all pages; the center keeps showing the selected
widget's name even when its icon is on another page. Release the left stick
before pointing at an icon on the new page. Menu and Y retain their tray actions.

View keeps its pinned-view focus shortcut. Inside a widget, the right stick
still scrolls; release it to neutral when moving between the wheel and a widget.
If Windows composition is unavailable, WidgetRail uses the rail as a fallback.

## Settings and system controls

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
