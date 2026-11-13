# Playnite Home poster loading geometry

Implementation scope: 2026-11-12. Native acceptance is recorded separately below.

## Cause and ownership

The user recording shows a horizontal Home rail with headers but no poster rows,
followed by the populated rail expanding upward in its bottom-aligned Home layout.
`WidgetIndexedCollectionView.ConfigureContainerLayout` deliberately reserves only
the scrolling-axis estimate for unloaded items. Its horizontal `MinHeight = 0`
avoids stretching the eventual native item/focus box. Changing that minimum
globally would regress authored row geometry and would not know Playnite's
responsive poster proportions.

The widget already owns the poster sizing policy through WRSS: the tile, fixed
tile and poster classes specify width, limits, aspect ratio, margins and padding.
The existing Grid/Spacer SDK declarations can reserve this designed space while
the collection remains lazy. The missing host behavior was support for an
authored `aspect-ratio` on a noninteractive Spacer. The host now measures such a
spacer using the same native poster panel as real poster content. An ordinary
spacer without an aspect ratio keeps its prior measurement.

Playnite overlays its existing indexed rail with an invisible sizing element
using the same poster classes and rail padding. No duplicated numeric height
formula, loading flag, extra range request, hidden game item, image decode or
size-history cache is introduced. It remains a constant-size presentation tree.
The collection's width estimate is corrected to 162 DIPs (150-DIP maximum poster
plus its 12-DIP horizontal margin); it remains an estimate, not a fixed row width.

The initial proposal for a new collection cross-axis estimate was rejected: a
numeric estimate would duplicate styling and still drift at compact viewport
sizes. No SDK/protocol API addition is necessary. Native list animations are not
disabled; source inspection alone did not establish that they independently
cause the recorded movement.

## Consumer matrix

| Consumer | Disposition |
| --- | --- |
| Playnite Home poster rail | Uses shared-class responsive reserved space before lazy items arrive. |
| Playnite favorites | Already has an authored 94-DIP row height; unchanged. |
| Playnite Browse and other adaptive grids | Keep native grid measurement/retention and realized focus geometry. |
| Other horizontal collections | No implicit height assumption added; their current sizing is unchanged. |
| Existing Spacer consumers, templates and SDK examples | No authored aspect-ratio found in the pre-change source scan; ordinary spacer behavior remains unchanged. |
| Future artwork skeletons | Can use the existing Spacer plus shared artwork sizing classes; no new author-managed loading lifecycle. |

## Validation

The managed Playnite test checks that loading geometry is nonfocusable, that the
collection still contains no eager row trees, and that every relevant resolved
size property matches the actual poster's theme/package style. It exports
`Native-Home-Rail-Loading.indexed.json` when `WRAIL_PLAYNITE_LAYOUT_OUTPUT` is set.

The existing native Playnite presentation fixture now checks the exported rail
at 360, 470 and 680-DIP widget heights. It measures cold, populated and cold-again
states using the exact exported reservation and acquired poster, asserting stable
height and bottom-aligned top position. This simulates item availability and does
not substitute for a live Bridge range-lifecycle test. Ordinary native poster
checks continue afterward, including the real acquired row's focus-box dimensions.

Native run entry point: `scripts/Test-WinUiPlaynitePresentation.ps1`, with the new
exported indexed fixture and a fresh evidence directory after the matching WinUI
validation build. The integrated Debug build passed with zero warnings/errors.
The native fixture passed 23 checks at 125% display scale. Cold/populated rail
height pairs were 199.2/199.2, 228/228 and 256/256 DIPs for the 360, 470 and 680-DIP
widget viewports. Evidence: `artifacts/input-discord/playnite-native-container-3`
and `playnite-layout/Native-Home-Rail-Loading.measurements.json` under that root.

An earlier fixture failed at the fractional-width middle case because it rendered
the acquired poster as an ordinary fragment, applying the border directly to the
aspect-ratio panel. Real indexed rows put the border on their native item
container. The fixture now uses that actual container/style path; its unchanged
height/position expectations pass. This correction does not alter shipping code
or loosen tolerances. Parent integration owns the actual Playnite smoke check.
