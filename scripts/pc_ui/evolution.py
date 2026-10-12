"""The games' own screens after an evolution, for the PC viewer's "Congratulations! ..." screen.

Ruby/Sapphire/Emerald and FireRed/LeafGreen (src/evolution_scene.c): the screen is black but for the battle
text box (LoadBattleTextboxAndBackground; graphics/battle_interface/textbox + its tilemap, rows 14-19), the
evolved Pokémon centred on (120, 64), and the message in B_WIN_MSG (tile 2,15) in the text box palette's
colours 1 (white) and 6 (shadow). Ruby/Sapphire's text box is Emerald's.

Diamond/Pearl/Platinum and HeartGold/SoulSilver (pokeplatinum src/evolution.c): the egg-hatch background
(res/graphics/egg_hatch), the Pokémon centred on (128, 80) and the message box in the player's frame at tile
(2,19), 27x4.

Writes evolution_bg.png and the layout's "evolution" entry into each skin.
"""
import json
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from pc_ui import gba, nds  # noqa: E402


def _gba_textbox(png, tilemap, pal):
    tiles, entries = gba.load_tiles(png), gba.read_tilemap(tilemap)
    colors = gba.read_jasc(pal)
    img = Image.new("RGBA", (240, 160), (0, 0, 0, 255))
    box = gba.new_screen(240, 48)
    gba.draw_tilemap(box, entries, 32, tiles, [colors] * 16, src_x=0, src_y=14, w=30, h=6)
    img.alpha_composite(box.convert("RGBA"), (0, 112))
    return img, colors


def _ds_background(repo):
    d = os.path.join(repo, "res", "graphics", "egg_hatch")
    png, nscr = os.path.join(d, "background.png"), os.path.join(d, "background.NSCR")
    if not (os.path.exists(png) and os.path.exists(nscr)):
        return None
    im = Image.open(png)
    px, pal = im.load(), im.getpalette()
    cols = [tuple(pal[i * 3:i * 3 + 3]) for i in range(len(pal) // 3)]
    scr = nds.Nscr(open(nscr, "rb").read())
    tw = im.size[0] // 8
    out = Image.new("RGBA", (256, 192), (0, 0, 0, 255))
    o = out.load()
    for n, e in enumerate(scr.entries):
        x0, y0 = (n % scr.w) * 8, (n // scr.w) * 8
        if x0 >= 256 or y0 >= 192:
            continue
        t, hf, vf, p = e & 0x3FF, (e >> 10) & 1, (e >> 11) & 1, e >> 12
        tx, ty = (t % tw) * 8, (t // tw) * 8
        if ty >= im.size[1]:
            continue
        for y in range(8):
            for x in range(8):
                c = px[tx + (7 - x if hf else x), ty + (7 - y if vf else y)]
                ci = p * 16 + c % 16
                if ci < len(cols):
                    o[x0 + x, y0 + y] = cols[ci] + (255,)
    return out


def _add(target, image, entry):
    layout_path = os.path.join(target, "layout.json")
    if not os.path.exists(layout_path):
        return False
    image.save(os.path.join(target, "evolution_bg.png"))
    layout = json.load(open(layout_path, encoding="utf-8"))
    layout["evolution"] = entry
    with open(layout_path, "w", encoding="utf-8") as f:
        json.dump(layout, f, indent=1)
    return True


def build(decomp, out):
    made = []
    e = os.path.join(decomp, "pokeemerald", "graphics", "battle_interface")
    if os.path.exists(os.path.join(e, "textbox.png")):
        img, colors = _gba_textbox(os.path.join(e, "textbox.png"), os.path.join(e, "textbox_map.bin"), os.path.join(e, "textbox_0.pal"))
        entry = {"image": "evolution_bg.png", "sprite": [120, 64], "text": [16, 121], "width": 208, "lines": 2,
                 "line_height": 16, "font": "normal", "color": list(colors[1]), "shadow": list(colors[6])}
        for game in ("e", "rs"):
            if _add(os.path.join(out, game), img, entry):
                made.append(game)
    fr = os.path.join(decomp, "pokefirered", "graphics", "battle_interface")
    if os.path.exists(os.path.join(fr, "textbox.png")):
        img, colors = _gba_textbox(os.path.join(fr, "textbox.png"), os.path.join(fr, "textbox.bin"), os.path.join(fr, "textbox1.pal"))
        entry = {"image": "evolution_bg.png", "sprite": [120, 64], "text": [18, 122], "width": 206, "lines": 2,
                 "line_height": 16, "font": "normal", "color": list(colors[1]), "shadow": list(colors[6])}
        if _add(os.path.join(out, "frlg"), img, entry):
            made.append("frlg")
    bg = _ds_background(os.path.join(decomp, "pokeplatinum"))
    if bg is not None:
        # the message box (drawn by the viewer in the player's frame) at tile (2,19), 27x4
        entry = {"image": "evolution_bg.png", "sprite": [128, 80], "window": [16, 152, 216, 32], "text": [16, 152],
                 "width": 216, "lines": 2, "line_height": 16}
        for game in ("dp", "pt", "hgss"):
            if _add(os.path.join(out, game), bg, entry):
                made.append(game)
    print(f"evolution screens: {', '.join(made) or 'none'}")


if __name__ == "__main__":
    build(sys.argv[1], sys.argv[2])
