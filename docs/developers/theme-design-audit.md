# Built-in theme design audit and expansion

Date: 2026-10-15. Baseline: `main` at `235283ddee5d4a2ac73e1a0e19ac57c7d888c02e`.
The change adds five built-in themes and improves the five existing themes.
Existing identities and selections remain valid. Mono Ink is now first and the
default for new/reset settings; Default is renamed Warm Graphite without changing
its `builtin.default` identity. No skills are installed and no local appearance
settings are changed by this work.

## Design basis

The following sources were read from temporary clones, not installed as skills:

- [Frontend Design](https://github.com/anthropics/skills/blob/dbd4588f9e1033efb41dad4bef2f7947c8993d44/skills/frontend-design/SKILL.md): intentional identity, type hierarchy, restraint, and critique.
- [Theme Factory](https://github.com/anthropics/skills/blob/dbd4588f9e1033efb41dad4bef2f7947c8993d44/skills/theme-factory/SKILL.md): cohesive semantic palettes and consistent font roles. Its slide-deck selection workflow does not apply to implementing this requested collection.
- [UI/UX Pro Max](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill/blob/56ea9cf174ed86786187daa9de0e6247cb0747e4/.claude/skills/ui-ux-pro-max/SKILL.md), including its accessibility and delivery references: contrast, visible focus, meaningful states, restrained motion and readable labels.

The UI/UX database's gaming query suggested a web showcase with 3D effects and
downloaded fonts. A narrower desktop query still included marketing-page
sections. Those recommendations were rejected as unsuitable. Its specific
focus-contrast and WinUI accessibility guidance was useful. The actual
[WRSS contract](../reference/wrss.md) and production cascade take precedence:
platform baseline, selected theme defaults, explicit widget styles, then host
accessibility presentation policy. No browser-only effects or font downloads
were introduced.

The themes keep the baseline layout, spacing, 44-DIP shared control targets and
interaction behavior. Palette, corner hierarchy and heading typography carry
their identities. Windows font files for Segoe UI, Bahnschrift and Georgia were
verified on the development machine. No new assets or runtime dependencies are
required.

The original five themes retain `Segoe UI Variable Text, Segoe UI` for body
text and `Segoe UI Variable Display, Segoe UI` for headings. WinUI resolves the
comma-separated fallback lists; WidgetRail passes them through. Amber Signal
uses `Bahnschrift, Segoe UI`, and Velvet Dusk uses
`Georgia, Times New Roman, Segoe UI` for headings. The remaining new themes use
Segoe UI. The earlier claim that native font handling could not interpret these
lists was incorrect; removing the original fallbacks was not a bug fix.

## Five additions

All new identities use `widgetrail.builtin.<suffix>`, version `1.0.0`. They are
embedded in PlatformSettings, reserved by ThemeIdentity and discoverable through
the existing catalog. The registered IDs and names are unique, ignoring case.

| Name / suffix | Identity and hierarchy | Main palette | Typography |
| --- | --- | --- | --- |
| Mineral Light / `mineral-light` | Daylight surfaces; 12/6/4-DIP container/control/detail corners; opaque panels and light artwork scrims | `#f5f8f8`, `#ffffff`, `#d0e0e3`, `#075b73`, `#172e34` | Segoe UI; medium-weight headings |
| Canopy / `canopy` | Evergreen utility panels; 16/10/6-DIP corners; subtle depth | `#14261e`, `#1b3026`, `#304b3e`, `#b4deb0`, `#eef5ec` | Segoe UI; medium-weight headings |
| Amber Signal / `amber-signal` | Instrument readouts; 6/4/2-DIP corners; compact, ordered controls | `#241e13`, `#30271a`, `#493b27`, `#ffce73`, `#fff3dc` | Segoe UI body, Bahnschrift headings |
| Velvet Dusk / `velvet-dusk` | Quiet library; 18/8/4-DIP corners; expressive headings, ordinary control labels | `#252030`, `#302a3d`, `#493f59`, `#d9c0ff`, `#f6f0fa` | Segoe UI body, Georgia headings |
| Mono Ink / `mono-ink` | Achromatic structure; 4/2/2-DIP corners; no decorative shading or shadows | `#1c1c1c`, `#272727`, `#454545`, `#ededed`, `#fafafa` | Segoe UI; bold headings |

These occupy spaces not covered by the original warm-neutral, blue-grey, cyan,
fuchsia and scarlet set. Semantic status colors remain separate from decorative
color, and widgets retain their labels/icons: color alone does not communicate
status. Mono Ink is not a replacement for High Contrast.

## Findings and applied improvements

| Theme | Finding | Applied change |
| --- | --- | --- |
| All original themes | The inset primary-button focus ring used the same accent as its fill. At the unshaded point it measured 1:1. A present outline was not necessarily a visible one. | Shared primary focus now uses its contrasting foreground (`--canvas`). Theme focus width increases from 1.5 to 2 DIPs. Focus still preserves the authored fill. |
| Default | Supporting/status text lost contrast on brighter state surfaces. | Brighter supporting and status inks, slightly quieter control shading, and a denser modal surface. The warm neutral accent and original font fallback lists remain. |
| Cool Slate | The steel accent was too subdued for small text on active surfaces. | A lighter steel accent plus brighter supporting/status inks; its blue-grey character and control geometry remain. |
| Neon Circuit | Magenta eyebrows and tracked uppercase metadata competed with cyan focus and made small labels harder to scan. | Neutral sentence-case metadata and less tracking. Cyan and angular geometry remain. Supporting/status colors are more readable. |
| Arcade Rush | Pill-shaped text controls, circular icons and heavily rounded containers gave too many elements equal visual emphasis. Fuchsia focus and small text were weak against the active plum surface. | Readable pink accents; 12-DIP text-control corners and 15-DIP segmented containers; circular icon buttons remain. Labels use sentence case. |
| Redline | Repeating a scarlet underline on every container competed with selection/focus. Saturated scarlet was weak as small text, and heavily tracked uppercase labels added noise. | Keep one scarlet tray edge; use a lighter coral for interaction; remove repeated card underlines; use quieter sentence-case metadata and medium-weight headings. |

All original modal surfaces move from 80% to 96% opacity. The new themes use
opaque modal/panel fills, with separate backdrop veils and artwork scrims.
Shared layout, motion settings and host accessibility policy remain intact.
The corrected baseline primary focus rule also serves all five new themes.

## Measured results

These palette-only results did not catch missing colors on actual widget text.
The later [widget-style audit](widget-style-audit.md) supersedes this report's
coverage claims for widget compatibility and records the native theme-picker
failure, source-level corrections, and remaining validation limits.

Measurements resolve the production theme cascade, then use sRGB relative
luminance. Ordinary sampled surfaces are composited over black and white, with
both shading endpoints and the unshaded value. Modal samples include their
backdrop veil. Poster scrims are sampled over black and white artwork. These
are conservative authored-style bounds, not measurements of native pixels.

Normal, focused and selected shared surfaces are included. Disabled text is
not held to the enabled-text contrast target. The table uses minima across
the sampled combinations; not every screen contains every combination.
Text targets are 4.5:1; focus/glyph/scrollbar targets are 3:1. The 2-DIP focus
width is a project design choice, not a claim of WCAG conformance.

| Existing theme | Secondary text, before → after | Accent text, before → after | Status text, before → after | Focus edge, before → after | Filled primary focus, before → after |
| --- | --- | --- | --- | --- | --- |
| Default | 4.17 → 5.74 | 4.58 → 4.87 | 3.34 → 5.75 | 4.58 → 5.00 | 1.00 → 8.23 |
| Cool Slate | 4.20 → 6.19 | 3.83 → 5.86 | 3.24 → 5.33 | 3.83 → 5.86 | 1.00 → 10.28 |
| Neon Circuit | 4.37 → 6.71 | 6.64 → 7.12 | 3.55 → 5.81 | 6.64 → 7.12 | 1.00 → 11.62 |
| Arcade Rush | 4.05 → 5.60 | 2.59 → 5.22 | 2.60 → 5.51 | 2.59 → 5.22 | 1.00 → 9.35 |
| Redline | 4.75 → 6.35 | 2.77 → 5.72 | 3.46 → 6.87 | 2.77 → 5.72 | 1.00 → 9.61 |

| New theme | Primary text | Secondary text | Accent text | Status text | Focus edge | Filled primary focus |
| --- | --- | --- | --- | --- | --- | --- |
| Mineral Light | 10.46 | 5.25 | 5.61 | 5.24 | 5.61 | 6.33 |
| Canopy | 7.94 | 5.38 | 5.89 | 5.30 | 5.89 | 11.56 |
| Amber Signal | 9.02 | 5.86 | 6.77 | 5.90 | 6.77 | 11.75 |
| Velvet Dusk | 8.13 | 5.38 | 5.60 | 5.67 | 5.60 | 10.41 |
| Mono Ink | 9.19 | 6.28 | 8.19 | 6.16 | 8.19 | 16.25 |

All ten themes also pass the sampled primary-button foreground, poster-copy,
controller-glyph and scrollbar targets. The executable regression gate enforces
these targets for the whole collection, not only the additions.

## Verification and reproduction

The PlatformSettings and WidgetStyling projects use executable test harnesses;
run their DLLs after building them. Playnite uses MSTest with native MTP mode.
From the repository root:

```powershell
dotnet build tests/PlatformSettings.Tests/PlatformSettings.Tests.csproj -c Release '-bl:artifacts/theme-design/settings-{}.binlog'
dotnet tests/PlatformSettings.Tests/bin/Release/net8.0/PlatformSettings.Tests.dll --theme-audit-json artifacts/theme-design/audit.json
dotnet build tests/WidgetStyling.Tests/WidgetStyling.Tests.csproj -c Release '-bl:artifacts/theme-design/styling-{}.binlog'
dotnet tests/WidgetStyling.Tests/bin/Release/net8.0/WidgetStyling.Tests.dll
dotnet test --project tests/PlayniteLibraryWidget.Tests/PlayniteLibraryWidget.Tests.csproj -c Release --filter 'FullyQualifiedName~PlayniteLibraryLayoutTests' '-bl:artifacts/theme-design/playnite-{}.binlog'
./scripts/Export-ThemeDesignPreview.ps1 -AuditJson artifacts/theme-design/audit.json -OutputPath artifacts/theme-design/preview.html
```

The preview optionally accepts `-BaselineAuditJson` pointing to an earlier
audit export to expose a before/after collection selector. It is self-contained
HTML with no network resources. Open the resulting file in a browser.
It illustrates resolved colors, type and corners; it does not reproduce native
shading, font fallback, clipping, accessibility overrides or motion.

Results: baseline PlatformSettings 27/27; final PlatformSettings 28/28;
WidgetStyling 37/37; affected Playnite layout class 48 passed, 0 failed, 0 skipped.
The checks cover catalog identity and version protection, selection persistence,
switch-back isolation, explicit widget precedence, controller glyph geometry,
focus/selection/disabled composition, shipping layout diagnostics and palette
contrast. Preview pages were visually inspected for the new and revised sets.

Native acceptance remains separate: inspect representative Settings, Audio
Mixer, Playnite and media widgets at multiple scales, over bright/dark content,
with High Contrast, reduced transparency and reduced motion. Explicit widget
colors, brand fills, compound child text and artwork can still depart from the
global palette. In particular, Mineral Light needs native review of widgets
that assumed a dark theme. This work does not install or launch WidgetRail.
