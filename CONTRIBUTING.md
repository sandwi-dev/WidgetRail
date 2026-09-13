# Contributing to WidgetRail

WidgetRail has two kinds of contributors: people extending the overlay with
widgets and themes, and people improving the platform itself. Both are welcome.

## Build a widget or theme

Start with the [widget quickstart](docs/developers/widget-quickstart.md) or
[theme guide](docs/developers/theme-packaging.md). The
[SDK Gallery](samples/SdkGalleryWidget/README.md) demonstrates shared UI components.
You do not need to modify the host to add a widget.

## Work on the platform

Follow [Building from source](docs/maintainers/building.md) for Windows prerequisites.
Work on a branch, keep changes focused, and include reproduction steps and
relevant validation in your pull request.

| Area | Location |
|---|---|
| Native overlay, rendering and controller input | `src/OverlayHost` |
| Public C# authoring API | `src/WidgetSdk` |
| Declarative data contracts | `src/WidgetProtocol` |
| Widget processes and lifecycle | `src/WidgetRuntime`, `src/WidgetWorkerHost` |
| Host orchestration and Windows capabilities | `src/WidgetBridge`, `src/PlatformBroker`, `src/Windows*Provider` |
| Styling | `src/WidgetStyling` |
| Bundled widgets / examples | `src/FirstPartyWidgets`, `samples` |
| Developer CLI / templates | `tools/WrailCli`, `templates` |

Read [Architecture](docs/maintainers/platform-architecture.md) and
[Security and trust](docs/maintainers/security-and-trust.md) before changing a
process boundary. For UI elements, use the
[end-to-end guide](docs/maintainers/adding-declarative-ui-elements.md).

## Verify a change

From the repository root:

```powershell
.\scripts\Verify.ps1 -Configuration Release -Lane managed
```

The native lane requires the C++ and Rust tools:

```powershell
$env:RUSTUP_TOOLCHAIN = '1.97.1'
.\scripts\Verify.ps1 -Configuration Release -Lane native
```

For focused work, select existing step IDs from
`scripts/verification-steps.json`:

```powershell
.\scripts\Verify.ps1 -Configuration Release -StepId documentation-tests
```

The runner writes bounded output and evidence under `artifacts/verification`.
Keep NuGet auditing enabled. See [build execution](docs/maintainers/build-execution.md)
for restore requirements. Direct MSBuild commands should retain a unique binary log.

Automated checks cannot establish game compatibility or physical controller,
display, sleep/resume and audio behavior. State clearly what you tested and
what remains unverified. Tests for power controls must never shut down or
suspend the test machine.

## Change an API deliberately

Use the [versioning policy](docs/maintainers/versioning.md) and
[SDK compatibility workflow](docs/developers/widget-sdk-compatibility.md).
Update examples and docs with the implementation. Do not edit a baseline just
to silence a regression.

## Documentation and reports

Public docs live in `docs/users`, `docs/developers`, `docs/reference` and
`docs/maintainers`. Keep ticket transcripts and one-off development records out
of those guides. The three retained planning documents are working records, not public API requirements.

For bugs, provide the exact build, steps, expected behavior and observed behavior.
Do not post credentials or raw diagnostic archives without reviewing them.

## Write docs for someone learning

Introduce one idea at a time. Explain a term before using its internal name.
Prefer a short working example, explain what the reader should see, and link to
reference material for limits and advanced behavior. Keep operational evidence,
ticket histories, and obsolete design proposals out of the public learning path.

When adding a public SDK feature, give its family a focused documentation topic
and update `tests/Documentation.Tests/sdk-doc-map.json`. That map catches missing
entry points when public SDK files are added. It does not prove that prose is
complete or correct: review behavior against the implementation and compile
working examples as well.
