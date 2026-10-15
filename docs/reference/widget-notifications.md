# Widget notifications

`HostServices.Notifications` is an intrinsic host service for ordinary sandboxed
workers and full-trust application widgets. No manifest permission is required.
User notification preferences remain authoritative. A resident widget may publish
in Background, Visible or Interactive state; the service does not keep a widget
running by itself. Destroyed workers cannot publish.

```csharp
var notice = new WidgetNotification("now-playing", song.Title)
{
    CategoryId = "song-changes", // declared in the manifest below
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
| Visibility | OverlayHidden | Always allows banners with the overlay open. SourceHidden admits a new notice only while neither the main widget nor a visible pin shows its owner. |
| ShowWidgetIcon | true | May be hidden when thumbnail artwork is the meaningful visual. |
| DurationMilliseconds | 4000 | Requested duration, from 2000 to 10000 ms. Quiet host policy still applies. |
| BackgroundOpacityPercent | 100 | Background only, from 40 to 100. Text and artwork retain full opacity; high contrast uses an opaque system surface. |

Keys are scoped to the authenticated widget. `ShowAsync` replaces a previous
notification with the same key. `UpdateAsync` only changes an existing current
entry, preserving its ID, selection and receipt time; it cannot recreate an
expired banner or change retention or category. Updating a visible banner does not extend
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

All banner content shares one themed surface: a subtle vertical shade, rounded
edge lighting and an outer-only native shadow. A transparent gutter is included
in the window dimensions and placement checks so shadows are not clipped or laid
over another surface. Background opacity does not fade text or artwork, and the
shadow is masked out of the card interior. High contrast uses an opaque surface,
solid contrasting outline and no shadow. Native depth resources are reused across
banner updates and resized in place; the existing quiet timing and fade behavior
are unchanged. These are host styles, not new widget layout/API options.

The tray menu offers **Notification preferences** directly when it is the only
preference, or under **Preferences** alongside Website settings. Choices are
All notifications is the master control; General covers uncategorized and website
notifications. Pending/mixed categories offer Banner + list, List only, or Off;
banner-only categories offer On/Off. Banner-only requests are suppressed by the
master List only and Off choices; the host never
converts temporary status into pending entries. Category preferences share this host-owned UI. A category cannot override a host mute.

WebView notifications keep their existing default card, originating-page callbacks,
pending retention and Always visibility (overlay open or hidden). General delivery/opacity
preferences apply to them without replacing website activation callbacks. Browser lifecycle invalidation
and stale-widget fallback behavior are unchanged.

The ordinary SDK `WidgetNotification` default remains OverlayHidden for compatibility.
Spotify, YouTube Music, SDK Gallery examples and app updates explicitly use Always;
Game Help uses SourceHidden to avoid announcing an answer already on screen.
Song changes start enabled for new installations, without overriding saved choices.

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

## Widget icon badges

Pending entries also contribute to the owning widget's rail and radial badge.
Banner-only entries do not. See [widget activity and icon badges](widget-activity.md)
for display priority, automatic audio detection and ordinary-widget reporting.

## Categories and shared configuration

Declare up to eight named categories. IDs are stable ASCII keys (1–64 characters);
`general` is reserved for callers that omit `CategoryId`, including websites.
Names are at most 64 characters and descriptions at most 256, without control or
formatting characters.

```json
"notificationCategories": [
  {
    "id": "song-changes",
    "name": "Song changes",
    "description": "Brief song banners; never added to the notification list.",
    "enabledByDefault": true,
    "retention": "bannerOnly",
    "defaultOpacityPercent": 100
  }
]
```

Defaults are recorded at first use and never replace saved choices. Preferences
are host-owned and survive widget updates; they do not depend on the package hash.
The existing per-widget setting is the master: Off suppresses all categories,
and List only suppresses their banners. An explicit category opacity overrides
`BackgroundOpacityPercent`; the unconfigured General category preserves the
notification's requested opacity. High contrast still wins.

A setup/settings action opens exactly the same controls as the tray:

```csharp
await HostServices.Notifications.ConfigureAsync("song-changes", cancellationToken);
```

This requests UI; it cannot silently change settings. It requires an interactive
worker and a current, visible input-owning surface. `Accepted` means delivered,
not that the user saved a preference. Handle capability failure without breaking
the underlying feature. Do not keep a duplicate widget-local enable/opacity value.
Configuration has a separate bounded admission interval, so a song change cannot
consume the user's preferences request.

`SourceHidden` is useful for a result that already has an in-widget review path.
It does not create a new pending entry when its owner is visible. Existing pending
entries remain until explicitly opened/dismissed/withdrawn; showing the owner does
not silently clear them. A new `ShowAsync` still replaces its key, including when
SourceHidden suppresses that replacement because the result is already visible. Quiet updates cannot resurrect a suppressed notice.
Game Help uses this policy with a bounded result ID and its declared result intent.
Capture Library needs no completion notice: its existing review flow remains the
destination.

These additions require a host release containing notification categories; older
hosts reject unknown manifest/command fields. Existing notifications without
categories retain their defaults. The rendered-view protocol is unchanged. No new
keep-alive, background polling, Windows notification UI, inline replies or
restart-persistent pending notifications are introduced.

### Category delivery contract and preference navigation

A category may declare `retention: "bannerOnly"` or `retention: "pending"`.
When omitted, both retention kinds remain supported for compatibility. This is
validated against each publication at the broker/frontend boundary; it does not
convert a notification from one kind to another. The shared General category
continues to accept both kinds.

Banner-only categories offer **On/Off** plus **Banner opacity**, never “List only”
or “Banner + list.” Pending categories offer **Banner + list / List only / Off**.
The per-widget master still takes priority. A previously saved List only choice
for a now-banner-only category remains suppressed and appears as Off; existing
On/Off and opacity choices are not reset by the declaration update.

Settings/action lists use the shared first-option focus policy. Preference value
pickers focus and reveal the current value; returning restores the originating
row. Preference pages use Back/Done because changes save immediately. Permission
and destructive confirmations retain safe Cancel focus. B remains available.
