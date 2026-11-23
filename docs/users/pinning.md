# Pin video and compact widgets

A **pinned view** stays visible when you hide the main overlay. It is useful for
watching a video beside a game or keeping compact playback information nearby.

## Pin a view

1. Open a widget that supports pinning. For video, first choose the content you want to play.
2. Return to the tray and press **Menu** to open pin controls. For YouTube Video,
   choose **Pin video**. The same command is available in Player and beside the
   current video in Discover.
3. Choose an available layout and adjust its placement and size.
4. Hide the overlay. The pinned view remains on screen.

Follow the controller hints in the pin controls as you make adjustments. Later,
you can manage any pin from another tray icon. Up to six surfaces can be pinned
at once across widgets. Layouts that support independent selection can coexist
from the same widget. At capacity, choose a pin to replace or cancel.

## Mouse and keyboard

With the overlay open, click an interactive pin to select it and enable its
content. The first click only activates the pin; subsequent clicks operate its
controls. Click another interactive pin to switch, or the main overlay to return.
Display-only pins never accept content input. Closing the overlay makes every
pin click-through again.

Open the overlay and click the **Manage pins** icon beside the widget rail, or press
**Ctrl+Shift+P** while the overlay's native controls have keyboard focus.
With several pins, choose one by name; highlighting it outlines its window.
With one pin, **Adjust pin** starts directly. Right-click an inactive pin to
adjust that exact pin, including a display-only pin. Right-clicking an interactive
pin keeps its content's normal context menu. Use **Tab**, arrow keys, **Enter**
or **Space** with the native buttons and lists; **Esc** cancels adjustment.

- **Adjust pin:** drag the pin's interior to move it, or drag an edge or
  corner to resize. Arrow keys also move it; **Shift+arrow** resizes it.
- **Opacity:** use the slider beside the pin or its arrow keys in the same adjustment mode.
- **Save** commits the adjustment; **Cancel** or **Esc** restores its previous
  position, size and opacity. Focus returns to **Manage pins**.
- **Interact:** click the pin, use the tray's Interact action, or press
  **Ctrl+Shift+I** from the main overlay's native controls. With several eligible
  pins, choose one. Display-only pins are excluded. Click **Return to overlay**, or another part of the
  main overlay, to return to it. Browser pages keep ownership of their **Esc** key;
  the configured overlay keyboard shortcut remains available to leave browser
  interaction or hide the overlay. **Return** is hidden and takes no space while
  you are already in the main overlay.
- **Close (×):** unpins the adjusted surface without saving unfinished edits.
  Closing a browser pin returns its page to the widget.

The host's **Manage pins** button includes pins from every widget. A widget's
own management button is limited to that widget's pins.
Widget and tray management routes enter the same adjustment mode. The toolbar
stays beside the pin where space allows, remains within the monitor's work area,
and stays readable when you lower the pin's opacity. Use **Enter** to save or
**Esc** to cancel; the configured overlay shortcut remains available.
Clicking the main widget, widget rail or widget wheel also cancels the adjustment
and keeps the overlay open. That first click does not activate the underlying
control. Clicking empty backdrop hides the overlay as usual.

## Controller controls and saved placement

With several pins, **Adjust pin** and **Unpin** open a named chooser.
**Interact** lists only pins with interactive content.
Moving the highlight outlines the actual window.
Press **A** to select it or **B** to cancel. During adjustment, use the left
stick or D-pad to move, the right stick to resize, and **LB/RB** for opacity.
Press **A** to save, **B** to restore the opening state, or **X** to close the pin.
At a display edge, continue moving toward an adjacent display to transfer the
pin. Align the pin with that display first if the displays are offset. The pin
keeps its logical size across DPI changes. **B** also restores its opening monitor.

Widgets can also offer **Manage pin** beside their pinned content. It opens
controls for that exact pin and outlines its window. YouTube's **Manage pins**
button considers only YouTube pins: one opens directly, several show a chooser,
and none shows “This widget has no open pins.” It never selects a Discord pin.
Passive pins still offer movement, opacity and unpinning, but not Interact.

Each layout remembers its monitor, position, size and opacity. If another pin
occupies its saved position, the opening pin moves to an available location.
If the monitor has no free rectangle large enough, placement minimizes overlap.
The saved position stays intact until you explicitly save a move/resize.
Saving opacity preserves the preferred position. Discord screen shares use
stable Screen 1, Screen 2 and Screen 3 positions. A surviving share keeps its slot.
Discord supports three incoming shares at once; each uses one of the host's six
pin positions. Unpin a screen before opening a fourth share.

Discord closes the matching screen pin when its sender stops or you choose
**Stop watching**. If the sender starts again, **Watch & pin** opens a fresh pin.

Most pins last for the current application session. Restarting WidgetRail does not
restore their selection or playback state; placement preferences remain.
Discord's voice controls and participants are exceptions: after you pin either,
leaving the call closes it and joining voice again reopens it. Each choice is
remembered independently across restarts of the same installed package, and is
not tied to a server. Discord must be running and report an active call; WidgetRail
does not join a call for you. Explicitly unpinning a voice layout stops it reopening.
At the six-pin limit, reopening waits for space without replacing another pin.
Shared screens, YouTube Music and other ordinary pins do not gain this behavior.
Ending adjustment restores its entry point when that control is still available.

## Switch focus and return

With the overlay open, **View** enters the sole interactive pin or opens a chooser
of interactive pins. Display-only pins, including Discord participants and
shared-screen video, are excluded. With only display-only pins, no pin interaction
option appears. Their move/resize, opacity and unpin controls remain available.
Inside a pin, **View** returns to main. **B** dismisses local controls first,
then returns to the overlay. Follow the pin's controller guide for its available actions.
Passive pins are display-only. Interactive pins accept an activation click while
the overlay is open; with the overlay closed, all pins remain click-through.

For Browser and hosted webapps, pinning transfers the existing live page. The
main widget shows that the page is pinned; it does not create another page or
sign-in. **Unpin** returns the page to its widget. A hosted pop-out can instead
use **Pin pop-out** and **Return pop-out to widget** through LS controls.

Unpinning is not a general stop-playback action. Use **Stop widget** to end that
widget's sessions and all its pins. [Lifecycle and saved data](lifecycle.md) explains
what is kept, and [browsing](browsing.md#pages-pop-outs-and-pins) covers new-window choices.

## Why layouts differ

The widget author decides which views can be pinned. A video widget may offer a
player-only view. A music widget may offer compact controls or a larger queue view.
Some widgets have no pinned views at all.

Supporting pinning does not automatically make the whole widget pinnable.
This lets authors avoid offering a tiny, unusable copy of a complex interface.

## Video and game compatibility

The [YouTube add-on](../../samples/YouTubeWidget/README.md) demonstrates pinned
video playback. Its setup and playback depend on the provider. Pinning does not
remove account requirements or provider playback restrictions.

Host-owned video padding is transparent, so unused space around the picture
reveals the desktop. The picture keeps its aspect ratio and opacity. Black bars
encoded in the source or drawn inside a provider's iframe remain part of that
provider's picture.

For games, try a windowed or borderless display mode. An always-on-top desktop
window is not a guarantee that a view will appear over every exclusive-fullscreen
game. [Read the compatibility notes](known-limitations.md).
