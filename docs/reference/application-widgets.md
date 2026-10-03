# Full-access application widgets

Use the ordinary sandboxed widget model unless your integration requires an
application with normal Windows user access. These are separate trust choices,
not a way to silently upgrade a denied capability.

## The public bootstrap

A full-access package declares the `full-trust-application-v1` runtime and its
executable entrypoint. That executable uses `WidgetApplicationBootstrap.RunAsync`
with its widget factory and host-provided arguments.

The bootstrap establishes both the overlay session and the authenticated host-service
connection supplied by WidgetRail. Use the same typed `HostServices` APIs as a
sandboxed widget. Required and optional host permissions are declared in the
manifest and require the same consent, lifecycle, and resource-ownership checks.
Full-trust approval does not automatically grant those permissions.

The application also owns direct network, credential, or process integrations
permitted by its Windows user token. Provider-specific clients belong in that
application, not in the host. Game Help, for example, owns its Gemini client while
using shared window enumeration, capture and restricted document presentation.

Use the bootstrap rather than parsing private pipe arguments or connecting to
broker internals yourself. The host binds the service channel to the process it
launched and retires its resources when that process ends. The overload accepting
`Func<WidgetHostServices, Widget>` supports constructor injection; the ordinary
factory continues to attach services through the widget's protected property.

Host permission revocation prevents further host-service access. It is not a
sandbox around a full-trust application's own Windows or network APIs, and cannot
recall bytes the application has already read.

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
