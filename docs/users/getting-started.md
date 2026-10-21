# Install and get started

WidgetRail runs on Windows 10 build 19041 or newer and Windows 11, on x64 systems.
You can use a controller, with keyboard navigation available as a fallback.

## Choose an edition

| Edition | Choose it when… |
|---|---|
| Production | You want the everyday PC, audio, media, network, app, and Browser widgets. |
| Bare | You want Settings and WidgetRail Catalogue, then choose your own widgets. |

Both editions can install the same optional widgets through Catalogue. Developer
reference examples remain buildable from source. Only
Settings and Catalogue are built-in. Both include the `wrail` CLI and widget templates. They use the same application
version and share your WidgetRail data. Installing the other edition replaces
the active edition; it does not create an independent setup.

## Install

1. Get the installer for your edition from [GitHub Releases](https://github.com/sandwi-dev/WidgetRail/releases).
   If there is no published download yet, use the [source build guide](../maintainers/building.md).
2. Quit WidgetRail if another copy is running.
3. Run setup. It installs for your Windows account.
4. Review the startup option. It is off by default; enabling it starts WidgetRail quietly when you sign in.
5. Open WidgetRail from the Start menu.

You do not need the .NET SDK to use the app. Setup includes private .NET base and
Windows Desktop runtimes. Music playback uses the shared host browser runtime.
It checks GameInput and WebView2 and installs them if needed. GameInput may ask
for administrator approval. A missing WebView2 runtime requires an internet download.

If Microsoft's GameInput update becomes stuck, restart Windows normally and run
setup again. See [Troubleshooting](troubleshooting.md) for more help.

## Open the overlay

Press **View + Menu** together. Press the same shortcut again to hide it.
You can choose **Guide** instead in **Settings → Controllers → Controller shortcut**;
both the Xbox Guide button and PlayStation PS button are supported. For keyboard
opening, enable **Settings → Controllers → F1 keyboard shortcut**. It is off by default.

The row of widget icons is the **tray**. Choose an icon to open its widget.
Use the on-screen controller guide to discover the actions available at your
current focus. [Learn the controls](controls.md).

## Try one useful action

Production includes Audio Mixer. In Bare, install it from **WidgetRail Catalogue**
first. Open **Audio Mixer**, choose an application that is playing audio, and adjust its
volume. Approve access if requested. Press **View + Menu** again to hide the
overlay and return to your desktop or game.

For later sessions, select **Start WidgetRail when I sign in** during setup.
Windows **Settings → Apps → Startup** controls an existing startup entry; rerun
setup to create one if it is missing. The current overlay does not expose a
startup switch. Startup is optional and starts the application quietly.

## Recommended Windows settings

For a comfortable controller setup:

- **Automatically hide the taskbar:** on Windows 11, open Settings → Personalization → Taskbar → Taskbar behaviors. On Windows 10, use the taskbar's desktop-mode auto-hide setting. This leaves more room for WidgetRail near the screen edge.
- **Xbox full-screen experience / Xbox mode:** if your Windows build offers this mode, turn it off and use the normal Windows desktop with WidgetRail. Its name and availability vary by device and Windows version; look for the Xbox mode or full-screen experience setting on your PC.
- **Game Bar controller shortcut:** in Windows Settings → Gaming → Game Bar, turn off **Allow your controller to open Game Bar**.
- **View + Menu emulation:** if Windows offers **Use View + Menu as Guide button in apps** (shown with button icons), turn it off. Windows otherwise translates the combination into a Guide event before WidgetRail receives it. This is separate from allowing the controller to open Game Bar.
- **WidgetRail opening shortcut:** in WidgetRail Settings → Controllers, set **Controller shortcut** to **Guide**. Use the Xbox Guide button or PlayStation PS button to open and close WidgetRail. This reduces conflicts with games that use View and Menu.
- **Borderless windowed games:** start with this display mode when using the overlay or pinned video. Exclusive fullscreen can hide desktop overlays.

These choices are optional. WidgetRail does not change these Windows settings for you.

## Enable the features you want

1. Open **Settings → Widgets** and select a widget you want to use. Optional widgets,
   including those preinstalled by Production, request permission when a feature
   needs it. Review or revoke access in **Permissions & configuration**. A changed
   package may request access again. Enable the widget if it is disabled.
2. Choose **Disable widget** for widgets you do not need. They leave the tray
   and can be enabled again later. Settings stays enabled so you can always
   manage the application.
3. Open **Settings → General** to choose **Position**. Adjust **Interface size**
   in **Appearance**.
   B opens the radial chooser; directional navigation also lets you use the rail. See [Customization](customization.md) for the full set of options.

Spotify, YouTube, and Playnite are separate add-ons. Follow each widget's README
for installation and service setup; installing WidgetRail alone does not configure them.

## Updates

Updates are explicit. Open **Settings → About & troubleshooting → App updates**,
choose **Download update**, then **Install and restart**. Routine updates run
without a wizard when prerequisites are ready. If interactive setup is needed,
WidgetRail stays open and asks you to confirm that route; have a mouse and keyboard
available for setup and Windows approval prompts. See [App updates](../reference/app-updates.md)
for checks, preserved settings and failure recovery. You can also run a newer
installer manually.

Use **Catalogue → Updates** for individual updates or **Update all**. For a
local add-on, open its details in **Settings → Widgets → Update from file**.
Choose the newer `.wrwidget` package and approve full access if requested. Updates
preserve enabled state and ordinary widget settings. Permissions and credentials
are scoped to the package; a changed package can require approval or sign-in again. See
[Managing widgets](customization.md#manage-widgets).

## Uninstall

Quit WidgetRail, then uninstall it through Windows Settings → Apps. The uninstaller
asks whether to keep your data or delete it. **Keep data is the default.**
Deletion applies to WidgetRail data shared by this account, including development copies.
Shared Microsoft runtimes and optional controller drivers are not removed.

To remove the saved-data folder manually later, press Win+R, enter `%LOCALAPPDATA%`,
and delete only the `WidgetRail` folder. Temporary files are under `%TEMP%\WidgetRail`.
Close every WidgetRail copy first. To also unregister its Windows sandbox profiles,
reinstall and choose the uninstaller's delete-data option.

Credentials stored in Windows Credential Manager are kept, including provider
and add-on credentials. Full-access add-ons can also store data outside WidgetRail's
folders. Follow their instructions for any additional cleanup.
