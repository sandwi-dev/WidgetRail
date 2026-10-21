# Notifications

Supported widgets and websites can show quiet banners without taking controller
focus. WidgetRail uses its own banners and notification panel, not Windows'
notification center. A notification does not start a stopped widget in the background.

## Read or dismiss

Focus the **bell beside the clock** to show the panel, or use the tray menu's
**Notifications** command. Press **Up** to enter its list.
The bell counts pending entries. **A** opens an actionable entry, **X** dismisses
the focused entry, and **B** returns to the bell while leaving the panel visible.
B from the bell follows the usual radial-chooser Back behavior. **Clear all**
removes pending entries. New arrivals preserve your current selection.

Some notices, such as song changes, are informational banners only. They expire
without becoming bell/list entries. Pending entries last for the current
WidgetRail session; restarting the app clears them.

## Change delivery

On the source widget's tray menu, select **Notification preferences**, sometimes
under **Preferences** alongside Website settings.

| Choice | Result |
| --- | --- |
| Banner + list | Allow banners and keep pending entries |
| List only | Keep pending entries without banners |
| Off | Suppress notifications from that source |

**All notifications** is the widget's master control. **General** includes
uncategorized and website notifications. Widgets can also expose named categories.
Banner-only categories, such as **Song changes**, offer **On/Off** and **Banner
opacity** instead of list choices. A category cannot override a muted master;
List only suppresses banner-only notices rather than turning them into list entries.

Choices save immediately and survive restarts and widget updates. B leaves the
page. Change a preference back to restore delivery; previously suppressed notices
are not replayed.

## Website notifications

The website must have notification permission and a running page session.
Grant the site's request if wanted; review its permission in Website settings.
Host delivery preferences still apply after website permission is granted.
An expired **Session ended** entry can open the widget normally instead of its
old page callback.

## Why a banner may be missing

A banner can be suppressed by preferences, quiet intervals, lack of screen space,
fullscreen/system-picker presentation, or the widget's visibility rules. A list
entry without a banner is not necessarily a failure. Game Help avoids announcing
an answer already visible; music banners depend on the player being observed.
Stopping/disabling a widget ends its ability to publish new notices.

Check the source widget, website permission and delivery preferences before
repeatedly generating test events. See [troubleshooting](troubleshooting.md#i-did-not-get-a-notification).
Developers should use the [notification reference](../reference/widget-notifications.md)
for categories, delivery and lifecycle contracts.
