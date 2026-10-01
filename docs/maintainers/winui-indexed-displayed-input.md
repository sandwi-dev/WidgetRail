# Indexed activation during publication

The deterministic regression establishes a real session frame and item lease,
publishes a second snapshot with the same query, and deliberately leaves the UI
on its first frame. Before correction the lease remains current, but
`ClaimsInput` calls `ValidateAuthority`, requires `LastGood.Authority` equality,
and throws `presentation_stale`. The frontend consumes the press. No bridge input
request is sent. This reproduces a concrete dropped activation; it does not prove
every observed real Playnite failure had the same cause.

The existing bridge already supports this interval: it retains up to 16 previous
input-origin snapshots and compares exact origin/current bindings before
admission. The correction preserves that contract instead of replaying an action.

Native indexed input now passes its actual displayed `WidgetPresentationFrame`
to new presentation-session overloads. The session records weak provenance for
published frame objects, so an equal reconstructed record is not an admitted
origin. No extra history of full widget trees is retained. The session verifies
current lease/query ownership, resolves origin and current input scopes through
the shared protocol contract, and rejects changed bindings or ownership before
dispatch. The original frame sequence is sent unchanged. The bridge and worker
retain their existing independent admission checks.

Strict authority-only methods are unchanged. Ordinary action/artwork handling
and indexed context-menu callers were not changed in this bounded correction.

Validation: the new test failed against baseline with 84 existing session tests
green; after correction all 88 session cases pass. New coverage includes an
unchanged displayed activation, altered shortcuts, a newly disabled collection,
fabricated frame copies and missing item identities. Existing modal/query tests
also exercise the displayed-frame overload. All 16 indexed bridge checks pass,
including a real worker action dispatched from an older displayed frame across
an unrelated publication. WinUI analyzer build passes. No UI deployment or
physical controller acceptance is claimed by this checkpoint.
