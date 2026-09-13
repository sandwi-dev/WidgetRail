# Widget SDK stability, migration, and deprecation

Status: active pre-release authoring policy

This page defines how WidgetRail reviews changes to its public authoring SDK.
The product is pre-release, and no post-1.0 compatibility promise is made.
Repository-local development also does not justify keeping obsolete APIs or
parallel compatibility layers. Changes are deliberate, reviewable, and paired
with migration evidence when they affect authors.

## The reviewed release unit

An SDK compatibility decision reviews these parts together as one release
unit:

- the `WidgetRail.WidgetSdk` package;
- the matching `wrail` distribution;
- the ControllerWidget template selected by
  `ControllerWidgetTemplateVersion`;
- the reviewed public surface in `src/WidgetSdk/PublicApi.txt`; and
- the declarative snapshot protocol range supported by that unit.

`eng/WidgetSdkRelease.props` is the canonical source for
`WidgetSdkReleaseVersion`, `WidgetSdkPackageId`, and
`ControllerWidgetTemplateVersion`. The current supported snapshot range is
defined separately by `ProtocolConstants.MinimumSupportedVersion` through
`ProtocolConstants.CurrentVersion`. Review those values with the release unit,
but do not infer that the SDK version and protocol version move together.

The compatibility suite checks that the generated API matches
`PublicApi.txt`, the SDK and CLI metadata agree with the release properties,
the ControllerWidget template carries the same supported template version,
and an external restore consumes the exact local package contents. Ordinary
test execution is read-only; only the explicit baseline updater changes
`PublicApi.txt`.

## Classify every public API diff

Use the exact missing and added symbols reported by the compatibility suite.
A signature change has both a removed old signature and an added new
signature.

| Classification | Meaning | Required evidence | Pre-release version movement |
|---|---|---|---|
| Compatible addition | Existing reviewed source continues to compile and the new surface is optional. | Reviewed `PublicApi.txt` addition, affected docs/examples, compatibility suite, and matching release-unit metadata. | May remain in the current pre-release minor line. Any distinct artifact intentionally distributed outside the repository still receives a new immutable release-unit version. |
| Deprecation | The old surface remains callable, but a documented replacement is now preferred. | Compatible-addition evidence plus a migration record naming the old surface, replacement, and planned interval. | Remains in the current pre-release minor line while the old surface exists. |
| Breaking change | A public symbol is removed, its signature or behavior is incompatible, or an old template contract is no longer accepted. | Reviewed removal/change, complete migration record and before/after example, updated templates/examples, the new scaffold, and a fixture from the immediately preceding distributed unit that either migrates or fails with the documented precise diagnostic. | Advance the pre-release minor line and reset its patch position; update the complete release unit together. |
| Emergency removal | A security or correctness emergency requires removal before an applicable deprecation interval completes. | Breaking-change evidence plus an explicit user-approved planner decision and an exact supported-consumer impact report. | Use the breaking-change movement; the exception changes timing, not evidence or version identity. |

An addition is not compatible merely because it appears as an added baseline
line. Review default behavior, overload resolution, inheritance, nullability,
generic constraints, and generated-template use. Similarly, do not preserve a
removed pre-release surface unless an identified supported consumer requires a
bounded migration interval.

## External distribution and deprecation interval

The deprecation interval begins only after an SDK release unit is intentionally
distributed for use outside this repository. A release review records that
event with the immutable release-unit version, distribution location or
channel, intended consumer set, and artifact digest. A local build, repository
fixture, or unpublished package is not external distribution.

After that trigger, a public surface must remain present for at least one
subsequent published release-unit interval after it is first marked deprecated
before removal. Before the trigger, pre-release cleanup may remove an obsolete
surface in a reviewed breaking release without manufacturing a compatibility
shim. An emergency may bypass the interval only through the explicit authority
and consumer-impact evidence in the table above.

## Migration record

Every approved breaking change, and every deprecation intended for later
removal, adds a table to the public compatibility guide with all of these
fields:

| Required field | What to record |
|---|---|
| Old surface or behavior | Exact symbol, signature, template behavior, or author-visible contract being changed. |
| Replacement | Exact supported API or author workflow; an empty replacement requires explicit rationale. |
| First deprecated release unit | Immutable release-unit version, or `not distributed` when no external interval has begun. |
| First removed release unit | Immutable breaking release-unit version, or the planned earliest unit while still deprecated. |
| Protocol impact | `none`, or the separately reviewed protocol version and migration effect. |
| Template impact | `none`, or the separately reviewed ControllerWidget template version and scaffold effect. |
| Before/after example | Copyable author code or commands showing the complete migration. |

Do not use an indefinite compatibility branch as the replacement. If there is
no reasonable replacement, the record says so and identifies the supported
consumer impact explicitly.

## SDK API and wire protocol are separate

The SDK API baseline describes the author-facing managed surface. The snapshot
protocol describes bounded cross-process presentation data. They have separate
versions, validators, compatibility evidence, and review decisions:

- adding an SDK method does not authorize or require a protocol version;
- adding a protocol feature does not authorize an SDK compatibility shim;
- a snapshot uses the minimum protocol required by its actual features within
  the supported range; and
- a change affecting both boundaries records and verifies each impact
  independently.

Never use an assembly or package version as a substitute for protocol feature
admission. Do not add a runtime fallback, multi-version resolver, or duplicate
public surface unless a later assignment approves that explicit compatibility
design.

## Review workflow

Follow the copyable sequence in the
[compatibility guide](widget-sdk-compatibility.md#review-an-sdk-change). It
keeps classification, migration evidence, baseline mutation, release-unit
metadata, and focused verification in review order. Publication, signing,
remote feeds, compatibility shims, and post-1.0 policy remain separate future
decisions.
