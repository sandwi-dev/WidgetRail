# WinUI Release readiness

The first analyzer-enabled Release publish of the integrated frontend failed
with IL2026. Release enables trimming; reflection-based JSON calls in production
configuration, media transport, diagnostics and validation pages are not safe in
that configuration. This is an unresolved shipping gate, not permission to suppress
trim warnings or describe a Debug candidate as release-qualified.

The initial correction gives shell options/preferences a generated JSON context
and constructs media command/DevTools envelopes from explicit native JSON values.
Options retain their case-insensitive reading; persisted preferences retain their
existing strict names, bounds, validation and atomic replacement. Media messages
retain authority numbers, command sequencing and optional-field omission.

All 67 managed shell checks pass both normally and with
`JsonSerializerIsReflectionEnabledByDefault=false`; the emitted runtimeconfig was
checked for that false feature switch. The native frontend analyzer build passes
without warnings. Evidence: `artifacts/winui-release-probe/` and unique binlogs.
This does not yet prove a successful trimmed publish or native browser playback.

Still required:

- Separate validation pages/fixture entrypoints from the normal shipping build.
  Keep an explicit validation build so native correctness probes remain available.
- Give production shell/layout diagnostics explicit serialization. New switch
  diagnostics must likewise avoid reflection; keep expensive tree/file capture
  opt-in and outside ordinary frame work.
- Rerun the actual trimmed publish and resolve any subsequent dependency findings.
- Verify a Release production shell against its matching Bridge/workers, including
  settings round trips, media commands, artwork, menus and lifecycle cleanup.
- Complete installer bootstrap, runtime provisioning and rollback qualification.
  No package registration, certificate change, installation or distribution was
  performed by the publish probe.

Release and Debug comparisons must use the same actual widget workload and normal
motion/depth settings. The resource observer alone cannot establish smooth frames
or complete first-frame presentation; use switch milestones plus native pixel/frame
evidence and repeated runs.
