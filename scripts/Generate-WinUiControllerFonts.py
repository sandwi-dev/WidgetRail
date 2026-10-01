"""Build ink-normalized controller subsets for native WinUI FontIcon.

Requires fonttools 4.66.0 (development tooling only). The checked-in fonts need
no generator or Python at build/runtime. Source glyphs and their licenses remain
under assets/fonts/kenney and assets/fonts/promptfont.
"""
from pathlib import Path
import hashlib
import json
import re

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.recordingPen import DecomposingRecordingPen
from fontTools.pens.transformPen import TransformPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parent.parent
FONT_ROOT = ROOT / "assets/fonts"
OUTPUT = FONT_ROOT / "winui-controller"
WIDE = {"LeftBumper", "RightBumper", "LeftTrigger", "RightTrigger"}


def build(source, family, filename, prompts):
    font = TTFont(source)
    glyph_set = font.getGlyphSet()
    source_cmap = font.getBestCmap()
    glyphs = {".notdef": TTGlyphPen(None).glyph()}
    metrics = {".notdef": (1000, 0)}
    cmap, records = {}, []
    for prompt, code in sorted(prompts, key=lambda pair: pair[1]):
        original = glyph_set[source_cmap[code]]
        bounds = BoundsPen(glyph_set)
        original.draw(bounds)
        x_min, y_min, x_max, y_max = bounds.bounds
        width, height = x_max - x_min, y_max - y_min
        if width <= 0 or height <= 0:
            raise ValueError(f"Empty controller glyph: {prompt}")
        advance = 1350 if prompt in WIDE else 1000
        # Match ControllerPromptFont::Draw's optical envelope, not the source
        # typeface's padded em box. Wide shoulders retain their authored aspect.
        scale = .88 * min(advance / width, 1000 / height)
        transform = (scale, 0, 0, scale,
                     advance / 2 - (x_min + x_max) * scale / 2,
                     500 - (y_min + y_max) * scale / 2)
        drawing = DecomposingRecordingPen(glyph_set)
        original.draw(drawing)
        pen = TTGlyphPen(None)
        drawing.replay(TransformPen(pen, transform))
        name = f"prompt{code:04X}"
        glyphs[name] = glyph = pen.glyph()
        glyph.recalcBounds(None)
        metrics[name] = (advance, glyph.xMin)
        cmap[code] = name
        records.append(dict(prompt=prompt, codepoint=code, advance=advance,
                            bounds=[glyph.xMin, glyph.yMin, glyph.xMax, glyph.yMax]))

    builder = FontBuilder(1000, isTTF=True)
    builder.setupGlyphOrder(list(glyphs))
    builder.setupCharacterMap(cmap)
    builder.setupGlyf(glyphs)
    builder.setupHorizontalMetrics(metrics)
    builder.setupHorizontalHeader(ascent=1000, descent=0, lineGap=0)
    source_notice = font["name"].getDebugName(0) or ""
    license_text = font["name"].getDebugName(13) or "CC0 1.0 Universal"
    license_url = font["name"].getDebugName(14) or "https://creativecommons.org/publicdomain/zero/1.0/"
    builder.setupNameTable(dict(familyName=family, styleName="Regular", fullName=family,
                               uniqueFontIdentifier=family + " 1.0", psName=family.replace(" ", ""),
                               version="Version 1.0",
                               copyright=source_notice + "\nWidgetRail ink-normalized subset; see NOTICE.txt.",
                               licenseDescription=license_text, licenseInfoURL=license_url))
    builder.setupOS2(version=4, sTypoAscender=1000, sTypoDescender=0, sTypoLineGap=0,
                     usWinAscent=1000, usWinDescent=0, fsSelection=0xC0)
    builder.setupPost()
    builder.font["head"].created = builder.font["head"].modified = font["head"].created
    builder.font.recalcTimestamp = False
    destination = OUTPUT / filename
    builder.save(destination)
    return dict(source=str(source.relative_to(ROOT)), sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                file=filename, sha256=hashlib.sha256(destination.read_bytes()).hexdigest(), glyphs=records)


def main():
    mappings = re.findall(r"\(ControllerPrompt\.(\w+), (true|false)\) => 0x([0-9A-F]+)",
                          (ROOT / "src/OverlayFrontend.WinUI/Presentation/WidgetGlyphs.cs").read_text())
    if len(mappings) != 44:
        raise ValueError("Review the controller prompt inventory before regenerating fonts")
    OUTPUT.mkdir(exist_ok=True)
    specs = [
        ("kenney/kenney_input_xbox_series.ttf", "WidgetRail Kenney Xbox", "xbox.ttf",
         [(name, int(code, 16)) for name, ps, code in mappings if ps == "false"]),
        ("kenney/kenney_input_playstation_series.ttf", "WidgetRail Kenney PlayStation", "playstation.ttf",
         [(name, int(code, 16)) for name, ps, code in mappings if ps == "true" and name != "Guide"]),
        ("promptfont/promptfont.ttf", "WidgetRail Controller Guide", "guide.ttf", [("Guide", 0xE000)]),
    ]
    records = [build(FONT_ROOT / source, family, filename, prompts) for source, family, filename, prompts in specs]
    (OUTPUT / "inventory.json").write_text(json.dumps(records, indent=2) + "\n", encoding="utf-8")
    print(f"Generated {len(records)} controller subsets; {sum(len(record['glyphs']) for record in records)} glyphs")


if __name__ == "__main__":
    main()
