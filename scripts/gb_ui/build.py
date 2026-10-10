#!/usr/bin/env python3
"""Builds the Game Boy PC skins for pkmgr-box (native/pkmgr-box-gb.c): Red/Blue, Yellow, Gold/Silver, Crystal.

Usage: scripts/gb_ui/build.py <decomp dir> <output dir>

<decomp dir> holds pret checkouts named pokered, pokeyellow, pokegold and pokecrystal. Writes <output>/<rb|y|gs|c>/
with the games' own 8x8 tiles, each a PNG in the colours the screen shows it in (colour 0 see-through):

  frame.png            the text box frame: ┌ ─ ┐ │ └ ┘ side by side
  lv/id/no/pkmn.png    <LV>, <ID>, №, <PK><MN> (16x8)
  cursor, cursor_empty, arrow_down, male, female .png   ▶ ▷ ▼ ♂ ♀
  hp_label.png         "HP:" (16x8); hp_<green|yellow|red>.png: the bar's empty, 1-7 px and full tiles (9 tiles)
  Gen 1: hp_end.png, line_v/line_h/line_corner/line_end.png (the status screen's line boxes), to.png, bold_p.png,
         dot.png
  Gen 2: select.png (the PC's selection outline, 2 tiles), item/mail.png, arrow_left/right.png (the box arrows),
         hp_cap.png, exp.png (empty, 1-7 px from the right, full), exp_cap_left.png, divider_v.png, divider_h.png,
         page_<small|large>_<pink|green|blue>.png (2x2 tiles), bold_p.png, shiny.png, arrow_page_left.png
  layout.json          {"style": "gb", "gen", "colors": {bg, fg, disabled, pages}}

Tile numbers and files are the games' (pokered engine/pokemon/status_screen.asm, home/list_menu.asm; pokecrystal
engine/gfx/load_font.asm, engine/pokemon/stats_screen.asm, bills_pc.asm). Requires Pillow.
"""
import json
import os
import sys

from PIL import Image

WHITE = (247, 243, 247)  # the Game Boy Color's white as NextUI's Game Boy core shows it
BLACK = (0, 0, 0)


def rgb5(r, g, b):
    return (r * 255 // 31, g * 255 // 31, b * 255 // 31)


HP_BARS = {"green": rgb5(0, 23, 0), "yellow": rgb5(31, 21, 0), "red": rgb5(31, 0, 0)}  # gfx/battle/hp_bar.pal
HP_LIGHT = rgb5(30, 26, 15)
EXP_BAR = rgb5(4, 17, 31)  # gfx/battle/exp_bar.pal
PAGES = {  # gfx/stats/pages.pal (colours 1 and 2) and stats.pal (each page's background)
    "pink": {"bg": rgb5(31, 19, 31), "c1": rgb5(31, 19, 31), "c2": rgb5(31, 15, 31)},
    "green": {"bg": rgb5(21, 31, 14), "c1": rgb5(21, 31, 14), "c2": rgb5(17, 31, 0)},
    "blue": {"bg": rgb5(17, 31, 31), "c1": rgb5(17, 31, 31), "c2": rgb5(17, 31, 31)},
}


def tile(path, col, row, colors, count=1):
    """count 8x8 tiles from (col, row) on (row-major), in colors (index 0-3; None = see-through)."""
    img = Image.open(path).convert("L")
    cols = img.width // 8
    out = Image.new("RGBA", (8 * count, 8), (0, 0, 0, 0))
    for n in range(count):
        index = row * cols + col + n
        tx, ty = (index % cols) * 8, (index // cols) * 8
        for y in range(8):
            for x in range(8):
                level = img.getpixel((tx + x, ty + y))
                c = {255: 0, 170: 1, 85: 2, 0: 3}.get(level, 3 - round(level / 85))
                if colors[c] is not None:
                    out.putpixel((n * 8 + x, y), colors[c] + (255,))
    return out


def tiles(path, picks, colors):
    """Several tiles (col, row) side by side."""
    out = Image.new("RGBA", (8 * len(picks), 8), (0, 0, 0, 0))
    for i, (c, r) in enumerate(picks):
        out.alpha_composite(tile(path, c, r, colors), (i * 8, 0))
    return out


def save(img, out, name):
    img.save(os.path.join(out, name))


TEXT = [None, (170, 170, 170), (85, 85, 85), BLACK]


def game_font(repo, font, out):
    """The game's own font for the viewer: font.png (gfx/font/font.png, loaded at tile $80) in white on
    see-through, so the viewer can tint it, and charmap.json: each character's tile in it (constants/charmap.asm:
    letters, digits, punctuation, é, 's...)."""
    import re
    save(tile(font, 0, 0, [None, None, None, (255, 255, 255)], 128), out, "font_row.png")
    sheet = Image.new("RGBA", (128, 64), (0, 0, 0, 0))
    strip = Image.open(os.path.join(out, "font_row.png"))
    for i in range(128):
        sheet.alpha_composite(strip.crop((i * 8, 0, i * 8 + 8, 8)), ((i % 16) * 8, (i // 16) * 8))
    os.remove(os.path.join(out, "font_row.png"))
    save(sheet, out, "font.png")
    chars = {}
    for line in open(os.path.join(repo, "constants", "charmap.asm"), encoding="utf-8"):
        m = re.match(r'\s*charmap\s+"([^"<>]+)",\s*\$([0-9a-fA-F]{2})', line)
        if not m:
            continue
        text, code = m.group(1), int(m.group(2), 16)
        # the Western games' Latin characters (the Japanese ones share codes with them)
        if code >= 0x80 and text not in chars and all(ord(c) < 0x3000 for c in text):
            chars[text] = code - 0x80
    chars[" "] = -1  # a blank tile
    with open(os.path.join(out, "charmap.json"), "w", encoding="utf-8") as f:
        json.dump({"chars": chars}, f, ensure_ascii=False)


def font_glyphs(font, out):
    """gfx/font/font.png at $80: <PK><MN> $E1-$E2, ▷ $EC, ▶ $ED, ▼ $EE, ♂ $EF, ♀ $F5, <DOT> $F2."""
    save(tiles(font, [(1, 6), (2, 6)], TEXT), out, "pkmn.png")
    save(tile(font, 12, 6, TEXT), out, "cursor_empty.png")
    save(tile(font, 13, 6, TEXT), out, "cursor.png")
    save(tile(font, 14, 6, TEXT), out, "arrow_down.png")
    save(tile(font, 15, 6, TEXT), out, "male.png")
    save(tile(font, 5, 7, TEXT), out, "female.png")
    save(tile(font, 2, 7, TEXT), out, "dot.png")


def gen1(repo, out):
    gfx = os.path.join(repo, "gfx")
    os.makedirs(out, exist_ok=True)
    extra = os.path.join(gfx, "font", "font_extra.png")  # $60-$7F
    battle = os.path.join(gfx, "font", "font_battle_extra.png")  # $62-$7F, 15 tiles a row
    hud1, hud2, hud3 = (os.path.join(gfx, "battle", f"battle_hud_{n}.png") for n in (1, 2, 3))
    save(tiles(extra, [(9, 1), (10, 1), (11, 1), (12, 1), (13, 1), (14, 1)], TEXT), out, "frame.png")
    font_glyphs(os.path.join(gfx, "font", "font.png"), out)
    game_font(repo, os.path.join(gfx, "font", "font.png"), out)
    save(tile(hud1, 1, 0, TEXT), out, "lv.png")  # $6E ":L" (battle_hud_1 over font_battle_extra)
    save(tile(battle, 2, 1, TEXT), out, "id.png")  # $73
    save(tile(battle, 3, 1, TEXT), out, "no.png")  # $74
    save(tile(battle, 14, 0, TEXT), out, "to.png")  # $70
    save(tile(os.path.join(gfx, "font", "P.png"), 0, 0, TEXT), out, "bold_p.png")  # $72
    # the status screen's line boxes (DrawLineBox): │ $78, ─ $76, ┘ $77, the left end $6F
    save(tile(hud2, 0, 0, TEXT), out, "line_v.png")
    save(tile(hud3, 0, 0, TEXT), out, "line_h.png")
    save(tile(hud3, 1, 0, TEXT), out, "line_corner.png")
    save(tile(hud1, 2, 0, TEXT), out, "line_end.png")
    # HP bar: "HP:" $71 $62, empty $63, 1-7 px $64-$6A, full $6B, right end $6D (battle_hud_1)
    save(tiles(battle, [(0, 1), (0, 0)], [None, HP_LIGHT, (85, 85, 85), BLACK]), out, "hp_label.png")
    save(tile(hud1, 0, 0, TEXT), out, "hp_end.png")
    for name, color in HP_BARS.items():
        save(tiles(battle, [(1 + n, 0) for n in range(9)], [None, HP_LIGHT, color, BLACK]), out, f"hp_{name}.png")
    return {"style": "gb", "gen": 1}


def gen2(repo, out, crystal):
    gfx = os.path.join(repo, "gfx")
    os.makedirs(out, exist_ok=True)
    frame = os.path.join(gfx, "frames", "1.png")  # ┌ ─ ┐ / │ └ ┘ (3x2)
    save(tiles(frame, [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)], TEXT), out, "frame.png")
    font_glyphs(os.path.join(gfx, "font", "font.png"), out)
    game_font(repo, os.path.join(gfx, "font", "font.png"), out)
    battle = os.path.join(gfx, "font", "font_battle_extra.png")  # $60-$78, 16 tiles a row
    save(tile(os.path.join(gfx, "battle", "enemy_hp_bar_border.png"), 2, 0, TEXT), out, "lv.png")  # $6E
    save(tile(battle, 3, 1, TEXT), out, "id.png")  # $73
    save(tile(battle, 4, 1, TEXT), out, "no.png")  # $74
    save(tile(battle, 1, 1, TEXT), out, "arrow_page_left.png")  # $71 ◀
    pc = os.path.join(gfx, "pc")
    save(tiles(os.path.join(pc, "pc.png"), [(0, 0), (1, 0)], TEXT), out, "select.png")
    mail = os.path.join(pc, "pc_mail.png")  # $5C mail, $5D item, $5E/$5F the box arrows
    save(tile(mail, 0, 0, TEXT), out, "mail.png")
    save(tile(mail, 1, 0, TEXT), out, "item.png")
    save(tile(mail, 0, 1, TEXT), out, "arrow_left.png")
    save(tile(mail, 1, 1, TEXT), out, "arrow_right.png")
    # HP bar: "HP:" $60 $61, empty $62, 1-7 px $63-$69, full $6A; the right cap $41 (stats_tiles)
    save(tiles(battle, [(0, 0), (1, 0)], [None, HP_LIGHT, (85, 85, 85), BLACK]), out, "hp_label.png")
    for name, color in HP_BARS.items():
        save(tiles(battle, [(2 + n, 0) for n in range(9)], [None, HP_LIGHT, color, BLACK]), out, f"hp_{name}.png")
    stats = os.path.join(gfx, "stats", "stats_tiles.png")  # $31-$41
    save(tile(stats, 16, 0, [None, HP_LIGHT, (85, 85, 85), BLACK]), out, "hp_cap.png")
    save(tile(stats, 0, 0, TEXT), out, "divider_v.png")  # $31
    save(tile(battle, 2, 0, TEXT), out, "divider_h.png")  # $62
    save(tile(stats, 13, 0, TEXT), out, "bold_p.png")  # $3E
    save(tile(stats, 14, 0, TEXT), out, "shiny.png")  # $3F
    # EXP bar: empty $62, 1-7 px from the right $55-$5B (expbar.png), full $6A; caps $40, $41
    expbar = os.path.join(gfx, "battle", "expbar.png")
    exp_colors = [None, HP_LIGHT, EXP_BAR, BLACK]
    strip = Image.new("RGBA", (72, 8), (0, 0, 0, 0))
    strip.alpha_composite(tile(battle, 2, 0, exp_colors), (0, 0))
    for n in range(7):
        strip.alpha_composite(tile(expbar, n, 0, exp_colors), (8 + n * 8, 0))
    strip.alpha_composite(tile(battle, 10, 0, exp_colors), (64, 0))
    save(strip, out, "exp.png")
    save(tile(stats, 15, 0, exp_colors), out, "exp_cap_left.png")
    save(tile(stats, 16, 0, exp_colors), out, "exp_cap_right.png")
    # the page squares: small $36-$39, large $3A-$3D, in each page's colours
    for page, pal in PAGES.items():
        colors = [None, pal["c1"], pal["c2"], BLACK]
        for size, first in (("small", 5), ("large", 9)):
            square = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
            for i in range(4):
                square.alpha_composite(tile(stats, first + i, 0, colors), ((i % 2) * 8, (i // 2) * 8))
            save(square, out, f"page_{size}_{page}.png")
    return {"style": "gb", "gen": 2, "crystal": crystal,
            "colors": {"pages": {name: list(p["bg"]) for name, p in PAGES.items()}}}


def main():
    decomp, out = sys.argv[1], sys.argv[2]
    games = [("rb", "pokered", gen1), ("y", "pokeyellow", gen1),
             ("gs", "pokegold", lambda r, o: gen2(r, o, False)), ("c", "pokecrystal", lambda r, o: gen2(r, o, True))]
    for name, repo, build in games:
        path = os.path.join(decomp, repo)
        if not os.path.isdir(os.path.join(path, "gfx")):
            print(f"gb_ui: no {repo}")
            continue
        target = os.path.join(out, name)
        layout = build(path, target)
        layout.setdefault("colors", {}).update({"bg": list(WHITE), "fg": list(BLACK), "disabled": [165, 165, 165]})
        layout["font"] = {"height": 7, "cap_top": 0, "color": list(BLACK)}
        with open(os.path.join(target, "layout.json"), "w", encoding="utf-8") as f:
            json.dump(layout, f, indent=1)
        print(f"gb_ui: {name}")


if __name__ == "__main__":
    main()
