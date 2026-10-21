# Everyday workflows

Start WidgetRail, then press **View + Menu** to open it. If you changed the opening
shortcut to Guide/PS, use that instead throughout these examples. Follow the
on-screen guide for your controller's button names. Opening WidgetRail does not
automatically stop the game or application underneath it.

## Adjust audio and return to a game

**Before you start:** Production includes Audio Mixer. In Bare, install it from
Catalogue. Start audio in an application so it has a session to adjust.

1. Open the overlay and select **Audio Mixer**.
2. Approve the requested audio access if needed, then select the application's
   volume control and adjust it. Device controls change the output device's volume.
3. Listen for the change, then press the opening shortcut again to hide the overlay.

The volume stays at its new value. To undo the change, reopen Audio Mixer and
restore the previous volume. Hiding the overlay is not a mute command.

## Launch or switch applications

**Before you start:** Install **Games & Apps** or **Task Switcher** from Catalogue
if missing. Grant the requested application/window access when using it.

1. In **Games & Apps**, choose an application from your library to launch it.
   Use **Running**, or open **Task Switcher**, to select an existing window.
2. Activate the application/window. Windows decides whether it can come to the
   foreground; protected or elevated windows can refuse the switch.
3. Reopen WidgetRail with your shortcut when needed. Hide it to leave without
   launching or switching anything.

Launching a game may open its own launcher. Returning to WidgetRail does not undo
the launch; use the application's own controls to close it. See [focus troubleshooting](troubleshooting.md#the-overlay-is-visible-but-not-focused)
if the overlay or target window does not receive input.

## Add a website to the rail

**Before you start:** Have an HTTP(S) website address. No developer tools or
separate widget package are needed.

1. Open **Settings → Widgets → Add webapp**. Enter the address and a name.
2. Choose **Add and open**. The website opens and its icon is added to the rail.
3. Sign in on the website if required. That webapp retains its own sign-in;
   your desktop browser's sign-in is not imported.
4. B leaves page controls/pop-outs first, then returns through normal widget
   navigation. Hide the overlay when finished.

Use **Settings → Widgets → Manage webapps** to rename, disable or remove it.
Removal also deletes its local browser data. [Webapp guide](webapps.md).

## Browse and enter text

**Before you start:** Open Browser, a webapp, or another widget's hosted page.
Browser is included in Production and installable from Catalogue in Bare.

1. In Browser, press **Y** to enter an address or search query and commit it.
2. Use **D-pad** to move between webpage controls, or **left stick** for free
   pointer movement. **A** clicks; **right stick** scrolls.
3. Point at a text field and press A to open the controller keyboard. Type or
   paste. Website-field edits reach the page as you type; **B** closes that
   keyboard without undoing those edits. Use **Send Enter** to send Enter to the page.
4. Press **LS/L3** for website controls or the browser toolbar. B dismisses
   transient controls; your opening shortcut hides WidgetRail.

Widget fields and the Browser address editor instead keep a draft until Done;
B cancels that draft. See [browsing and text entry](browsing.md) for navigation,
zoom, dragging, suggestions and compatibility limits.

## Pin a page or video

**Before you start:** Open content in a widget that offers pinning. YouTube Video
can play a supported public link without a search key.

1. Return to that widget's tray icon and press **Menu**. Choose an offered pin
   layout. For an existing pin, review the replacement confirmation.
2. Hide the overlay: the pin remains visible.
3. With WidgetRail open, use **View** to switch focus to the pin. **B** returns
   to the overlay. Follow the pin's guide for playback or page controls.
4. Use the tray's pin controls to move/resize, change opacity or **Unpin**.

A live browser page transfers to the pin; it is not a new signed-out copy.
Unpinning returns that page to its widget. Other widgets define their own pinned
layouts and playback behavior. [Pinning guide](pinning.md).

## Open content across widgets

**Before you start:** Install and enable the destination widget; complete any
service setup it requires. A stopped widget needs to be started again.

1. Choose a supported action, such as opening a web link from another widget.
2. If WidgetRail offers a destination choice, select the handler you want.
3. Content opens in that widget, or updates an eligible existing pin when the
   action supports it. A normal web link can use your default Windows browser
   when no eligible widget handles it; account/setup links may request that directly.

B follows the destination's navigation, rather than a universal return-to-sender
history. Use the rail/radial chooser to return to the original widget. Cancelling
a destination chooser leaves the action unperformed. [Connected widgets](connected-widgets.md).

## Review and quiet notifications

**Before you start:** The widget must support notifications. Websites also need
notification permission and a running page session.

1. Open the **bell beside the clock**, or **Notifications** from the tray menu.
2. Open an actionable entry with **A**, dismiss it with **X**, or close the panel
   with **B** without clearing entries.
3. For delivery preferences, use the source widget's tray menu → **Notification
   preferences**, sometimes grouped under **Preferences**.
4. Choose **List only** to keep pending entries without banners, or **Off** to
   silence that widget. Song-change categories offer On/Off because they have no list entries.

Preferences save immediately; B leaves the preferences page. Quiet banners do not
take controller focus. Pending entries last only for the running WidgetRail session.
[Notification guide](notifications.md).
