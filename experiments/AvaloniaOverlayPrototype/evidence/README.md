# AVP-003 retained evidence

`scripts/Verify-Avp003.ps1` performs one bounded Release build, confirms that
the isolated invalid compiled-binding fixture fails with `AVLN2000`, and runs
the focused MSTest.Sdk 4.3.2 suite. The real project uses compiled bindings and
typed AXAML scopes; the invalid fixture is not part of its solution.

Correction evidence includes canonical bounded semantic-ID encoding, a
latest-wins insertion/reorder that restores the same raw ID and AutomationId
after recycling, rejection of unknown-item and undeclared-action tuples, and a
worker-thread remote completion whose bound state is published only through
the Avalonia UI scheduler.

The tracked [`composition-comparison.json`](composition-comparison.json)
summarizes the seven-sample manual/direct-DI microcomparison. Raw outputs remain
under ignored `artifacts/avp003/composition-comparison`; manual composition is
the retained runtime decision.

After the AVP-003 commit, `scripts/Measure-Avp003.ps1` writes one exact-commit
ordinary Windows measurement to ignored `artifacts/avp003/measurement.json` and
copies the framework-dependent visible runtime beside it. The artifact binds
the source commit to executable ProductVersion and SHA-256 and records package,
resource, switch, complete-frame, native-transition, 10,000-item virtualization,
raw/Automation identity reorder, semantic focus/scroll-return, snapshot action
authority, scheduler-thread, and exact valid-action evidence.

Transition diagnostics inspect Avalonia visual/composition state only. Physical
controller behavior, transparent Windows composition, mixed-monitor DPI, and
visual quality remain planner/user checks. GPU cost remains unavailable without
an authorized ETW/PresentMon lane.
