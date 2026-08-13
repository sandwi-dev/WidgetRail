# Widget presentation session tests

This focused MSTest.Sdk 4.3.2 suite covers the managed bridge-session boundary:
the existing hello handshake, correlated cancellation, exact-authority artwork
completion, bounded retained diagnostics, and configuration limits. Real sandboxed and full-trust runtime
parity remains in the two `Managed presentation session ...` cases in the
existing `WidgetBridge.Tests` runner so those cases exercise the production
`WidgetBridgeServer` and worker supervisors rather than a second backend.
