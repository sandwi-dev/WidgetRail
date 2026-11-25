# Current platform status

Reviewed against source on 2026-11-25: application preview `0.1.0-preview.44`, SDK/CLI
`0.4.9-dev`. The current refinement source uses presentation protocol 148, including opt-in live status text, addressed widget activity summaries, background intent delivery, preferred collection entry and
remote-image/media galleries and explicit passive video animation in addition to backend web
sessions, native capture, explicit modal dismissal, exact pin-selection identities,
interactive/passive pin declarations, desktop acrylic surfaces, opted-in pin
restoration, numeric values in interactive rich previews, graph empty-state text,
bounded indexed selection, query-bound occurrence lookup, extended saved-video metadata,
command menus, primary rich-preview entry, worker-bound inline preview actions,
opted-in native saved-media previews, semantic action glyphs, bounded timeline
following/recovery, inline rich images, source-bound native notification intents,
opt-in boundary demands/deferred reveals and message-content focus targets, and
capability-qualified recording settings and opted-in held-trigger selection. The local refinement
branch supports up to six pins with shared adjustment controls, three incoming
Discord streams, and the independent local Playnite integration; see
[implementation and qualification](../input-discord-refinements-plan.md).
The [GitHub releases page](https://github.com/sandwi-dev/WidgetRail/releases) lists published installers and widget packages. See [versioning](versioning.md) for the source version files.

Discord Controls `0.1.0.80` is a full-access Catalogue package. Its web client integration supports calls, messages and stream pins. Playnite `0.4.100` can request access to its friend and voice activity and open a stream pin from the Currently Playing shelf. Discord's guide covers the supported streaming modes and the client-modification Terms of Service warning. [Discord guide](../users/discord.md)

## Frontend and distribution

`OverlayFrontend.WinUI` is the production frontend. XAML owns ordinary controls,
layout, virtualization, focus and accessibility; Microsoft.UI.Composition owns
presentation motion. Native Windows, controller and GPU capture work remains in
`OverlayPlatformInterop` and `WinUiWindowPreviewNative`. The original renderer and
Rust/Taffy build graph are retired. Browser and media surfaces use host-owned WebView2.

Distribution uses unsigned, per-user Inno installers with an unpackaged,
self-contained WinUI frontend and private service runtimes. No application MSIX,
identity-package registration or certificate trust is required. See the
[installer contract](../../eng/installer/README.md).

Bare contains Settings and WidgetRail Catalogue. Task Switcher and Notifications
are host utilities available in both bundles. Production adds the initial
optional-widget selection in `eng/release-content.json`: Capture Library,
Performance Monitor, Now Playing, Games & Apps, Audio Mixer,
Network Controls, Power and Browser. Both bundles have the same host capabilities,
runtimes and CLI. Developer edition is retired. SDK Gallery, Clock, Embedded Media
Sample and Full Application Reference remain buildable source examples; they are
not Catalogue listings or required release downloads. Other optional widgets are
distributed through Catalogue. A listing requires a published package to be
downloadable.

Production's initial packages are enabled once through ordinary sandbox package
admission, without granting capabilities. Existing selections and later removal
are preserved. Only Settings and Catalogue remain sealed built-in widgets; their
host-managed permission defaults preserve explicit user denials. Other packages
use the same review, consent, update and uninstall flow regardless of publisher.

## Implemented behavior and contracts

- Controller opening supports View + Menu or Guide/PS. The optional configurable
  keyboard shortcut is controlled from Settings → Controllers.
- Startup at sign-in is available in the installer and Settings → General. The
  WinUI host advertises the startup backend, which targets its executable with
  `--hidden`. Windows Startup Apps can override an existing entry. This restores
  the Settings switch hidden in preview.37 and earlier WinUI releases.
- App updates use explicit download and install actions. Supporting installers
  use unattended routine installation after preflight, with a separately confirmed
  interactive fallback. See [App updates](../reference/app-updates.md).
- Pins are session-only; placement preferences survive restart, pinned selection
  does not. See [Pinning](../users/pinning.md) for input and lifecycle behavior.
- Shared authoring includes responsive layouts, indexed collections, rich text,
  keyboard suggestions, rich previews, anchored inspection, scroll reveal, native
  media, hosted browsers and provider documents. Use the
  [WinUI authoring contract](../reference/winui-authoring.md) and
  [reference index](../reference/README.md) for exact supported declarations.
- [Intents](../reference/widget-intents.md) support handler availability, delivery
  feedback, Windows routing and passive pinned destinations. B follows ordinary
  widget/tray navigation; there is no return-to-sender stack.
- [Capture and Game Help](../reference/widget-capture-and-game-help.md) keep capture
  host-owned. Game Help owns its provider client, credentials and response handling.
- The old held-D-pad preference is not consumed by WinUI; ordinary directional
  navigation can scroll when the last focusable control has been reached.

Playnite Library 0.4.5 uses WidgetRail for Playnite 1.1.0, an independently identified,
authenticated loopback integration with port discovery and typed API operations.
The Achievements destination and progress tracker consume optional provider cache
data and events. Refresh/Y in a game's Achievements tab also requests provider
synchronization when available. See the [integration plan](../playnite-local-integration-plan.md).
The experimental desktop-acrylic navigation bars were reverted; existing panel
styling and accepted whole-surface/pin/banner materials remain.

## Qualification and documentation

Report automated checks, native inspection and physical acceptance separately,
against the exact source and payload under test. A successful build or fixture
cannot establish couch readability, controller hardware behavior, every Windows
provider, mixed-monitor behavior, clean-machine prerequisites or a real installed
update/uninstall. Preserve outstanding acceptance requirements in the active
verification record and [release checklist](release-checklist.md).

Deferred CI repairs remain separate work. No status document authorizes a merge,
push, release, installation or profile change. Current guides describe supported
behavior; Git history retains superseded plans and experiments. Do not restore old
MSIX or renderer workflows from historical logs. Documentation checks validate
links and selected contracts, not every prose claim or runtime behavior.
