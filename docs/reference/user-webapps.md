# User-created webapps

Open **Settings → Widgets → Add webapp**, enter or paste a website address, optionally edit its name, then choose **Add and open**. The new webapp is enabled immediately. The browser's **Page actions → Add as webapp** uses the same form with the current address and title filled in.

Each webapp has a stable identity and its own persistent browser profile. Renaming it preserves sign-in, website permissions and extension preferences. It uses the host's standard webapp browser, notifications, pinning and lifecycle behavior without a separate widget executable. Tray **Widget → Manage widget** opens its editor; **Settings → Widgets → Manage webapps** also provides rename and removal. Confirmed removal deletes the entry, cached icon and its browser data, including sign-ins, bookmarks/history and profile-specific extension settings. No second data-deletion prompt is shown. Locked native profile files are deleted automatically by WebView after its browser process exits. Renaming, disabling and restarting preserve data.

Turn off **Enabled** in the editor to remove a webapp from the rail and radial chooser without deleting it. It remains available in **Manage webapps** so you can enable it again.

HTTP-only sites keep ordinary links inside the webapp, matching the browser's navigation policy. HTTPS sites use the standard webapp policy for links to other origins.

The host obtains the website icon from WebView2 when the configured site loads and caches it on disk. The rail and radial chooser can display that cache after restart without initializing the website. Navigation to unrelated websites does not replace the icon. Missing or invalid icons use the built-in fallback; failed refreshes retain the last valid icon.

Definitions are stored in `webapps.json` under the active settings root. Icons are bounded PNG files in `webapp-icons`. Writes are atomic and serialized, edits detect stale revisions, and late icon callbacks cannot recreate removed entries. The editor accepts only HTTP(S) addresses without embedded credentials. Website code remains inside the existing WebView boundary and does not receive widget capabilities.
