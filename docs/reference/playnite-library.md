# Playnite Library integration

Use the [package user guide](../../samples/PlayniteLibraryWidget/README.md) for
installation, guided connection setup, controls, achievements and recovery.
The [setup decision matrix](../../samples/PlayniteLibraryWidget/SETUP-DECISIONS.md)
is the maintained contract for running/stopped profiles, portable installations,
Bridge availability, ambiguity and safe replacement.

## Runtime and data ownership

Playnite Library is a full-access application widget. It owns its bounded local
Bridge client, credentials, guided setup and provider parsing. Playnite remains a
separate application. The widget's Windows Credential Manager entry is separate
from host-owned widget state; uninstalling the widget does not uninstall Bridge.

Home and Library publish immutable indexed queries. The frontend requests ranges
and owns virtualization, focus and scrolling. Provider pagination is internal;
rendered rows are not a moving cursor window. Opening/closing details preserves
the originating game; B at either main tab returns to the host tray.

Artwork can use local Playnite files through the framework's file-artwork
contract, with provider fallback. The widget resolves authority for requested
artwork; it does not embed every library image in its UI snapshot.

Achievements read PlayniteAchievements on demand per game, with SuccessStory
fallback. Activity uses GameActivity. Optional data can be absent even when the
games API works. Searching/filtering loaded achievements does not call the
provider. See [development documentation](../../samples/PlayniteLibraryWidget/DEVELOPMENT.md)
for eval cost, caching, artwork and state ownership, and
[indexed collections](../developers/indexed-collections.md) for SDK authoring.

## Achievement capture settings API

The local integration advertises `achievements.capture-settings.v1`.
`GET /v1/capture-settings` returns `availability` (`ready`, `missing`, or
`unsupported`), and when ready a revision, active-edit state, current settings,
provider-supported enum options, and effective screenshot/recording roots.

`PATCH /v1/capture-settings` uses the existing `X-WidgetRail-Command` idempotency
header. Its body contains `expectedRevision` and a nonempty `settings` subset.
Supported fields are screenshot enablement, clean/toast/framed variants, capture
delay and resolution; recording enablement, variants, pre-roll seconds, FPS,
resolution, quality and audio; and screenshot/recording directories. Enum values
must come from the returned options. Recording seconds describe pre-roll, not a
fixed total clip duration. Numeric writes accept delay 0–3600 seconds, pre-roll
5–60 seconds and FPS 10–60; unrelated provider settings remain unchanged.

Directory writes accept existing canonical absolute local directories without
reparse components. An empty directory retains the provider's default or fallback.
The widget first obtains a host-owned folder grant for library access, then uses
that selected path to configure the provider. Setting a provider path alone does
not grant the host file access. If configuration fails after choosing a new folder,
the widget can unlink that newly created source without removing provider files.

Writes run on Playnite's UI thread through the public `ISettings` edit/verify/save
transaction. They reject an already-open provider settings edit and stale revisions.
The integration reads persisted settings back through public `LoadPluginSettings`
because a successful live update alone does not prove the settings were saved.
A failed commit restores prior capture values and attempts persisted rollback;
`capture_settings_rollback_failed` explicitly reports an unsuccessful recovery.
The API never writes provider JSON directly or calls private provider methods.
