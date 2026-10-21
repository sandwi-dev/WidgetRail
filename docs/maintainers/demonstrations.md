# Product demonstrations and screenshots

There is no checked-in recording of the current desktop → WidgetRail → desktop
workflow. Sample MP4s in SDK/template assets are test media, not product demos.
The public README uses existing illustrative screenshots and has no recording
instructions or empty demo slot. Do not label a mockup as a working demonstration.

## Existing public images

| Images | Verification context |
| --- | --- |
| `docs/images/overview-rail.png`, `pinned-video.png` | Existing README illustrations from an earlier build. Exact tested build/configuration was not recorded with these assets. They do not demonstrate current controls or release acceptance. |
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
