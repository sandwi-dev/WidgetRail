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

## Choosing the running game window

If a launcher hands off to the game and Resume cannot find its window, open the
running game's options and select **Choose game window…**. Select the game itself
in the window picker. This requires window-switching permission. The Playnite
add-on remembers the choice for that game and profile and checks it against live
windows on subsequent launches. Use **Change game window…** to pick again or
**Use automatic detection** to clear the choice.

## Game session captures

The **Captures** tab on game details also shows WidgetRail screenshots, clips and saved replays from that game. Open the tab and allow library read and management access for automatic linking. The Playnite add-on records the identity of a newly launched process during Playnite's starting and started events, even with WidgetRail closed. It saves the observed launch and closes the interval when tracking stops. Each Playnite profile keeps up to 64 recent intervals per game. Completed capture links persist independently of that history.

On opening the game's Captures tab, the widget links media whose observed recording interval lies completely within a session for that process. It can reconcile later while the original media stays in the library. The achievement filter continues to show captures explicitly associated with that achievement. The add-on itself does not take screenshots or start a WidgetRail recorder.

Some Unity games report their crash handler as the launched process. For that specific helper, the add-on checks its live parent, executable paths, Unity player files and launch time before recording the player's identity. Unverified ancestry yields no automatic link. The tab opens a screenshot or clip with A, and B returns to the same capture. Down from the section tabs enters the latest capture and Up returns to the selected Captures tab.

For an external launch, choose **Link captures…**. The picker groups screenshots and clips from the same observed process instance. A suggested session can match words in the game's title or an executable you previously confirmed for that game and Playnite profile. Opening a session shows its time range and selected captures. A toggles a capture and X links the reviewed selection, up to 256 per batch. Captures without process evidence appear individually. A suggestion requires your choice.

You can also start in Capture Library. Choose **Link to game…** in a capture's details or select up to 64 captures in the gallery and use the same command. Search for a game in Playnite Library, choose it, and confirm the link. The media stays in Capture Library. Existing references to other games stay attached. An editable capture in Game Details offers **Unlink from this game** in its Menu, followed by Undo in the Captures tab.

When the session preview covers only part of the game’s library, **Browse all captures** opens the complete set inside Playnite. LB and RB change pages, A opens a capture, and Menu offers Unlink on editable links. Focus moves to Undo after unlinking and returns to the restored capture after Undo. **Open Capture Library** is still available for editing or exporting media.

In the **Link to game…** chooser, LB and RB page through games. A page turn or committed search focuses the first available match. From confirmation, B returns to the selected game on the same results page. **Cancel** closes the chooser.

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
