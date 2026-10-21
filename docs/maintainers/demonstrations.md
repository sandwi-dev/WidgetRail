# Product demonstrations and screenshots

The [widget gallery](../users/widget-gallery.md) contains native 16:9 screenshots
captured on 21 October 2026 over Hollow Knight: Silksong. The README uses the
Audio Mixer and pinned-video examples from that set. The [capture manifest and
credits](../images/widgets/README.md) identify the actual candidate, installed
widget versions, display configuration, input method and content sources.

There is still no checked-in recording of the complete game → WidgetRail → game
workflow. Sample MP4s in SDK/template assets are test media, not product demos.
Do not label a mockup, a set of stills or a standalone game clip as that workflow.

## Capture coverage

The set contains 32 screenshots covering 19 marketplace widgets plus Settings and
Catalogue. Discord and Recent Apps were intentionally skipped. Playnite titles,
play history and achievement progress are approved for inclusion; account
identifiers remain excluded. Task Switcher was captured after the other
applications were minimized, so their contents are not visible.

Catalogue image URLs should reference the commit containing the reviewed PNGs.
Local URL edits are preparation only: the images become available remotely after
that commit is published, and customers receive the changed listing through the
app's catalogue distribution path. Check URL availability before release.

## Existing public images

| Images | Verification context |
| --- | --- |
| `docs/images/widgets/` | Reviewed 21 October 2026 local-candidate captures; provenance and image hashes are beside the files. These are not release-acceptance evidence. |
| `docs/images/overview-rail.png`, `pinned-video.png` | Earlier illustrations retained for historical references. Exact tested build/configuration was not recorded; the README now uses the new gallery images. |
| `docs/images/overview-games-apps-radial.png`, `themes.png`, `sdk-gallery.png` | Existing supporting illustrations; likewise not evidence of the current build. |
| Package `screenshots/` folders | Use each package's captions where available. Do not infer a current build from the file's timestamp or the commit adding it. |

Keep images that illustrate still-supported concepts; replace ones whose visible
flow contradicts the current app. The documentation checker verifies local image
existence, not visual accuracy. See [PoB's screenshot context](../../samples/PobCompanion/screenshots/README.md).

## Record the primary demonstration

Record one uninterrupted supported configuration:

**Desktop or borderless game → View + Menu → adjust app audio → switch an app or
pin content → hide WidgetRail → continue the original activity.**

Keep the actual opening shortcut visible, including if configured to Guide/PS.
Show that WidgetRail was started first or starts at sign-in; avoid implying that
picking up a controller starts it automatically.

Add focused clips for creating a webapp, controller browsing/text entry, a
supported cross-widget action, notifications without focus loss, and pin interaction.
Record only already-authorized service interactions; avoid visible credentials,
private notifications and unrelated personal data.

For each new clip/image, record the app version and source commit, widget versions,
Windows/WebView2 versions where relevant, display resolution/scale, theme,
controller/input method and the workflow actually exercised. Then embed or link
the real media in the relevant user guide. Until recorded, keep production tasks
here rather than publishing placeholders in the README.
