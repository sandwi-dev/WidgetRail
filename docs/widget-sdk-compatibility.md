# Widget SDK compatibility and release unit

Status: checked-in pre-release API baseline and local release-unit validation

`eng/WidgetSdkRelease.props` is the canonical local release-unit contract. It
sets the pre-release SDK version, package ID, and supported ControllerWidget
template version stamped into both `WidgetSdk.dll` and `gbar.dll`. The
version-1 template manifest must match that metadata. `gbar new widget` writes
an exact content-addressed package version such as
`0.1.0-dev.local.<16-hex>` into the generated project and uses that exact value
in its `PackageReference`. A CLI, SDK assembly, template, or generated
dependency from another release unit fails closed instead of silently mixing.

This is a local pre-release contract. It does not publish a NuGet package,
promise post-1.0 semantic-version compatibility, sign the SDK, or change the
runtime wire protocol.

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
dotnet run --project .\tests\WidgetSdk.Compatibility.Tests\WidgetSdk.Compatibility.Tests.csproj `
  --configuration Release -- --update
```

Review `src/WidgetSdk/PublicApi.txt` as a normal source change, then rerun the
compatibility, WidgetSdk, and GbarCli scaffold checks. The update command does
not edit the release-unit properties, template, package, docs, or protocol; it
only makes the reviewed public-surface decision explicit.
