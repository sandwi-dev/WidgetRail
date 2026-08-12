# Theme packaging and distribution

Status: implemented data-only theme authoring and installation workflow

This guide is the distribution contract for global Game Bar Alternative
themes. Widget-local styling uses the same [GBSS language](gbss.md), but a
global theme is a separate immutable `.gbartheme` package selected by exact ID
and version in Settings.

## Command reference

Build `gbar` from the repository root, then use the theme command group:

```powershell
dotnet build .\tools\GbarCli\GbarCli.csproj -c Release
$gbar = '.\tools\GbarCli\bin\Release\net8.0\gbar.exe'

& $gbar theme new "Ocean Night" `
  --output .\scratch\OceanNight `
  --id dev.example.ocean-night `
  --publisher dev.example `
  --version 1.0.0
& $gbar theme validate .\scratch\OceanNight
& $gbar theme preview .\scratch\OceanNight
& $gbar theme pack .\scratch\OceanNight `
  --output .\scratch\dev.example.ocean-night-1.0.0.gbartheme
& $gbar theme inspect .\scratch\dev.example.ocean-night-1.0.0.gbartheme
& $gbar theme install .\scratch\dev.example.ocean-night-1.0.0.gbartheme
& $gbar theme list
& $gbar theme remove dev.example.ocean-night 1.0.0
```

| Command | Contract |
| --- | --- |
| `theme new <Name>` | Creates strict `theme.json` and safe starter `theme.gbss`. Optional: `--output`, `--id`, `--publisher`, `--version`. |
| `theme validate <directory-or-package>` | Validates the manifest, closed package contents, imports, variables, selectors, and typed GBSS. |
| `theme preview <directory-or-package>` | Prints deterministic computed styles for supported semantic roles using the real built-in-plus-user cascade. |
| `theme pack <directory>` | Revalidates and writes deterministic `.gbartheme` bytes. Optional: `--output`. |
| `theme inspect <package>` | Reports identity, publisher claim, entry, counts/sizes, SHA-256, and trust caveat without executing content. |
| `theme install <source>` | Revalidates and atomically installs a local or pinned remote package. Optional: `--sha256`, `--settings-root`. |
| `theme list` | Lists built-in and installed versions, publisher claims, validity, and a bounded error diagnostic. Optional: `--settings-root`. |
| `theme remove <exact-id> <exact-version>` | Atomically retires one confirmed inactive user-installed version. Built-in and selected versions are protected. Optional: `--settings-root`. |

Settings → Appearance presents the same version-management policy grouped by
theme ID. Invalid packages remain reviewable and removable by their exact
catalog directory identity, but never selectable. Selection and removal
revalidate beneath the catalog lock; cancellation or catalog replacement before
the atomic rename leaves the selected appearance record and unrelated versions
unchanged.

The computed preview is suitable for terminals and CI. It is not a native
graphical preview and does not simulate accessibility policy, resolution, DPI,
or physical monitor behavior.

## Manifest and identity

`theme.json` must be an exact-case root file with this strict schema-version-2
shape:

```json
{
  "schemaVersion": 2,
  "id": "dev.example.ocean-night",
  "publisher": "dev.example",
  "name": "Ocean Night",
  "version": "1.0.0",
  "entryFile": "theme.gbss"
}
```

- `id` is a lowercase portable identifier, cannot be `builtin.default`, and
  must equal the publisher or begin with `<publisher>.`.
- `publisher` is a lowercase reverse-DNS identifier. It is a claim, not a
  verified identity.
- `name` contains 1–80 printable characters.
- `version` uses canonical dotted numeric notation.
- `entryFile` is a normalized package-relative `.gbss` path.

Unknown, duplicate, missing, or wrong-case JSON members are errors. The runtime
continues to discover legacy schema-version-1 local theme directories, but
public `theme pack` accepts schema version 2 only.

## Package contents and limits

A `.gbartheme` is a deterministic ZIP containing only `theme.json` and the
UTF-8 `.gbss` closure reachable from `entryFile`. `theme pack` orders paths
ordinally, fixes timestamps and metadata, and prints a SHA-256 digest so the
same source produces the same package bytes.

The validator rejects:

- absolute, escaping, backslash, non-NFC, unsafe Windows, overlong, duplicate,
  or case-colliding paths;
- explicit directory entries, archive symlinks, and reparse points in source
  or installation paths;
- GBSS files that are not reachable from the entry file;
- invalid or escaping imports, malformed variables/selectors/types, scripts,
  URLs, expressions, and unsupported properties; and
- assemblies, executables, scripts, browser content, images, fonts, and any
  other asset type.

| Boundary | Limit |
| --- | ---: |
| Archive entries/source files | 65 |
| Archive path | 240 characters |
| One entry | 4 MiB |
| Expanded package | 4 MiB |
| Compressed archive | 4 MiB |
| Installed user-theme versions | 128 |

The GBSS compiler applies additional source, statement, import-depth,
selector, declaration, and expanded-variable limits documented in the
[GBSS reference](gbss.md).

## Local and GitHub installation

Install a local package directly:

```powershell
& $gbar theme install .\dev.example.ocean-night-1.0.0.gbartheme
```

For GitHub Releases, publish the `.gbartheme` and its digest, then name the
exact tag and asset:

```powershell
& $gbar theme install `
  github:example/themes@v1.0.0/dev.example.ocean-night-1.0.0.gbartheme `
  --sha256 <64-hex-digest>
```

An absolute HTTPS URL is also accepted. Every remote install requires
`--sha256`; the GitHub shorthand never queries `latest`, clones source, builds
a repository, or executes downloaded content. The shared downloader permits
HTTPS port 443, validates each bounded redirect, rejects credentials,
fragments, localhost, obvious private/link-local IP literals, encoded
responses, oversized streams, and timed-out transfers. These controls do not
claim to prevent DNS rebinding.

Installation validates the archive and production theme cascade, enforces the
128-version catalog limit, stages under a random directory on the settings
volume, holds a five-second bounded cross-process lock, and atomically moves
the result to:

```text
%LOCALAPPDATA%\GameBarAlternative\themes\<id>\<version>\
```

An existing ID/version is never overwritten. A new ID is published by an
atomic staged-ID move without an observable empty parent; a new version under
an existing ID is published by an atomic staged-version move. Publish changed
content under a new version, install it, review it, and select the exact version
under Settings → Appearance. A valid catalog/settings change is applied
through the no-poll
appearance watcher; an invalid change retains the last-good revision and does
not restart widget workers.

## Trust and current limitations

A theme is data-only and cannot add UI nodes, actions, controller bindings,
permissions, network requests, or lifecycle work. Validation prevents code and
unsupported assets from entering the package. Host accessibility policy is
applied after the theme cascade, so theme rules cannot override it.

The publisher field is not authenticated, and SHA-256 proves only exact bytes.
Obtain remote digests through an independently trusted channel. Theme packages
are not signed and there is no certificate chain, revocation service,
automatic updater, remove/rollback command, theme gallery, graphical/screenshot
preview, or asset broker yet.

Continue with [Settings and global themes](settings-and-themes.md) for the
cascade, live reload, controller selection, and accessibility behavior, and
[security and trust](security-and-trust.md) for the broader platform boundary.
