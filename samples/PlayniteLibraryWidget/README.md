# Playnite Library

Playnite Library is a controller-first, full-trust Community sample backed by a
user-installed Playnite Bridge instance. The package owns its bounded localhost
client, protected credential prompt, presentation, and organization state. It
does not copy a product app-library provider into the package.

## Setup and safety

Open **Playnite setup** in the widget, paste the Playnite Bridge token, and save.
The token is stored under the package-scoped Windows Credential Manager target
and is never rendered, logged, or copied into package or private state. The UI
offers explicit replace and remove actions. Missing credentials produce an
actionable setup screen; no environment variable is part of the product flow.

The transport is structurally limited to `127.0.0.1:19821`, disables proxy,
redirect, cookie, and decompression behavior, and exposes only the fixed routes
needed for bounded library reads, artwork, launch request admission, favorites,
hidden state, categories, and completion status. It does not expose arbitrary
methods or paths, install/uninstall, metadata refresh, deletion, evaluation, or
account operations.

## Data and authority

Playnite game GUIDs are the exact item, action, and mutation authority. Library
queries traverse deterministic 64-item Bridge pages up to 10,000 games, then
apply the current search, source, collection, sort, and 32-item presentation
window locally. Installed and owned-but-not-installed games remain distinct.
Manual and emulated games are ordinary Playnite records; optional source,
category, completion, metadata, and artwork fields fail closed when absent or
malformed.

Favorites, hidden state, category membership, completion status, and launch
requests are re-resolved against the current exact GUID before mutation. A
successful launch response means only that Playnite Bridge accepted the request;
it is not presented as proof that a process started. WIDGE-121 owns later
observed-start and overlay-close behavior.

The application may retain a bounded last-good catalog for presentation when
the Bridge becomes unavailable. Retained entries are marked stale, expose no
launch capability, and cannot authorize mutations. A fresh current observation
is required before any action.

## Build and package

From the repository root:

```powershell
pwsh -NoProfile -File samples/PlayniteLibraryWidget/Build-CommunityPackage.ps1 -Configuration Release
```

The script publishes the full-trust application, validates the staged manifest
and payload, rejects product-provider/debug files, and emits the immutable
archive under `artifacts/community-addons/playnite-library/`. It does not install
or launch unless its separate explicit install switch is supplied.

Focused deterministic coverage uses fake Playnite Bridge and credential seams;
it neither reads a real credential nor connects to a live Playnite instance.
