# Security and trust

Status: mandatory AppContainer isolation is implemented for installed/community
workers; unsigned public distribution is not yet supported safely

The platform uses defense in depth, but several production boundaries remain
planned. A structurally valid package is not necessarily trustworthy.

## Implemented controls

- Strict manifests and snapshots reject unknown JSON members.
- Widget trees, strings, depth, quick actions, focus targets, progress,
  images, icons, and interaction states have bounded validation.
- General images accept HTTPS only, reject embedded credentials, and are
  downloaded by a bounded host cache rather than widget drawing code. Protocol
  v6 additionally accepts a canonical RGBA8 PNG data source capped at 12 KiB
  and 64 by 64 pixels for broker-sanitized artwork; it is decoded locally and
  cannot contain a native file or executable path.
- GBSS is an allowlisted data language. It rejects scripts, URLs, expressions,
  arbitrary functions, traversal, reparse-point escapes, and oversized input.
- Native rendering uses semantic elements and a closed icon set; widgets
  cannot supply native handles, SVG, font glyphs, or arbitrary paths.
- The native host never loads third-party managed assemblies.
- Bridge and worker communication uses random named pipes, strict versioned
  envelopes, message ceilings, bounded waits, and failure events. Isolated
  worker endpoints are single-client global pipes whose ACL names only the
  desktop host and the exact AppContainer SID and whose mandatory label permits
  Low-integrity access.
- On Windows, each worker is created suspended, assigned to a per-worker Job
  Object, and only then resumed. Trusted bridge policy applies a bounded job-
  memory ceiling, one-active-process limit, kill-on-close, die-on-unhandled-
  exception behavior, and the complete basic UI-restriction set. Timeout,
  restart, failure, and disposal paths release the job and its process.
- Every installed/community worker must start in a stable host-derived,
  exact-content-specific, capability-free AppContainer at Low integrity. For
  unsigned packages, installation seals a SHA-256 digest of the complete
  normalized content tree and the authority ID derives from that verified
  digest rather than manifest publisher text. Different bytes—even with the
  same asserted package ID and version—receive a different profile and cannot
  inherit broker consent, private secrets, or private widget state. The launch
  passes a small allowlisted environment, grants
  read/execute only to the generic runtime and exact immutable package roots,
  verifies the resulting token's SID/integrity/zero-capability shape before
  resume, and has no desktop-token fallback. Failure to create or verify any
  isolation component prevents the worker from running.
- The managed capability broker has a closed versioned vocabulary for audio,
  network/Bluetooth, activity, app-library, media, exact-port loopback JSON,
  write-only private secrets, and host-granted private widget state. Ordinary
  OS/provider authority rechecks the manifest declaration, durable consent,
  authenticated package/publisher/instance, and lifecycle on every operation.
  Private state is a separate non-consent host grant that manifests are
  forbidden to declare and worker IPC cannot add. Requests/results/events are
  strict and bounded; public DTOs contain sanitized labels and opaque IDs.
- The bridge creates a fresh broker companion for every worker start/restart.
  Its nonce handshake binds the worker to bridge-selected identity,
  declarations, consent store, and backend. Widget code receives typed
  `HostServices` APIs; it cannot select a broker identity,
  declaration, or provider through widget protocol messages. The isolated main
  and broker pipes additionally verify the connecting process ID before the
  existing nonce/package/publisher/instance authentication proceeds.
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
- Exact-loopback authority fixes the destination to one manifest-declared
  nonprivileged IPv4 port on `127.0.0.1`, allows bounded strict-JSON GET/POST
  only, and disables DNS, proxy, redirects, cookies, decompression, and socket
  exposure. A separate private-secret grant offers exists/metadata/save/delete
  but never returns stored values; the trusted provider can inject one slot as
  Bearer only while both consent/lifecycle leases remain valid. An explicit
  request option can atomically delete that exact scoped slot on HTTP 401 before
  returning, without granting a Visible worker general delete authority. See [local
  companion HTTP and private secrets](community-companion-services.md).
- Private widget state stores exactly one canonical JSON document/tombstone per
  authenticated publisher/package authority. Its SHA-256-derived filename
  contains no package path, the Windows store rejects reparse/corrupt state,
  serializes across processes, rate-limits mutations, and atomically replaces
  one revisioned envelope. Unsigned changed package bytes receive a different
  content-bound authority and cannot inherit state. Uninstall currently retains
  it; per-widget clear-local-data UI remains unimplemented. See [Private widget
  state](private-widget-state.md).
- Public widget configuration is a separate bounded settings store keyed by
  declared publisher/package identity. It is intentionally readable JSON for
  values such as an OAuth Client ID and is managed locally through `gbar config`
  until a controller-native editor exists. An unsigned digest authority can
  resolve only one unambiguous document whose publisher namespace owns its
  package ID; this convenience never applies to consent, private state, OAuth
  tokens, or credentials. It is not private state or a credential
  vault: secret/password/token/credential-like CLI keys are rejected, and OAuth
  tokens remain in provider-owned Windows protected storage.
- Package extraction rejects absolute/traversing/ambiguous Windows paths,
  links, reparse points, collisions, excessive entries, and zip expansion
  beyond configured limits.
- Installed versions are immutable and staged before atomic move. The installer
  writes host-owned integrity metadata after extraction; packages cannot
  provide that reserved path. Catalog discovery recomputes the bounded content-
  tree digest and rejects missing, malformed, or mismatched metadata.
- Remote acquisition accepts only credential-free, fragment-free HTTPS URLs on
  port 443, revalidates up to five HTTPS redirects, rejects obvious localhost
  and private/loopback/link-local address literals, and applies connection,
  response, total-operation, and 72 MiB compressed-download limits.
- Remote downloads use unique temporary files that are deleted on every exit
  path. Remote sources require an expected SHA-256 digest, comparison occurs
  before package installation, and every successful remote install reports the
  actual digest.
- Newly discovered widget IDs default to disabled. Local and remote updates are
  rejected while the ID is enabled; installation, active-version selection,
  review, and enablement remain separate decisions. Remote installation also
  requires an exact independently obtained SHA-256 pin.
- Installed package versions are immutable. Active-version selection and
  rollback require the widget to be disabled, leave it disabled, and persist an
  exact schema-2 pin. A missing pinned version fails catalog discovery closed
  rather than selecting different code.
- Only enabled, host-compatible installed packages using the closed capability
  vocabulary are joined. The bridge watches the trusted catalog file plus the
  installed catalog state/package tree, coalesces notifications, and publishes
  only a complete validated semantic revision. Invalid trusted shell JSON
  retains the complete last-good catalog. Invalid installed state or package
  integrity instead publishes a trusted-only revision, synchronously removes
  Community registrations, retires their workers, and prevents relaunch by the
  old ID. Conflicts, unsupported capability declarations, and invalid per-
  package GBSS omit the affected package with bounded diagnostics.
  Reload/list does not launch a worker.
- The generic worker host rejects entrypoint/dependency path escape and reparse
  points, requires a public concrete SDK `Widget` type with a usable public
  constructor, and returns path-free errors for rejected assembly/type cases.
  The assembly is loaded inside its already-contained worker, never into the
  native host or bridge.

## Not yet a production guarantee

The following are **not implemented as a complete public security boundary**:

- publisher signatures, certificate validation, transparency, or revocation;
- a public SDK/package signing service or curated marketplace;
- CPU-rate/time and broader resource quotas beyond the current Job Object
  memory/single-process/cleanup policy;
- disk/profile size quotas, AppContainer profile garbage collection, and a
  user-facing profile/storage-management surface;
- Win32k system-call disable for managed workers; testing it caused CoreCLR DLL
  initialization failure (`0xC0000142`), so Job Object UI restrictions remain
  enabled but that stronger mitigation is not;
- production hardening/hardware/privacy evidence for the narrow Core Audio and
  Windows network providers, and a security audit/history UI;
- a general OAuth/account broker, readable credential API, or internet/LAN
  authority beyond the narrow implemented local-companion services;
- automatic update discovery/review, version removal, or crash-quarantine UI;
- a graphical/file-picker installer and safe automatic updates;
- universal anti-cheat or controller-containment compatibility.

The runtime now prevents installed/community widgets from falling back to a
normal desktop token. A community worker has a package-specific AppContainer
SID, Low integrity, zero OS capability SIDs, no network capability, explicit
read/execute grants for only its runtime/package inputs, a stripped launch
environment, and the Job Object limits above. This materially
reduces direct file, network, credential, process-spawn, and desktop/UI attack
surface. It does not prove the package publisher or make arbitrary code safe in
the broader software-supply-chain sense.

The bridge discovers enabled packages from the current-user catalog, watches
bounded catalog inputs without polling, and launches workers lazily through the
generic worker host. File-system events are only hints: each publication comes
from a complete validation and semantic comparison. A malformed trusted shell
catalog retains last-good; invalid installed state/integrity publishes trusted-
only and retires Community registrations instead of retaining their authority.
Declaration/identity/process-policy changes
retire the old client before a new authenticated session can start lazily;
stale worker events are ignored.

This is an execution path and safe reload boundary, not a complete trust
boundary. Catalog enablement has no signature or publisher proof. Host-sealed
content-tree verification rejects in-place package tampering; a watcher and
digest still do not prove who authored the originally installed bytes.
Closed declared capabilities receive an authenticated broker channel, and the
production bridge composes narrow Windows providers, including Core Audio,
network/Bluetooth, media, app library, exact loopback, and private secrets. The
capability-free worker token cannot use those OS APIs directly;
the trusted broker performs only declared, consented, lifecycle-valid closed
operations. The same authenticated channel may carry only the Bridge-attached
private-state host grant; that exception has no Settings consent and grants no
OS/provider authority. This is still not publisher trust, a security audit,
CPU/disk quota coverage, profile cleanup, or proof across the hardware/privacy
matrix.

Trusted bundled workers are a temporary exception to the community policy.
Settings currently uses the host-trusted Job-only launch for desktop-user
resources not yet exposed through narrow brokers. YT Music no longer uses that
exception: its local companion and secret needs are public broker services and
the addon runs through the normal package AppContainer. The remaining Settings
exception is selected by host policy, never by a package manifest or worker
argument. An installed/community package cannot opt out of AppContainer
isolation.

The managed theme catalog has strict manifests, version-pinned directories,
package-relative GBSS imports, bounds, reparse/containment checks, and sanitized
diagnostics. The implemented data-only `.gbartheme` workflow scaffolds,
validates, previews computed styles, deterministically packs/inspects, and
atomically installs local or SHA-256-pinned HTTPS/GitHub Release packages. The
bridge watches only the settings file and current-user theme tree, debounces
notifications, and republishes only a fully valid last-good snapshot. It only
reads data consumed by the allowlisted GBSS compiler; executable content,
scripts, remote resources, fonts, shaders, and arbitrary assets are rejected.
Theme publisher signing, revocation, removal/update/rollback, gallery, and
graphical preview remain unimplemented. See [theme packaging and
distribution](theme-packaging.md).

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

Manifests model required and optional permission strings, residency policy,
architecture, and resource requests. The bridge accepts only the closed
versioned capability vocabulary (including validated exact-port loopback IDs),
fixes the combined declared set for one authenticated worker session, and the
broker enforces declaration, stored decision, and lifecycle.
Settings stores decisions by package, publisher, and capability; the channel
also binds the concrete instance. Missing decisions fail closed, including for
required capabilities, and no package is auto-granted.

`HostServices.PrivateState` is the one current host-provided exception: it is
not a permission, has no consent toggle, and a manifest that declares its
internal `storage.private-state.v1` ID is rejected. The Bridge attaches it
through a separate host-only grant set after authenticating the worker; no
worker message can add that or any other grant. Its Background availability is
only for bounded persistence and does not relax ordinary lifecycle checks.

For installed/community workers, the capability-free AppContainer constrains
direct desktop authority while this permission system controls the separate
trusted broker. The container receives no network or other OS capability SID
and cannot request one through its manifest, arguments, or protocol. Audio and
network access therefore remains available only through the typed broker's
declaration, durable consent, lifecycle, identity, and operation checks. This
does not replace publisher signing/revocation, CPU and disk/profile quotas,
profile cleanup, or an audit UI; do not treat unsigned public distribution as
production-safe yet. See [widget capabilities](capabilities.md) for the exact
developer and transport contract.

## Reporting security problems

No dedicated vulnerability intake is configured in this prototype repository.
Before a public release, add a repository `SECURITY.md` with private reporting,
supported versions, and disclosure expectations. Until then, do not publish
credentials, tokens, exploit details, or private user data in a public issue.

See [publishing and installation](publishing-and-installation.md) and the
detailed [package safety contract](widget-packaging.md).
