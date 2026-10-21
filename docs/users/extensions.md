# Browser extensions

WidgetRail can import user-supplied **Manifest V3 (MV3) browser extensions** through
WebView2. Open **Settings → Extensions** to get started. Extension compatibility
depends on the installed WebView2 runtime and the APIs that each extension uses.
Extensions are supplied by the user.

## Installing and managing

1. Obtain an MV3 extension as an **installable ZIP** or **unpacked extension folder** from its author.
   Source-code archives and `.crx` files are not installable through this UI.
2. Use **Settings → Extensions** to manage the shared library. Alternatively,
   use the tray's **Preferences → Extensions** for the current page's profile.
3. Choose **Install from ZIP** or **Install from folder**, review the requested
   website access and browser capabilities, and choose **Install**.

Install each extension once. **Enabled by default** controls whether it runs in
existing and future eligible profiles. Each profile can use **Default**, **On**,
or **Off**; explicit On/Off choices survive changes to the shared default.
The install review leaves the default checkbox off. New extensions stay disabled
in every profile, including the widget used to install them, unless you explicitly
enable the default during installation. Existing choices survive updates.

The tray retains its compact current-profile dialogs: choose an extension,
change **This profile**, open **Extension options**, or update/remove it. It does
not redirect to Settings. **Settings → Extensions** is a dedicated page with
shared defaults and a list of profile overrides. Each enabled profile runs the
extension separately and can add memory and CPU usage; enable it only where needed.
Open a Browser/webapp once to
register its profile. Newly registered profiles inherit each extension's default.

Reload existing web pages after changing enable state: disabling cannot undo a
content script that already changed a loaded page. Extension options are shown
only when the extension declares them and is enabled for the selected profile.

Updates are manual. Select the installed extension and import its newer package.
WidgetRail preserves its stable directory/ID and native extension settings.
Identity-changing updates are rejected; install those separately. Updates apply
to every profile using the shared package. Removal requires confirmation and
removes the extension and its saved data from every profile. To stop it in just
one profile, choose **Off** instead. Importing and updating require fresh review.

Options open as an additional hosted page; **B** closes it and returns to the
original live website. If another additional page already occupies that slot,
the existing replacement confirmation applies. Opening options does not reload
the original page or end its playback/call. Open options through the current
widget's **Preferences → Extensions**. Central Settings manages the shared
package and enable preferences; it does not launch another widget's options page.

## Profiles and privacy

- Browser now uses persistent storage, retaining website sign-ins across restarts.
  Its existing bookmark/history file stays at the same location.
- Package files are shared. Website cookies, sign-ins and extension settings
  remain isolated per widget/account profile. Same-profile hosted windows and
  pins share that profile's state.
- Private browser profiles are not offered for installation. WidgetRail does
  not change them to persistent profiles to enable extensions.
- Background music players and provider media surfaces are not offered in the
  extension manager. Enabling an extension for Browser does not add it to those players.

## Compatibility boundaries

Browser toolbar/action popups are **not exposed**. Extensions that require those
popups may not be usable here. Options pages are supported through the extension's
profile controls.

There is no extension-store browser, automatic extension updater, store signature
verification, or Windows notification UI. Extensions that depend on toolbar UI,
unsupported runtime APIs or native companion applications may not work.


Developers and maintainers: see [extension storage and profile contracts](../reference/browser-extensions.md).
