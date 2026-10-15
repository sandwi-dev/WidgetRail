# SDK client packaging repair

Status: implemented and validated locally on 2026-10-15 in
`codex/sdk-client-packaging`. No merge, publication or installer creation.

## Reproduction

At main `01e3feee`, the exact CLI test `Built wrail artifacts support an isolated
external SDK consumer` built the external full-trust application but failed with
`Widget worker exited with code 1 before connecting`. `WidgetApplicationRuntime`
referenced `PlatformBroker`, while the offline SDK correctly excluded the host
broker. Evidence: `artifacts/sdk-client-packaging/baseline.log`.

## Implementation

- Extract the existing client and common wire implementation into
  `WidgetBrokerTransport`, with only .NET runtime dependencies. Move shared
  identifiers and deadline constants without changing their values.
- Keep the server, capability catalog, request admission, consent, lifecycle,
  gesture policy, native peer verification, providers and storage in the host.
- Keep existing type namespaces and forward former broker assembly identities.
- Point `WidgetApplicationRuntime` at the client transport. Export that dependency
  with the offline SDK and include it in the deterministic package hash.
- Preserve the SDK source API, bootstrap API, templates and wire protocol.
- Test dependency closure, absence of host assemblies, forwarding and a real
  external process using only the exported package. Exercise denied/granted
  reads, undeclared requests, revocation and a fresh authenticated restart.

## Validation

Evidence is under `artifacts/sdk-client-packaging/`:

- `external04.log`: isolated external restore/build/start, ordinary full-trust
  behavior, authenticated services, consent and restart, plus sandboxed package
  validation/packing passed.
- `release-unit01.log`: deterministic package/hash, complete dependency closure,
  .NET-only transport dependencies and former type identities passed.
- `author-loop01.log`: the exported full-application reference completed its
  isolated restore, build, validate, scenario, package and installation workflow.
- `broker-tests01.log`: 81/81 broker checks passed, including malformed frames,
  identity/authentication, consent, cancellation, subscriptions and deadlines.
- `runtime-tests01.log`: 116/116 worker-runtime checks passed.
- `fulltrust-services01.log`: bridge full-trust host services passed, including
  background notifications/activity, undeclared denial and restart identity.
- `sdk-compatibility-direct01.log`: 17/17 SDK compatibility tests passed without
  baseline changes. The native `dotnet test` invocation reported zero tests;
  running the built MTP executable directly discovered and passed all 17. The
  zero-test invocation is not counted as validation.
- Relevant CLI, broker, worker runtime and bridge Release builds passed with
  zero warnings/errors and individual binary logs.
- Extraction review confirmed the server/client algorithms and host request
  admission bodies are unchanged. Whitespace validation passed.

No physical controller test is needed for this packaging-only change. The
accepted running overlay and normal profile remain untouched. Updating an
existing external project requires the newly exported package/version; its
widget code and permission declarations stay unchanged.
