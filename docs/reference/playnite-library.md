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
