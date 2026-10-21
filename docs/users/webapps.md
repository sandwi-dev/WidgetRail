# Add your own webapps

A webapp puts a website on the rail with its own persistent sign-in and
preferences. It uses WidgetRail's hosted browser and controller controls; you do
not need to build or install a widget package.

1. Open **Settings → Widgets → Add webapp**.
2. Enter an HTTP(S) website address and optionally change the name.
3. Select **Add and open**. The new webapp starts enabled.

From Browser, **Page actions → Add as webapp** opens the same form with the
current address and title filled in. The new webapp has a separate profile:
it does not copy Browser's sign-in.

Use the site's own sign-in controls if needed. D-pad moves between webpage
controls, left stick points, A clicks, right stick scrolls, and LS opens Website
controls. See [browsing](browsing.md) for text entry, pop-outs and compatibility.

## Manage a webapp

Use **Settings → Widgets → Manage webapps**, or its tray menu → **Widget → Manage
widget**. Renaming preserves its sign-in and preferences. Turn off **Enabled** to
remove it from the rail without deleting its saved data; return here to enable it.

**Stop widget** in the tray ends the current session while leaving the icon
available. **Start widget** or Reload starts it again. Simply hiding the overlay
can leave the webapp running, including its playback or calls.

Confirmed **removal** deletes the webapp, cached icon and its local browser data,
including website sign-ins, bookmarks/history and profile-specific extension
settings. There is no second data-deletion confirmation. Locked browser files
may finish deleting after the browser process exits. Removing a webapp does not
delete your account or content on the website.

## Permissions and data

Microphone, camera, screen-sharing and notification requests use the host's
website controls. Review website permissions through the tray menu. Browser
extensions can be enabled for this webapp's profile independently of others.

Profiles separate ordinary hosted websites; they are not a sandbox around a
full-access Windows widget. See [extensions](extensions.md),
[notifications](notifications.md) and [lifecycle](lifecycle.md).
The [developer reference](../reference/user-webapps.md) covers identity and storage contracts.
