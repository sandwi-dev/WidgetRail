# Path of Building for WidgetRail

Plan and compare Path of Exile builds from your controller, inside the WidgetRail overlay.

Use **PoE1 Path of Building (Beta)** for Path of Exile or **PoE2 Path of Building (Beta)** for Path of Exile 2. Both use pinned Path of Building Community beta engines and their calculations.

![PoE2 calculations and loadout controls in WidgetRail](screenshots/calculations.png)

*An example planning build. [See more screenshots and their context](screenshots/README.md).*

## What you can do

- Import a build and keep separate loadouts for different stages of your character.
- Edit skills, support gems, equipment, passives, and configuration.
- Preview tree and equipment changes before applying them, and undo saved edits.
- Inspect damage, defences, and the calculation breakdowns PoB provides.
- Browse the official trade website beside item comparisons and rank possible upgrades.
- Export PoB XML or share codes; PoE2 also supports in-game build-planner files.

## Get started

1. [Install WidgetRail](../../docs/users/getting-started.md), then open **Catalogue** and install the widget for your game. Installation requests full access so the widget can run its local PoB engine.
2. Open the widget and paste a **PoB share code** or **pobb.in link**, import an exported XML file, or create a new build.
3. Use **LB/RB** to switch sections. The on-screen controller guide shows the actions available for the current control. Keyboard and mouse also work.

Edits save automatically. Tree and equipment proposals use **Apply** or **Discard**; **Undo/Redo** is available in the build controls.

For a build on **poe.ninja** or **pob.cool**, copy its PoB code and use **Paste build from clipboard**. Direct poe.ninja profile-link import is unavailable, and pob.cool requires a mouse and keyboard.

## Learn more

These widgets cover build planning and comparison, with some desktop PoB features still unavailable. Choose the correct game when importing; older engines may not understand builds exported by a newer beta.

- [User guide](USER_GUIDE.md): imports, loadouts, editing, controller shortcuts, trade, and limitations.
- [Screenshots](screenshots/README.md): skills, passives, configuration, comparisons, and ranked results.
- [Developer documentation](DEVELOPMENT.md): engine compatibility, architecture, storage, building, and testing.

## Credits

Calculations and editing tools come from **Path of Building Community**, building on **Openarl's** original project. Path of Exile, its data, and its artwork belong to **Grinding Gear Games**. The planning screenshots use **Ghost Activity's** public Martial Artist guide. See [full credits and licensing](DEVELOPMENT.md#credits).

[PoB Redux](https://github.com/pobredux/pob-redux) inspired the gem-tag search, changed-configuration filtering, inline skill editing, unified search and build-comparison workflows. WidgetRail adapts those ideas for controller use; calculations and eligibility continue to come from PoB.
