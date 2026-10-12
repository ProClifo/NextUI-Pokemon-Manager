"""Diamond/Pearl's and Platinum's PC Storage System skins (dp, pt) from pret's pokediamond and pokeplatinum.

Both games' box graphics are the same set of files (pokeplatinum: res/prebuilt/graphic/box.narc, 145 members;
pokediamond: files/graphic/box/narc_NNNN.*, 121 members, 8 wallpapers fewer), decoded with pc_ui.nds. Positions
are the games' top screen (256x192), from src/applications/pc_boxes (ov19_*):
  member 0 + tiles 1 + palettes 5   the screen's frame: the left panel showing the Pokémon under the hand
  member 6                          the party panel, slid in at x 112 (rows 2-25)
  members 9/10                      the PARTY POKéMON / CLOSE button bar, centred at (183,176), OBJ palette 1
  members 12/13                     the hand (cells 0/1), its shadow (4) and the box arrows (5/6), OBJ palette 0
  members 15/16                     the plate behind the info line, centred at (56,176)
  wallpaper k                       palette 28+3k, tiles 29+3k, screen 30+3k: 168x160 at (88,0), title banner included
Platinum's message and menu frames come from its window graphics (winframe.narc, res/graphics/windows).
The summary screen is Platinum's (pc_ui.ds_summary).
"""
import glob
import json
import os
import shutil

from PIL import Image

from pc_ui import ds_summary, nds, pt

SCREEN = (256, 192)


def _files_pt(repo):
    path = os.path.join(repo, "res", "prebuilt", "graphic", "box.narc")
    return nds.narc_files(open(path, "rb").read()) if os.path.exists(path) else None


def _files_dp(repo):
    names = sorted(glob.glob(os.path.join(repo, "files", "graphic", "box", "narc_*.*")))
    return [nds.lz_decompress(open(n, "rb").read()) for n in names] or None


def _canvas_cell(cell, chars, colors, size=32):
    """A sprite cell drawn into a size x size image whose centre is the cell's origin (OAM coordinates are
    relative to the sprite's position)."""
    img, x0, y0 = nds.render_cell(cell, chars, colors)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    if img is not None:
        out.alpha_composite(img, (size // 2 + x0, size // 2 + y0))
    return out


def _palette(colors, slot):
    """One 16-colour palette for every OAM palette number (sprites drawn with an explicit palette)."""
    return colors[slot * 16:slot * 16 + 16] * 16


def _place_cell(dst, cell, chars, colors, cx, cy):
    img, x0, y0 = nds.render_cell(cell, chars, colors)
    if img is not None:
        dst.alpha_composite(img, (cx + x0, cy + y0))


def _frame_3x3(tiles, colors, order):
    """A 24x24 window frame (3x3 tiles of 8 pixels, as the viewer's draw_window reads it)."""
    img = Image.new("RGBA", (24, 24), (0, 0, 0, 0))
    for k, t in enumerate(order):
        nds.draw_tile(img, tiles[t], (k % 3) * 8, (k // 3) * 8, colors)
    return img


def _text_width(platinum_repo):
    """The width of a text in Platinum's system font (font_system.json's glyph widths)."""
    chars = pt.load_charmap(platinum_repo)
    meta = os.path.join(platinum_repo, "res", "fonts", "font_system.json")
    widths = json.load(open(meta, encoding="utf-8"))["glyphWidths"] if os.path.exists(meta) else []

    def width(text):
        return sum(widths[chars[c]] if c in chars and chars[c] < len(widths) else 6 for c in text)
    return width


def _copy_e(target, e_skin):
    """Emerald's skin files other than its PC's and fonts (status icons, markings...), under what follows."""
    os.makedirs(target, exist_ok=True)
    if os.path.isdir(e_skin):
        for name in os.listdir(e_skin):
            src = os.path.join(e_skin, name)
            if name.startswith(("card_", "font_", "pc_", "hand", "arrow_", "wallpapers", "layout", "charmap")):
                continue
            dst = os.path.join(target, name)
            if os.path.isdir(src):
                shutil.copytree(src, dst, dirs_exist_ok=True)
            else:
                shutil.copy(src, dst)


def build_skin(fs, target, wallpapers, e_skin, platinum_repo):
    _copy_e(target, e_skin)
    bg_colors = nds.nclr(fs[5])
    bg_chars = nds.Ncgr(fs[1])
    obj_colors = nds.nclr(fs[26])

    # The screen: backdrop, the info line's plate (an OBJ behind the frame), the frame, the button bar.
    bg = Image.new("RGBA", SCREEN, bg_colors[0] + (255,))
    _place_cell(bg, nds.Ncer(fs[16]).cells[0], nds.Ncgr(fs[15]), _palette(obj_colors, 1), 56, 176)
    frame = nds.render_screen(nds.Nscr(fs[0]), bg_chars, bg_colors)
    bg.alpha_composite(frame.crop((0, 0) + SCREEN))
    _place_cell(bg, nds.Ncer(fs[10]).cells[0], nds.Ncgr(fs[9]), _palette(obj_colors, 1), 183, 176)
    bg.save(os.path.join(target, "pc_bg.png"))

    # The party panel (rows 2-25 of its 15x26 screen).
    party_scr = nds.Nscr(fs[6])
    party = nds.render_screen(party_scr, bg_chars, bg_colors).crop((0, 16, party_scr.w * 8, 16 + 192))
    party.save(os.path.join(target, "pc_party.png"))

    # The hand (two frames side by side), its shadow and the arrows.
    hand_cells = nds.Ncer(fs[13]).cells
    hand_chars = nds.Ncgr(fs[12])
    white = _palette(obj_colors, 0)
    hand = Image.new("RGBA", (64, 32), (0, 0, 0, 0))
    hand.alpha_composite(_canvas_cell(hand_cells[0], hand_chars, white), (0, 0))
    hand.alpha_composite(_canvas_cell(hand_cells[1], hand_chars, white), (32, 0))
    hand.save(os.path.join(target, "hand.png"))
    _canvas_cell(hand_cells[4], hand_chars, white).save(os.path.join(target, "hand_shadow.png"))
    arrows = {}
    for name, cell, (cx, cy) in (("left", 5, (108, 20)), ("right", 6, (236, 20))):
        img, x0, y0 = nds.render_cell(hand_cells[cell], hand_chars, white)
        img.save(os.path.join(target, f"arrow_{name}.png"))
        arrows[name] = [cx + x0, cy + y0]

    # Wallpapers.
    wp_dir = os.path.join(target, "wallpapers")
    os.makedirs(wp_dir, exist_ok=True)
    for k in range(wallpapers):
        img = nds.render_screen(nds.Nscr(fs[30 + 3 * k]), nds.Ncgr(fs[29 + 3 * k]), nds.nclr(fs[28 + 3 * k]), transparent0=False)
        img.save(os.path.join(wp_dir, f"{k:02d}.png"))

    white_text, white_shadow = [255, 246, 255], [115, 115, 131]
    name_text, name_shadow = [238, 230, 255], [90, 90, 106]
    pc = {
        "wallpaper": [88, 0],
        "box_name": [172, 13],
        "box_name_color": [98, 98, 98],
        "box_name_shadow": [255, 255, 255],
        "box_name_font": "normal",
        "message_font": "message",
        "arrows": arrows,
        # Icons are 32x32, centred at (112 + 24 col, 40 + 24 row).
        "grid": {"x": 96, "y": 24, "dx": 24, "dy": 24, "cols": 6, "rows": 5},
        "party_button": [111, 160, 72, 32],
        "close_button": [183, 160, 72, 32],
        # The hand is 16 pixels above the icon's centre; over the header, the buttons and EXIT as ov19_021D9D48.
        "hand_offset": [0, -16],
        "hand_title": [152, -8],
        "hand_party_button": [143, 144],
        "hand_close_button": [219, 144],
        "hand_cancel": [176, 152],
        "hand_shadow_offset": [0, 24],
        "hand_flip_on_buttons": False,
        "mon_sprite": [44, 84],  # the centre of the 80x80 sprite area
        "mon_text": [
            {"field": "species", "x": 2, "y": 8, "font": "normal", "color": white_text, "shadow": white_shadow},
            {"field": "dex_no", "x": 56, "y": 24, "prefix": "No.", "prefix_x": 38, "font": "normal", "color": white_text, "shadow": white_shadow},
            {"field": "nickname", "x": 2, "y": 128, "font": "normal", "color": name_text, "shadow": name_shadow},
            {"field": "gender", "x": 70, "y": 128, "color": name_text, "shadow": name_shadow,
             "male": {"color": [148, 230, 238], "shadow": [41, 123, 164]},
             "female": {"color": [246, 131, 131], "shadow": [197, 41, 41]}},
            {"field": "level", "x": 16, "y": 144, "prefix": "Lv", "prefix_x": 2, "font": "normal", "color": name_text, "shadow": name_shadow},
            {"field": "item", "x": 10, "y": 168, "font": "normal", "color": white_text, "shadow": white_shadow},
        ],
        "party": {"x": 112, "y": 0, "grid": True,
                  "slots": [[128, 12], [176, 20], [128, 52], [176, 60], [128, 92], [176, 100]],
                  "cancel": [176, 168, 32, 16]},
    }
    _finish(target, e_skin, platinum_repo, pc)


def _finish(target, e_skin, platinum_repo, pc):
    """What the DS skins share: Platinum's fonts, windows, menu cursor and summary screen, and the layout."""
    # Fonts, charmap, frames and the menu cursor are Platinum's (Diamond/Pearl's and HGSS's are the same).
    from pc_ui import build
    fonts = pt.fonts(platinum_repo, target, build._font_sheet)
    win = os.path.join(platinum_repo, "res", "prebuilt", "graphic", "winframe.narc")
    if os.path.exists(win):
        wfs = nds.narc_files(open(win, "rb").read())
        _frame_3x3(nds.Ncgr(wfs[0]).tiles, nds.nclr(wfs[24]), range(9)).save(os.path.join(target, "window_frame.png"))
    msg = os.path.join(platinum_repo, "res", "graphics", "windows", "message_box_00.png")
    if os.path.exists(msg):
        m = Image.open(msg).convert("RGBA")
        out = Image.new("RGBA", (24, 24), (0, 0, 0, 0))
        for k, col in enumerate((0, 2, 5)):  # the left edge, the white middle, the right edge
            out.alpha_composite(m.crop((col * 8, 0, col * 8 + 8, 24)), (k * 8, 0))
        out.save(os.path.join(target, "message_frame.png"))
    # the 20 message box frames of OPTIONS > FRAME (message_box_00..19), drawn in the save's own one
    os.makedirs(os.path.join(target, "message_frames"), exist_ok=True)
    for n in range(20):
        f = os.path.join(platinum_repo, "res", "graphics", "windows", "message_box_%02d.png" % n)
        if os.path.exists(f):
            m = Image.open(f).convert("RGBA")
            out = Image.new("RGBA", (24, 24), (0, 0, 0, 0))
            for k, col in enumerate((0, 2, 5)):
                out.alpha_composite(m.crop((col * 8, 0, col * 8 + 8, 24)), (k * 8, 0))
            out.save(os.path.join(target, "message_frames", "%d.png" % (n + 1)))
    cursor = os.path.join(platinum_repo, "res", "fonts", "arrow_cursor.png")
    if os.path.exists(cursor):
        Image.open(cursor).convert("RGBA").save(os.path.join(target, "cursor_menu.png"))

    layout = {}
    e_layout = os.path.join(e_skin, "layout.json")
    if os.path.exists(e_layout):
        layout = json.load(open(e_layout, encoding="utf-8"))
    layout.update({
        "screen": list(SCREEN),
        "fonts": fonts,
        "card": False,
        "font": {"color": [106, 106, 98], "shadow": [197, 205, 205], "height": 12, "cap_top": 3},
        "sprite_size": 80,  # the DS games' Pokémon pictures are 80x80 (the GBA's 64x64)
        "summary_origin": [8, 16],
    })
    summary = ds_summary.build(platinum_repo, target, _text_width(platinum_repo))
    if summary:
        layout["summary"] = summary
        del layout["summary_origin"]
    layout["pc"] = dict(pc, **{
        "menu": {"right": 248, "bottom": 152, "line_height": 16, "text_x": 10, "text_y": 0,
                 "color": [16, 24, 32], "shadow": [172, 189, 189], "font": "normal"},
        "message": [16, 168, 224, 16],
        "message_text": [0, 0],
        "message_frame": "message_frame.png",
    })
    with open(os.path.join(target, "layout.json"), "w", encoding="utf-8") as f:
        json.dump(layout, f, indent=1)


def _font_drawer(platinum_repo):
    """Draws text in Platinum's system font (res/fonts/font_system.png: glyph pixels 1 = letter, 2 = shadow)."""
    chars = pt.load_charmap(platinum_repo)
    sheet = Image.open(os.path.join(platinum_repo, "res", "fonts", "font_system.png"))
    widths = json.load(open(os.path.join(platinum_repo, "res", "fonts", "font_system.json"), encoding="utf-8"))["glyphWidths"]
    cols = sheet.width // 16
    px = sheet.load()

    def width(text):
        return sum(widths[chars[c]] for c in text if c in chars and chars[c] < len(widths))

    def draw(img, text, x, y, color, shadow):
        out = img.load()
        for c in text:
            g = chars.get(c)
            if g is None or g >= len(widths):
                continue
            gx, gy = (g % cols) * 16, (g // cols) * 16
            for j in range(16):
                for i in range(16):
                    v = px[gx + i, gy + j]
                    if v in (1, 2) and 0 <= x + i < img.width and 0 <= y + j < img.height:
                        out[x + i, y + j] = tuple(color if v == 1 else shadow) + (255,)
            x += widths[g]
    return width, draw


def build_hgss(hg_repo, target, e_skin, platinum_repo):
    """HeartGold/SoulSilver's PC (pokeheartgold files/a/0/1/9, as overlay 14 loads it; that screen isn't
    decompiled, so the places come from its assembly and the art):
      BG3 = tiles 1, map 0 (64x32: the backdrop); the wallpaper k = tiles 16+k, palette 40+k, the shared 21x20
            map 15, drawn at tile (0, 1), so 168x160 at (0, 8)
      BG2 = tiles 3, map 2 over it: the box's rim and the striped blue column on the right; BG palette = member 4
      the party = map 8 with tiles 14 (88x144: six slots in two staggered columns and CANCEL)
      OBJ tiles 66, cells 67, palette 69: the box arrows (cells 0-3), the hand (8 open, 9 holding), its shadow (12)
    On the DS the Pokémon under the hand is shown on the touch screen; here its picture, name and level go in the
    blue column, with PARTY and CLOSE buttons (the touch screen's) under them."""
    path = os.path.join(hg_repo, "files", "a", "0", "1", "9")
    if not os.path.exists(path):
        return False
    _copy_e(target, e_skin)
    fs = []
    for b in nds.narc_files(open(path, "rb").read()):
        if b[:1] in (b"\x10", b"\x11"):
            try:
                b = nds.lz_decompress(b)
            except Exception:
                pass
        fs.append(b)
    pal = nds.nclr(fs[4])
    bg = Image.new("RGBA", SCREEN, pal[0] + (255,))
    bg.alpha_composite(nds.render_screen(nds.Nscr(fs[0]), nds.Ncgr(fs[1]), pal).crop((0, 0) + SCREEN))
    bg.save(os.path.join(target, "pc_bg.png"))
    fg = nds.render_screen(nds.Nscr(fs[2]), nds.Ncgr(fs[3]), pal).crop((0, 0) + SCREEN)

    width, draw = _font_drawer(platinum_repo)
    # The touch screen's colours: cream panels with a grey-blue rim, dark text (the party panel's palette).
    rim, cream, shade, ink, ink_shadow = (90, 90, 107), (255, 247, 222), (214, 206, 189), (82, 82, 82), (214, 206, 189)

    def button(img, rect, label):
        x, y, w, h = rect
        d = img.load()
        for j in range(h):
            for i in range(w):
                edge = i in (0, w - 1) or j in (0, h - 1)
                corner = (i in (0, w - 1)) and (j in (0, h - 1))
                if corner:
                    continue
                d[x + i, y + j] = (rim if edge else shade if j >= h - 3 else cream) + (255,)
        draw(img, label, x + (w - width(label)) // 2, y + (h - 16) // 2 + 1, ink, ink_shadow)
    party_button, close_button = [180, 148, 72, 18], [180, 170, 72, 18]
    button(fg, party_button, "PARTY")
    button(fg, close_button, "CLOSE")
    fg.save(os.path.join(target, "pc_fg.png"))

    party = nds.render_screen(nds.Nscr(fs[8]), nds.Ncgr(fs[14]), pal).crop((0, 0, 88, 144))
    draw(party, "CANCEL", 10 + (68 - width("CANCEL")) // 2, 118, ink, ink_shadow)
    party.save(os.path.join(target, "pc_party.png"))

    cells = nds.Ncer(fs[67]).cells
    chars = nds.Ncgr(fs[66])
    obj = nds.nclr(fs[69])
    hand = Image.new("RGBA", (64, 32), (0, 0, 0, 0))
    hand.alpha_composite(_canvas_cell(cells[8], chars, obj), (0, 0))
    hand.alpha_composite(_canvas_cell(cells[9], chars, obj), (32, 0))
    hand.save(os.path.join(target, "hand.png"))
    _canvas_cell(cells[12], chars, obj).save(os.path.join(target, "hand_shadow.png"))
    arrows = {}
    for name, cell, (cx, cy) in (("left", 0, (16, 28)), ("right", 2, (152, 28))):
        img, x0, y0 = nds.render_cell(cells[cell], chars, obj)
        img.save(os.path.join(target, f"arrow_{name}.png"))
        arrows[name] = [cx + x0, cy + y0]

    wp_dir = os.path.join(target, "wallpapers")
    os.makedirs(wp_dir, exist_ok=True)
    for k in range(24):
        nds.render_screen(nds.Nscr(fs[15]), nds.Ncgr(fs[16 + k]), nds.nclr(fs[40 + k]), transparent0=False).save(
            os.path.join(wp_dir, f"{k:02d}.png"))

    white, white_shadow = [255, 255, 255], [57, 82, 140]
    px0, py0 = 80, 24  # the party panel, over the box's right half
    pc = {
        "wallpaper": [0, 8],
        "foreground": True,
        "box_name": [84, 20],
        "box_name_color": [82, 82, 82],
        "box_name_shadow": [214, 206, 189],
        "box_name_font": "normal",
        "message_font": "message",
        "arrows": arrows,
        # As Platinum's grid, 88 to the left and 8 down: icons centred at (24 + 24 col, 48 + 24 row).
        "grid": {"x": 8, "y": 32, "dx": 24, "dy": 24, "cols": 6, "rows": 5},
        "party_button": party_button,
        "close_button": close_button,
        "hand_offset": [0, -16],
        "hand_title": [68, -2],
        "hand_party_button": [200, 132],
        "hand_close_button": [200, 154],
        "hand_cancel": [px0 + 28, py0 + 100],
        "hand_shadow_offset": [0, 24],
        "hand_flip_on_buttons": False,
        "mon_sprite": [216, 64],
        "mon_text": [
            {"field": "nickname", "x": 180, "y": 104, "font": "normal", "color": white, "shadow": white_shadow},
            {"field": "gender", "x": 244, "y": 104, "color": white, "shadow": white_shadow,
             "male": {"color": [148, 230, 238], "shadow": [41, 123, 164]},
             "female": {"color": [246, 131, 131], "shadow": [197, 41, 41]}},
            {"field": "level", "x": 196, "y": 122, "prefix": "Lv", "prefix_x": 180, "font": "normal", "color": white, "shadow": white_shadow},
        ],
        "party": {"x": px0, "y": py0, "grid": True,
                  # the slots' centres in the panel: (24, 20/52/84) and (64, 28/60/92)
                  "slots": [[px0 + 24 - 16, py0 + 20 - 20], [px0 + 64 - 16, py0 + 28 - 20], [px0 + 24 - 16, py0 + 52 - 20],
                            [px0 + 64 - 16, py0 + 60 - 20], [px0 + 24 - 16, py0 + 84 - 20], [px0 + 64 - 16, py0 + 92 - 20]],
                  "cancel": [px0 + 10, py0 + 115, 68, 26]},
    }
    _finish(target, e_skin, platinum_repo, pc)
    return True


def build(decomp, out):
    """Builds pt (and dp) under out; returns the skins made."""
    made = []
    platinum = os.path.join(decomp, "pokeplatinum")
    e_skin = os.path.join(out, "e")
    for game, repo, loader, wallpapers in (("pt", platinum, _files_pt, 32),
                                           ("dp", os.path.join(decomp, "pokediamond"), _files_dp, 24)):
        fs = loader(repo) if os.path.isdir(repo) else None
        if not fs or not os.path.isdir(platinum):
            print(f"pc_ui: no {game}")
            continue
        build_skin(fs, os.path.join(out, game), wallpapers, e_skin, platinum)
        made.append(game)
        print(f"pc_ui: {game}")
    if os.path.isdir(platinum) and build_hgss(os.path.join(decomp, "pokeheartgold"), os.path.join(out, "hgss"), e_skin, platinum):
        made.append("hgss")
        print("pc_ui: hgss")
    else:
        print("pc_ui: no hgss")
    return made
