# Maintain current documentation

The README explains the benefit and first successful interaction. `docs/users`
owns everyday workflows. Integration package READMEs own setup, verification,
reconnection and credential cleanup. Developer/reference pages own exact API and
lifecycle contracts; maintainers' pages own build, recovery and release procedures.

Link to a topic's detailed page instead of duplicating its setup steps. Verify
claims against current source, including labels, manifests and release content.
Source behavior and downloadable release behavior can differ. Do not infer
publication, live-service success or physical acceptance from a passing fixture.

## Run the check

From the repository root:

```powershell
dotnet build tests/Documentation.Tests/Documentation.Tests.csproj -c Release '-bl:artifacts/documentation/check-{}.binlog'
dotnet run --project tests/Documentation.Tests/Documentation.Tests.csproj -c Release --no-build
```

This repository uses a small console checker for documentation, not a test-runner
assembly. It checks local Markdown links/images, SDK topic mapping and selected
source/documentation contracts. It does not fetch external links, exercise the
app or prove every prose claim. Review changed UI labels and service requirements
against source before release; use [the release checklist](release-checklist.md).

## Retire old notes

Keep current contracts and reproducible diagnostic procedures. Completed task
plans, temporary candidate paths, speculative alternatives and per-iteration logs
belong in Git history. Do not maintain an archive tree duplicating that history.
Transfer unresolved qualification categories to release preparation without
claiming that a removed checklist was passed.

The three owner-maintained delivery/agent documents remain separate from public
guides. Preserve local edits to them; a documentation cleanup does not change
their task ownership or authorize application changes.

For examples/screenshots, follow the [demonstration inventory](demonstrations.md).
