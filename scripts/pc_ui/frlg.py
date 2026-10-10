"""Pokémon FireRed / LeafGreen PC (box) + summary screen UI assets, from pokefirered.

    build(decomp_root, out_dir)

decomp_root: a full pret/pokefirered checkout. Uses only Python 3 + Pillow; all helpers
live in this module (no shared gba.py). Every position is in GBA pixels and is cited from
the C sources in the comments:
  src/pokemon_storage_system_tasks.c / _data.c / _graphics.c / _menu.c   (PC)
  src/pokemon_summary_screen.c, src/list_menu.c                         (summary)
  src/text.c, src/text_printer.c, src/new_menu_helpers.c (gFontInfos), src/menu.c,
  src/text_window.c / text_window_graphics.c                             (text + frames)

FireRed and LeafGreen share every graphic used here (the only FIRERED/LEAFGREEN #ifdefs
in src/graphics.c are title-screen assets), so layout.json carries an empty
"leafgreen_overrides".
"""

import json
import os
import re
import sys

from PIL import Image


# ===========================================================================
# Low level helpers (tiles, palettes, tilemaps)
# ===========================================================================

def _q(v):
    c = (int(v) >> 3) & 31
    return (c << 3) | (c >> 2)


def gba_rgb(r, g, b):
    """Quantise an 8-bit colour to 5-bit GBA channels and expand back."""
    return (_q(r), _q(g), _q(b))


def rgb15(v):
    """RGB(r5, g5, b5) as in the C source -> 8-bit tuple."""
    r, g, b = v
    return ((r << 3) | (r >> 2), (g << 3) | (g >> 2), (b << 3) | (b >> 2))


def read_jasc(path):
    with open(path) as f:
        lines = [ln.strip() for ln in f if ln.strip()]
    n = int(lines[2])
    return [gba_rgb(*[int(v) for v in ln.split()[:3]]) for ln in lines[3:3 + n]]


def png_palette(path):
    """What gbagfx emits as .gbapal for an indexed PNG."""
    pal = Image.open(path).getpalette() or []
    return [gba_rgb(*pal[i:i + 3]) for i in range(0, len(pal) - 2, 3)]


def pal16(cols):
    cols = list(cols[:16])
    return cols + [(0, 0, 0)] * (16 - len(cols))


def png_indices(path, bpp=4):
    im = Image.open(path)
    w, h = im.size
    mask = (1 << bpp) - 1
    if im.mode == "P":
        data = [v & mask for v in im.tobytes()]
    elif im.mode in ("L", "I", "I;16"):
        raw = list(im.convert("L").tobytes())
        data = [(v // 17) & mask for v in raw]
    else:
        raise ValueError("unsupported PNG mode %s for %s" % (im.mode, path))
    return w, h, data


def load_tiles(path, bpp=4):
    """8x8 tiles in gbagfx order (row-major)."""
    w, h, data = png_indices(path, bpp)
    tiles = []
    for ty in range(h // 8):
        for tx in range(w // 8):
            t = []
            for y in range(8):
                r = (ty * 8 + y) * w + tx * 8
                t.extend(data[r:r + 8])
            tiles.append(t)
    return tiles


def read_tilemap(path):
    with open(path, "rb") as f:
        b = f.read()
    return [b[i] | (b[i + 1] << 8) for i in range(0, len(b) - 1, 2)]


def new_img(w, h, fill=(0, 0, 0, 0)):
    return Image.new("RGBA", (w, h), fill)


def draw_tile(img, tile, x, y, pal, hf=False, vf=False):
    px = img.load()
    W, H = img.size
    for j in range(8):
        dy = y + j
        if not 0 <= dy < H:
            continue
        sy = 7 - j if vf else j
        for i in range(8):
            dx = x + i
            if not 0 <= dx < W:
                continue
            c = tile[sy * 8 + (7 - i if hf else i)]
            if c:
                px[dx, dy] = tuple(pal[c]) + (255,)


def draw_map(img, entries, map_w, tiles, pals, dest_x=0, dest_y=0, src_x=0, src_y=0,
             w=None, h=None, tile_base=0):
    """Draw a rect of a text-BG tilemap (dest/src in tiles). Tile T -> tiles[T - tile_base];
    palette = entry bits 12-15 into pals (BG palette RAM, list of 16 palettes)."""
    map_h = len(entries) // map_w
    w = map_w - src_x if w is None else w
    h = map_h - src_y if h is None else h
    for j in range(h):
        for i in range(w):
            e = entries[(src_y + j) * map_w + src_x + i]
            t = (e & 0x3FF) - tile_base
            p = pals[(e >> 12) & 15]
            if 0 <= t < len(tiles) and p is not None:
                draw_tile(img, tiles[t], (dest_x + i) * 8, (dest_y + j) * 8, p,
                          bool(e & 0x400), bool(e & 0x800))


def put_rect(dst, dst_w, src, src_w, dx, dy, sx, sy, w, h):
    """CopyRectToBgTilemapBufferRect-like copy between tilemap lists."""
    for j in range(h):
        for i in range(w):
            dst[(dy + j) * dst_w + dx + i] = src[(sy + j) * src_w + sx + i]


def sheet_frame(path, pal, x, y, w, h):
    """Crop a w x h sprite frame at (x, y) from a sprite sheet PNG with a palette."""
    W, H, data = png_indices(path)
    img = new_img(w, h)
    px = img.load()
    for j in range(h):
        for i in range(w):
            c = data[(y + j) * W + x + i]
            if c:
                px[i, j] = tuple(pal[c]) + (255,)
    return img


def frame_1d(path, pal, first_tile, w_tiles, h_tiles):
    """1D-mapped OBJ frame: w_tiles*h_tiles consecutive tiles starting at first_tile."""
    tiles = load_tiles(path)
    img = new_img(w_tiles * 8, h_tiles * 8)
    n = first_tile
    for ty in range(h_tiles):
        for tx in range(w_tiles):
            draw_tile(img, tiles[n], tx * 8, ty * 8, pal)
            n += 1
    return img


def blit_bitmap(img, path, pal, tile_offset, w, h, x, y, src_w=128):
    """BlitBitmapRectToWindow from a 4bpp tile bitmap (src_w px wide) starting tile_offset
    tiles in; colour 0 is transparent (as in BlitBitmapRect4Bit's colour key)."""
    W, H, data = png_indices(path)
    px = img.load()
    tpr = src_w // 8
    ox, oy = (tile_offset % tpr) * 8, (tile_offset // tpr) * 8
    for j in range(h):
        for i in range(w):
            sx, sy = ox + i, oy + j
            if sy >= H:
                continue
            c = data[sy * W + sx]
            if c and 0 <= x + i < img.width and 0 <= y + j < img.height:
                px[x + i, y + j] = tuple(pal[c]) + (255,)


# ===========================================================================
# Text (src/text.c; fonts from graphics/fonts, widths from src/text.c)
# ===========================================================================

class Charmap:
    def __init__(self, root):
        self.chars, self.names = {}, {}
        rc = re.compile(r"^'(.+)'\s*=\s*((?:[0-9A-Fa-f]{2}\s*)+)")
        rn = re.compile(r"^([A-Z_0-9]+)\s*=\s*((?:[0-9A-Fa-f]{2}\s*)+)")
        with open(os.path.join(root, "charmap.txt"), encoding="utf-8") as f:
            for ln in f:
                ln = ln.split("@")[0].rstrip()
                m = rc.match(ln)
                if m:
                    ch = m.group(1)
                    if ch == "\\'":
                        ch = "'"
                    self.chars.setdefault(ch, [int(x, 16) for x in m.group(2).split()])
                    continue
                m = rn.match(ln)
                if m:
                    self.names.setdefault(m.group(1), [int(x, 16) for x in m.group(2).split()])

    def encode(self, s):
        out, i = [], 0
        while i < len(s):
            if s[i] == "{":
                j = s.index("}", i)
                out += self.names[s[i + 1:j]]
                i = j + 1
            elif s[i] == "\n":
                out.append(0xFE)
                i += 1
            else:
                out += self.chars[s[i]]
                i += 1
        return out


# sKeypadIcons (src/text.c): {tileOffset, width, height}, index = F8 argument
KEYPAD_ICONS = {0x00: (0x0, 8, 12), 0x01: (0x1, 8, 12), 0x02: (0x2, 16, 12), 0x03: (0x4, 16, 12),
                0x04: (0x6, 24, 12), 0x05: (0x9, 24, 12), 0x06: (0xC, 8, 12), 0x07: (0xD, 8, 12),
                0x08: (0xE, 8, 12), 0x09: (0xF, 8, 12), 0x0A: (0x20, 8, 12), 0x0B: (0x21, 8, 12),
                0x0C: (0x22, 8, 12)}

# gFontInfos (src/new_menu_helpers.c): maxLetterHeight is the line advance on '\n'
# (RenderText: currentY += maxLetterHeight + lineSpacing). Letter spacing only applies to
# Japanese glyphs and keypad icons (RenderText / GetStringWidth), so English advance = width.
FONTS = {
    #           png               widths array                        cell w, line h
    "small":  ("latin_small.png", "sFontSmallLatinGlyphWidths", 8, 13),
    "copy1":  ("latin_normal.png", "sFontNormalCopy1LatinGlyphWidths", 16, 14),
    "normal": ("latin_normal.png", "sFontNormalLatinGlyphWidths", 16, 14),
}


class Font:
    def __init__(self, root, kind, charmap):
        png, warr, cell, lh = FONTS[kind]
        self.kind, self.cell, self.line_h, self.cm = kind, cell, lh, charmap
        self.w, _, self.data = png_indices(os.path.join(root, "graphics", "fonts", png), 2)
        src = open(os.path.join(root, "src", "text.c")).read()
        m = re.search(r"%s\[\]\s*=\s*\{(.*?)\};" % warr, src, re.S)
        self.widths = [int(v) for v in re.findall(r"\d+", m.group(1))]
        self.keypad = os.path.join(root, "graphics", "fonts", "keypad_icons.png")

    def _tokens(self, s):
        codes = self.cm.encode(s) if isinstance(s, str) else list(s)
        i = 0
        while i < len(codes):
            c = codes[i]
            if c == 0xFF:
                return
            if c == 0xF9:      # CHAR_EXTRA_SYMBOL -> glyph 0x100 | arg
                yield ("g", 0x100 | codes[i + 1])
                i += 2
            elif c == 0xF8:    # CHAR_KEYPAD_ICON
                yield ("k", codes[i + 1])
                i += 2
            elif c == 0xFE:
                yield ("n", 0)
                i += 1
            else:
                yield ("g", c)
                i += 1

    def width(self, s):
        best = cur = 0
        for k, g in self._tokens(s):
            if k == "n":
                best, cur = max(best, cur), 0
            elif k == "k":
                cur += KEYPAD_ICONS[g][1]
            else:
                cur += self.widths[g]
        return max(best, cur)

    def glyph_xy(self, g):
        per_row = self.w // self.cell
        return (g % per_row) * self.cell, (g // per_row) * 16

    def draw(self, img, s, x, y, fg, shadow, pal=None):
        """Draw like the text printer with its top-left at (x, y). Glyph value 1 = fg,
        2 = shadow (GenerateFontHalfRowLookupTable colors[] = {bg, fg, shadow}); bg is left
        alone (callers fill the window). Keypad icons are blitted with raw indices of the
        window palette `pal`."""
        px = img.load()
        cx, cy = x, y
        for k, g in self._tokens(s):
            if k == "n":
                cx, cy = x, cy + self.line_h
                continue
            if k == "k":
                off, w, h = KEYPAD_ICONS[g]
                if pal is not None:
                    blit_bitmap(img, self.keypad, pal, off, w, h, cx, cy)
                cx += w
                continue
            gw = self.widths[g]
            gx, gy = self.glyph_xy(g)
            for j in range(16):
                for i in range(gw):
                    v = self.data[(gy + j) * self.w + gx + i]
                    col = fg if v == 1 else shadow if v == 2 else None
                    if col is not None and 0 <= cx + i < img.width and 0 <= cy + j < img.height:
                        px[cx + i, cy + j] = tuple(col[:3]) + (255,)
            cx += gw
        return cx

    def cap_height(self):
        g = self.cm.chars["A"][0]
        gx, gy = self.glyph_xy(g)
        rows = [j for j in range(16)
                if any(self.data[(gy + j) * self.w + gx + i] == 1 for i in range(self.cell))]
        return max(rows) - min(rows) + 1


# ===========================================================================
# Constants
# ===========================================================================

def _p(root, *parts):
    return os.path.join(root, *parts)


def _l(c):
    return [int(c[0]), int(c[1]), int(c[2])]


# sMenuInfoIcons (src/list_menu.c): type -> tile offset into graphics/interface/menu_info.png
# (32x12 each, blitted with BG palette 6 = pokemon_types.pal on the summary screen).
TYPE_ICONS = [("normal", 0x20), ("fighting", 0x64), ("flying", 0x60), ("poison", 0x80),
              ("ground", 0x48), ("rock", 0x44), ("bug", 0x6C), ("ghost", 0x68),
              ("steel", 0x88), ("mystery", 0xA4), ("fire", 0x24), ("water", 0x28),
              ("grass", 0x2C), ("electric", 0x40), ("psychic", 0x84), ("ice", 0x4C),
              ("dragon", 0xA0), ("dark", 0x8C)]
MENU_INFO_LABELS = [("type", 0xA8), ("power", 0xC0), ("accuracy", 0xC8), ("pp", 0xE0),
                    ("effect", 0xE8)]
BALLS = ["poke", "great", "ultra", "master", "safari", "net", "dive", "nest", "repeat",
         "timer", "luxury", "premier"]
# sStatusAilmentIconAnimTable: 32x8 frames at tiles 0,4,..,28
STATUS = ["poison", "paralysis", "sleep", "freeze", "burn", "pokerus", "fainted"]


# ===========================================================================
# PC
# ===========================================================================

def _pc_pals(root):
    """BG palette RAM on the PC screen:
      InitPalettesAndSprites: 0 = interface.pal, 2 = interface_no_display_mon.pal,
        15 = item_info_frame (png palette), 3 = scrolling_bg (png palette)
      InitSupplementalTilemaps: 1 = party_menu.pal
      InitPokeStorageBg0: LoadStdWindowGfx(1, 2, BG_PLTT_ID(13)) -> 13 = stdpal_3
        (GetTextWindowPalette(3))
      Task_InitPokeStorage state 2: LoadUserWindowGfx(1, 0xB, BG_PLTT_ID(14)) -> 14 = user
        frame palette (frame type 0 = graphics/text_window/type1)."""
    g = _p(root, "graphics", "pokemon_storage")
    tw = _p(root, "graphics", "text_window")
    pals = [None] * 16
    pals[0] = pal16(read_jasc(_p(g, "interface.pal")))
    pals[1] = pal16(read_jasc(_p(g, "party_menu.pal")))
    pals[2] = pal16(read_jasc(_p(g, "interface_no_display_mon.pal")))
    pals[3] = pal16(png_palette(_p(g, "scrolling_bg.png")))
    pals[13] = pal16(read_jasc(_p(tw, "stdpal_3.pal")))
    pals[14] = pal16(png_palette(_p(tw, "type1.png")))
    pals[15] = pal16(png_palette(_p(g, "item_info_frame.png")))
    return pals


def _frame_img(tiles_path, pal, fill):
    """3x3 frame tiles 0..8 (tile 4 = interior, replaced by the window fill colour)."""
    tiles = load_tiles(tiles_path)
    img = new_img(24, 24)
    for k in range(9):
        if k == 4:
            img.paste(Image.new("RGBA", (8, 8), tuple(fill) + (255,)), (8, 8))
        else:
            draw_tile(img, tiles[k], (k % 3) * 8, (k // 3) * 8, pal)
    return img


def _build_pc(root, out, fonts, layout):
    g = _p(root, "graphics", "pokemon_storage")
    pals = _pc_pals(root)
    # BG1: charBaseIndex 1, baseTile 0x100 (sBgTemplates); gPokeStorageMenu_Gfx is loaded by
    # DecompressAndLoadBgGfxUsingHeap(1, ..., 0) -> menu.png tile 0 = map tile 0x100.
    menu_tiles = load_tiles(_p(g, "menu.png"))
    BASE = 0x100

    # ---- pc_bg.png --------------------------------------------------------------
    img = new_img(240, 160, tuple(pals[0][0]) + (255,))   # backdrop = BG palette 0 colour 0
    # BG3 (SetScrollingBackground): scrolling_bg tiles at char base 3, map at screen 31,
    # palette 3; scroll 0.
    draw_map(img, read_tilemap(_p(g, "scrolling_bg.bin")), 32, load_tiles(_p(g, "scrolling_bg.png")),
             pals, w=30, h=20)
    # BG1 interface: LoadPokeStorageMenuGfx sets menu.bin (32x20) as BG1's buffer, then the
    # TilemapUtil pieces: PKMN DATA (8x4 at (1,0), ResetForPokeStorage; rows 0-1 = shown
    # while a Pokemon is displayed, UpdateWaveformAnimation), party menu closed = rows 20-21
    # of party_menu.bin (12x22) at (10,0) (InitSupplementalTilemaps) and the CLOSE BOX
    # button rows 0-1 of close_box_button.bin (9x4) at (21,0) (UpdateCloseBoxButtonTilemap(TRUE)).
    bg1 = read_tilemap(_p(g, "menu.bin"))
    pkmn_data = read_tilemap(_p(g, "pkmn_data.bin"))
    party_map = read_tilemap(_p(g, "party_menu.bin"))
    close_map = read_tilemap(_p(g, "close_box_button.bin"))
    put_rect(bg1, 32, pkmn_data, 8, 1, 0, 0, 0, 8, 2)
    put_rect(bg1, 32, party_map, 12, 10, 0, 0, 20, 12, 2)
    put_rect(bg1, 32, close_map, 9, 21, 0, 0, 0, 9, 2)
    draw_map(img, bg1, 32, menu_tiles, pals, w=30, h=20, tile_base=BASE)
    # Window 0 (sWindowTemplates[0]: bg 1, (0,11) 9x7, palette 3) is cleared with
    # PIXEL_FILL(1) by PrintDisplayMonInfo -> opaque palette-3 colour 1 behind the mon text.
    info_fill = tuple(pals[3][1]) + (255,)
    img.paste(Image.new("RGBA", (72, 56), info_fill), (0, 88))
    # Waveform sprites (CreateWaveformSprites: 16x8 centred (8 + 63*i, 9), PALTAG_MISC_2 =
    # misc2.pal). Still frame of the "on" anims: left tile 2, right tile 10.
    misc2 = pal16(read_jasc(_p(g, "misc2.pal")))
    wave = _p(g, "waveform.png")
    img.alpha_composite(frame_1d(wave, misc2, 2, 2, 1), (0, 5))
    img.alpha_composite(frame_1d(wave, misc2, 10, 2, 1), (63, 5))
    img.save(_p(out, "pc_bg.png"))
    # Waveform animation frames: row 0 left (2,4,6), row 1 right (10,4,12); 8 frames each.
    strip = new_img(48, 16)
    for k, (lt, rt) in enumerate(((2, 10), (4, 4), (6, 12))):
        strip.alpha_composite(frame_1d(wave, misc2, lt, 2, 1), (k * 16, 0))
        strip.alpha_composite(frame_1d(wave, misc2, rt, 2, 1), (k * 16, 8))
    strip.save(_p(out, "pc_waveform.png"))

    # ---- party panel --------------------------------------------------------------
    # Open party menu = rows 0-19 of the 12x22 map at tile (10,0); SetPartySlotTilemap puts a
    # 4x3 filled/empty piece at (7, 3*(pos-1)+1) for pos 1..5 (slot 0 is always "filled").
    filled = read_tilemap(_p(g, "party_slot_filled.bin"))
    empty = read_tilemap(_p(g, "party_slot_empty.bin"))
    pm = list(party_map)
    for pos in range(1, 6):
        put_rect(pm, 12, filled, 4, 7, 3 * (pos - 1) + 1, 0, 0, 4, 3)
    party = new_img(96, 160)
    draw_map(party, pm, 12, menu_tiles, pals, w=12, h=20, tile_base=BASE)
    party.save(_p(out, "pc_party.png"))
    for name, data in (("pc_party_slot_filled.png", filled), ("pc_party_slot_empty.png", empty)):
        im = new_img(32, 24)
        draw_map(im, data, 4, menu_tiles, pals, tile_base=BASE)
        im.save(_p(out, name))
    # CLOSE BOX button flash frame (rows 2-3, UpdateCloseBoxButtonTilemap(FALSE))
    im = new_img(72, 16)
    draw_map(im, close_map, 9, menu_tiles, pals, src_y=2, w=9, h=2, tile_base=BASE)
    im.save(_p(out, "pc_close_button_alt.png"))

    # ---- hand cursor ---------------------------------------------------------------
    # CreateCursorSprites: 32x32 OBJ, sheet cursor.png; anims BOUNCE (tiles 0/16), STILL (0),
    # OPEN (32), FIST (48, used while carrying a mon). paletteTag = PALTAG_MISC_2 = misc2.pal
    # (white/grey hand); PALTAG_MISC_1 = misc1.pal (yellow) only in multi-move mode
    # (cursorPalNums[sInMultiMoveMode]).
    cur = _p(g, "cursor.png")
    hand = new_img(64, 32)
    hand.alpha_composite(sheet_frame(cur, misc2, 0, 0, 32, 32), (0, 0))
    hand.alpha_composite(sheet_frame(cur, misc2, 0, 96, 32, 32), (32, 0))
    hand.save(_p(out, "hand.png"))
    allf = new_img(128, 32)
    for k in range(4):
        allf.alpha_composite(sheet_frame(cur, misc2, 0, 32 * k, 32, 32), (32 * k, 0))
    allf.save(_p(out, "hand_frames.png"))
    misc1 = pal16(read_jasc(_p(g, "misc1.pal")))
    hm = new_img(64, 32)
    hm.alpha_composite(sheet_frame(cur, misc1, 0, 0, 32, 32), (0, 0))
    hm.alpha_composite(sheet_frame(cur, misc1, 0, 96, 32, 32), (32, 0))
    hm.save(_p(out, "hand_multi.png"))
    sheet_frame(_p(g, "cursor_shadow.png"), misc2, 0, 0, 16, 16).save(_p(out, "hand_shadow.png"))

    # ---- box scroll arrows (sSpriteTemplate_BoxScrollArrow: 8x16, PALTAG_MISC_2,
    #      anim 0 = tile 0 (left), anim 1 = tile 2 (right)) ----
    arr = _p(g, "box_scroll_arrow.png")
    frame_1d(arr, misc2, 0, 1, 2).save(_p(out, "arrow_left.png"))
    frame_1d(arr, misc2, 2, 1, 2).save(_p(out, "arrow_right.png"))

    # ---- window frames ---------------------------------------------------------------
    # Action menu (AddMenu): DrawStdFrameWithCustomTileAndPalette(win, FALSE, 11, 14) = the
    # player's user frame (LoadUserWindowGfx(1, 0xB, BG_PLTT_ID(14)); default frame type 0 =
    # text_window/type1.png + its palette); window palette 15 (item_info_frame) filled with
    # PIXEL_FILL(1).
    tw = _p(root, "graphics", "text_window")
    menu_fill = pals[15][1]
    _frame_img(_p(tw, "type1.png"), pals[14], menu_fill).save(_p(out, "window_frame.png"))
    os.makedirs(_p(out, "window_frames"), exist_ok=True)
    for n in range(1, 11):
        f = _p(tw, "type%d.png" % n)
        if os.path.exists(f):
            _frame_img(f, pal16(png_palette(f)), menu_fill).save(_p(out, "window_frames", "type%d.png" % n))
    # Message box (PrintStorageMessage): DrawTextBorderOuter(1, 2, 13) = std.png frame
    # (LoadStdWindowGfx -> gStdTextWindow_Gfx, palette stdpal_3 in slot 13), window 1 has
    # palette 13 and PIXEL_FILL(1).
    msg_fill = pals[13][1]
    _frame_img(_p(tw, "std.png"), pals[13], msg_fill).save(_p(out, "window_frame_message.png"))

    # Menu cursor: Menu_InitCursor(win, FONT_NORMAL_COPY_1, 0, 2, 16, ...) prints
    # gText_SelectorArrow with the default colours (fg 2, shadow 3) of window palette 15.
    copy1 = fonts["copy1"]
    cimg = new_img(copy1.width("▶"), 16)
    copy1.draw(cimg, "▶", 0, 0, pals[15][2], pals[15][3])
    cimg.save(_p(out, "cursor_menu.png"))

    # ---- markings (graphics/misc/mon_markings.png, 16 combos of 32x8, own palette;
    #      CreateMonMarkingComboSprite) ----
    mk = _p(root, "graphics", "misc", "mon_markings.png")
    mk_pal = pal16(png_palette(mk))
    strip = new_img(32, 128)
    for c in range(16):
        strip.alpha_composite(frame_1d(mk, mk_pal, c * 4, 4, 1), (0, c * 8))
    strip.save(_p(out, "markings_pc.png"))

    # ---- layout -------------------------------------------------------------------
    p3 = pals[3]
    win0 = (0, 88)  # sWindowTemplates[0]: tilemapLeft 0, tilemapTop 11
    party_slots = [[104 - 16, 64 - 16]] + [[152 - 16, 24 * (i - 1) + 16 - 16] for i in range(1, 6)]
    layout["pc"] = {
        # DrawWallpaper: 20x18 wallpaper tilemap at tile x = bg2_X/8 + 10 = 10, y = 2 on BG2,
        # BG2HOFS = bg2_X = 0 at rest -> 160x144 image at (80, 16).
        "wallpaper": [80, 16],
        "wallpaper_size": [160, 144],
        # InitBoxTitle: two 32x16 OBJs at x = GetBoxTitleBaseX + 32*i, y = 28 (centres);
        # GetBoxTitleBaseX = 240 - 64 - width/2 (FONT_NORMAL_COPY_1). Text is printed at
        # (0, 2) of that 64x16 buffer (DrawTextWindowAndBufferTiles) -> left = 160 - width/2,
        # top = 22. Colours: sBoxTitleColors = shadow RGB(7,7,7), text RGB_WHITE (all walls).
        "box_name": [160, 22 + 7],
        "box_name_text": {"left": "160 - width/2", "top": 22, "font": "copy1",
                          "color": _l(rgb15((31, 31, 31))), "shadow": _l(rgb15((7, 7, 7)))},
        # CreateBoxScrollArrows: 8x16 centred (92 + 136*i, 28)
        "arrows": {"left": [88, 20], "right": [224, 20]},
        # InitBoxMonSprites: CreateMonIconSprite(.., 8*(3*col)+100, 8*(3*row)+44) = centre of
        # a 32x32 icon
        "grid": {"x": 84, "y": 28, "dx": 24, "dy": 24, "cols": 6, "rows": 5},
        "icon_size": [32, 32],
        # party button = party_menu rows 20-21 at tile (10,0); close = (21,0) 9x2
        "party_button": [80, 0, 96, 16],
        "close_button": [168, 0, 72, 16],
        # GetCursorCoordsByPos: box (100+24c, 32+24r) centre of the 32x32 hand -> top-left
        # = icon top-left + (0,-12); buttons (120 + 88*pos, 14) -> (104|192, -2)
        # (y = 8 instead of 14 while carrying a mon).
        "hand_offset": [0, -12],
        "hand_button_offset": [24, -2],
        "hand_button_offset_carrying": [24, -8],
        "hand_party_offset": [0, -12],          # party cursor (104,52)/(152,24(i-1)+4)
        "hand_party_cancel": [136, 116],        # party cursor (152,132) -> hand top-left
        "hand_box_title": [162 - 16, 12 - 16],  # CURSOR_AREA_BOX_TITLE (162,12)
        "hand_shadow_offset": [8, 28],          # SpriteCB_CursorShadow (x, y+20), 16x16; box only
        "hand_frames": {"point": 0, "grab": 1},
        # CreateDisplayMonSprite: 64x64 OBJ centred (40, 48)
        "mon_sprite": [40, 48],
        # PrintDisplayMonInfo: window 0 at (0,88), FillWindowPixelBuffer(PIXEL_FILL(1)),
        # default colours of palette 3 (fg 2, shadow 3). texts 0..2 FONT_NORMAL at
        # y = 0/14/28 (x 6, 6, 10), text 3 FONT_SMALL at y 44 (x 6).
        "mon_text": [
            {"field": "nickname", "x": win0[0] + 6, "y": win0[1] + 0, "font": "normal"},
            {"field": "species", "x": win0[0] + 6, "y": win0[1] + 14, "font": "normal", "prefix": "/"},
            {"field": "level", "x": win0[0] + 10, "y": win0[1] + 28, "font": "normal",
             "format": "<gender symbol or space> {LV_2}<level> (SetDisplayMonData)",
             "male": {"color": _l(p3[4]), "shadow": _l(p3[5])},
             "female": {"color": _l(p3[6]), "shadow": _l(p3[7])}},
            {"field": "item", "x": win0[0] + 6, "y": win0[1] + 44, "font": "small"},
        ],
        "mon_text_color": _l(p3[2]),
        "mon_text_shadow": _l(p3[3]),
        "mon_text_bg": _l(p3[1]),
        "mon_text_rect": [0, 88, 72, 56],
        "markings": [40 - 16, 150 - 4],   # CreateMarkingComboSprite: 32x8 centred (40,150)
        "party": {
            "x": 80, "y": 0,
            # CreatePartyMonsSprites: centres (104,64) and (152, 24*(i-1)+16)
            "slots": party_slots,
            # CANCEL button drawn in party_menu.bin rows 17-19, cols 6-11
            "cancel": [80 + 6 * 8, 17 * 8, 6 * 8, 3 * 8],
            "slot_rects": [[80 + 7 * 8, (3 * (p - 1) + 1) * 8, 32, 24] for p in range(1, 6)],
        },
        # AddMenu: width = longest item (chars) + 2 tiles, height = 2*items, tilemapLeft =
        # 29 - width, tilemapTop = 15 - height. PrintTextArray(.., FONT_NORMAL_COPY_1, 8, 2, 16)
        # and the cursor at (0, 2 + 16*i). Frame 8 px outside the window rect.
        "menu": {"right": 29 * 8, "bottom": 15 * 8, "line_height": 16, "text_x": 8,
                 "text_y": 2, "cursor_x": 0, "padding": 8, "font": "copy1",
                 "width_tiles": "max(len(item))+2", "height_tiles": "2*items",
                 "fill": _l(menu_fill), "color": _l(pals[15][2]), "shadow": _l(pals[15][3])},
        # sWindowTemplates[1]: bg 0, (11,17) 18x2; PrintStorageMessage text at (0,2),
        # FONT_NORMAL_COPY_1, palette 13 default colours; frame 8 px outside (std frame).
        "message": [11 * 8, 17 * 8, 18 * 8, 2 * 8],
        "message_text": [0, 2],
        "message_font": "copy1",
        "message_fill": _l(msg_fill),
        "message_color": _l(pals[13][2]),
        "message_shadow": _l(pals[13][3]),
        "message_frame": "window_frame_message.png",
        # sYesNoWindowTemplate (24,11) 5x4, user frame like the action menu
        "yes_no": [24 * 8, 11 * 8, 5 * 8, 4 * 8],
    }


# ===========================================================================
# Summary
# ===========================================================================

def _sum_pals(root):
    """PokeSum_HandleLoadBgGfx: bg.png palettes 0-4 (+ 0/1 again for a non-shiny mon;
    a shiny mon gets bg palettes 6/5 in slots 0/1 -> sum_*_shiny.png), 6 = pokemon_types.pal
    (ListMenuLoadStdPalAt(BG_PLTT_ID(6), 1)), 7 = text_header.pal, 8 = text_moves.pal."""
    s = _p(root, "graphics", "summary_screen")
    bgp = png_palette(_p(s, "bg.png"))
    bgpals = [pal16(bgp[i * 16:(i + 1) * 16]) for i in range(len(bgp) // 16)]
    pals = [None] * 16
    for i in range(5):
        pals[i] = bgpals[i]
    pals[6] = pal16(read_jasc(_p(root, "graphics", "interface", "pokemon_types.pal")))
    pals[7] = pal16(read_jasc(_p(s, "text_header.pal")))
    pals[8] = pal16(read_jasc(_p(s, "text_moves.pal")))
    return pals, bgpals


PAGE_PROGRESS_BASE = 345
# PokeSum_DrawPageProgressTiles: (tile offset, x) pairs for rows 0 and 1 of BG3, cols 13..18
PROGRESS = {
    "info": [(17, 33, 13), (16, 32, 14), (18, 34, 15), (20, 36, 16), (18, 34, 17), (21, 37, 18)],
    "skills": [(49, 65, 13), (1, 19, 14), (17, 33, 15), (16, 32, 16), (18, 34, 17), (21, 37, 18)],
    "moves": [(49, 65, 13), (1, 19, 14), (49, 65, 15), (1, 19, 16), (17, 33, 17), (48, 64, 18)],
}


def _build_summary(root, out, fonts, layout):
    s = _p(root, "graphics", "summary_screen")
    pals, bgpals = _sum_pals(root)
    tiles = load_tiles(_p(s, "bg.png"))  # BG1-3 share charBaseIndex 2, tile offset 0
    normal, small = fonts["normal"], fonts["small"]
    tm = {n: read_tilemap(_p(s, n + ".bin")) for n in
          ("page_info", "page_skills", "page_moves", "moves_info_page", "moves_page")}
    menu_info = _p(root, "graphics", "interface", "menu_info.png")

    # sLevelNickTextColors {bg, fg, shadow}: [0] on palette 6 (right/bottom panes),
    # [1] on palette 7 (header), [2] male / [3] female on palette 7.
    def col(pal, fgi, shi):
        return pals[pal][fgi], pals[pal][shi]

    C_BODY = col(6, 14, 10)
    C_HEAD = col(7, 1, 2)
    C_MALE = col(7, 9, 8)
    C_FEMALE = col(7, 5, 4)
    # sPrintMoveTextColors on palette 8: [0] normal, [1]..[3] PP getting low
    C_MOVE = [col(8, 7, 8), col(8, 1, 2), col(8, 3, 4), col(8, 5, 6)]

    def compose(page, page_pals):
        """Layer stack at rest (after the page flips, see PokeSum_InitBgCoordsBeforePageFlips /
        PokeSum_UpdateBgPriorityForPageFlip): BG3 = moves_info_page (normal view, CB2_SetUpPSS
        step 10) + page-progress tiles; then
          INFO:   BG1 page_skills (prio 2) under BG2 page_info (prio 1)
          SKILLS: BG2 page_info under BG1 page_skills (new page slides in on top)
          MOVES:  BG2 page_moves only (BG1 is slid out to HOFS -240)."""
        img = new_img(240, 160, tuple(page_pals[0][0]) + (255,))
        bg3 = list(tm["moves_info_page"])
        for t0, t1, x in PROGRESS[page]:
            bg3[0 * 32 + x] = PAGE_PROGRESS_BASE + t0
            bg3[1 * 32 + x] = PAGE_PROGRESS_BASE + t1
        draw_map(img, bg3, 32, tiles, page_pals, w=30, h=20)
        stack = {"info": ["page_skills", "page_info"], "skills": ["page_info", "page_skills"],
                 "moves": ["page_moves"]}[page]
        for n in stack:
            draw_map(img, tm[n], 32, tiles, page_pals, w=30, h=20)
        return img

    def header(img, title, controls):
        # PokeSum_PrintPageName: window (0,0), FONT_NORMAL at (4,1), colours [1]
        normal.draw(img, title, 0 + 4, 0 + 1, *C_HEAD)
        # PokeSum_PrintControlsString: window (19,0), FONT_SMALL at x = 0x54 - width, y 0
        small.draw(img, controls, 19 * 8 + 0x54 - small.width(controls), 0, *C_HEAD, pal=pals[7])

    # Exp/HP bars: OBJ 8x8 tiles; HP 9 tiles centred (172 + 8i, 36), EXP 11 tiles centred
    # (156 + 8i, 132) (CreateHpBarObjs / CreateExpBarObjs). Tiles: 0 = label-left (anim 9),
    # 1 = label-right (anim 10), fill tiles anim 0 (empty) .. 8 (full), last = anim 11 cap.
    hpex_pal = pal16(png_palette(_p(s, "exp_bar.png")))  # gSummaryScreen_HpExpBar_Pal
    hp_pals = [hpex_pal, pal16(read_jasc(_p(s, "hp_bar_yellow.pal"))),
               pal16(read_jasc(_p(s, "hp_bar_red.pal")))]

    def bar(path, pal, n_fill, level):
        t = load_tiles(path)
        im = new_img((n_fill + 3) * 8, 8)
        seq = [9, 10] + [level] * n_fill + [11]
        for i, a in enumerate(seq):
            draw_tile(im, t[a], i * 8, 0, pal)
        return im

    hp_png, exp_png = _p(s, "hp_bar.png"), _p(s, "exp_bar.png")
    bar(hp_png, hp_pals[0], 6, 0).save(_p(out, "hp_bar_empty.png"))
    for k, nm in enumerate(("green", "yellow", "red")):
        bar(hp_png, hp_pals[k], 6, 8).save(_p(out, "hp_bar_full_%s.png" % nm))
    bar(exp_png, hpex_pal, 8, 0).save(_p(out, "exp_bar_empty.png"))
    bar(exp_png, hpex_pal, 8, 8).save(_p(out, "exp_bar_full.png"))

    def fill_rows(path, pal):
        t = load_tiles(path)
        e, f = t[0], t[8]
        rows = sorted({k // 8 for k in range(64) if e[k] != f[k]})
        cols = [_l(pal[f[r * 8 + 3]]) for r in rows]
        return rows, cols

    hp_rows, hp_cols = fill_rows(hp_png, hp_pals[0])
    exp_rows, exp_cols = fill_rows(exp_png, hpex_pal)
    HP_X, HP_Y = 172 - 4, 36 - 4
    EXP_X, EXP_Y = 156 - 4, 132 - 4

    def page_images(page_pals, suffix):
        # ---------------- INFO ----------------
        img = compose("info", page_pals)
        header(img, "POKéMON INFO", "{DPAD_RIGHT}PAGE {A_BUTTON}CANCEL")  # gText_PokeSum_Controls_PageCancel
        img.save(_p(out, "sum_info%s.png" % suffix))
        # ---------------- SKILLS ----------------
        img = compose("skills", page_pals)
        header(img, "POKéMON SKILLS", "{DPAD_LEFTRIGHT}PAGE")
        # PokeSum_PrintExpPoints_NextLv: window SKILLS_4 (6,12), (26,7) / (26,20), colours [0]
        normal.draw(img, "EXP. POINTS", 6 * 8 + 26, 12 * 8 + 7, *C_BODY)
        normal.draw(img, "NEXT LV.", 6 * 8 + 26, 12 * 8 + 20, *C_BODY)
        # HP/EXP bar frames are static sprites (empty fill baked in; the viewer paints the fill)
        img.alpha_composite(bar(hp_png, hp_pals[0], 6, 0), (HP_X, HP_Y))
        img.alpha_composite(bar(exp_png, hpex_pal, 8, 0), (EXP_X, EXP_Y))
        img.save(_p(out, "sum_skills%s.png" % suffix))
        # ---------------- KNOWN MOVES ----------------
        img = compose("moves", page_pals)
        header(img, "KNOWN MOVES", "{DPAD_LEFT}PAGE {A_BUTTON}DETAIL")
        img.save(_p(out, "sum_moves%s.png" % suffix))

    page_images(pals, "")
    shiny = list(pals)
    shiny[0], shiny[1] = bgpals[6], bgpals[5]
    page_images(shiny, "_shiny")

    # ---- type icons (BlitMenuInfoIcon, 32x12, palette 6) and the label icons ----
    os.makedirs(_p(out, "types"), exist_ok=True)
    for name, off in TYPE_ICONS:
        im = new_img(32, 12)
        blit_bitmap(im, menu_info, pals[6], off, 32, 12, 0, 0)
        im.save(_p(out, "types", name + ".png"))
    os.makedirs(_p(out, "menu_info"), exist_ok=True)
    for name, off in MENU_INFO_LABELS:
        im = new_img(40, 12)
        blit_bitmap(im, menu_info, pals[6], off, 40, 12, 0, 0)
        im.save(_p(out, "menu_info", name + ".png"))

    # ---- balls (gBallSpriteTemplates, 16x16, frame 0) ----
    os.makedirs(_p(out, "balls"), exist_ok=True)
    for b in BALLS:
        f = _p(root, "graphics", "interface", "ball", b + ".png")
        if os.path.exists(f):
            sheet_frame(f, pal16(png_palette(f)), 0, 0, 16, 16).save(_p(out, "balls", b + ".png"))

    # ---- status icons (status_ailment_icons.png, 32x8, frame k = tiles 4k) ----
    os.makedirs(_p(out, "status"), exist_ok=True)
    sf = _p(s, "status_ailment_icons.png")
    sp = pal16(png_palette(sf))
    for k, nm in enumerate(STATUS):
        frame_1d(sf, sp, k * 4, 4, 1).save(_p(out, "status", nm + ".png"))

    # ---- shiny star (8x8, anim frame tile 1), pokerus-cured dot (8x8) ----
    f = _p(s, "shiny_star.png")
    frame_1d(f, pal16(png_palette(f)), 1, 1, 1).save(_p(out, "shiny_star.png"))
    f = _p(s, "pokerus_cured.png")
    frame_1d(f, pal16(png_palette(f)), 0, 1, 1).save(_p(out, "pokerus_cured.png"))

    # ---- markings on the summary (CreateMonMarkingAllCombosSprite with marking.pal) ----
    mk = _p(root, "graphics", "misc", "mon_markings.png")
    mpal = pal16(read_jasc(_p(s, "marking.pal")))
    strip = new_img(32, 128)
    for c in range(16):
        strip.alpha_composite(frame_1d(mk, mpal, c * 4, 4, 1), (0, c * 8))
    strip.save(_p(out, "markings_summary.png"))

    # ---- layout ------------------------------------------------------------------
    def c(pair):
        return {"color": _l(pair[0]), "shadow": _l(pair[1])}

    # Permanent windows (sWindowTemplates_Permanent_Bg1): LVL_NICK (0,2) -> (0,16).
    # PrintMonLevelNickOnWindow2: level "{LV_2}<n>" FONT_NORMAL at (4,2), nickname at (40,2),
    # gender at (105,2) (male [2] / female [3]).
    common = [
        dict(field="level", x=4, y=16 + 2, font="normal", prefix="{LV_2}", **c(C_HEAD)),
        dict(field="nickname", x=40, y=16 + 2, font="normal", **c(C_HEAD)),
        dict(field="gender", x=105, y=16 + 2, font="normal", male=c(C_MALE), female=c(C_FEMALE)),
        # PokeSum_CreateMonPicSprite: CreateMonPicSprite(.., 60, 65) 64x64 centre
        # CreateBallIconObj: 16x16 centred (106, 88)
        dict(field="ball", x=106 - 8, y=88 - 8, w=16, h=16),
        # PokeSum_CreateMonMarkingsSprite: 32x8 centred (20, 91)
        dict(field="markings", x=20 - 16, y=91 - 4, w=32, h=8),
        # ShowOrHideStatusIcon: 32x8 centred (16, 38)
        dict(field="status", x=16 - 16, y=38 - 4, w=32, h=8),
        # CreateShinyStarObj: 8x8 centred (106, 40); CreatePokerusIconObj: 8x8 at (114, 92)
        dict(field="shiny_star", x=106 - 4, y=40 - 4, w=8, h=8),
        dict(field="pokerus_cured", x=114 - 4, y=92 - 4, w=8, h=8),
    ]
    R_INFO = (15 * 8, 2 * 8)   # sWindowTemplates_Info[0]: (15,2) 15x12, palette 6
    info = common + [
        # PrintInfoPage (FONT_NORMAL, colours [0])
        dict(field="dex_number", x=R_INFO[0] + 47, y=R_INFO[1] + 5, digits=3, zero_pad=True, **c(C_BODY)),
        dict(field="species", x=R_INFO[0] + 47, y=R_INFO[1] + 19, **c(C_BODY)),
        # PokeSum_PrintMonTypeIcons: BlitMenuInfoIcon(RIGHT_PANE, type, 47|83, 35), 32x12
        dict(field="type1", x=R_INFO[0] + 47, y=R_INFO[1] + 35, w=32, h=12),
        dict(field="type2", x=R_INFO[0] + 83, y=R_INFO[1] + 35, w=32, h=12),
        dict(field="ot", x=R_INFO[0] + 47, y=R_INFO[1] + 49, **c(C_BODY)),
        dict(field="id", x=R_INFO[0] + 47, y=R_INFO[1] + 64, digits=5, zero_pad=True, **c(C_BODY)),
        dict(field="item", x=R_INFO[0] + 47, y=R_INFO[1] + 79, empty_text="NONE", **c(C_BODY)),
        # PokeSum_PrintTrainerMemo_*: window INFO_4 (1,14) 28x6, AddTextPrinterParameterized4
        # (.., FONT_NORMAL, 0, 3, letterSpacing 0, lineSpacing 0) -> line height 14
        dict(field="memo", x=8 + 0, y=14 * 8 + 3, width=28 * 8, line_height=14, **c(C_BODY)),
    ]
    R_SK = (20 * 8, 2 * 8)     # sWindowTemplates_Skills[0]: (20,2) 10x14
    # PrintSkillsPage: x = base + (63|27 - strlen*6): right-aligned assuming 6 px per char
    skills = common + [
        dict(field="hp", x=R_SK[0] + 14 + 63, y=R_SK[1] + 4, align="right", format="%d/%d",
             char_width=6, **c(C_BODY)),
        dict(field="attack", x=R_SK[0] + 50 + 27, y=R_SK[1] + 22, align="right", char_width=6, **c(C_BODY)),
        dict(field="defense", x=R_SK[0] + 50 + 27, y=R_SK[1] + 35, align="right", char_width=6, **c(C_BODY)),
        dict(field="sp_atk", x=R_SK[0] + 50 + 27, y=R_SK[1] + 48, align="right", char_width=6, **c(C_BODY)),
        dict(field="sp_def", x=R_SK[0] + 50 + 27, y=R_SK[1] + 61, align="right", char_width=6, **c(C_BODY)),
        dict(field="speed", x=R_SK[0] + 50 + 27, y=R_SK[1] + 74, align="right", char_width=6, **c(C_BODY)),
        dict(field="exp_points", x=R_SK[0] + 15 + 63, y=R_SK[1] + 87, align="right", char_width=6, **c(C_BODY)),
        dict(field="next_lv", x=R_SK[0] + 15 + 63, y=R_SK[1] + 100, align="right", char_width=6, **c(C_BODY)),
        # PokeSum_PrintAbilityNameAndDesc: window SKILLS_5 (1,16): name (66,1), desc (2,15)
        dict(field="ability", x=8 + 66, y=16 * 8 + 1, **c(C_BODY)),
        dict(field="ability_desc", x=8 + 2, y=16 * 8 + 15, width=29 * 8 - 2, line_height=14, **c(C_BODY)),
        # HP bar fill = tiles 2..7 of the 9-tile bar; EXP fill = tiles 2..9 of 11 (8 px each,
        # partial tile = anim 0..8 = 0..8 px)
        dict(field="hp_bar", rect=[HP_X + 16, HP_Y + hp_rows[0], 48, len(hp_rows)],
             colors={"green": hp_cols,
                     "yellow": fill_rows(hp_png, hp_pals[1])[1],
                     "red": fill_rows(hp_png, hp_pals[2])[1]},
             note="palette by GetHPBarLevel: green >1/2, yellow >1/5, red <=1/5"),
        dict(field="exp_bar", rect=[EXP_X + 16, EXP_Y + exp_rows[0], 64, len(exp_rows)], colors=exp_cols),
    ]
    R_MV = (20 * 8, 2 * 8)     # sWindowTemplates_Moves[0]: (20,2) 10x18, palette 8
    moves = common + [
        # PrintMovesPage / PokeSum_PrintMoveName: name (3, 28i+5); "{PP}" (36, 28i+16);
        # cur PP right-aligned 2 digits at 46 (+ 6*(2-len)); "/" at 58; max PP at 64 (+...).
        # Empty slot: name "-", cur "--" (x wraps to 45), no "/" or max.
        # PokeSum_DrawMoveTypeIcons: BlitMenuInfoIcon(window MOVES_5 (15,2), type, 3, 28i+5)
        dict(field="moves", row_dy=28,
             type={"x": 15 * 8 + 3, "y": 2 * 8 + 5, "w": 32, "h": 12},
             name=dict(x=R_MV[0] + 3, y=R_MV[1] + 5, empty="-", **c(C_MOVE[0])),
             pp_label=dict(x=R_MV[0] + 36, y=R_MV[1] + 16, text="{PP}"),
             pp=dict(cur_right=R_MV[0] + 46 + 12, slash_x=R_MV[0] + 58, max_right=R_MV[0] + 64 + 12,
                     y=R_MV[1] + 16, char_width=6,
                     states={"normal": c(C_MOVE[0]), "half": c(C_MOVE[1]),
                             "quarter": c(C_MOVE[2]), "empty": c(C_MOVE[3])},
                     empty_text="--", empty_x=R_MV[0] + 45)),
    ]
    layout["summary"] = {
        "sprite": [60, 65],   # PokeSum_CreateMonPicSprite
        "info": info,
        "skills": skills,
        "moves": moves,
        "text_font": "normal",
        "font_line_height": 14,
        "pp_state_note": "PokeSum_PrintMoveName: cur==max -> normal; cur==0 -> empty; "
                         "cur <= max/4 -> quarter; cur <= max/2 -> half (max 2/3 special-cased)",
        "header_note": "page name, controls and LVL/nickname row are drawn on BG1 (palette 7); "
                       "sum_*.png already contain page name + controls",
        "shiny_variant": "sum_*_shiny.png: BG palettes 0/1 replaced by bg.png palettes 6/5 "
                         "for a shiny Pokemon (PokeSum_HandleLoadBgGfx)",
    }


# ===========================================================================
# Entry point
# ===========================================================================

def build(decomp_root, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    cm = Charmap(decomp_root)
    fonts = {k: Font(decomp_root, k, cm) for k in FONTS}
    pcp = _pc_pals(decomp_root)
    layout = {
        "game": "frlg",
        # Standard window text: FONT_NORMAL_COPY_1 / FONT_NORMAL default colours fg 2, shadow 3
        # of the window palette (item_info_frame / stdpal_3 are identical: dark grey on
        # white with light grey shadow).
        "font": {"color": _l(pcp[15][2]), "shadow": _l(pcp[15][3]),
                 "height": fonts["normal"].cap_height(), "line_height": 14,
                 "fonts": {"normal": "FONT_NORMAL (latin_normal, sFontNormalLatinGlyphWidths)",
                           "copy1": "FONT_NORMAL_COPY_1 (latin_normal, sFontNormalCopy1LatinGlyphWidths)",
                           "small": "FONT_SMALL (latin_small, sFontSmallLatinGlyphWidths)"}},
        "leafgreen_overrides": {},
        "leafgreen_note": "FireRed and LeafGreen share all PC/summary graphics and palettes "
                          "(no FIRERED/LEAFGREEN ifdefs outside the title screen)",
    }
    _build_pc(decomp_root, out_dir, fonts, layout)
    _build_summary(decomp_root, out_dir, fonts, layout)
    with open(os.path.join(out_dir, "layout.json"), "w") as f:
        json.dump(layout, f, indent=1, ensure_ascii=False)


if __name__ == "__main__":
    build(sys.argv[1], sys.argv[2])
