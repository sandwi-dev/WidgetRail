# WidgetRail identity and clean-cutover contract

Status: approved and frozen DLV-257 contract

Drafted: 2026-08-16

Approved: 2026-08-17

Clean-break amendment approved: 2026-08-17

This reviewer-owned document freezes the intended product identity before any
production rename begins. The user approved every material choice in this
contract on 2026-08-17, so it is the exact mapping authority for DLV-258 through
DLV-260. It does not authorize publication, account actions, repository moves,
Store reservation, domain registration, or work outside those assigned
deliverables.

## Product identity

The user selected:

- Display name: **WidgetRail**
- Tagline: **WidgetRail — a controller-first widget platform for Windows.**
- Ecosystem names: **WidgetRail SDK**, **WidgetRail Community**, `wrail` CLI,
  and `.wrwidget` packages.
- Technical PascalCase root: `WidgetRail`
- Lowercase repository/package slug: `widgetrail`
- Public SDK/NuGet identity: `WidgetRail.WidgetSdk`

The following mechanical extensions are approved and frozen:

| Surface | Current | Proposed |
| --- | --- | --- |
| Publisher display | Not frozen | `WidgetRail Project` |
| Product-owned package/publisher root | `org.gbar` | `widgetrail` |
| CLI executable and command | `gbar`, `gbar.exe` | `wrail`, `wrail.exe` |
| Project-local tool/config directory | `.gbar` | `.widgetrail` |
| Environment-variable prefix | `GBA_` / `GBAR_` | `WRAIL_` |
| Widget archive | `.gbarwidget` | `.wrwidget` |
| Global theme archive | `.gbartheme` | `.wrtheme` |
| Launcher experience archive | `.gbarlauncher` | `.wrlauncher` |
| Style language/acronym | GBSS / `.gbss` | WRSS / `.wrss` |
| Scenario manifest | `gbar.scenarios.json` | `widgetrail.scenarios.json` |
| Local application-data root | `%LOCALAPPDATA%\GameBarAlternative` | `%LOCALAPPDATA%\WidgetRail` |
| HTTP user agent | `GameBarAlternative/<version>` | `WidgetRail/<version>` |
| Random/internal prefix | `gba-` / `gbar-` | `wrail-` |

`widgetrail` is the approved technical publisher root instead of
`org.widgetrail` or `com.widgetrail` because no corresponding domain ownership
has been established. It remains a dotted-ID-compatible first segment without
claiming a reverse-DNS namespace the project does not own.

## Preliminary availability evidence

Searches performed on 2026-08-16 found no exact indexed result for
`WidgetRail` as software, a Windows app, a GitHub repository, a NuGet package,
or a trademark. An exact GitHub repository search also returned no repository.
These are encouraging negative search results, not proof of availability.

- Microsoft requires a unique Store product name, and an apparently unused
  public name may still be privately reserved. Only Partner Center can confirm
  and reserve it: [Microsoft Store name reservation](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/reserve-your-apps-name).
- Microsoft's app guidance requires an independent name and collateral free of
  Microsoft Brand Assets: [Microsoft Trademark and Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks).
- USPTO advises searching similar marks and related goods/services, not only an
  exact spelling, and recommends qualified counsel for clearance:
  [USPTO comprehensive clearance guidance](https://www.uspto.gov/trademarks/search/comprehensive-clearance-search-similar-trademarks).
- `widgetrail.com` registration status could not be established from indexed
  search results. A live registrar/RDAP check and any registration are external
  actions and remain pending.
- GitHub organization/user ownership and a future `widgetrail` repository name
  remain unreserved external identities.

No legal conclusion is made here. Before public release, obtain a professional
trademark clearance opinion and reserve the Store/repository/domain identities
through the user's own accounts.

## Repository inventory

A current non-Avalonia, non-history, non-generated scan at accepted main
`1ae21df` found:

- 5,889 `GameBarAlternative` occurrences across 485 files.
- 47 `Game Bar Alternative` display-name occurrences across 30 files.
- 284 `org.gbar` occurrences across 69 files.
- 191 `.gbarwidget` occurrences across 42 files.
- 75 `.gbartheme` occurrences across 14 files.
- 33 `.gbarlauncher` occurrences across 9 files.
- 1,384 word-form `gbar` occurrences across 168 files.
- 14 `GBAR_` and 334 `GBA_` occurrences.

Most changes are mechanical namespace/import/test/document updates. The
identity-sensitive seams below require explicit clean cutover or residue review.

## Exact old-to-new mapping

### Public managed identity

Apply one mechanical mapping:

- Namespace and assembly root `GameBarAlternative.*` -> `WidgetRail.*`.
- SDK package `GameBarAlternative.WidgetSdk` -> `WidgetRail.WidgetSdk`.
- CLI project/namespace `GbarCli` / `GameBarAlternative.GbarCli` ->
  `WrailCli` / `WidgetRail.WrailCli`; distributed command `wrail`.
- Product metadata, authorship, templates, samples, active public docs, build
  scripts, project files, solution entries, resource logical names, and package
  outputs use `WidgetRail` only.
- Generic executable filenames that carry no old brand (`OverlayHost.exe`,
  `WidgetBridge.exe`, `WidgetWorkerHost.exe`) remain unchanged. Their assembly,
  manifest, file-description, UI, and accessibility metadata become WidgetRail.

Do not ship dual public SDK namespaces, type forwarders, old/new package IDs,
or compatibility facades. This is a pre-release cutover.

### Package and publisher identifiers

Map only project-owned identifiers; never rewrite an unknown third-party ID
merely because it begins with `org.gbar`.

- `org.gbar.firstparty.*` -> `widgetrail.firstparty.*`
- `org.gbar.builtin.*` -> `widgetrail.builtin.*`
- `org.gbar.samples.*` -> `widgetrail.samples.*`
- `org.gbar.community.reference.*` ->
  `widgetrail.community.reference.*`

The identity inventory must enumerate the exact known IDs at implementation
time. Built-in Settings, bundled widgets, sample packages, the Community Game
Launcher reference, themes, consent definitions, catalog fixtures, and package
metadata must agree on the new exact IDs. Third-party IDs such as
`dev.example.*` remain unchanged.

### Package formats and developer tools

- `.gbarwidget` -> `.wrwidget`
- `.gbartheme` -> `.wrtheme`
- `.gbarlauncher` -> `.wrlauncher`
- GBSS and `.gbss` -> WRSS and `.wrss`
- `gbar.scenarios.json` -> `widgetrail.scenarios.json`
- `.gbar` local feed/config root -> `.widgetrail`
- `gbar` help text, examples, scripts and environment names -> `wrail`

Old archive extensions are removed from the new-install/public path. New code
does not import old archive formats or retain a legacy-format fallback. This is
a pre-release clean break; packages needed after cutover are installed again in
the new format.

### Native and operating-system identity

Use the exact `WidgetRail` root for:

- Window classes: `WidgetRail.OverlayHost`, `WidgetRail.Backdrop`,
  `WidgetRail.Chrome`, `WidgetRail.TextEntryModal`, and the pinned-surface class.
- UI Automation root name and automation ID: `WidgetRail` and
  `WidgetRail.Overlay`.
- App manifest assembly identity: `WidgetRail.OverlayHost`.
- Singleton mutex and activation pipe:
  `Global\WidgetRail.OverlayHost.Owner.<identity>` and
  `\\.\pipe\WidgetRail.OverlayHost.Activation.<identity>`.
- Random host/worker/broker/diagnostic pipe prefixes and temporary directories:
  `wrail-`.
- AppContainer profile prefix: `WidgetRail.Widget.`.
- HTTP user agent and artwork/resource cache keys: `WidgetRail/<version>` and
  `wrail-artwork`.
- Product-owned persisted-format headers such as pinned placement, performance
  runtime, scroll evidence, and launcher selection: `wrail-...` with their
  existing schema version retained unless the payload itself changes.

There must be one new singleton/window/pipe authority. Old names are residue
audit or generated-output cleanup inputs, never a simultaneously live
compatibility runtime.

### Local state and secrets

WidgetRail starts with a fresh `%LOCALAPPDATA%\WidgetRail` product-owned store.
New code reads and writes only that root. It must not probe, read, copy, move,
merge, reinterpret, stage, delete, or otherwise touch the old
`%LOCALAPPDATA%\GameBarAlternative` root.

There is no automatic compatibility or migration path for old settings,
appearance or placement state, consent decisions, package catalogs, installed
packages, enablement/order/selection state, themes, launcher experiences,
diagnostics configuration, or trusted private state. The sole current user may
reconfigure settings, grant consent, and install current `.wrwidget` packages
again after cutover. The old root remains untouched as recoverable legacy data
unless the user later gives explicit authority for a separate manual action.

External provider databases, installed applications, user files, account data,
and Windows Credential Manager entries remain untouched. Credential targets
containing `GameBarAlternative` are not copied, renamed, or reused; new source
uses WidgetRail targets and affected Community apps require explicit
re-authentication.

### Repository, publication, and output identity

- Intended repository slug: `widgetrail` under a user-selected owner.
- Intended ecosystem labels: WidgetRail SDK and WidgetRail Community.
- Store display name: WidgetRail, pending Partner Center reservation.
- Build/package staging roots and artifact names use `WidgetRail`, `widgetrail`,
  `wrail`, or the new archive extensions according to their surface.
- The local checkout directory and saved Codex project may remain
  `GameBarAlternative` until the complete tracked-source cutover is accepted;
  renaming them is an optional final user action.

## Legacy disposition

Untouched legacy data outside the active product path:

- The old `%LOCALAPPDATA%\GameBarAlternative` root is not a migration input and
  is never opened by WidgetRail. It remains untouched unless the user later
  authorizes a separate manual action.
- Old settings, consent, catalog, package, and persisted-schema data receive no
  runtime reader or compatibility bridge.

Removed at cutover:

- Active old namespaces, assemblies, package IDs, CLI/config names, archive
  extensions, UI strings, native identifiers, user agents, templates, samples,
  active documentation, generated runtime artifacts, and publication metadata.
- Dual live state roots, old-root readers, singleton/pipe owners, legacy public
  package formats, importers, or SDK compatibility layers.

Intentionally unchanged historical evidence:

- Git commit history and tags.
- `docs/history/**` snapshots and closed DLV/AVP evidence.
- Closed Avalonia experiment sources unless separately authorized.
- Accurate third-party notices, quotations, external URLs, and historical
  artifact hashes that must retain their original names.

Every old-name match remaining after DLV-260 must be classified as one of those
historical/external exceptions. Unexplained active residue blocks acceptance.

## Approved decisions

On 2026-08-17 the user approved all four choices without modification:

1. Publisher display `WidgetRail Project` and technical ID root `widgetrail`.
2. The complete archive/style mapping: `.wrwidget`, `.wrtheme`, `.wrlauncher`,
   and WRSS/`.wrss`.
3. Tool/config mapping: `wrail`, `.widgetrail`, `WRAIL_`, and
   `widgetrail.scenarios.json`.
4. Existing Windows Credential Manager entries are not migrated; affected
   Community samples re-authenticate under new WidgetRail credential targets.

The user additionally approved a clean local-state break on 2026-08-17:
WidgetRail does not contain old-version migration or compatibility logic, uses
only its new local-data root, and leaves the old root untouched.

This completes DLV-257 and authorizes the assigned local DLV-258 technical
cutover. Store reservation, domain registration, GitHub ownership, and legal
clearance remain external pre-publication gates; they are not DLV-258 inputs and
no external action is authorized here.
