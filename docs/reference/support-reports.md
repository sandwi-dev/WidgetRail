# Settings support reports

Settings uses the existing host-trusted diagnostics companion. Community widgets
cannot acquire this channel via a manifest capability. `export-support-bundle`
accepts no path or widget identity and uses the same nonce and bound process
checks as other Settings management operations. It returns a bounded result and
ZIP basename, never an arbitrary filesystem path.

The Bridge writes to the user's Windows Documents known folder under
`WidgetRail/Support`. Each explicit export produces a uniquely named ZIP using a
temporary sibling and a final move. Cancellation and I/O failures remove the
partial file; a transport failure after completion can leave a valid ZIP, so the
UI advises checking the destination before retrying. No upload or background
export is performed. Existing exports are not deleted automatically.

The ZIP contains `README.txt` and `diagnostics.json` (format version 1). The JSON
is reconstructed with fixed service keys, app/OS versions, controller readiness,
counts, widget package identifiers, worker start/running state and allowlisted
failure categories/timestamps. It does not serialize the snapshot wholesale.
Free-form summaries, display names, exception messages, raw logs, settings,
recovery tokens/identities, private paths and widget content are excluded.
Unknown worker codes become `other`; identifiers reveal which widgets are present,
which the UI explains before export. Snapshot bounds limit report size.

`PlatformWorkerDiagnostic.LastFailureAtUtc` is optional for compatibility. The
Bridge records UTC when it receives an actual failure; reads do not change the
timestamp. The page displays local time, with an explicit unknown-time fallback.
The snapshot retains the latest failure per registered widget, not a durable
incident journal. Export includes all reported workers; the page bounds recent
failure display to 16 and explains when more exist.

`DiagnosticHealth` treats unavailable core services and stopped failed workers as
requiring attention. A running worker's previous failure remains visible without
making it a current failure. Unreported overlay/Guide telemetry is neutral;
explicitly degraded telemetry is not. Controller failures and pending authority
recovery also require attention. Settings/theme validation joins this summary in
the Settings presentation layer.

Validation lives in `PlatformDiagnostics.Tests`, `SettingsWidget.Tests`, and the
diagnostics projection scenarios in `WidgetBridge.Tests`. Physical controller,
theme/readability and protected Documents-folder checks remain separate from
these automated checks.
