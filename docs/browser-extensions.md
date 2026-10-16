# Browser extensions

WidgetRail supports user-installed **Manifest V3 (MV3) extensions for Microsoft
Edge and other Chromium-based browsers** through WebView2. Extension compatibility
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
- Private profiles remain private. Installation is unavailable there: native
  functional extension checks did not pass in InPrivate on runtime
  **155.0.4283.45**. WidgetRail does not silently change
  private profiles to persistent.
- Packaged background web sessions (including music playback adapters) use a
  separate `pkg-` profile namespace. Provider media already has its own namespace.
  Neither is offered in the extension manager. Enabling the environment's
  extension service does not install extensions into those profiles.
- This is a host/user feature, not a new widget permission or JavaScript bridge.
  Widget authors choose their existing `BrowserStorage.Persistent` option; they
  cannot install extensions or inspect their data through the SDK.

## Compatibility boundaries

Validated on WebView2 **155.0.4283.45** using MV3 extension packages and synthetic
websites. Checks cover installation, request rules, content scripts, options
pages, storage, profile isolation, enable/disable, update, restart and removal.
These checks do not establish compatibility with every feature of every
Edge or Chromium extension.

Browser toolbar/action popups are **not exposed**. Navigating directly to a
popup's HTML does not reproduce browser-toolbar context: the active-tab query
in the probe selected the extension's own page, not the website. Options pages
are supported because they are designed as ordinary extension pages.

There is no extension-store browser, automatic extension updater, store signature
verification, or Windows notification UI. Extensions that depend on toolbar UI,
unsupported runtime APIs or native companion applications may not work.

## Storage and validation

Imports are copied to `BrowserExtensions/packages/<slot>/files` below the chosen
WidgetRail settings root. WebView2 references that stable directory; deleting the
original downloaded ZIP/folder is safe after a successful import. Each library
entry uses one stable package directory across all profiles. Catalog metadata
stores defaults, explicit overrides and applied revisions; WebView2 owns each
profile's extension data. Interrupted updates restore the prior package and
profile policies; interrupted removals finish on the next initialization.

The importer rejects path traversal, absolute/alternate-stream/device paths,
archive links, folder reparse points, duplicate files, nested source archives,
and packages over 256 MB or 25,000 entries. Filesystem work runs away from the
UI thread; native WebView2 calls stay on their creating thread. Update journals
restore the previous package after an interrupted replacement. Native runtime
extensions such as Edge's PDF viewer are not exposed as user-managed installs.

Validation artifacts live in ignored `artifacts/browser-extensions`. Functional
checks use isolated profiles and synthetic websites. Extension performance
benchmarking is intentionally outside this change's scope.
