# PoE Build Companion

Two controller-first widgets share one implementation and run the unmodified Path of Building Community calculation engines. PoE 1 and PoE 2 remain separate because their build formats and game data differ.

## Using a build

Paste a PoB share code or supported public build link, or enter the path to an exported XML file. The widget keeps the original import and a separate working copy. It reopens the selected working copy after restart.

- **Calcs** groups statistics beside their calculation setup. Main-skill DPS identifies the skill; disabled skills show an unavailable value and PoB's reason. Full DPS identifies its included groups and contributors. Saved desktop Calcs breakdowns identify their own skill and buff mode, which can differ from the main sidebar selection.
- **Tree** shows upstream passive-tree positions, rendered connections and node artwork, with an adjacent node inspector. Hover or dwell with the controller pointer for modifiers and point costs. Search selection frames the node; Main tree and Ascendancy frame their respective areas. L2/R2 use the same internal map zoom as the minus/plus buttons, keeping the toolbar and surrounding layout at their normal size.
- **Skills** and **Items** keep the list beside the selected entry. Skill options inspect and edit that group without changing the main calculation group. Item comparisons name the replaced and proposed items and show changed statistics before Apply. Descriptions preserve upstream inline formatting through native rich text and theme colors.
- **Configuration** retains upstream section names and order in a category list. Controls scroll within each section; search covers all sections, and Show explanations reveals the upstream help. Retained settings that are currently inapplicable are identified explicitly. Changes remain previews until applied.
- **Notes** displays a continuous scrolling document and opens guide links through the host.

Node allocation and refund changes are previews. **Apply** saves the working copy; **Discard** restores the previous tree and calculations. Refund previews include dependent nodes. **Undo** restores up to twelve committed changes during the current session. Switching active sets is saved immediately. Export uses the upstream engine's XML format and standard compressed share-code encoding.

Tree preview actions stay above the scrolling comparison panel. Right from a skill or item row enters its details; Left returns to the selected row. Section switches explicitly focus the destination content and use the host's section transitions. Animation follows the user's motion preference. Startup shows a loading indicator while the calculation engine prepares the saved build.

Use **Build & import** to rename a build, open another saved build, import, export or change character details. LB/RB switches sections. Controller focus and list navigation remain native; the optional tree visualization uses a host-owned packaged web session.

Passive search results and saved builds use virtualized lists with stable row identities. Returning from a passive inspector restores the selected row. Notes, configuration sections and equipment-slot choices scroll continuously rather than using Next page actions.

## Scope and compatibility

This is a companion for inspecting and comparing builds, not a replacement for every desktop PoB workflow. It does not implement trade searching, account/character import, the desktop item-crafting editor, advanced gem/socket-group authoring, every specialized tree choice, or every desktop calculation view. Unsupported allocation choices are rejected rather than guessed. Historical builds whose gems were removed by the pinned engine are rejected rather than silently losing them. Calculations are only as applicable as the selected engine version and the build's assumptions.

The six categorical color roles follow the selected WidgetRail theme; labels and geometry also convey meaning. The tree consumes the host's packaged-page theme projection, including high contrast. Remote websites receive no theme injection.

## Engine lifecycle and storage

The Lua process starts on first calculation and does no periodic recalculation while idle. The widget unloads after 120 seconds of inactivity under the host residency policy. Calculation state and game data occupy hundreds of MiB; this is not a lightweight web page. Cancellation retires the calculation process, and the next command restores the displayed build before applying an edit.

Default working copies live under `%LOCALAPPDATA%/WidgetRail/applications/pob1` and `pob2`. Immutable engine archives are extracted to a digest-keyed cache under `%LOCALAPPDATA%/WidgetRail/pob-engines`. Extraction is bounded, cancellation-aware and staged before publication. `WIDGETRAIL_POB1_DATA`, `WIDGETRAIL_POB2_DATA` and `WIDGETRAIL_POB_CACHE` allow isolated development profiles. `WIDGETRAIL_POB_DATA` remains a common data-directory override when no per-game override is set.

## Rebuilding

`engine-lock.json` pins the PoB releases, LuaJIT and lua-utf8 source commits. `Prepare-Engines.ps1` fetches those exact sources and builds the x64 runtime using Visual Studio C++ tools. It writes `paths.json` containing the prepared source/runtime directories. `Build-CommunityPackage.ps1` accepts `-Game 1` or `-Game 2`, those directories, and a current `wrail` CLI path, producing a validated `.wrwidget` package in a fresh output directory.

Artwork preparation uses Python with the versions in `requirements-artwork.txt`; pass `-Python` when using a dedicated build environment. `Build-TreeArtwork.py` reads artwork directly from the pinned Git objects, converts upstream sprite metadata and DDS arrays into small browser atlases, and verifies the resource bounds for each tree. Only the open tree's assets enter its web session. Python and the conversion libraries are not runtime dependencies. Node artwork retains the upstream game's visual identity; surrounding controls, connectors, focus and preview markers follow the selected WidgetRail theme.

Install each resulting `.wrwidget` through **Settings → Widgets → Install local widget**. The packages use the full-access application runtime to launch their local Lua engine.

Engine upgrades are deliberate: update the lock, rebuild, run the real-engine fixture suite and inspect both widgets. The bridge adapter is the compatibility boundary; game calculations remain upstream. Do not silently replace an engine in a running build.

Each package includes the applicable upstream PoB license, LuaJIT copyright and lua-utf8 license. Fixtures used during development are not bundled as sample builds. Path of Building Community and Path of Exile are separate projects; these widgets are not affiliated with their maintainers or Grinding Gear Games.
