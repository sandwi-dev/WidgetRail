# Full-access application widgets

Use the ordinary sandboxed widget model unless your integration requires an
application with normal Windows user access. These are separate trust choices,
not a way to silently upgrade a denied capability.

## The public bootstrap

A full-access package declares the `full-trust-application-v1` runtime and its
executable entrypoint. That executable uses `WidgetApplicationBootstrap.RunAsync`
with its widget factory and host-provided arguments.

The bootstrap establishes the overlay session. It does not connect the application
to the sandbox capability broker. The application owns any direct provider,
network, credential, or process integrations permitted by its Windows user token.

Do not copy host-side launch code, parse private pipe arguments yourself, or
reference `PlatformBroker` to impersonate a sandboxed client.

## Review and distribution

Users explicitly approve the full-access runtime before enabling it. A package
digest still identifies bytes rather than proving a publisher's identity.
Explain the integration's access, account requirements, and cleanup behavior.

These applications can store data outside WidgetRail's own directories, so
their uninstall instructions need to identify those additional stores.

The [Full Application sample](../../samples/FullApplicationWidget/README.md) is
a starting point. [Spotify](../../samples/SpotifyWidget/README.md) and
[YouTube](../../samples/YouTubeWidget/README.md) show provider integrations.

See the [security model](../maintainers/security-and-trust.md) and exact bootstrap
in [`WidgetApplicationBootstrap.cs`](../../src/WidgetApplicationRuntime/WidgetApplicationBootstrap.cs).
