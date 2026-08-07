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
- On Windows, each worker is created suspended, assigned to a per-worker Job
  Object, and only then resumed. Trusted bridge policy applies a 16–256 MiB
  aggregate job-memory ceiling, one-active-process limit, kill-on-close, and
  die-on-unhandled-exception behavior. Timeout, restart, failure, and disposal
  paths release the job and its process.
- The managed capability broker has four closed versioned
  audio/network grants. It binds a session to package, publisher, and instance
  identity and rechecks the manifest declaration, durable consent decision,
  and lifecycle on every operation. Requests/results/events are strict and
  bounded; public DTOs contain sanitized labels and opaque IDs.
- The bridge creates a fresh broker companion for every worker start/restart.
  Its nonce handshake binds the worker to bridge-selected identity,
  declarations, consent store, and backend. Widget code receives typed
  `HostServices` audio/network APIs; it cannot select a broker identity,
  declaration, or provider through widget protocol messages.
- Broker consent storage is strict, size/entry bounded, reparse-point rejecting,
  cross-process locked, and atomically replaced. Event subscriptions are
  bounded/coalescing. A coalesced cross-process file watcher reconciles changes;
  denial, malformed/deleted consent, watcher failure, or Destroying promptly
  revokes subscriptions fail closed.
- Controller Settings lists only supported capabilities declared by an
  installed package, distinguishes required/optional and current decision,
  requires an explicit confirmation scope for grants, permits immediate
  deny/revoke, never auto-grants first-party packages, and fails closed on
  malformed catalog/consent state.
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
- At bridge startup, only enabled, host-compatible installed packages using the
  closed capability vocabulary are joined. Conflicts, unsupported capability
  declarations, malformed/tampered catalog entries, and invalid per-package
  GBSS fail soft to the bundled trusted catalog.
- The generic worker host rejects entrypoint/dependency path escape and reparse
  points, requires a public concrete SDK `Widget` type with a usable public
  constructor, and returns path-free errors for rejected assembly/type cases.
  The assembly is loaded inside its already-contained worker, never into the
  native host or bridge.

## Not yet a production guarantee

The following are **not implemented as a complete public security boundary**:

- publisher signatures, certificate validation, transparency, or revocation;
- a public SDK/package signing service or curated marketplace;
- AppContainer launch for community workers;
- CPU-rate/time and broader resource quotas beyond the current Job Object
  memory/single-process/cleanup policy;
- real Core Audio/WLAN providers and a security audit/history UI;
- secure token brokering for third-party integrations;
- user-facing update review, rollback, or quarantine UI;
- a graphical install/review flow, live bridge catalog reload, and safe
  automatic updates;
- universal anti-cheat or controller-containment compatibility.

The runtime's out-of-process worker and Job Object policy improve reliability
and bound memory/process count. They do not prevent a normal desktop process
from accessing the current user's files, network, or credentials. AppContainer
or an equivalent least-privilege token boundary is still required before
untrusted public widget binaries are safe.

The bridge now discovers enabled packages from the current-user catalog at
startup and launches them lazily through the generic worker host. This is an
execution path, not a complete trust boundary: catalog enablement has no
signature or publisher proof, and package/manifest changes require an overlay/
bridge restart. Closed declared capabilities receive an authenticated broker
channel, but the production bridge currently connects only the simulator.

The managed development theme catalog has strict manifests, version-pinned
directories, package-relative GBSS imports, bounds, reparse/containment checks,
and sanitized diagnostics. The bridge watches only the settings file and
current-user theme tree, debounces notifications, and republishes only a fully
valid last-good snapshot. It only reads data consumed by the allowlisted GBSS
compiler. There is still no supported theme distribution package, installer,
signature, or publisher trust decision. Future distribution must never turn
themes into a route for DLLs, scripts, remote resources, fonts, shaders, or
arbitrary paths. See [settings and global themes](settings-and-themes.md).

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

Manifests model required and optional permission strings, background policy,
architecture, and resource requests. The bridge accepts only the four closed
audio/network IDs, fixes the combined declared set for one authenticated worker
session, and the broker enforces declaration, stored decision, and lifecycle.
Settings stores decisions by package, publisher, and capability; the channel
also binds the concrete instance. Missing decisions fail closed, including for
required capabilities, and no package is auto-granted.

This permission system constrains access through the typed broker, not arbitrary
desktop code. Without AppContainer or an equivalent restricted token, a
malicious worker can access user-writable files, inspect its own process, or
call Windows APIs directly. It may therefore bypass the broker or tamper with
the current-user consent file. Do not treat the implemented UI/transport as a
production sandbox for untrusted widgets. See [widget
capabilities](capabilities.md) for the exact developer and transport contract.

## Reporting security problems

No dedicated vulnerability intake is configured in this prototype repository.
Before a public release, add a repository `SECURITY.md` with private reporting,
supported versions, and disclosure expectations. Until then, do not publish
credentials, tokens, exploit details, or private user data in a public issue.

See [publishing and installation](publishing-and-installation.md) and the
detailed [package safety contract](widget-packaging.md).
