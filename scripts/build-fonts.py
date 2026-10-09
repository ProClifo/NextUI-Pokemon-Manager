#!/usr/bin/env python3
"""Builds TrueType pixel fonts of the games' menu fonts for the per-game menus.

Usage: scripts/build-fonts.py <decomp dir> <output dir> [preview.png]

<decomp dir> holds checkouts of pret/pokered, pokegold, pokecrystal, pokeemerald, pokefirered and
pokeplatinum (only their font graphics, width tables and character maps are read).

TrueType text is only pixel-sharp when one font pixel is a whole number of screen pixels, and the
tools draw text at fixed sizes (times the device's UI scale of 2 or 3): minui-list's items at 16 and
its title at 14, minui-presenter's message at a size we pick and its button labels at 12. So each font
is written once per use and scale: <name>-<scale>x-list.ttf, -title.ttf and -message.ttf. fonts.json
records the files and the message size to pass to minui-presenter, and <name>.chars the characters the
font covers (text with anything else falls back to the NextUI font). Requires Pillow and fontTools.

The fonts declare themselves Bold: minui-list asks SDL_ttf for bold text, and SDL_ttf only fakes bold
(by smearing glyphs sideways, which would ruin pixel art) when the font itself isn't bold.
"""
import json
import os
import re
import sys

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from PIL import Image

UPEM = 10080  # divisible enough that every size below gets a whole number of font units per pixel
LIST_PX, TITLE_PX, BUTTON_PX = 16, 14, 12  # FONT_LARGE, FONT_MEDIUM, FONT_SMALL before the UI scale


def gb_charmap(path):
    """charmap "A", $80 lines from a pret Game Boy decomp (single characters only)."""
    chars = {}
    for line in open(path, encoding="utf-8"):
        m = re.match(r'\s*charmap\s+"(.)",\s*\$([0-9a-fA-F]{2})', line)
        if m and m.group(1) not in chars:
            chars[m.group(1)] = int(m.group(2), 16)
    return chars


def gba_charmap(path):
    """'A' = BB lines from pokeemerald/pokefirered charmap.txt (single characters, single bytes)."""
    chars = {}
    for line in open(path, encoding="utf-8"):
        m = re.match(r"'(.)'\s*=\s*([0-9A-Fa-f]{2})\s*(?:@.*)?$", line.strip())
        if m and m.group(1) not in chars:
            chars[m.group(1)] = int(m.group(2), 16)
    return chars


def nds_charmap(path):
    """HHHH=c lines from pokeplatinum tools/msgenc/charmap.txt."""
    chars = {}
    for line in open(path, encoding="utf-8"):
        m = re.match(r"([0-9A-Fa-f]{4})=(.)$", line.rstrip("\n"))
        if m and m.group(2) not in chars:
            chars[m.group(2)] = int(m.group(1), 16)
    return chars


def c_array(path, name):
    """The numbers in `name[] = { ... };` from a C source file."""
    text = open(path, encoding="utf-8").read()
    m = re.search(re.escape(name) + r"\[\]\s*=\s*\{(.*?)\};", text, re.S)
    return [int(x) for x in re.findall(r"\d+", m.group(1))]


class Sheet:
    """A grid of glyph cells; ink(x, y) is true for text-coloured pixels."""

    def __init__(self, path, cell_w, cell_h, ink):
        image = Image.open(path)
        self.image = image if image.mode == "P" else image.convert("L")
        self.px = self.image.load()
        self.cell_w, self.cell_h = cell_w, cell_h
        self.cols = self.image.width // cell_w
        self.ink = ink

    def bitmap(self, index):
        ox, oy = (index % self.cols) * self.cell_w, (index // self.cols) * self.cell_h
        if oy + self.cell_h > self.image.height:
            return None
        return [[self.ink(self.px[ox + x, oy + y]) for x in range(self.cell_w)] for y in range(self.cell_h)]


def baseline_of(bitmap):
    rows = [y for y, row in enumerate(bitmap) if any(row)]
    return rows[-1] + 1


def build(name, family, glyphs, cell_h, baseline, out_dir, unit_px):
    """glyphs: {char: (bitmap rows, advance in pixels)}; unit_px: font units per font pixel."""
    order = [".notdef"] + [f"u{ord(c):04X}" for c in glyphs]
    fb = FontBuilder(UPEM, isTTF=True)
    fb.setupGlyphOrder(order)
    fb.setupCharacterMap({ord(c): f"u{ord(c):04X}" for c in glyphs})
    outlines, metrics = {}, {}
    empty = TTGlyphPen(None)
    outlines[".notdef"] = empty.glyph()
    metrics[".notdef"] = (unit_px * 4, 0)
    for char, (bitmap, advance) in glyphs.items():
        pen = TTGlyphPen(None)
        for y, row in enumerate(bitmap):
            x = 0
            while x < len(row):
                if not row[x]:
                    x += 1
                    continue
                start = x
                while x < len(row) and row[x]:
                    x += 1
                top, bottom = (baseline - y) * unit_px, (baseline - y - 1) * unit_px
                pen.moveTo((start * unit_px, bottom))
                pen.lineTo((start * unit_px, top))
                pen.lineTo((x * unit_px, top))
                pen.lineTo((x * unit_px, bottom))
                pen.closePath()
        glyph_name = f"u{ord(char):04X}"
        outlines[glyph_name] = pen.glyph()
        metrics[glyph_name] = (advance * unit_px, 0)
    fb.setupGlyf(outlines)
    fb.setupHorizontalMetrics(metrics)
    ascent, descent = baseline * unit_px, (cell_h - baseline) * unit_px
    fb.setupHorizontalHeader(ascent=ascent, descent=-descent)
    fb.setupNameTable({"familyName": family, "styleName": "Bold"})
    fb.setupOS2(sTypoAscender=ascent, sTypoDescender=-descent, sTypoLineGap=0, usWinAscent=ascent,
                usWinDescent=descent, usWeightClass=700, fsSelection=0x20)  # BOLD
    fb.setupPost()
    fb.font["head"].macStyle = 1  # bold
    fb.save(os.path.join(out_dir, name))


MANIFEST = {}


def units(pixels, size_px):
    """Font units per font pixel so that one font pixel is `pixels` screen pixels at `size_px`."""
    assert UPEM * pixels % size_px == 0, (pixels, size_px)
    return UPEM * pixels // size_px


def make(name, family, glyphs, cell_h, out_dir, scales):
    """scales: {ui scale: (screen pixels per font pixel for text, for the smaller button labels)}."""
    baseline = baseline_of(glyphs["A"][0])
    entry = {"chars": f"{name}.chars"}
    for scale, (text_px, button_px) in scales.items():
        files = {}
        for role, size in (("list", LIST_PX), ("title", TITLE_PX), ("message", BUTTON_PX)):
            per_pixel = button_px if role == "message" else text_px
            file = f"{name}-{scale}x-{role}.ttf"
            build(file, family, glyphs, cell_h, baseline, out_dir, units(per_pixel, size * scale))
            files[role] = file
        # minui-presenter draws the message at --font-size-default (times the scale) with the same file
        # as its button labels; this size makes the message text_px screen pixels per font pixel.
        assert BUTTON_PX * text_px % button_px == 0
        files["message_size"] = BUTTON_PX * text_px // button_px
        entry[f"{scale}x"] = files
    MANIFEST[name] = entry
    with open(os.path.join(out_dir, f"{name}.chars"), "w", encoding="utf-8") as f:
        f.write("".join(sorted(glyphs)))
    return glyphs


def game_boy(src, out_dir, name, family):
    """8x8 tiles for character codes $80-$FF, monospaced."""
    sheet = Sheet(os.path.join(src, "gfx/font/font.png"), 8, 8, lambda v: v < 128)
    glyphs = {" ": ([[False] * 8 for _ in range(8)], 8)}
    for char, code in gb_charmap(os.path.join(src, "constants/charmap.asm")).items():
        if code >= 0x80 and char != " " and (bitmap := sheet.bitmap(code - 0x80)) is not None:
            glyphs[char] = (bitmap, 8)
    # 8 px glyphs: 3 screen pixels per font pixel at 2x (2 for button labels), 4 at 3x (3).
    return make(name, family, glyphs, 8, out_dir, {2: (3, 2), 3: (4, 3)})


def gba(src, out_dir, name, family, widths):
    """16x16 cells indexed by character code; palette index 1 is the text colour (2 is its shadow)."""
    sheet = Sheet(os.path.join(src, "graphics/fonts/latin_normal.png"), 16, 16, lambda v: v == 1)
    glyphs = {}
    for char, code in gba_charmap(os.path.join(src, "charmap.txt")).items():
        if code < len(widths) and (bitmap := sheet.bitmap(code)) is not None and (any(map(any, bitmap)) or char == " "):
            glyphs[char] = ([row[: widths[code]] for row in bitmap], widths[code])
    return make(name, family, glyphs, 16, out_dir, {2: (2, 1), 3: (3, 2)})


def nds(src, out_dir, name, family):
    """Platinum's system font: 16x16 cells for character codes 1 and up, with a width table."""
    fonts = os.path.join(src, "res/fonts")
    widths = json.load(open(os.path.join(fonts, "font_system.json")))["glyphWidths"]
    sheet = Sheet(os.path.join(fonts, "font_system.png"), 16, 16, lambda v: v == 1)
    glyphs = {}
    for char, code in nds_charmap(os.path.join(src, "tools/msgenc/charmap.txt")).items():
        index = code - 1
        if 0 <= index < len(widths) and (bitmap := sheet.bitmap(index)) is not None and (any(map(any, bitmap)) or char == " "):
            glyphs[char] = ([row[: widths[index]] for row in bitmap], widths[index])
    return make(name, family, glyphs, 16, out_dir, {2: (2, 1), 3: (3, 2)})


def preview(fonts, path):
    """A sheet of 'The quick brown fox' in every font, drawn from the glyph bitmaps, for checking."""
    text = "Pokémon Manager: The quick brown fox 0123 ♂♀"
    lines = []
    for name, glyphs in fonts:
        line = [g for g in (glyphs.get(c) for c in text) if g]
        lines.append(line)
    height = sum(len(line[0][0]) + 4 for line in lines) if lines else 0
    width = max(sum(adv for _, adv in line) for line in lines)
    image = Image.new("L", (width + 4, height + 4), 255)
    y = 2
    for line in lines:
        x = 2
        for bitmap, advance in line:
            for gy, row in enumerate(bitmap):
                for gx, on in enumerate(row):
                    if on:
                        image.putpixel((x + gx, y + gy), 0)
            x += advance
        y += len(line[0][0]) + 4
    image.resize((image.width * 3, image.height * 3), Image.NEAREST).save(path)


def main():
    src, out_dir = sys.argv[1], sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)
    emerald, firered = os.path.join(src, "pokeemerald"), os.path.join(src, "pokefirered")
    fonts = [
        ("gen1", game_boy(os.path.join(src, "pokered"), out_dir, "gen1", "Pokemon RBY")),
        ("gen2-gs", game_boy(os.path.join(src, "pokegold"), out_dir, "gen2-gs", "Pokemon GS")),
        ("gen2-c", game_boy(os.path.join(src, "pokecrystal"), out_dir, "gen2-c", "Pokemon Crystal")),
        ("gen3-rse", gba(emerald, out_dir, "gen3-rse", "Pokemon RSE",
                         c_array(os.path.join(emerald, "src/fonts.c"), "gFontNormalLatinGlyphWidths"))),
        ("gen3-frlg", gba(firered, out_dir, "gen3-frlg", "Pokemon FRLG",
                          c_array(os.path.join(firered, "src/text.c"), "sFontNormalLatinGlyphWidths"))),
        ("gen4", nds(os.path.join(src, "pokeplatinum"), out_dir, "gen4", "Pokemon DPPt")),
    ]
    with open(os.path.join(out_dir, "fonts.json"), "w", encoding="utf-8") as f:
        json.dump(MANIFEST, f, indent=1)
    if len(sys.argv) > 3:
        preview(fonts, sys.argv[3])
    print(f"{len(fonts)} fonts: " + ", ".join(f"{n} ({len(g)} characters)" for n, g in fonts))


if __name__ == "__main__":
    main()
