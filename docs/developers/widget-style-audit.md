# Widget styling audit

## Acceptance — 2026-10-15

The user accepted the theme and widget WRSS changes and authorized merging them
into main after testing the candidate from `f774122b`. The verified candidate is
`artifacts/theme-main-candidate/candidate02`; Spotify and YouTube Video received
style-only local package updates. Earlier validation and pending-acceptance
statements below describe the audit's history. The newly reported YouTube Music
settings/library focus jump is tracked separately in the
[next widget and notification plan](../widget-notification-adoption-plan.md);
its cause is not yet established and it is not claimed fixed by this theme work.


Date: 2026-10-15. Started from `969f8efd` on `codex/theme-design-audit` after the
theme-design branch incorporated `main`. This extends the earlier
[palette audit](theme-design-audit.md) to the styles shipped by widgets.

## Why the original audit missed the failure

The supplied native screenshot showed light theme-name text disappearing on
Mineral Light's white theme-picker rows while version numbers and page copy
remained visible. `SettingsPresentation.RenderThemes` creates real `UI.Text`
children with class `theme-name`. That class did not declare `color`. The bridge
resolves those nodes as `text`; the earlier palette test sampled `body` instead.
The native adapter leaves the native foreground unchanged when no computed
color exists. The browser study had supplied text colors itself and therefore
could not expose this failure.

The design skills called for visual/accessibility review. They did not provide
a WidgetRail-specific inheritance model or validate the implementation. The
mistake was treating synthetic palette checks and an illustrative browser study
as sufficient compatibility evidence. A passing palette is not proof that real
widgets apply it. The native screenshot is the failure evidence; the regression
now resolves the actual picker snapshot labels under every theme.

## Scope and method

The repository inventory contains 30 widget WRSS files: 11 first-party widgets,
13 samples, four template styles and two historical fixtures. All were parsed
and compiled against all ten built-in themes, using the production cascade and
WinUI layout diagnostics. The final sources contain 1,257 selectors.

The reproducible [audit tool](../../tools/WidgetStyleAudit/README.md) uses Roslyn
to inspect literal UI factory calls and their style classes. It evaluates seven
state combinations and records explicit color pairs, missing text ink, private
palette tokens, literal colors and compositing declarations. Every selector is
resolved even when a literal call site cannot be mapped. Source review then
distinguishes theme-dependent UI from intentional media/brand treatment.

The final `--verify` run has zero shipping/template missing-ink or calculated
text/focus-pair findings **within that model**. This does not mean every native
screen is verified: 84 dynamic-class call sites and 429 selector-only entries
remain explicit runtime-composition coverage gaps. They include helper-generated
components and selectors for runtime modifier combinations. Full details and
source locations are in the generated JSON, not discarded from the result.

## Corrections

1. **Plain text and the theme picker.** The shared baseline now supplies
   `color: var(--text)` to the actual `text` role. Class-specific and widget-owned
   colors retain precedence. The picker also explicitly colors `theme-name`.
   This fixes the demonstrated missing declaration and prevents ordinary text
   in other widgets from depending on the native host's default light/dark ink.
   It does not force all text to one color or replace semantic badge colors.
2. **Recent Apps.** Its private dark surfaces were combined with global text
   colors, producing dark-on-dark combinations under Mineral Light. Private
   palette names now alias global semantic tokens; retry surfaces also follow
   the theme. Layout and history behavior are untouched.
3. **Network Controls.** Focused secondary actions switched to a white
   `accent-contrast` fallback while retaining a neutral surface. Focus now
   preserves ordinary text ink. The existing focus edge remains independent.
4. **SDK Gallery.** The root and poster scrim used fixed dark fills with
   theme-dependent text. They now use semantic surfaces/scrims. Generic focused
   icon-button styling no longer replaces variant ink; primary focus uses its
   contrasting foreground. Generic badge overrides no longer erase the shared
   info/warning/danger colors. Neutral badge icons now have explicit baseline ink.
5. **Spotify.** Custom primary actions had an accent-colored inset edge on an
   accent fill. Their focus edge now uses the contrasting foreground. The
   separately authored play button already had this treatment and is retained.
6. **Embedded Media and its starter template.** The accent-filled play button
   now uses its contrasting foreground for focus. Transport behavior, sizes and
   the embedded media adapter remain unchanged.
7. **YouTube.** Small hard-coded red section/eyebrow text failed against light
   surfaces; it now uses the theme accent. Disabled primary controls regain the
   normal focus color because their fill is neutral, not accent. The existing
   enabled-primary focus treatment is retained. The black video viewport stays
   black intentionally.
8. **Basic/data/multipage and media templates.** Removed private `--surface`
   and `--text-muted` definitions that overrode user-selected themes. Title and
   status styles also match the IDs actually emitted by the starter profiles,
   in addition to their supported class selectors.
9. **Obsolete role selectors.** Clock's `clock-widget` selector did not match
   its actual Stack node; it now targets the stable `clock-root` ID. Browser's
   unused `browser-widget` selector was removed. Neither name exists in the
   bridge's closed widget-role mapping.

## File-by-file disposition

Each row below was included in the ten-theme compilation matrix. “Reviewed”
means source/resolved-style review, not native visual acceptance.

| Widget/source | Disposition |
| --- | --- |
| Audio Mixer | Reviewed semantic surfaces, slider/focus colors and transparent containers; retain widget layout and state recipes. |
| Capture Library | Shared plain-text correction covers headings; thumbnails and capture previews keep their own pixels. |
| Games and Apps | Reviewed semantic surfaces and poster classes; retain shared artwork/focus presentation. |
| Marketplace | Shared text correction covers unclassified labels; status/metadata already use semantic colors. |
| Media Sessions | Reviewed custom player recipes; existing contrasting play-button focus and semantic text are retained. |
| Network Controls | Corrected focused secondary text; transparent empty signal text is intentional, not a contrast defect. |
| Performance Monitor | Shared text correction covers titles/values; metric surfaces and hints already use theme tokens. |
| Power | Reviewed semantic action/status colors and state styling; no package correction needed. |
| Recent Apps | Replaced private dark palette and retry fill with semantic theme values. |
| Settings | Corrected theme-name ink and added actual-picker snapshot regression coverage. Shared text fallback covers other unclassified labels. |
| Task Switcher | Shared text correction covers title/app labels; window-preview imagery remains independent. |
| Browser | Removed the unused custom-role rule; native browser chrome follows the host and remote page CSS is not WRSS-owned. |
| Clock | Corrected root selector; shared text correction covers the large unclassified clock label, and date already uses muted ink. |
| Discord | No widget WRSS rules; browser content has its own appearance. |
| Display Profiles | Shared text correction covers profile/title/dialog labels; explicit semantic status colors are retained. |
| Embedded Media | Corrected filled-play focus; retain black media pixels and semantic host controls. |
| Full Application reference | Shared text correction covers plain labels; minimal layout-only package styles are retained. |
| Game Help | Shared text correction covers headings/plain content; dynamic citation styles require runtime composition checks. |
| Playnite Library | Reviewed semantic controls, transparent stage layers and alpha-based panel fills. Preserve explicit widget geometry, artwork and selection states. |
| SDK Gallery | Corrected dark surface assumptions, primary focus and semantic badge override loss. Demonstration-only artwork tints remain. |
| Spotify | Corrected custom primary focus. Preserve the already-correct play button, branding and shared navigation semantics. |
| Twitch | No widget WRSS rules; browser content has its own appearance. |
| YouTube | Corrected small label ink and disabled-primary focus; preserve black video viewport and original media. |
| YouTube Music | Fixed dark artwork scrim is paired with explicitly light title/subtitle ink, so it remains intentional. Ordinary controls use theme tokens. |
| Default starter WRSS | Removed palette overrides; aligned title/status ID selectors with basic/data/multipage source. |
| Media starter WRSS | Removed palette overrides; aligned title/status IDs; pinned layouts retain their geometry. |
| Embedded-media starter WRSS | Corrected filled-play focus; adapter content remains isolated. |
| Webapp starter WRSS | No widget style rules; external page content retains ownership. |
| FullApplication test fixture | Historical fixture retains explicit light text. It is not a shipping theme-compatibility example. |
| YT Music companion test fixture | Historical, non-distributed companion retains its legacy dark palette. Reported separately with 47 grouped low-text pairs, three low-focus pairs and two WinUI layout warnings. Do not use it as proof of shipping theme compatibility. |

The two fixture sources are under `tests/`; the companion README explicitly
identifies it as a preserved regression fixture, not the distributed YouTube
Music application. Its known styling findings were not hidden by rewriting
historical test input.

Four embedded HTML documents were also inventoried: the Embedded Media and
YouTube adapters, YouTube Music playback host and embedded-media starter adapter.
The three video adapters use intentionally dark media canvases; the audio-only
YouTube Music playback host has no styled visible UI. They are not recolored through WRSS.
Original-color logos/artwork are likewise separate from semantic UI ink.

## Catalog changes requested alongside this audit

The order is Mono Ink, Velvet Dusk, Canopy, Amber Signal, Mineral Light, Warm
Graphite, Cool Slate, Neon Circuit, Arcade Rush, Redline. Mono Ink is the default
for missing/reset settings. Existing selections persist. Warm Graphite retains
the old `builtin.default` ID, so renaming the display label does not invalidate
saved settings or change the shared baseline's identity.

## Verification and remaining acceptance

The source audit is reproducible with the commands in the tool README. Its JSON
includes source locations and explicit coverage gaps. Existing regression
harnesses also now exercise real theme-picker labels, neutral focused actions,
primary foreground/focus pairs and semantic badge colors across all themes.

Final results: PlatformSettings 30/30, WidgetStyling 37/37, SettingsWidget 91/91, RecentApps 7/7,
NetworkControls 31/31, SDK Gallery 13/13, Spotify 80/80, and the affected YouTube
snapshot/style test 1/1. The first Settings run had one catalog-operation lock
failure outside the style path; the single retry passed all 91. No catalog
locking implementation was changed.

Native acceptance is still required for final rendered text, helper-generated
content, opacity/shading, image backgrounds, scaling and accessibility settings.
The native failure screenshot was supplied by the user; this change has not
been installed into or visually verified in the running WidgetRail instance.
The browser study remains illustrative and is not evidence that this fix is
rendered correctly. The dark/transparent shell tray visible in the screenshot
also needs a separate native shell contrast check; it is not a widget WRSS file.
