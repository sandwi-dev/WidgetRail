# Widget SDK migration and deprecation proposal

Status: proposal for planner approval; no compatibility policy is changed by
this document

## Current executable report

The report below was captured from the rebuilt corrected DLV-202 artifact graph
at commit `5fbf690bcfa8f20cb4eaff8012a6c7df17a0817f`. The follow-up DLV-203
commit changes documentation only.

| Field | Current evidence |
|---|---|
| Release unit | `0.1.0-dev` |
| Package | `WidgetRail.WidgetSdk` |
| Controller template | version 2 |
| Reviewed public API | 2,997 symbols |
| `WidgetSdk.dll` product version | `0.1.0-dev+5fbf690bcfa8f20cb4eaff8012a6c7df17a0817f` |
| `wrail.dll` product version | `0.1.0-dev+5fbf690bcfa8f20cb4eaff8012a6c7df17a0817f` |
| `WidgetSdk.dll` SHA-256 | `2392741C815C604BA29CFEABF6843EF3033907E4CC5901774E0D4C6B5AD26193` |
| `wrail.dll` SHA-256 | `E081DE4D0FF1D018BA3422074F68168F046B8528FBFAB950581195643BE70981` |

Reproduce the bounded report after a Release rebuild:

```powershell
$propsText = Get-Content -Raw .\eng\WidgetSdkRelease.props
$p = [xml]$propsText
$api = @(Get-Content .\src\WidgetSdk\PublicApi.txt |
  Where-Object { $_ -and -not $_.StartsWith('#') })
$sdk = Get-Item .\src\WidgetSdk\bin\Release\net8.0\WidgetSdk.dll
$wrail = Get-Item .\tools\WrailCli\bin\Release\net8.0\wrail.dll
$sdkHash = Get-FileHash $sdk.FullName -Algorithm SHA256
$gbarHash = Get-FileHash $wrail.FullName -Algorithm SHA256
[pscustomobject]@{
  ReleaseVersion = $p.Project.PropertyGroup.WidgetSdkReleaseVersion
  PackageId = $p.Project.PropertyGroup.WidgetSdkPackageId
  TemplateVersion = $p.Project.PropertyGroup.ControllerWidgetTemplateVersion
  PublicApiSymbols = $api.Count
  WidgetSdkProductVersion = $sdk.VersionInfo.ProductVersion
  WrailProductVersion = $wrail.VersionInfo.ProductVersion
  WidgetSdkSha256 = $sdkHash.Hash
  WrailSha256 = $gbarHash.Hash
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

1. Treat the SDK package, `wrail` distribution, controller template, API
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
of DLV-198 or its DLV-203 provenance refresh.
