# Widget icon provenance

WidgetRail package icons are sealed package resources admitted through the public
`presentation.packageIcon` and `iconAssets` manifest contract. Every icon keeps
the manifest's semantic `presentation.icon` as its deterministic fallback.

The following WIDGE-207 icons are original WidgetRail project artwork. They were
authored as small static SVG silhouettes for original-color presentation and do
not derive from an external icon library:

| Package | Asset ID | Source |
| --- | --- | --- |
| Now Playing | `media-sessions.mark` | `src/FirstPartyWidgets/MediaSessionsWidget/assets/icons/media-sessions.svg` |
| Games & Apps | `games-apps.mark` | `src/FirstPartyWidgets/GamesAppsWidget/assets/icons/games-apps.svg` |
| Audio Mixer | `audio-mixer.mark` | `src/FirstPartyWidgets/AudioMixerWidget/assets/icons/audio-mixer.svg` |
| Network Controls | `network-controls.mark` | `src/FirstPartyWidgets/NetworkControlsWidget/assets/icons/network-controls.svg` |
| Embedded Media Sample | `embedded-media.mark` | `samples/EmbeddedMediaWidget/assets/icons/embedded-media.svg` |
| YT Music | `ytmusic.mark` | `samples/YtMusicWidget/assets/icons/yt-music.svg` |
| Playnite Library | `playnite-library.mark` | `samples/PlayniteLibraryWidget/assets/icons/playnite-library.svg` |
| Clock | `clock.mark` | `samples/ClockWidget/assets/icons/clock.svg` |
| Full Application Reference | `full-application.mark` | `samples/FullApplicationWidget/assets/icons/full-application.svg` |

The YouTube package uses the user-supplied red digital YouTube icon. The supplied
Illustrator file is a one-page PDF 1.6 vector document created by Adobe
Illustrator 29.0. `samples/YouTubeWidget/assets/icons/youtube-red.svg` is a direct
static-vector transcription of its two painted paths, original `#ff0033` and
white colors, and exact cropped artwork bounds. It does not embed the supplied
PNG or any base64 raster payload.

Supplied source evidence:

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `yt_icon_red_digital.ai` | 333679 | `B5958B5F28B494B9E3E3962BDF5619C69C6733E409CC23845C6910AF84D80C22` |
| `yt_icon_red_digital.eps` | 805366 | `DAD0E500B6D4725C5F97A7D4AD49CDE5C03B33CC51D2A228A3B84592D5258AE0` |
| `yt_icon_red_digital.png` | 16687 | `1027B1B0517727ADB9697155A270744381C3CE9B047B1C8BD8A9389DC7D07A83` |

The accepted Spotify and SDK Gallery icon sets predate WIDGE-207 and retain
their existing source provenance. Settings intentionally retains its semantic
gear icon and has no package SVG. A package/reference entry in this table does
not enable, install, or select that widget; in particular, Embedded Media remains
subject to its existing catalog and release policy.

The Power widget uses original project-owned power, restart and sleep SVGs;
the tray and Shut down control share `power.shutdown`. The Task Switcher
package also uses original project-owned SVG artwork.
