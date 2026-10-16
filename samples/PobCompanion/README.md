# PoE Build Companion

Two controller-first widgets share one implementation and run the unmodified Path of Building Community calculation engines. PoE 1 and PoE 2 remain separate because their build formats and game data differ.

## Using a build

Paste a PoB share code or supported public build link, or enter the path to an exported XML file. The widget keeps the original import and a separate working copy. It reopens the selected working copy after restart.

Imports default to **New build**. In Build & import, choose **Replace current build** to refresh the open build from a file, link or share code. The replacement is validated before confirmation, retains the build's name and identity, and participates in Undo/Redo across restarts. Finish any pasted item draft first. Account sign-in is not included: it requires an authorized GGG application registration for WidgetRail, separate from the upstream desktop client's identity.

For a browser-based character import, choose **Open pob.cool** on the welcome screen or in Build & import. The Windows web intent opens the appropriate game in your default browser. Import your character on that site, copy its PoB export code, then return to WidgetRail and choose **Paste build from clipboard**. WidgetRail does not automate the site, inspect its login, or monitor the clipboard; clipboard text is read only when you select Paste.

- **Calcs** groups statistics beside their calculation setup. Main-skill DPS identifies the skill; disabled skills show an unavailable value and PoB's reason. Full DPS identifies its included groups and contributors. Breakdowns identify their skill and calculation mode where relevant, and explain when their total differs from the overview. Matching defensive totals omit unrelated skill context. Exact repeated resource-calculation sequences from upstream are collapsed without changing their values or source rows.
- **Tree** shows upstream passive-tree positions, rendered connections and node artwork, with an adjacent node inspector. Hover or dwell with the controller pointer for modifiers and point costs. Search selection frames the node; Main tree and Ascendancy frame their respective areas. L2/R2 use the same internal map zoom as the minus/plus buttons, keeping the toolbar and surrounding layout at their normal size.
- **Skills** and **Items** keep the list beside the selected entry. Skill options inspect and edit that group without changing the main calculation group. Item comparisons name the replaced and proposed items and show changed statistics before Apply. Descriptions preserve upstream inline formatting through native rich text and theme colors.
- **Configuration** retains upstream section names and order in a category list. Controls scroll within each section; search covers all sections, and Show explanations reveals the upstream help. Retained settings that are currently inapplicable are identified explicitly. Changes recalculate and save immediately; Undo reverses them.
- **Notes** displays a continuous scrolling document and opens guide links through the host.

Configuration, main-skill selection, skill toggles/options, gem edits and character level recalculate and save directly, without confirmation. Undo and Redo are available in the header and preserve the current section. Switching active sets also saves directly and participates in the same history. A new edit clears Redo. Each saved build retains its own history of up to twelve changes across unloading and restart; a new or imported build starts with an empty history.

Node allocation/refunds, equipment replacements and structural class/ascendancy changes retain a comparison preview. Previews are saved automatically as pending work, without applying them. **Apply** commits the proposal; **Discard** restores the previous calculations without consuming Undo or Redo history. Both decisions persist. Refund previews include dependent nodes. Pasted items awaiting a slot also survive unloading; **Discard item draft** explicitly removes them. Export uses the upstream engine's XML format and standard compressed share-code encoding. Unlike desktop PoB's separate Save action, the companion continues to save its working copy automatically.

Tree preview actions stay above the scrolling comparison panel. Right from a skill or item row enters its details; Left returns to the selected row. Section switches explicitly focus the destination content and use the host's section transitions. Animation follows the user's motion preference. Startup shows a loading indicator while the calculation engine prepares the saved build.

Long lists offer contextual **L3** jumps to their surrounding controls and back to the remembered row: passive search, saved-build search/sort, set selectors, gem-group actions and expanded tree-preview actions. Configuration cycles through search/set controls, Show explanations and the section list. Ordinary lists qualify above eight rows; taller gem cards count as two visual rows. Short lists, absent set selectors and ordinary reading content do not add shortcuts. These jumps preserve the list viewport, and the tree WebView keeps its own L3 toolbar action.

Comparison previews use upstream PoB's displayed-stat comparison rules, including relevant stats, percentage changes, lower-is-better values and separate minion/player groups. They compare the actual committed and proposed calculations, including any automatic character-level change. Tree previews start with this overall impact and point counts; **Show changed nodes** expands all node names and modifiers, using pre-refund descriptions for removed nodes. Expanding details does not apply or discard the change. Restored previews regenerate their summaries from both saved XML snapshots.

Subsequent work uses a floating progress notification with Cancel (or B), without inserting rows into the workspace. Progress remains until the operation completes. Informational feedback lasts three seconds; warnings and errors last eight seconds. A new operation replaces prior feedback.

Use **Build & import** to rename a build, open another saved build, import, export or change character details. LB/RB switches sections, with native controller symbols beside the tabs instead of a separate footer. Controller focus and list navigation remain native; the optional tree visualization uses a host-owned packaged web session.

Build & import uses a compact form with labelled fields and separate build, import and export groups. Saved builds supports search, Recent/Name sorting, and adjacent Open, Rename, Duplicate and Delete actions. Duplicate keeps committed and pending work in an independent copy with fresh history. Delete requires confirmation and removes only that build's managed files; exported XML and previously preserved recovery copies remain on disk. The calculation inspector shows skill context for main-skill damage, group context for Full DPS, and the selected statistic's breakdown for other values.

Native passive descriptions and tree hover cards share theme-aware rich text, preserving upstream wording and colors and emphasizing values and recognizable stat terms in otherwise plain modifiers. The selected tree node has a screen-space frame and name label; Locate selected centers it at a readable minimum zoom. Buff-only main skills show **No direct damage** instead of a misleading zero DPS, while damaging skills retain PoB's calculated values and disabled-skill reasons.

Description emphasis also covers charge types, ailments, and mechanics such as Empower and Repeatable. Main-skill descriptions recognize multiword skill references from the pinned engine's own granted-effect names, including hidden effects; single-word names remain neutral to avoid ambiguous matches in prose. Upstream warning colors always take precedence. This formatting adds no links or gameplay behaviour.

Passive search results and saved builds use virtualized lists with stable row identities. Returning from a passive inspector restores the selected row. Notes, configuration sections and equipment-slot choices scroll continuously rather than using Next page actions.

Tree previews list every changed passive with its numeric modifier descriptions. Refunded nodes use their pre-refund names and modifiers, including chosen attributes; allocation previews use the proposed state. An automatic character-level change is shown alongside point counts so its contribution to changed totals is visible.

## Scope and compatibility

This is a companion for inspecting and comparing builds, not a replacement for every desktop PoB workflow. It does not implement trade searching, account/character import, the desktop item-crafting editor, advanced gem/socket-group authoring, every specialized tree choice, or every desktop calculation view. Unsupported allocation choices are rejected rather than guessed. Historical builds whose gems were removed by the pinned engine are rejected rather than silently losing them. Calculations are only as applicable as the selected engine version and the build's assumptions.

The six categorical color roles follow the selected WidgetRail theme; labels and geometry also convey meaning. The tree consumes the host's packaged-page theme projection, including high contrast. Remote websites receive no theme injection.

## Engine lifecycle and storage

The Lua process starts on first calculation and does no periodic recalculation while idle. The widget unloads after 120 seconds of inactivity under the host residency policy. Calculation state and game data occupy hundreds of MiB; this is not a lightweight web page. Cancellation retires the calculation process, and the next command restores the displayed build before applying an edit.

Completed user actions atomically save the committed build, pending comparison or item draft, and Undo/Redo together in a versioned `.work.json` record. XML snapshots use the existing compressed PoB encoding; the record is bounded to 16 MiB. The ordinary working `.xml` remains a mirror for compatibility, while recovery treats the work record as authoritative if a process exit interrupted that mirror write. Existing XML-only builds remain readable. No shutdown callback or resident process is required to preserve completed work. Reopening recalculates with the current engine and restores only the context needed to finish pending work; ordinary tabs, search, focus, scroll and tree viewport are not persisted.

If pending work or history cannot restore, recovery tries the committed build in the record, then the saved XML. Before resetting the active record it preserves the exact failed bytes in a uniquely named `.recovery.*.json` file and shows a warning. If preservation or repair cannot finish, the recovered build remains viewable but read-only; Duplicate provides an independent editable copy. If neither build source can load, the widget reports the failure without replacing either source.

Default working copies live under `%LOCALAPPDATA%/WidgetRail/applications/pob1` and `pob2`. Immutable engine archives are extracted to a digest-keyed cache under `%LOCALAPPDATA%/WidgetRail/pob-engines`. Extraction is bounded, cancellation-aware and staged before publication. `WIDGETRAIL_POB1_DATA`, `WIDGETRAIL_POB2_DATA` and `WIDGETRAIL_POB_CACHE` allow isolated development profiles. `WIDGETRAIL_POB_DATA` remains a common data-directory override when no per-game override is set.

## Rebuilding

`engine-lock.json` pins the PoB releases, LuaJIT and lua-utf8 source commits. `Prepare-Engines.ps1` fetches those exact sources and builds the x64 runtime using Visual Studio C++ tools. It writes `paths.json` containing the prepared source/runtime directories. `Build-CommunityPackage.ps1` accepts `-Game 1` or `-Game 2`, those directories, and a current `wrail` CLI path, producing a validated `.wrwidget` package in a fresh output directory.

Artwork preparation uses Python with the versions in `requirements-artwork.txt`; pass `-Python` when using a dedicated build environment. `Build-TreeArtwork.py` reads artwork directly from the pinned Git objects, converts upstream sprite metadata and DDS arrays into small browser atlases, and verifies the resource bounds for each tree. Only the open tree's assets enter its web session. Python and the conversion libraries are not runtime dependencies. Node artwork retains the upstream game's visual identity; surrounding controls, connectors, focus and preview markers follow the selected WidgetRail theme.

Install each resulting `.wrwidget` through **Settings → Widgets → Install local widget**. The packages use the full-access application runtime to launch their local Lua engine.

The executable test harness accepts `game engine-directory runtime-directory fixture-directory`. Add `--work-state-only` for focused tests that destroy and recreate widgets and Lua processes, restore previews and history, exercise item drafts, and inject save failures. Use `--management-only` for recovery fallbacks, build-library operations and contextual inspectors. These tests use independent temporary working copies. XML comparisons preserve authored values and ordered lists while accounting for upstream map ordering and documented load defaults.

Use `--comparison-only` to compare allocation, large-refund and minion summaries against a separate upstream desktop tooltip invocation. The oracle loads the before/after XML without loading the widget adapter. It also checks restored and unchanged comparisons.

`Tests/Test-LibraryFocus.ps1` checks native arrow navigation into and out of the virtualized library, including remembered rows and detail controls. Open Saved builds in a disposable candidate profile with at least two builds, then supply its process ID, main-window handle and a fresh evidence directory. It changes focus only and records each WinApp result.

Engine upgrades are deliberate: update the lock, rebuild, run the real-engine fixture suite and inspect both widgets. The bridge adapter is the compatibility boundary; game calculations remain upstream. Do not silently replace an engine in a running build.

Each package includes the applicable upstream PoB license, LuaJIT copyright and lua-utf8 license. Fixtures used during development are not bundled as sample builds. Path of Building Community and Path of Exile are separate projects; these widgets are not affiliated with their maintainers or Grinding Gear Games.
