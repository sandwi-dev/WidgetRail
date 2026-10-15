# Widget notifications

`HostServices.Notifications` is an intrinsic host service for ordinary sandboxed
workers and full-trust application widgets. No manifest permission is required.
User notification preferences remain authoritative. A resident widget may publish
in Background, Visible or Interactive state; the service does not keep a widget
running by itself. Destroyed workers cannot publish.

```csharp
var notice = new WidgetNotification("now-playing", song.Title)
{
    Body = song.Artist,
    Metadata = "3:45",
    ArtworkUrl = song.ThumbnailUrl, // optional HTTPS artwork
    ShowWidgetIcon = false,
    Retention = WidgetNotificationRetention.BannerOnly,
    Interaction = WidgetNotificationInteraction.Informational,
    Visibility = WidgetNotificationVisibility.Always,
    DurationMilliseconds = 4000,
    BackgroundOpacityPercent = 80,
};
var receipt = await HostServices.Notifications.ShowAsync(notice, cancellationToken);
await HostServices.Notifications.UpdateAsync(notice with { Metadata = "3:46" }, cancellationToken);
await HostServices.Notifications.DismissAsync(notice.Key, cancellationToken);
```

Use `WidgetOperationLifetime.Widget` for intentional background notification work.
Keep optional notification failures separate from the feature doing useful work.
`Accepted` means the request was admitted and sent to the host; it is not proof of
banner display. Preferences, rate limits, quiet intervals and occupied screen
space can suppress presentation. Calls are bounded to one in flight and at most
five new/update publications per second per worker. Dismiss remains available.

## Independent options

| Option | Default | Behavior |
|---|---|---|
| Retention | Pending | Kept in the notification panel until opened, dismissed or withdrawn. BannerOnly never enters the panel or bell count. |
| Interaction | OpenWidget | A opens the owning widget normally. Informational has no open action and does not steal focus. |
| Visibility | OverlayHidden | Always also allows a banner while the overlay is visible. |
| ShowWidgetIcon | true | May be hidden when thumbnail artwork is the meaningful visual. |
| DurationMilliseconds | 4000 | Requested duration, from 2000 to 10000 ms. Quiet host policy still applies. |
| BackgroundOpacityPercent | 100 | Background only, from 40 to 100. Text and artwork retain full opacity; high contrast uses an opaque system surface. |

Keys are scoped to the authenticated widget. `ShowAsync` replaces a previous
notification with the same key. `UpdateAsync` only changes an existing current
entry, preserving its ID, selection and receipt time; it cannot recreate an
expired banner or change retention. Updating a visible banner does not extend
its deadline. Burst replacement also keeps the current deadline. After a banner,
the existing eight-second quiet interval prevents another immediate interruption.
Pending means retained for the current WidgetRail session, not stored across restarts.

Titles are bounded to 256 characters, body to 2048, metadata to 256 and artwork
URLs to 2048. Artwork must use HTTPS without embedded credentials. Host artwork
acquisition and decoding use the shared byte/pixel limits, a bounded target size
and cancellation; failed artwork leaves readable text and a placeholder. Provider
content is not written to diagnostic logs.

## Presentation and user control

Both sources use the same host cards, passive banner window, notification panel,
focus/layering infrastructure and placement policy. Corners avoid visible pins.
When the overlay is open, placement also avoids the widget, rail, Guide and open
popup surfaces. If no corner fits, the banner is suppressed. Fullscreen and system
pickers do not receive overlaid notifications. Banners never take controller focus
or use Windows notification UI.

Every widget's tray menu groups notification preferences under **Preferences**: Banner + list, List only,
or Off. Banner-only requests are suppressed by List only and Off; the host never
converts temporary status into pending entries. Widget-specific opt-in settings
can further disable a feature's notifications but cannot override a host mute.

WebView notifications keep their existing default card, originating-page callbacks,
pending retention and hidden-overlay-only banners. Browser lifecycle invalidation
and stale-widget fallback behavior are unchanged.

## Open a specific destination

Pending, interactive notifications can carry an optional `OpenIntent`:

```csharp
new WidgetNotification("download-ready", "Download complete")
{
    Body = "Open to view the completed item.",
    OpenIntent = WidgetIntentRequest.Create(myDetailsContract, payload,
        WidgetIntentPresentation.OpenWidget),
};
```

Declare the requested contract in the sender's manifest `intents.requests` as
for a normal intent action. The Bridge validates the declaration and payload when
publishing, stores a bounded opaque grant, and revalidates it on Open. Publication
never launches a destination. Open uses the existing handler resolution, dynamic
availability, presentation and delivery machinery. No platform permission is
implicitly granted. Other websites' notification click callbacks are unchanged.

Replacing or withdrawing the notification invalidates its earlier destination;
dismissal releases the grant. A stale source cannot deliver a stored intent into
a new worker generation. Existing host behavior can open the originating widget
when a notification is stale. `BannerOnly` and `Informational` notifications
cannot carry `OpenIntent`. Omitting the property retains Open widget behavior.
Older hosts reject the new field rather than silently executing different
navigation; authors should target a host release supporting notification intents.
