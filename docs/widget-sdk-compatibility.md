# Widget SDK compatibility and release unit

Status: checked-in pre-release API baseline and discoverable local release-unit validation

`eng/WidgetSdkRelease.props` is the canonical local release-unit contract. It
sets the pre-release SDK version, package ID, and supported ControllerWidget
template version stamped into both `WidgetSdk.dll` and `gbar.dll`. The
version-2 template manifest must match that metadata. `gbar new widget` writes
an exact content-addressed package version such as
`0.1.0-dev.local.<16-hex>` into the generated project and uses that exact value
in its `PackageReference`. A CLI, SDK assembly, template, or generated
dependency from another release unit fails closed instead of silently mixing.

This is a local pre-release contract. It does not publish a NuGet package,
promise post-1.0 semantic-version compatibility, sign the SDK, or change the
runtime wire protocol.

DLV-213 adds the reviewed protocol-v16 advanced-presentation surface:
`WidgetAdvancedPresentationDeclaration`, the closed kind/preset/slot enums,
`WidgetAdvancedPresentationView`, `WidgetView.AdvancedPresentation`, and
`WidgetElement.InAdvancedPresentationSlot`. These are additive public symbols.
They do not change the `0.1.0-dev` release-unit version or template inventory;
the API baseline is intentionally regenerated in the same milestone. Older
views remain on their prior protocol version, and omission keeps ordinary
declarative presentation.

The current bounded artifact report and a non-operative migration/deprecation
proposal are recorded in the
[Widget SDK governance proposal](widget-sdk-governance-proposal.md). That page
does not change compatibility behavior; it gives the planner an explicit policy
decision and evidence shape to accept, revise, or reject.

## Check the current public surface

Run the SDK tests and compatibility check from the repository root:

```powershell
& .\scripts\Verify.ps1 -Configuration Release `
  -StepId @('widget-sdk-build', 'widget-sdk-tests', 'widget-sdk-compatibility-tests') `
  -OverallTimeoutSeconds 600
```

`src/WidgetSdk/PublicApi.txt` is an ordinal, checkout-path-free snapshot of the
exported types, constructors, fields, properties, events, methods, nullability,
defaults, generic constraints, and inheritance surface. The checker is bounded
to 5,000 symbols and a 1-MiB baseline. It prints every missing symbol as
`REMOVED_OR_CHANGED` and every new side of a change as
`COMPATIBLE_ADDITION_OR_CHANGED`. Either category fails verification so the
reviewed baseline cannot drift implicitly.

The compatibility project is the repository's first discoverable
`MSTest.Sdk` 4.3.2 suite. Ordinary `dotnet test` and verifier execution are
read-only: the named tests compare the current surface with the reviewed
baseline and exercise malformed, missing, oversized, path-bearing, and exact
diff-classification cases. Existing executable scenario suites retain their
current `dotnet run` contracts.

## Accept an intentional pre-release change

First inspect the exact reported symbols and classify the change:

- A compatible addition may be accepted without changing the release-unit
  version, but the baseline change still requires review.
- A removal or signature change is a breaking pre-release reset. Do not retain
  an obsolete API solely for compatibility. Change
  `WidgetSdkReleaseVersion` in `eng/WidgetSdkRelease.props` when the intended
  release unit changes, update affected examples/template code, and record the
  deliberate reset in the same review.
- A template contract change updates
  `ControllerWidgetTemplateVersion`, the closed `template.json`, scaffold
  fixtures, and public instructions together. Unsupported old local template
  formats fail rather than migrate silently.

After that decision, regenerate only the API baseline:

```powershell
dotnet run --project .\tools\WidgetSdkApiBaseline\WidgetSdkApiBaseline.csproj `
  --configuration Release -- update
```

Review `src/WidgetSdk/PublicApi.txt` as a normal source change, then rerun the
compatibility, WidgetSdk, and GbarCli scaffold checks. The update command does
not edit the release-unit properties, template, package, docs, or protocol; it
only makes the reviewed public-surface decision explicit. Tests never invoke
this command against the checked-in baseline.

## App-library launch observations

The current pre-release SDK adds public `WidgetAppLaunchObservationState`,
`WidgetAppLaunchObservation`, and
`WidgetAppLibraryService.LaunchObservedAsync`. This additive contract replaces
repository friend access previously used by the bundled Game Launcher. It
returns only bounded sanitized progress and support flags; it exposes no
process, path, launcher identity, provider authority, or durable launch token.
Community launchers must still persist only `SavedId`, resolve it immediately
before launch, and invoke the returned current opaque `AppId` exactly once.
