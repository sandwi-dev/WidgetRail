# Widget SDK migration and deprecation proposal

Status: proposal for planner approval; no compatibility policy is changed by
this document

## Current executable report

The report below was captured from the rebuilt DLV-196 artifact graph at commit
`51512b2`. The follow-up DLV-198 commit changes documentation only.

| Field | Current evidence |
|---|---|
| Release unit | `0.1.0-dev` |
| Package | `GameBarAlternative.WidgetSdk` |
| Controller template | version 2 |
| Reviewed public API | 2,997 symbols |
| `WidgetSdk.dll` product version | `0.1.0-dev+51512b2e82dd0dd0758b6cc38bda7f6ebdd29478` |
| `gbar.dll` product version | `0.1.0-dev+51512b2e82dd0dd0758b6cc38bda7f6ebdd29478` |
| `WidgetSdk.dll` SHA-256 | `FF2EE49506853214833F393B0A027013E6586F85F4F1DD9E9D0ED47416202B92` |
| `gbar.dll` SHA-256 | `302700D7C15746E1A37A5D7D4E7F0CFB38F613147B794D5E4AC88F436C2DC9DC` |

Reproduce the bounded report after a Release rebuild:

```powershell
$propsText = Get-Content -Raw .\eng\WidgetSdkRelease.props
$p = [xml]$propsText
$api = @(Get-Content .\src\WidgetSdk\PublicApi.txt |
  Where-Object { $_ -and -not $_.StartsWith('#') })
$sdk = Get-Item .\src\WidgetSdk\bin\Release\net8.0\WidgetSdk.dll
$gbar = Get-Item .\tools\GbarCli\bin\Release\net8.0\gbar.dll
$sdkHash = Get-FileHash $sdk.FullName -Algorithm SHA256
$gbarHash = Get-FileHash $gbar.FullName -Algorithm SHA256
[pscustomobject]@{
  ReleaseVersion = $p.Project.PropertyGroup.WidgetSdkReleaseVersion
  PackageId = $p.Project.PropertyGroup.WidgetSdkPackageId
  TemplateVersion = $p.Project.PropertyGroup.ControllerWidgetTemplateVersion
  PublicApiSymbols = $api.Count
  WidgetSdkProductVersion = $sdk.VersionInfo.ProductVersion
  GbarProductVersion = $gbar.VersionInfo.ProductVersion
  WidgetSdkSha256 = $sdkHash.Hash
  GbarSha256 = $gbarHash.Hash
} | ConvertTo-Json
```

The executable compatibility suite additionally proves that the generated API
matches `PublicApi.txt`, release-unit and template metadata agree, ordinary test
execution cannot rewrite the baseline, and a clean external restore consumes an
SDK nupkg whose `lib/net8.0` DLL set is exactly `WidgetSdk.dll` and
`WidgetProtocol.dll`.

## Proposed policy

This proposal should become policy only through a separately assigned planner
decision and implementation milestone.

1. Treat the SDK package, `gbar` distribution, controller template, API
   baseline, and protocol range as one reviewed release unit. Publish or retain
   evidence for them together; never infer compatibility from an assembly
   version alone.
2. Classify a public API diff as compatible addition, deprecation, or breaking
   removal/signature change. Compatible additions may remain within the current
   pre-release line. A breaking change advances the pre-release minor version,
   resets only affected pre-release author state, and ships a concise migration
   table in the same release unit.
3. Once an SDK artifact is intentionally distributed outside this repository,
   require one published release-unit interval between marking an API deprecated
   and removing it. Before that event, do not retain duplicate compatibility
   layers solely for this single-user development checkout. A security or
   correctness emergency may bypass the interval only through an explicit
   planner decision with an exact consumer-impact report.
4. At a breaking release, verify both the new scaffold and one fixture authored
   against the immediately preceding distributed release. The old fixture may
   fail with a documented precise migration diagnostic; it must not silently
   bind to another SDK, template, or protocol generation.
5. Keep public API and wire-protocol decisions separate. An SDK method addition
   does not imply a protocol version, and a protocol bump does not authorize an
   SDK compatibility shim. Each changed boundary keeps its own version and
   focused evidence.

## Proposed migration record

For each approved breaking change, add one table to the public compatibility
guide with: old symbol or behavior, replacement, first deprecated release unit,
first removed release unit, protocol impact, template impact, and a copyable
before/after snippet. The release review should reject an empty replacement or
an indefinite compatibility branch unless a supported external consumer is
identified.

No publication, signing, remote feed, multi-version resolver, deprecation
attribute, runtime fallback, API-baseline mutation, or protocol change is part
of DLV-198.
