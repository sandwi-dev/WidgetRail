# Integration setup and recovery

Install optional widgets through **Catalogue**, or a published `.wrwidget` through
**Settings → Widgets**. A fresh interactive installation starts enabled after any
required review. Updates preserve the existing enabled state; CLI installation
leaves new widgets disabled. Installation alone does not establish a working
service connection. Catalogue listings need a compatible published package.

| Integration | Available without service setup | Additional requirements | Setup, verification and recovery |
| --- | --- | --- | --- |
| Playnite Library | Connection setup | Playnite and its separate Bridge plugin; guided setup supplies a tested Bridge build and token connection | [Playnite guide](../../samples/PlayniteLibraryWidget/README.md#connect-your-library) |
| Spotify | Setup and account connection | Your Spotify developer app; Premium for streaming and playback-control APIs | [Spotify guide](../../samples/SpotifyWidget/README.md#connect-spotify) |
| YouTube Video | Supported public video links | A YouTube Data API key for search; provider restrictions still apply to playback | [YouTube guide](../../samples/YouTubeWidget/README.md#get-started) |
| YouTube Music | Supported public search/playback | Google account for personal library and likes; Chrome or Edge for sign-in | [YouTube Music guide](../../samples/YtMusicWidget/README.md#get-started) |
| Game Help | Setup | Your Gemini key and available API quota; capture permissions for images/clips | [Game Help guide](../../samples/GameHelpWidget/README.md) |
| PoE1 / PoE2 Path of Building | Local build creation, editing and calculations | Internet for remote imports/trade; official trade sign-in when required | [PoB guide](../../samples/PobCompanion/README.md) |
| Display Controls | Display/profile controls after permission | Compatible display hardware and Windows support | [Display Controls guide](../../samples/DisplayProfilesWidget/README.md) |
| Discord, Twitch, MapGenie and your own webapps | The website's public features | Website accounts/subscriptions where that service requires them | [Webapps](webapps.md) and [browser compatibility](browsing.md#compatibility-and-extensions) |

Service quotas, account restrictions and charges remain the provider's. A
subscription is not a guarantee that every API or embedded playback route works.
Follow the integration's own verification step before treating setup as complete.

## Disconnect before removal when needed

Disabling keeps saved data; it ends the WidgetRail session, not your remote account.
Uninstalling removes host-owned widget state and browser profiles. Full-access
integrations can also own encrypted files or Windows Credential Manager entries
outside that cleanup. Use their own disconnect/remove-key action first when you
want those removed, and revoke provider access at the service when appropriate.

Removing Playnite Library does not uninstall Playnite or its Bridge plugin.
Removing a webapp does not delete your website account or downloaded files.
[Lifecycle and saved data](lifecycle.md) explains the shared boundaries; individual
guides below describe their credential stores.
