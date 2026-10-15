# Widget broker transport

This .NET-only implementation assembly contains the capability pipe client and
the wire types, framing, strict JSON codec, identity validation and request
deadlines shared with `PlatformBroker`. It has no project or package dependencies.

`WidgetApplicationRuntime` depends on this assembly and `WidgetSdk`. The local
offline SDK exported by `wrail` includes the transport and incorporates its bytes
in the package content hash. Authors continue using `WidgetApplicationBootstrap`
and typed `HostServices`; the transport is not a separate authoring API.

The `WidgetRail.PlatformBroker` namespace is retained for existing transport
types. `PlatformBroker` forwards their old assembly identities to this assembly.
Framing, protocol versions, message limits, timeouts and cancellation behavior
are unchanged by the extraction.

The host retains the pipe server, expected-process/nonce checks, capability
catalog and operation admission, lifecycle and gesture authority, consent,
subscription revocation, platform providers, stores and SQLite dependencies.
Sharing wire code does not grant a widget any additional capability. Every
request still passes through the same authenticated host admission path.

`BrokerWireJson` owns structural JSON rules. The host's `BrokerJson.ParseRequest`
retains request validation, including capability/operation membership, before
dispatch. Operation identifiers and deadlines needed at both pipe ends are
shared without moving the host's capability policy table into this assembly.

Regression coverage includes the broker and worker-runtime scenario suites,
full-trust bridge host-service checks, and the CLI's isolated SDK consumer. The
CLI release-unit test checks every packaged assembly reference against the SDK
contents and .NET runtime, rejects host assemblies, and verifies type forwarding.
