# WidgetRail

### Your PC, from your controller.

WidgetRail brings games, music and everyday PC controls into one Windows
overlay. Open it with your controller, do what you need, and get back to your game.

It is also a framework for building your own controller-friendly widgets in C#.
You describe the UI; WidgetRail handles rendering, navigation, scrolling,
themes and the on-screen keyboard.

[Get started](docs/users/getting-started.md) ·
[Build a widget](docs/developers/widget-quickstart.md) ·
[Documentation](docs/README.md) ·
[Contribute](CONTRIBUTING.md)

## Keep your controller in your hands

- **Launch and switch apps.** Browse games and applications, switch between open
  windows with live previews, and close an app when you're done.
- **Control your audio.** Adjust individual apps, choose speakers and microphones,
  and change supported spatial sound options.
- **Keep music close.** Control Windows media sessions or add the Spotify and
  YouTube widgets.
- **Manage your PC.** Check connectivity, use Wi-Fi and Bluetooth controls, or
  put your PC to sleep, restart it, or shut it down.
- **Make it yours.** Reorder widgets, choose a theme, adjust scale, and pin
  supported widget views.

The default shortcut is **View + Menu**. Prefer the Guide button? Change the
shortcut in Settings. The on-screen controller guide shows the actions available
where you're focused.

## A home for your widgets

Build a small tool, a media experience, or a complete application interface.
Widgets share the overlay's visual language and controller behavior.

| You bring | WidgetRail provides |
|---|---|
| Your C# logic and data | Typed SDK, lifecycle and state helpers |
| Lists, cards, posters and controls | Responsive layout, focus navigation and scrolling |
| Your look and feel | Theme-aware styling with WRSS |
| A service or Windows integration | Permission-based [host services](docs/reference/capabilities.md) and [companion integration](docs/reference/community-companion-services.md) |
| A widget to share | Scaffolding, testing, packaging and installation tools |

WidgetRail uses a native Windows renderer. Standard declarative widgets do not
need a browser to draw their UI. Embedded web media is a separate, host-managed
feature.

Start with the [quickstart](docs/developers/widget-quickstart.md), explore the
[authoring guide](docs/developers/widget-authoring-guide.md), or learn from:

- [SDK Gallery](samples/SdkGalleryWidget/README.md): UI components and layouts.
- [Playnite Library](samples/PlayniteLibraryWidget/README.md): a game library with
  posters, search and controller browsing.
- [Spotify](samples/SpotifyWidget/README.md): search, playlists, playback and queue controls.
- [YouTube](samples/YouTubeWidget/README.md): embedded video and media navigation.

## Try WidgetRail

WidgetRail is currently a **source preview for Windows x64**. There is no
published end-user release or installer yet.

Follow [Build and run](docs/users/getting-started.md) to try the overlay locally.
Widget developers can start with the CLI and SDK without building the native host.
Service integrations may need their own account, configuration or companion app;
each sample explains its requirements.

Some games and Windows surfaces handle controller input differently. See the
[known limitations](docs/users/known-limitations.md), especially for elevated
games and optional Exclusive control.

## Help shape the platform

Useful contributions include widgets, themes, controller compatibility reports,
accessibility improvements and fixes to the framework itself.

Read [Contributing](CONTRIBUTING.md) for the project map and verification workflow.
For bugs, include your build, controller and clear reproduction steps; avoid
posting access tokens or unreviewed diagnostic files.

[Release readiness](docs/maintainers/release-readiness.md) tracks what remains
before the first downloadable release.

## Open platform, independent widgets

WidgetRail, its SDK, first-party widgets, samples and templates are MIT-licensed.
Build your own widgets
under the license you choose, and learn from or reuse our bundled widget code.

See [Licensing](LICENSING.md) for the boundaries and dependency requirements.
