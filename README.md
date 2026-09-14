<p align="center"><img src="assets/branding/widgetrail.svg" width="88" alt="WidgetRail icon"></p>

# WidgetRail

**Your PC's controls, media, and widgets—within reach of your controller.**

WidgetRail is a Windows overlay built for the moments between playing: change
the volume, choose your next track, switch apps, or keep a video beside your game.
Open it with **View + Menu**, do what you need, and get back to playing.

[Get started](docs/users/getting-started.md) · [Build a widget](docs/developers/widget-quickstart.md) · [Documentation](docs/README.md) · [Releases](https://github.com/sandwi-dev/WidgetRail/releases)

![The WidgetRail overlay over a game](docs/images/overview.png)

## Made for a controller

Move naturally through buttons, sliders, lists, and game posters with the D-pad
or left stick. Use the right stick to scroll. The controller guide changes with
your focus, so you can see the actions available without memorizing shortcuts.

Need to search or enter text? Bring up the built-in controller keyboard.
Prefer the Guide button to View + Menu? Change the opening shortcut in Settings.

## Keep a video in view

Pin a supported video view, then hide the main overlay. The video stays visible
while you use another app or play a game. Pin controls let you adjust its placement
and size, or remove it when you're done.

Widgets can also offer compact pinned layouts, such as music controls. They
choose which views make sense outside the full overlay.

![Pinned YouTube playback beside a game](docs/images/pinned-video.png)

Try the separately installed [YouTube widget](samples/YouTubeWidget/README.md),
or learn [how pinning works](docs/users/pinning.md). Availability over a game
depends on its display mode; see [compatibility notes](docs/users/known-limitations.md).

## Everyday controls, together

The standard edition includes these widgets:

| Widget | What you can do |
|---|---|
| Audio Mixer | Adjust app volumes, choose output and microphone devices, and select supported spatial sound options. |
| Games & Apps | Browse and launch your applications and discovered games. |
| Task Switcher | See live window previews, switch to an app, or ask it to close. |
| Now Playing | Control media sessions reported by Windows. |
| Network Controls | Manage supported Wi-Fi and Bluetooth controls. |
| Power | Sleep, restart, or shut down your PC. |
| Display Profiles | Save your monitor setups and switch between them with automatic rollback. |
| Settings | Choose your theme, manage widgets and permissions, and configure the overlay. |

The tray also keeps the time, internet connectivity, and Bluetooth status nearby.

Want more? The repository includes installable [Spotify](samples/SpotifyWidget/README.md),
[YouTube](samples/YouTubeWidget/README.md), and [Playnite Library](samples/PlayniteLibraryWidget/README.md)
widgets. These are separate add-ons, with their own setup and account or companion
requirements. They are not included in the standard installer.

## Make it yours

Choose a theme, adjust the interface scale, and arrange the widgets you use most.
Shared controls and controller hints follow the selected theme. Widget authors
can add artwork and animated background transitions that fit their content.

![WidgetRail theme comparison](docs/images/themes.png)

[Personalize WidgetRail](docs/users/customization.md)

## Build something for your setup

WidgetRail is also an **open-source C# widget framework**. Describe your interface
with SDK components and handle actions. The platform takes care of native
rendering, responsive layout, controller navigation, scrolling, and shared styling.

You can build a small utility or a multi-page experience. Start with a template,
then add the features you need:

- **Controller-ready components:** buttons, sliders, menus, posters, and responsive grids.
- **Paged collections:** load long lists as people browse, with shared loading and focus behavior.
- **Media and previews:** embed supported media or live application-window previews.
- **Pinned layouts:** offer a compact view that remains useful outside the main overlay.
- **Windows integrations:** request permission to use supported [host services](docs/reference/capabilities.md), or connect a [local companion](docs/reference/community-companion-services.md).
- **Developer tools:** scaffold, preview, test, and package widgets with `wrail`.

Standard widget interfaces use the native renderer. They do not need a browser
to draw their controls; embedded web media is a separate feature.

[Build your first widget](docs/developers/widget-quickstart.md) → [Understand the concepts](docs/developers/concepts.md) → [Explore the authoring guides](docs/developers/widget-authoring-guide.md)

## Get WidgetRail

WidgetRail is currently preview software for **Windows 10 (build 19041+) and
Windows 11, x64**. See [Releases](https://github.com/sandwi-dev/WidgetRail/releases)
for published downloads. If no release is listed yet, follow the
[source build instructions](docs/maintainers/building.md).

Choose **Production** for everyday use or **Developer** for additional sample
widgets and SDK Gallery. Both editions include the CLI and templates. Developer
is an edition with more examples, not a separate unstable update channel.

The installer includes a private .NET runtime and can install the required
Microsoft components. Updates are manual. Follow the
[installation guide](docs/users/getting-started.md) for setup and first use.

## Recommended setup

- Turn on **Automatically hide the taskbar** for more room around the overlay.
- Turn off **Xbox full-screen experience / Xbox mode** if your PC offers it, so WidgetRail runs alongside the normal Windows desktop.
- Use **borderless windowed** mode in games for the most reliable desktop overlay and pinned-video experience.

These are recommendations, not requirements. [Setup details](docs/users/getting-started.md#recommended-windows-settings)

## FAQ

### What if widgets look too big or too small, or the guide, tray, and widget overlap?

Open **Settings → Overlay → Interface size**. Decrease it if the overlay feels
too large or its parts overlap; increase it if widgets and controls are too small.
Adjust it until the overlay fits comfortably on your display. Interface size and
**Settings → Accessibility → Text size** are remembered for each display.
The controls show which display you are adjusting. A new display starts with your
existing default sizes; Windows display scaling is unchanged.

### Why does the overlay open but fail to take focus or switch apps?

Windows can refuse to give WidgetRail foreground focus, including over some
system windows such as Task Manager and Settings. The overlay may stay visible
while input still reaches the other app, and Task Switcher may be unable to
activate a window. This does not always mean the other app is running as administrator.

If the guide says **Focus blocked**, try clicking inside the overlay. If that
does not help, switch to the desktop or a regular app with Alt+Tab, then reopen
WidgetRail. [Foreground troubleshooting](docs/users/troubleshooting.md#the-overlay-cannot-take-foreground-focus)

### How do I install Playnite Library or another full-access widget?

Download its `.wrwidget` file and choose **Settings → Widgets → Install local
widget**. Full-access packages ask for confirmation because they run with your
Windows account's permissions. Review the source before approving installation.
They are installed disabled; review their settings and enable them separately.
Playnite also needs its [companion setup](samples/PlayniteLibraryWidget/README.md).

If an older build reports `full_trust_approval_required` without showing a
confirmation, update WidgetRail to a build containing the local-install fix.

### Why is an enabled widget missing from the tray?

Check whether the widget is also included under **Built in**. That copy takes
priority over an installed duplicate, even when the built-in copy is disabled.
Use **Manage built-in version** to enable it. Other widgets may need permission
review or a compatible package. [Manage widgets](docs/users/customization.md#manage-widgets)

### What if my controller shortcut does not open WidgetRail?

Make sure WidgetRail is running, then press **View + Menu** together and release
both buttons. Try **F1** on a keyboard. If you selected Guide as the shortcut,
Windows gaming features may also respond to that button.
[More troubleshooting](docs/users/troubleshooting.md)

## Open source, including the examples

The platform, SDK, first-party widgets, and templates are **MIT-licensed**.
Learn from the bundled widgets, reuse their code, and choose your own license
for the widgets you create. Third-party dependencies retain their licenses.

[Contribute](CONTRIBUTING.md) · [Report a bug](https://github.com/sandwi-dev/WidgetRail/issues) · [Licensing](LICENSING.md)
