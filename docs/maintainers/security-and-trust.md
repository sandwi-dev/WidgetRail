# Security and trust

WidgetRail separates widget UI from the native host and brokers specific Windows
operations. It also supports explicitly approved full-trust applications.
These are different execution models; an installable package is not necessarily
sandboxed or trustworthy.

This describes implemented boundaries, not an independent security certification.
See [Release readiness](release-checklist.md) for remaining distribution gates.

## Execution models

| Component | Authority |
|---|---|
| Native overlay and managed bridge/providers | Trusted desktop processes own rendering, windows, controller policy and Windows APIs |
| Sandboxed widget worker | Host-created AppContainer with constrained process lifetime and authenticated IPC |
| Full-trust application widget | Ordinary current-user Windows access after explicit approval; not constrained to SDK permissions |
| Settings worker | Trusted host administration path; not a community-widget permission model |

The host does not load community widget assemblies into its native rendering
process. Sandboxed widgets send bounded declarative data rather than native
window handles, arbitrary drawing commands or executable styles.

Full-trust packages can access resources available to the Windows user. Their
UI can use the shared SDK, but the UI contract does not turn the application's
code into a sandboxed process.

## Sandboxed widgets and capabilities

The host creates the worker with its selected identity and isolation setup,
restricts its process lifetime with a Job Object, and authenticates local IPC.
Failure to establish the required isolation prevents sandboxed launch; there
is no implicit desktop-token fallback for that package type.

Broker operations check the authenticated identity, declared capability,
stored consent, lifecycle and payload. Reads and control operations have
different lifecycle rules. Some exact host-authorized dashboard operations
have their own bounded gesture authority; this is not a generic exemption.
See [Capabilities](../reference/capabilities.md).

Built-in widgets require permission review too. Power, window management,
audio and network control are not granted merely because a widget is bundled.
Private widget state is a distinct package-scoped host service; it is not a
general filesystem capability.

## Declarative content

Manifests, snapshots and commands have strict schemas and size/depth limits.
WRSS is an allowlisted data format rather than executable CSS or JavaScript.
General artwork uses bounded host-owned decoding/cache paths.

Package SVG icons **are supported**, through declared static assets normalized
to a restricted subset. This does not allow arbitrary SVG documents or native
drawing code. Window previews use host-owned opaque window identities and a
separate permission; they do not expose pixel-reading APIs to the widget.

Embedded web media uses explicit host-owned sessions, navigation/resource
policies and lifetime management. It is separate from standard declarative UI.

## Package identity is not publisher authentication

The package pipeline validates archive paths, file identity, resource limits
and content digests. Installed versions are retained as exact package versions.
HTTPS acquisition and expected hashes can pin bytes.

An unsigned package's publisher name is still a claim. A digest does not prove
who wrote the package or that its behavior is benign. Publisher signing,
key rotation and revocation are not implemented. Full-trust approval and
capability consent do not provide publisher authentication either.

Only run code from a source you trust. Verify expected hashes through an
independent authenticated source. Do not describe the current package system
as a vetted marketplace.

## State, credentials and diagnostics

Public configuration is not a secret store. Provider authentication uses its
designated private storage; widget authors should use the appropriate typed
credential/companion service instead of writing tokens into config or URLs.
See [Companion services](../reference/community-companion-services.md) and
[Private state](../reference/private-widget-state.md).

Settings includes per-widget local-data management. Uninstalling a widget and
clearing its data are distinct actions. Optional caches should fail as misses
rather than crash the widget; they do not replace a credential store.

Logs and diagnostic archives can contain user information. Review them before
sharing. Do not commit runtime profiles or report credentials in public issues.

## Supported claims and remaining work

The code has focused isolation, validation, lifecycle and capability tests.
That evidence is not a blanket claim of safety for arbitrary untrusted
applications, driver combinations or game processes.

Before a binary release, verify the exact artifact on clean supported systems,
review dependency and runtime redistribution, define signing/support policy,
and establish private vulnerability reporting. See [SECURITY.md](../../SECURITY.md)
and the [release checklist](release-checklist.md).
