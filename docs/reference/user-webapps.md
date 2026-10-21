# User-created webapp contracts

For adding, renaming, disabling and removing websites, use the
[webapp user guide](../users/webapps.md).

Each webapp has a stable identity and a persistent profile isolated by widget and
account. Rename preserves the identity; removal retires the profile and cached
icon. Locked native files can finish deleting after WebView2 exits. The host uses
its standard website permissions, notifications, pinning and lifecycle.

HTTP-only sites keep ordinary links inside the webapp. HTTPS sites use the
standard webapp policy for links to other origins. The host caches a bounded site
icon on disk; unrelated navigation cannot replace it and late callbacks cannot
recreate a removed definition.

Definitions are stored in `webapps.json` under the active settings root. Icons are bounded PNG files in `webapp-icons`. Writes are atomic and serialized, edits detect stale revisions, and late icon callbacks cannot recreate removed entries. The editor accepts only HTTP(S) addresses without embedded credentials. Website code remains inside the existing WebView boundary and does not receive widget capabilities.

See [hosted browsers](widget-web-browser.md) for session and resource limits.
