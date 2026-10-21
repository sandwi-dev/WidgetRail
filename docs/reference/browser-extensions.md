# Browser extension contracts

For installation, profile choices, options and limitations, use the
[user guide](../users/extensions.md). This page describes host ownership and storage.

## Profile boundary

The shared library manages user-supplied MV3 packages. Only eligible persistent
browser profiles participate; private, packaged background (`pkg-`) and provider
media profiles are excluded. Widget authors select existing browser storage
options, but cannot install extensions or read their state through the SDK.
Profiles isolate website data; sharing the WebView2 environment does not combine
cookies or extension settings. Same-profile hosted windows and pins share data.

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

## Validation scope

Native checks on WebView2 155.0.4283.45 covered synthetic MV3 packages, request
rules, content scripts, options, storage, profile isolation, enablement, update,
restart and removal. That does not establish universal extension compatibility or
comparative performance. Recheck with the runtime and packages being released.
Browser toolbar popups and a store installer/updater are not implemented.
