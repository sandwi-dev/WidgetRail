# PoE widgets: user guide

[Overview and installation](README.md) | [Screenshots](screenshots/README.md) | [Developer documentation](DEVELOPMENT.md)

Use the widget matching your build: **PoE1 Path of Building (Beta)** or **PoE2 Path of Building (Beta)**. This guide describes the current source version; installed releases may differ.

[Builds and imports](#builds-and-imports) | [Saving and previews](#saving-undo-and-previews) | [Loadouts and skills](#loadouts-and-skills) | [Passive tree](#passive-tree) | [Calculations and configuration](#calculations-and-configuration) | [Items and trade](#items-and-trade) | [Controller navigation](#controller-navigation) | [Limits and compatibility](#limits-and-compatibility)

## Builds and imports

Paste a PoB share code or supported public build link, or enter the path to an exported XML file. The widget keeps the original import and a separate working copy. It reopens the selected working copy after restart.

Standard PoB codes copied from poe.ninja or pob.cool use the same import path and require no site-specific sign-in or integration. Direct downloads support pobb.in share links. For poe.ninja, open the website, copy the build's PoB code, and choose **Paste build from clipboard**. Direct poe.ninja profile links cannot be imported; copy the build's PoB code instead. Choose the widget for the build's game. Older codes can be migrated by newer engines, but reverse compatibility is not guaranteed. A beta export may contain fields an older engine does not understand, and different engine versions can calculate different totals.

Imports default to **New build**. In Build & import, choose **Replace current build** to refresh the open build from a file, link or share code. The replacement is validated before confirmation, retains the build's name and identity, and participates in Undo/Redo across restarts. Finish any pasted item draft first. Direct account import is unavailable; use a share code or an XML export instead.

Build & import uses a compact form with labelled fields and separate build, import and export groups. Saved builds supports search, Recent/Name sorting, and adjacent Open, Rename, Duplicate and Delete actions. Duplicate keeps committed and pending work in an independent copy with fresh history. Delete requires confirmation and removes only that build's managed files; exported XML and previously preserved recovery copies remain on disk.

### Websites and sign-in

The welcome screen and Build & import have a separate row for **Open pob.cool** and **Open poe.ninja**. Both follow the shared website-opening preference, which defaults to Windows' browser. Import your character on pob.cool or find a build on poe.ninja, copy its PoB export code, then return to WidgetRail and choose **Paste build from clipboard**. WidgetRail does not automate either site, inspect its login, or monitor the clipboard; clipboard text is read only when you select Paste.

General website links use a preference shared by both widgets: Windows' default browser, or a web session in a widget modal. Trade always uses the overlay so listings and comparisons stay together. The modal fills most of the widget without exceeding its bounds. Closing it returns to the build; **Return to website** reopens the retained trade session. Each widget has its own persistent website profile, so logins survive session disposal and overlay restart without sharing the Browser widget's cookies. Existing widget idle unloading also releases its web sessions. Account import through GGG OAuth remains unavailable without an approved application identity.

Choose **Open websites in** beside the poe.ninja and pob.cool shortcuts in Build & import. pob.cool requires a mouse and keyboard. After copying a build code, return to **Paste build from clipboard**. The shared on-screen keyboard also has a Paste key, including for password entry; password text is masked by default.

### Exporting

**Export to PoE2** writes a `.build` file for the selected tree, skills and equipment, or exports each linked loadout to its own file. The default destination is Windows' Documents folder under `My Games/Path of Exile 2/BuildPlanner`; the destination is editable. Existing files are preserved. These files provide in-game recommendations rather than equipping items or allocating passives. PoE1 retains the ordinary XML and share-code exports.

## Saving, Undo, and previews

Configuration, main-skill selection, skill toggles/options, gem edits and character level recalculate and save directly, without confirmation. Undo and Redo are available in the header and preserve the current section. Switching active sets also saves directly and participates in the same history. A new edit clears Redo. Each saved build retains its own history of up to twelve changes across unloading and restart; a new or imported build starts with an empty history.

Node allocation/refunds, equipment replacements and structural class/ascendancy changes retain a comparison preview. Previews are saved automatically as pending work, without applying them. **Apply** commits the proposal; **Discard** restores the previous calculations without consuming Undo or Redo history. Both decisions persist. Refund previews include dependent nodes. Pasted items awaiting a slot also survive unloading; **Discard item draft** explicitly removes them. Export uses the upstream engine's XML format and standard compressed share-code encoding. Unlike desktop PoB's separate Save action, the companion continues to save its working copy automatically.

Comparison previews use upstream PoB's displayed-stat comparison rules, including relevant stats, percentage changes, lower-is-better values and separate minion/player groups. They compare the actual committed and proposed calculations, including any automatic character-level change. Tree previews start with this overall impact and point counts; **Show changed nodes** expands all node names and modifiers, using pre-refund descriptions for removed nodes. Expanding details does not apply or discard the change. Restored previews regenerate their summaries from both saved XML snapshots.

Pending work and saved history survive widget unloading and restart. Recovery can reopen a committed build if newer work cannot be restored. If recovery cannot safely repair storage, the build remains viewable and Duplicate can create an editable copy. For file locations and recovery details, see [storage and recovery](DEVELOPMENT.md#engine-lifecycle-and-storage).

## Loadouts and skills

Gem, inventory and unique-library candidates offer **X → Inspect** (or the pointer
context menu). A keeps its normal selection action. Gem and unique-library focus
previews show already-loaded information and fit their content within the available
space. Unique previews include up to three upstream modifier lines, labelled as library ranges; Inspect shows the full item. Inventory uses its existing details pane instead of covering it with a
duplicate floating preview. Inspection keeps the list and search in place. Use the explicit
Add, Replace, Equip or Open item draft action when ready. Inventory inspection lets
you choose an eligible equipment slot. Inspection opens at the first detail and
keeps its actions visible while you scroll. Back/Close returns to the same candidate.
Gem inspection names the active loadout and calculation weapon set when supplied,
and separately shows which weapon sets have the selected group enabled. Missing context
is omitted rather than guessed.

For engines with linked loadouts, **Build & import → Save current setup as a new loadout** creates independent tree, item, skill and configuration sets. Existing loadouts remain accessible even when they previously shared a single set. Switching sets retains the main skill by its identity when there is one unambiguous match; otherwise it selects the first group instead of retaining an unrelated row number.

**Skills** and **Items** keep the list beside the selected entry. Skills keeps gem levels, quality, counts, enable, replace and remove controls beside the group list. Applicable skill-part and stat-set choices appear with the group; **More skill options** opens less common controls. **Group settings** contains naming, assignment, duplication, ordering and deletion. The group list stays available while adding or replacing gems. Gem creation uses upstream editor defaults. Inspecting another group does not change the main calculation group. If its options cannot be read, **Retry skill options** requests fresh data without changing the main skill or saved build. Item comparisons name the replaced and proposed items and show changed statistics before Apply. Descriptions preserve upstream inline formatting through native rich text and theme colors.

Group and gem management also saves immediately and participates in the same Undo/Redo history. Removing a group or gem asks for confirmation. Reordering preserves the actual main calculation group. Automatically granted skills identify their source and equipment slot; their level is read-only because PoB regenerates it from that source. Manage group exposes their upstream Group count separately from per-gem counts. Such groups cannot be duplicated, deleted, reassigned to another item, or have gems added/replaced/removed. Remove the granting item or passive instead. Existing ordinary gems remain editable; unsupported levels are reported instead of silently clamped.

Copy group and Paste skill group use desktop PoB's clipboard format and parser. If that parser cannot recognize every pasted gem, the paste is rejected without saving a partial group. Delete all editable groups affects the current skill set and keeps automatically granted groups. These operations also participate in Undo/Redo.

The core group workflows above follow desktop PoB. Independent skill sets can be created, copied, renamed and deleted. Applicable gem options expose upstream stages, active mines, corruption and global-effect controls; availability depends on the game and selected gem. This is not a claim of complete Skills-tab parity.

### Search and compare builds

Choose **Search** in the build controls to find passives, skill groups, gems, equipment, inventory items, configuration settings, calculation rows, saved builds and useful pages. Type a name or modifier, or choose a category. Results open the corresponding controls; they do not allocate, equip or load a saved build automatically. Saved-build results select the library entry for review. Known item warnings link to their item; a disabled-main-skill warning links to its supplied group. Warnings without an upstream target remain informational. If inventory or detailed-calculation search is unavailable, Search identifies that category; close and reopen Search to retry.

Reopening Search in the same build retains the query, category and selected result. A surviving result is brought back into view; changed builds refresh the matches, and switching to another build starts a separate search context. Destination LS/RS shortcuts remain available after search navigation.

**Build & import → Compare builds & loadouts** names the current build/loadout and the selected target. Results use aligned source, target and change columns; **Show build context** expands the sets and secondary calculation context. Numeric changes are signed differences, not an automatic judgement that an increase is better; a zero baseline has no percentage change. Browse PoB's calculated impact or differences in statistics, equipment, skills, passives and configuration. Select a difference for its full details; Back returns to that row. This comparison is read-only and has no Apply or transfer action. Each side uses its own configuration, so the difference is not necessarily an equipment gain.

## Passive tree

The map uses PoB's class and ascendancy backgrounds, node frames and socket artwork.
Jewel inspection uses upstream radius and centre data. Small background textures
appear as you zoom in; high-contrast mode keeps clear outlines and hides decorative
backgrounds. Jewel effects are static so the tree does not redraw continuously.

**Tree** shows upstream passive-tree positions, rendered connections and node artwork, with an adjacent node inspector. Hover or dwell with the controller pointer for modifiers and point costs. At overview zoom, small passives use compact markers and subdued connections; artwork and node outlines become visible as you zoom in. Controller targets stay 40 pixels wide independently of the drawing. Search selection frames the node; Main tree and Ascendancy frame their respective areas. L2/R2 use the same internal map zoom as the minus/plus buttons, keeping the toolbar and surrounding layout at their normal size.

An empty passive search lists **allocated and granted passives**. Entering text searches all interactive nodes in the current tree by name or modifier, including unallocated nodes. The result heading identifies which list is shown.

PoE2 attributes use a dropdown beside the selected node. PoE1 mastery effects open a compact dialog over the tree, showing complete descriptions and the current effect; choosing an effect opens the existing comparison preview. Short item settings use dropdowns in both equipped-item and draft editors. Large catalogs and options with substantial descriptions keep the searchable picker.

PoE2's tree offers **Shared tree**, **Weapon set 1** and **Weapon set 2** allocation targets, with budgets projected from PoB's desktop display. Map badges and node details identify set membership. Eligible PoE1 nodes offer upstream tattoo choices and removal through the normal comparison preview.

PoE1's **Find a Timeless Jewel** uses upstream seed data, modifier weights and socket scoring. Desired and fallback modifiers have searchable pickers; seed results are virtualized. Inspect a result for PoB's modifier/source breakdown before adding its jewel to inventory. Adding does not equip or purchase it. Desktop conveniences such as protected-notable selection, socket-distance filtering and direct seed trade URLs are not yet exposed.

Tree previews list every changed passive with its numeric modifier descriptions. Refunded nodes use their pre-refund names and modifiers, including chosen attributes; allocation previews use the proposed state. An automatic character-level change is shown alongside point counts so its contribution to changed totals is visible.

## Calculations and configuration

The calculation inspector shows skill context for main-skill damage, group context for Full DPS, and the selected statistic's breakdown for other values.

Use **Hide breakdown** to keep the selected total and calculation context visible while collapsing its supporting detail. **Show breakdown** restores the explanation and source rows. The choice is remembered for recently inspected calculations; it never changes the build. Expanding or collapsing returns focus to the same toggle.

**Calcs** groups statistics beside their calculation setup. Detailed calculations follow the main skill by default; the inspection selectors can choose another group, an active skill within that group, buff mode, and player/minion where supported. These inspection choices do not change the main skill or rewrite the build’s saved desktop Calcs settings. Formula text is directly scrollable; source-table rows remain expandable when their summary is condensed. Breakdowns and source attribution come from upstream; the widget does not maintain passive-effect rules or infer causes that upstream does not attach to a calculation. Main-skill DPS identifies the skill; disabled skills show an unavailable value and PoB's reason. Full DPS identifies its included groups and contributors. Breakdowns identify their skill and calculation mode where relevant, and explain when their total differs from the overview. Matching defensive totals omit unrelated skill context. Exact repeated resource-calculation sequences from upstream are collapsed without changing their values or source rows.

**Configuration** retains upstream section names and order in a category list. Controls scroll within each section; search covers all sections, and Show explanations reveals the upstream help. Retained settings that are currently inapplicable are identified explicitly. Changes recalculate and save immediately; Undo reverses them.

Saved inactive settings initially show their current values and changed-setting count. Expand that group to edit retained values. Searching for one reveals its control directly. Collapsing returns focus to the group toggle without changing any saved value.

Damage labels follow PoB's display mode: skills that report average damage are labelled accordingly, with hit damage per second shown separately in Calcs. Item replacement offers slots accepted by the engine for the pasted item. Configuration omits inapplicable default controls while retaining authored non-default settings.

Native passive descriptions and tree hover cards share theme-aware rich text, preserving upstream wording and colors and emphasizing values and recognizable stat terms in otherwise plain modifiers. The selected tree node has a screen-space frame; Locate selected centers it at a readable minimum zoom. Buff-only main skills show **No direct damage** instead of a misleading zero DPS, while damaging skills retain PoB's calculated values and disabled-skill reasons.

## Items and trade

### Item editing and upgrade search

Search supports custom upstream statistic weights, applicable advanced modifier/sockets/level options, and empty equipment slots. Imported/existing items expose PoB's predefined modifier sources and searchable choices through **Add modifier**, with upstream roll controls and Undo; custom text remains separate.

### Comparing a listing

**Search trade** calculates the query using upstream PoB and opens the official trade website in the overlay in one action. Generated searches use descending Weighted Sum; PoE2 applies this through the website's visible sorting control and waits for the replacement results before enabling comparisons. Ordinary browsing and subsequent manual ordering remain user-controlled. **Browse trade website** opens it without calculating upgrade filters. Progress is cancellable; a calculation failure opens nothing. **Compare in PoB** appears beside individual listings whose item response has loaded. Choose a compatible equipment slot to see PoB's calculated differences beside the retained website. **Equip in build** applies the item to the build; **Finish comparing** ends the comparison and returns to Items without equipping it. The widget preserves the listed price, seller, and return link; purchases use the official site's controls. Incomplete conversions and unrevealed modifiers are rejected. Like desktop PoB, recognized display-only modifiers are accepted, and items containing unmodeled effects are evaluated using their recognized modifiers. Ranked rows flag unmodeled effects; comparisons list the exact affected text. Warnings survive saved-result restoration. Owned items can still be copied in game with Ctrl+C and pasted for comparison.

### Evaluating and ranking results

**Load and evaluate results** loads up to the selected limit through the website's normal batched loading, then evaluates the received items. The default is 20, configurable to 50 or 100. Loading and evaluation show progress and support Cancel; the page returns to its previous scroll position afterward. If the website supplies fewer items, the summary reports the actual count. A virtualized shortlist remains beside the website; its controls show the exact equipment target, priority and limit. Select a result to inspect the complete comparison, then return to the shortlist. Changing the build, target or weights invalidates the displayed scores. Evaluated item copies and listing metadata survive unloading; website price and availability must still be checked before buying. **Order by** can retain strongest improvement, use PoB's price-adjusted score, or show lowest price. Price orders reuse already calculated scores and convert prices with PoB's own logic using GGG's public hourly Currency Exchange history. The selected league and market hour are shown; rates are cached for 61 minutes. This is a historical estimate, not a live exchange quote or a simple improvement-per-price division. Missing numeric prices or conversion rates are reported rather than guessed.

### Returning to a listing

**Return to listings** reuses the retained page or restores the saved public search URL after unloading. **Find seller's item** instead opens a seller-and-base search and attempts to locate the saved listing. Exact page scroll position is not retained. Check the official listing for its current price and availability before buying.

## Controller navigation

Use **Build & import** to rename a build, open another saved build, import, export or change character details. LB/RB switches sections, with native controller symbols beside the tabs instead of a separate footer. Controller focus and list navigation remain native; the optional tree visualization uses a host-owned packaged web session.

Tree preview actions stay above the scrolling comparison panel. Right from a skill or item row enters its details; Left returns to the selected row. Section switches explicitly focus the destination content and use the host's section transitions. Animation follows the user's motion preference. Startup shows a loading indicator while the calculation engine prepares the saved build.

**R3** jumps to the build controls (Loadout, Build & import, Undo/Redo); from those controls it returns to the remembered content control without moving the list viewport. It is unavailable during operations, pending comparisons and modal choices. Keyboards and browser dialogs retain exclusive input. Every section, including Calcs, uses the compact statistics summary above navigation.

Long lists offer contextual **L3** jumps to nearby search fields, filters, or actions, and back to the remembered row. These jumps preserve the list viewport. The tree map has its own L3 toolbar action. Follow the on-screen guide for the current context.

### Searching

Search fields offer whole-entry suggestions for gems, passives, inventory items,
saved builds, configuration, calculation sections, and item/modifier pickers.
Suggestions use the currently eligible choices and loaded PoB catalogs; typing
does not run calculations or contact a website. Passive names can also match
their modifier text. **LS/L3** fills the first suggestion; mouse or **A** selects
any suggestion. **RT/Done** confirms the search, and Cancel leaves it unchanged.

### Notes

**Notes** displays a continuous scrolling document. Link buttons use authored Markdown titles when available, otherwise the destination. Recognized YouTube videos open through the video widget intent with their timestamp; unavailable or rejected video requests fall back to the original web link. Other links use the web intent. Press **L3** from the document to focus the **Notes navigation tab** without moving the document's scroll position.

### Progress and cancellation

Subsequent work uses a floating progress notification with Cancel (or B), without inserting rows into the workspace. Progress remains until the operation completes. Informational feedback lasts three seconds; warnings and errors last eight seconds. A new operation replaces prior feedback.

## Limits and compatibility

This is a companion for planning and comparing builds, not a replacement for every desktop PoB workflow. It supports independent tree, equipment, skill and configuration sets; normal, magic and rare item drafting with upstream affixes and custom modifiers; a searchable unique library; inventory management; applicable gem controls; PoE1 masteries and PoE2 attribute choices; and detailed calculation categories with complete source lists. Item drafts expose applicable upstream quality, variant, influence, catalyst and rune controls. The complete desktop crafting editor, stash integration and all specialized tree mechanics are not fully reproduced. Unsupported choices are rejected rather than guessed. Historical builds whose gems were removed by the pinned engine are rejected rather than silently losing them. Calculations are only as applicable as the selected engine version and the build's assumptions.

The local calculation engine can use hundreds of MiB and starts when calculations are needed. It does not recalculate periodically while idle; normal widget unloading releases it. A cancelled or crashed engine is restored from the working build on a later command. See the [engine and compatibility notes](DEVELOPMENT.md) for implementation details.

This is an independent WidgetRail companion. See [credits](README.md#credits) for Path of Building Community, Grinding Gear Games, and the other projects used here.
