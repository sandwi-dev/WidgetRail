# Current platform status

Reviewed against source on 2026-10-20: application `0.1.0-preview.36`, SDK/CLI
`0.4.6-dev`, presentation protocol 98. These are source versions, not a claim that
a corresponding release has been published or that every workflow is qualified.
See [versioning](versioning.md) for the authoritative version files.

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

Bare contains Settings and WidgetRail Catalogue. Production adds the initial
optional-widget selection in `eng/release-content.json`: Capture Library,
Performance Monitor, Now Playing, Games & Apps, Task Switcher, Audio Mixer,
Network Controls, Power and Browser. Both bundles have the same host capabilities,
runtimes and CLI. Developer edition is retired; SDK Gallery and other optional
widgets are distributed through Catalogue. A listing requires a published package
to be downloadable.

Production's initial packages are enabled once through ordinary sandbox package
admission, without granting capabilities. Existing selections and later removal
are preserved. Only Settings and Catalogue remain sealed built-in widgets; their
host-managed permission defaults preserve explicit user denials. Other packages
use the same review, consent, update and uninstall flow regardless of publisher.

## Implemented behavior and contracts

- Controller opening supports View + Menu or Guide/PS. Optional F1 is off by
  default and controlled from Settings → Controllers.
- Startup at sign-in is selected in the installer. The startup backend targets
  the WinUI executable, but the current WinUI handshake does not advertise the
  Settings startup switch. Windows Startup Apps controls an existing entry.
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
