# Shared JSON contracts for trimmed WinUI hosts

The shared settings stores, snapshot codec and presentation transport now have
explicit generated metadata for their known contracts. The presentation session
uses named empty/display request records rather than anonymous request objects.
Strict member handling, camel-case wire names, enum converters, field omission,
framing limits and authority validation remain in place.

The generic Bridge codec retains its original reflection resolver when JSON
reflection is enabled, preserving existing untrimmed Bridge/tool callers and their
anonymous diagnostic payloads. When reflection is disabled (including the trimmed
WinUI host), only `BridgeJsonContext` contracts are available. This uses the
standard `JsonSerializer.IsReflectionEnabledByDefault` feature switch; it does not
root arbitrary types, suppress warnings or disable trimming.

The generic outgoing frame writes its fixed envelope with `Utf8JsonWriter` and
serializes the typed payload directly into that buffer. Artwork remains a direct
base64 write; no intermediate `JsonElement` copy is added to that path.

## Omitted properties and immutable defaults

Generated deserialization of `init` properties assigns type defaults for omitted
members, rather than preserving C# property initializers. Simply replacing the
serializer broke legacy settings: omitted display maps and disabled-widget lists
became null, and omitted animation/controller choices lost their documented
defaults. Existing settings tests caught this.

Settings now fill only missing optional properties from the models' legacy
defaults during the existing read-only migration pass. Required properties,
explicit nulls and unknown properties still fail their original checks. Fresh
installation defaults remain distinct from legacy omission defaults. Fully
populated settings avoid constructing fallback templates.

The snapshot codec preserves compact legacy snapshots through a bounded
compatibility path only after initial validation fails. It fills known omitted
collection/version properties and then reruns the same validator. Canonical SDK
snapshots already emit these fields and do not take that additional parse path.
Catalog and envelope omission defaults are similarly retained without accepting
explicit null collections or unsupported protocol versions.

## Evidence and remaining Release gate

Checks completed in `codex/winui-shared-json-trimming`:

- 247 presentation-session tests passed with ordinary reflection behavior.
- 7 focused transport/codec tests passed with JSON reflection disabled, including
  real named-pipe handshake, catalog, display, palette and shutdown requests.
- All 25 settings checks and 5 widget-configuration checks passed with reflection
  disabled. Their emitted runtimeconfig files confirm the false feature switch.
- Analyzer-enabled Release publish with `TrimmerSingleWarn=false` reports no
  WidgetRail trim diagnostics.

Publish is still unsuccessful: 35 IL2081 errors remain in Windows SDK/WinRT
generic marshalling fallback methods (`IAsyncOperation`, collection interfaces,
and key/value pair helpers). Their `WinRT.Marshaler<T>.AbiType` values do not meet
the target generic parameter's `PublicParameterlessConstructor` annotation.
The inspected SDK projection comes from Microsoft.Windows.SDK.NET.Ref
10.0.26100.57. These external diagnostics were not suppressed or rooted around.

Detailed logs and unique binlogs are under `artifacts/winui-release-probe/` in the
readiness worktree, particularly `shared-contracts-final.log`. This establishes
the shared serialization correction, not successful Release packaging or native
qualification. No frontend was deployed during this work.
