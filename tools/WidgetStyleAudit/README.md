# Widget style audit

Read-only source audit of first-party, sample, template and historical-fixture
WRSS against every embedded theme. Requires the repository-pinned .NET 10 SDK;
its bundled Roslyn assemblies parse C# factory calls without executing widgets.
No new NuGet dependency is installed. This tool is not part of the application.

From the repository root:

```powershell
dotnet build tools/WidgetStyleAudit/WidgetStyleAudit.csproj -c Release '-bl:artifacts/widget-style-audit/build-{}.binlog'
dotnet tools/WidgetStyleAudit/bin/Release/net10.0/WidgetStyleAudit.dll . artifacts/widget-style-audit/report.json --verify
```

The JSON inventories every WRSS file, counts selectors, records literal colors,
custom tokens, compositing declarations and compiler/WinUI diagnostics. It maps
literal `UI` factory calls to semantic roles/classes and lexical parent surfaces,
then resolves normal, focused, selected, selected-focused, pressed,
disabled-focused and busy-focused states using the production cascade.

`--verify` fails on unresolved plain-text ink and calculated text/focus contrast
findings in shipping or template sources, or on any compilation error. Legacy
test-fixture contrast findings are reported separately, not silently waived as
shipping passes. Zero source files is an error. Without `--verify`, findings are
reported but only compilation errors fail the command.

This is a review aid, not a whole-application accessibility certification:

- Dynamic class expressions and selectors without a literal factory mapping
  are explicit coverage gaps. They are still parsed and resolved as selectors,
  but their real node composition is not established by the scan.
- Local-variable composition, helper-generated descendants, runtime branches,
  ancestor state propagation and actual view geometry require snapshot/native
  tests. A lexical parent is not proof of the final runtime parent.
- Contrast pairs use base fills and black/white backgrounds only for fills with
  at least 90% alpha. Shading, compound opacity, backdrop policy, images and
  native resource fallback are not simulated. Lower-alpha declarations remain
  visible in the inventory for manual review.
- Embedded HTML is inventoried, not evaluated as WRSS. Remote website UI and
  original-color media/logo assets have separate ownership.

See [the audit report](../../docs/developers/widget-style-audit.md) for the
reviewed findings, corrections and validation boundaries.
