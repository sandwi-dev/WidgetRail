# PoB editor capability audit

Reviewed 2026-10-18 against the pinned engines in
[engine-lock.json](../samples/PobCompanion/engine-lock.json): PoE1 `274d129c`,
PoE2 `84a11f12`. This is a source-to-adapter/UI audit, not a claim of complete
desktop PoB parity or live testing of every item type.

The earlier tests verified the controls selected for adaptation. They did not
compare that selection against the complete upstream dialogs. Consequently a
missing control, such as the crafting Name field, could be absent from both the
implementation and its tests. Adapter tests alone cannot establish feature parity.

## Delivered and verified

| Upstream workflow | Widget treatment |
| --- | --- |
| Craft item Name | Rare drafts and stored/equipped rare items now have **Item name** in the shared editor. Existing-item changes use PoB's Edit Item Text callback with only the title line replaced. No modifier/base text is reconstructed. |
| Rare name default | Preserve `New Item` until the user edits it. It is the craft dialog's blank-title fallback, not a recently-added state. |
| Unique/normal/magic names | Do not expose them as arbitrary rare titles. Unique names can select gameplay behavior; normal/magic names encode their base/affixes. |
| Item modifiers | Predefined sources, custom modifiers, eligible affixes and individual rolls are exposed. Eligibility/formatting remains upstream. |
| Item properties | Applicable quality, influence, versions/variants, catalyst and rune choices are projected from upstream controls. |
| Skill groups | Label, slot, enabled state, Full DPS inclusion, group count, applicable weapon sets, duplication/removal and reordering have widget paths. |
| Individual gems | Level, quality, count, enable, applicable corruption and Vaal/global controls are exposed. Granted-gem restrictions are retained. |
| Gem search | Search/type filtering and on-demand upstream score sorting are exposed; these do not imply all desktop gem-picker preferences are exposed. |
| Configuration | Ordinary controls come from upstream definitions; grouped custom modifiers, names and enable switches have separate paths. |
| Calculation inspection | Uses upstream categories, breakdowns and sources. Calculation selection is separate from the main build skill. |
| Tree choices | Applicable attribute/mastery/tattoo choices and node notes use dedicated upstream adapters. |

## Confirmed editing gaps

| Area | Upstream evidence | Current limitation |
| --- | --- | --- |
| Specialized item modification | `ItemsTab` enchant/anoint/corruption buttons and dialogs | No corresponding dedicated widget dialogs. Availability differs by game/item; a method's existence is not proof it is currently usable. Importing an already-modified item is not the same as providing its editor. |
| Cluster-jewel authoring | `displayItemClusterJewelSkill`, `displayItemClusterJewelNodeCount` | No dedicated skill/passive-count controls. Most relevant to PoE1; PoE2 retaining code does not establish game support. |
| Crafting affix sorting | `craftingSorting` and upstream affix comparison tooltips | Widget lists eligible affixes but does not expose the desktop sorting control. |
| Gem creation/catalogue preferences | `defaultLevel`, `defaultQuality`, `showSupportGemTypes`, `showLegacyGems` | Widget uses upstream defaults/eligibility and per-gem editing; these global picker preferences are not exposed. |
| General item text editing | `EditDisplayItemText` | Manual item import exists, but there is no unrestricted in-widget raw-text editor. The new name control uses this callback narrowly. |

These are recorded gaps, not implemented features. No new gameplay rules or
parallel engines were introduced to fill them. See the [user guide](../samples/PobCompanion/USER_GUIDE.md) for the maintained
workflow and limitations. This inventory is a source audit of the revisions above,
not an active implementation plan.

## Render assumptions reviewed

The saved-ranking crash was a missing selected option after an equipment target
became inactive. A common adapter-view helper now renders an explicit unavailable
selection for source-backed set, main-skill, calculation-skill, gem-option and
trade selectors. It retains option/action identities and does not silently change
the build. Invalid multiple-selected/duplicate/malformed declarations are not
silently rewritten or hidden by a blanket render exception handler.

The removed-passive case also needed its focus destination retired: returning to
search must not retain a request for a missing detail button. Regression tests
cover this and missing upstream selections. Existing inventory/library/comparison
reconciliation was reviewed and retained; immutable enum selectors and guarded
lookups were not mechanically replaced.

Before claiming coverage after a future upstream update, compare the affected
dialog's visible controls and callbacks with this inventory, then exercise the
newly exposed control through draft/save/undo/reopen paths. A passing old test
suite is not evidence that new upstream controls have been adapted.
