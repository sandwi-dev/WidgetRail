# Widget presentation session

`WidgetPresentationSession` is the managed presentation-side facade for the
existing `WidgetBridge` backend. It connects to the bridge's random,
current-user-only named pipe, performs the existing correlated hello handshake,
and uses the shared bridge framing and JSON contract. It does not launch a
bridge, create a server, connect to a widget process, or define an alternate
wire format.

Call `ListWidgetsAsync`, retain the returned `WidgetPresentationTarget`, and use
`EstablishPresentationAsync` to obtain the exact runtime, presentation,
snapshot, and input-scope authority. Open-widget actions and controller input
must carry that authority. Successful invalidations are coalesced into a fresh
snapshot; a failed refresh retains the last accepted frame and publishes a
bounded typed failure.

The facade keeps bounded pending request, artwork, and diagnostic collections.
Disposal asks the existing bridge session to stop, closes the client endpoint,
and completes outstanding work with a terminal transport failure.
