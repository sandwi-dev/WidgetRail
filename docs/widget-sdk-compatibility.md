# Widget SDK compatibility and release unit

Status: checked-in pre-release API baseline and discoverable local release-unit validation

`eng/WidgetSdkRelease.props` is the canonical local release-unit contract. It
sets the pre-release SDK version, package ID, and supported ControllerWidget
template version stamped into both `WidgetSdk.dll` and `wrail.dll`. The
version-2 template manifest must match that metadata. `wrail new widget` writes
an exact content-addressed package version such as
`0.3.0-dev.local.<16-hex>` into the generated project and uses that exact value
in its `PackageReference`. A CLI, SDK assembly, template, or generated
dependency from another release unit fails closed instead of silently mixing.

This is a local pre-release contract. It does not publish a NuGet package,
promise post-1.0 semantic-version compatibility, sign the SDK, or change the
runtime wire protocol.

The active [SDK stability, migration, and deprecation
policy](widget-sdk-evolution.md) defines the reviewed release unit, public API
diff classifications, pre-release version movement, external-distribution
trigger, migration record, and separate wire-protocol review.

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

## Review an SDK change

From the repository root, use this review sequence:

```powershell
# 1. Build and classify the current public-surface diff.
& .\scripts\Verify.ps1 -Configuration Release `
  -StepId @('widget-sdk-build', 'widget-sdk-compatibility-tests') `
  -OverallTimeoutSeconds 600

# 2. After the classification, release-unit decision, documentation, and
#    migration record are reviewed, update only the checked-in API baseline.
dotnet run --project .\tools\WidgetSdkApiBaseline\WidgetSdkApiBaseline.csproj `
  --configuration Release -- update

# 3. Review the baseline and metadata changes, then verify the complete unit.
git diff -- .\src\WidgetSdk\PublicApi.txt .\eng\WidgetSdkRelease.props `
  .\templates\ControllerWidget .\docs
& .\scripts\Verify.ps1 -Configuration Release `
  -StepId @('widget-sdk-build', 'widget-sdk-tests', `
    'widget-sdk-compatibility-tests', 'wrail-cli-tests', `
    'documentation-tests') -OverallTimeoutSeconds 900
```

Step 1 is expected to fail when an intentional API diff has not yet been
accepted into `PublicApi.txt`; its exact missing and added symbols are the
classification input, not permission to update the baseline automatically.
Before step 2, apply the active
[SDK evolution policy](widget-sdk-evolution.md): classify the change as a
compatible addition, deprecation, breaking change, or emergency removal;
decide the pre-release release-unit movement; and add the required migration
record when applicable. Keep protocol and template impacts explicit even when
they are `none`.

### Compatible addition

A compatible addition may remain in the current pre-release minor line, but
the baseline change still requires review. Update affected public docs and
examples, and assign a new immutable release-unit version before intentionally
distributing a distinct artifact outside the repository.

### Deprecation

Keep the old surface callable, document the replacement, and add the complete
migration record. An externally distributed surface remains present for the
policy's required published release-unit interval before removal.

### Breaking change or emergency removal

A removal or signature change is a breaking pre-release reset. Advance the
pre-release minor line, update affected examples and template code, and add the
complete migration record with a copyable before/after example. A template
contract change also updates `ControllerWidgetTemplateVersion`, the closed
`template.json`, scaffold fixtures, and public instructions together.
Unsupported old local template formats fail rather than migrate silently.

An emergency removal follows the same evidence and version movement. Only an
explicit user-approved planner decision with an exact consumer-impact report
may bypass an applicable deprecation interval.

After that decision, step 2 regenerates only the API baseline:

```powershell
dotnet run --project .\tools\WidgetSdkApiBaseline\WidgetSdkApiBaseline.csproj `
  --configuration Release -- update
```

Review `src/WidgetSdk/PublicApi.txt` as a normal source change, then rerun the
compatibility, WidgetSdk, and WrailCli scaffold checks. The update command does
not edit the release-unit properties, template, package, docs, or protocol; it
only makes the reviewed public-surface decision explicit. Tests never invoke
this command against the checked-in baseline.

### Migration record template

Use this table in this guide for each approved breaking change or deprecation:

| Old surface or behavior | Replacement | First deprecated release unit | First removed release unit | Protocol impact | Template impact | Before/after example |
|---|---|---|---|---|---|---|
| `UI.MediaTile(...)` and `UI.AppTile(...)` | Argument-compatible `UI.Tile(...)` | `not distributed` | `0.2.0-dev` | none | none | Before: `UI.MediaTile(title, state, action, id)` or `UI.AppTile(title, state, action, id)`; after: `UI.Tile(title, state, action, id)` |
| `UI.ToggleButton(label, isOn, action, id)` | Argument-compatible `UI.Switch(label, isOn, action, id)` | `not distributed` | `0.3.0-dev` | none | none | Before: `UI.ToggleButton(label, isOn, action, id)`; after: `UI.Switch(label, isOn, action, id)` |

This repository has not claimed an externally distributed SDK release unit, so
the intentional pre-release consolidation required no deprecation interval.

## App-library launch observations

The current pre-release SDK adds public `WidgetAppLaunchObservationState`,
`WidgetAppLaunchObservation`, and
`WidgetAppLibraryService.LaunchObservedAsync`. This additive contract replaces
repository friend access previously used by the bundled Game Launcher. It
returns only bounded sanitized progress and support flags; it exposes no
process, path, launcher identity, provider authority, or durable launch token.
Community launchers must still persist only `SavedId`, resolve it immediately
before launch, and invoke the returned current opaque `AppId` exactly once.
