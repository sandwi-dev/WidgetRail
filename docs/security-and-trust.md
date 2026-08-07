# Security and trust

Status: strong validation and process-separation prototypes; untrusted public
widgets are not yet supported safely

The platform uses defense in depth, but several production boundaries remain
planned. A structurally valid package is not necessarily trustworthy.

## Implemented controls

- Strict manifests and snapshots reject unknown JSON members.
- Widget trees, strings, depth, quick actions, focus targets, progress,
  images, icons, and interaction states have bounded validation.
- Images accept HTTPS only, reject embedded credentials, and are downloaded by
  a bounded host cache rather than widget drawing code.
- GBSS is an allowlisted data language. It rejects scripts, URLs, expressions,
  arbitrary functions, traversal, reparse-point escapes, and oversized input.
- Native rendering uses semantic elements and a closed icon set; widgets
  cannot supply native handles, SVG, font glyphs, or arbitrary paths.
- The native host never loads third-party managed assemblies.
- Bridge and worker communication uses random local named pipes, strict
  versioned envelopes, message ceilings, bounded waits, and failure events.
- Package extraction rejects absolute/traversing/ambiguous Windows paths,
  links, reparse points, collisions, excessive entries, and zip expansion
  beyond configured limits.
- Installed versions are immutable and staged before atomic move.
- Remote acquisition accepts only credential-free, fragment-free HTTPS URLs on
  port 443, revalidates up to five HTTPS redirects, rejects obvious localhost
  and private/loopback/link-local address literals, and applies connection,
  response, total-operation, and 72 MiB compressed-download limits.
- Remote downloads use unique temporary files that are deleted on every exit
  path. Remote sources require an expected SHA-256 digest, comparison occurs
  before package installation, and every successful remote install reports the
  actual digest.
- Newly discovered widget IDs default to disabled. A remote install explicitly
  disables its widget ID and requires a separate user `gbar enable` decision.

## Not yet a production guarantee

The following are **not implemented as a complete public security boundary**:

- publisher signatures, certificate validation, transparency, or revocation;
- a public SDK/package signing service or curated marketplace;
- AppContainer launch for community workers;
- Job Object CPU/memory/process-tree enforcement;
- an implemented permission/capability broker for manifest permissions;
- secure token brokering for third-party integrations;
- user-facing permission consent, update review, rollback, or quarantine UI;
- a production installer that connects `WidgetCatalog` to native host
  discovery; and
- universal anti-cheat or controller-containment compatibility.

The runtime's out-of-process worker and timeout/restart behavior improve
reliability. They do not by themselves prevent a normal desktop process from
accessing the current user's files, network, or credentials.

## Trust decision today

Only run widgets that you wrote, reviewed, or obtained from a developer you
already trust. Prefer building from source. Do not execute an arbitrary DLL
with `gbar render`: that command loads code in the CLI process and is explicitly
development tooling, not a sandbox.

`gbar validate` proves syntax and bounded declarative resources. Package
validation proves archive containment and identity consistency. HTTPS protects
transport to the resolved servers, and `--sha256` can pin exact bytes. None of
these proves publisher identity or benign executable behavior. A digest is a
useful trust signal only when its expected value comes through an independent,
authenticated channel.

Remote install is acquisition plus the existing package validation; it is not
a repository installer. GitHub shorthand selects one exact Release asset and
does not invoke the GitHub API, select `latest`, clone, build, or run scripts.
Avoid URLs containing secrets in query parameters: credentials are rejected,
but command lines and shell history are still inappropriate places for tokens.

Direct HTTPS URLs are destinations the user explicitly authorizes. Rejection
of localhost and private address literals is not a complete server-side request
forgery boundary: the downloader does not claim DNS pinning or DNS-rebinding
protection. Use only public origins you intended to contact.

## Permissions

Manifests already model required and optional permission strings, background
policy, architecture, and resource requests. Validation checks their syntax
and bounds. Enforcement and user consent are planned; declaring a permission
does not currently grant a safe brokered API or constrain arbitrary worker
code.

## Reporting security problems

No dedicated vulnerability intake is configured in this prototype repository.
Before a public release, add a repository `SECURITY.md` with private reporting,
supported versions, and disclosure expectations. Until then, do not publish
credentials, tokens, exploit details, or private user data in a public issue.

See [publishing and installation](publishing-and-installation.md) and the
detailed [package safety contract](widget-packaging.md).
