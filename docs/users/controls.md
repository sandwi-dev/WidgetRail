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

**Stop widget** keeps a widget stopped until you start it again, including across
overlay restarts. Settings is always available and does not offer Stop widget;
its Restart widget action remains available. Old saved stop choices for Settings
are ignored. Settings still suspends when hidden.

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
Done, Cancel, Shift, Paste and Show/Hide never repeat.

The bottom-row **Paste** key inserts clipboard text at the caret or replaces the
selected text, without closing the keyboard. It also works in password fields,
which are masked by default, and website fields using the browser's normal paste behavior.

The caret remains visible while controller keys have focus. Private text starts
hidden whenever an editor opens. Switching from native mouse/keyboard editing
of a password back to controller editing starts the controller caret at the end;
use LB/RB to position it. Ordinary text retains its native selection.

Physical keyboard paste remains available, including Ctrl+V and Shift+Insert.
Both keyboard modes open with Q visibly focused. When a virtual key has focus,
physical Left/Right move the text caret and Enter activates the selected key.
Physical Space always types a space. Enter submits when the text editor has focus.
The keyboard sizes responsively while keeping every key and action visible without
scrolling. It can extend outside a small widget or pin.
Hiding the overlay or leaving the widget cancels an unsubmitted widget-field draft.

Browser and webapp fields work differently: point at a field and click A to open
the keyboard, including a field that already has focus. These edits reach the page
as you type. The keyboard floats beside the rendered field without shrinking the
webpage, preferring below the field and using the space above or beside it when needed.
B or Done closes the keyboard and keeps the edits; **Send Enter** is
the explicit action for sending an Enter key to the website. Existing password text
is represented only by masks. Widget fields, browser addresses and website text
prompts keep the draft/confirm behavior described above.

## Webapps and notifications

In a webapp, **D-pad** jumps the circle between nearby webpage controls. **Left
stick** remains free pointer movement, **A** clicks, and **right stick** scrolls.
D-pad scrolls by list rows, including text-only chat messages. Long rows advance
in smaller steps. With no clickable target, the circle stays near the advancing
edge of the list. Holding D-pad defers new hover until release; the circle glides
between targets. Left-stick movement is immediate, without a trail.
System reduced motion disables the glide. D-pad never clicks automatically
and does not move the pointer while A is held for dragging. Use the free pointer
for canvas controls or elements a page does not expose as interactive.
LS opens Website controls; its actions have direct labels. Website permissions can
also be changed from the widget's tray menu, including restoring a blocked microphone.

On a link, **Page actions → New window** offers **Open in pinned surface** and
**Open over current page** directly. The latter keeps the current page alive
underneath; B returns to it. These actions do not change your saved preference.

Website-triggered windows show **Open new window**, with **Open in pinned surface**,
**Open over current page**, and **Cancel**. **Remember for this website** saves the
choice for the current widget/account. Change it through **Website settings →
New windows → Ask each time / Pinned surface / Over current page**.
Replacing an existing page or pin still requires confirmation; a direct action
uses one **Replace / Cancel** confirmation without asking for placement again.

Pop-outs have no permanent toolbar, in either placement; press **LS** to reveal
their controls. An existing pop-out also offers **Pin pop-out** there and in the tray. This keeps
the existing live page pinned while its parent remains usable. View switches to the
pin; B returns to the overlay. **Return pop-out to widget** moves it back. Stopping
the parent session also closes its pop-out.

Replacing a pin always asks for confirmation. Replacing an additional page with
its own full widget closes that additional page; use **Return page to widget**
instead to keep it. Replacing it with a different widget returns the live page to
its original widget.

After granting a website notification permission, select **Notification preferences**
in its widget tray menu to choose Banner + list, List only, or Off. Quiet banners
fade away after four seconds. The bell beside the clock shows the number of pending
notifications and opens their panel; the tray-menu **Notifications** command also
remains available. From the last tray/radial widget, D-pad right moves to the bell;
D-pad left returns. For a left-aligned rail, use left at its first widget instead.
A opens the originating page, X dismisses the focused entry, and B closes the panel
without clearing it. Clear all is a secondary action. New arrivals keep your selection.
A **Session ended** entry opens its widget normally; if the widget was removed, the
entry disappears with a brief inline message. No extra warning needs dismissing.
Windows notification UI is not used.
Pending entries last for the current WidgetRail session; the preference survives restarts.

## Switch widgets with the radial chooser and rail

The radial chooser and horizontal rail are both available. Older rail-only
preferences are retired without changing your other settings.

- Press B at a widget's top level to open the wheel. Nested pages and menus still
  handle Back first.
- Move down past the widget's bottom row to enter the rail. Left/right previews
  widgets and enables their dashboard shortcuts. Press A or move up to enter.
  B opens the radial chooser with the current widget selected.

The wheel opens over retained widget content without changing its layout. Point
the left stick toward a widget to preview it, then press A to enter. D-pad and
keyboard arrows move between slots. B returns to the selected preview.

Each page holds eight icons with accessible names and tooltips. The center shows
the paging controller hint. Its preferred diameter is 400 logical pixels, scaled
by display/interface settings and reduced to fit available space. Hold the right
stick left/right to change pages. Browsing pages preserves the selected widget
until you choose another icon. Release the left stick before pointing at an icon
on the new page. Menu and Y retain their tray actions.


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
