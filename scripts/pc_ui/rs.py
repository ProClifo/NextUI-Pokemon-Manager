"""PC box + summary screen UI assets for Pokemon Ruby/Sapphire (pret/pokeruby).

build(decomp_root, out_dir) renders the static layers of the Pokemon Storage System
box screen and of the summary pages straight from the decomp's graphics, replaying the
VRAM / palette loads the C code performs, and writes layout.json with coordinates taken
from the source (functions cited inline; file names are relative to pokeruby/src).

Only Python 3 + Pillow. All helpers live in this module (no shared gba.py).

Run directly to build + write previews:
    python3 rs.py <pokeruby root> <out dir> [<preview dir>]
"""

import json
import os
import re
import struct
import sys

from PIL import Image

SCREEN_W, SCREEN_H = 240, 160


# --------------------------------------------------------------------------------------
# Low level GBA helpers
# --------------------------------------------------------------------------------------

def _rgb_to_gba(rgb):
    r, g, b = rgb[:3]
    return (r >> 3) | ((g >> 3) << 5) | ((b >> 3) << 10)


def _gba_to_rgb(c):
    r, g, b = c & 31, (c >> 5) & 31, (c >> 10) & 31
    return ((r << 3) | (r >> 2), (g << 3) | (g >> 2), (b << 3) | (b >> 2))


def _rt(rgb):
    """Round-trip an 8-bit colour through the GBA's 15-bit colour space."""
    return list(_gba_to_rgb(_rgb_to_gba(rgb)))


def read_pal(path):
    """Read a palette as a list of 15-bit GBA colours (.pal JASC, .bin raw, .png palette)."""
    if path.endswith('.bin') or path.endswith('.gbapal'):
        data = open(path, 'rb').read()
        return list(struct.unpack('<%dH' % (len(data) // 2), data))
    if path.endswith('.pal'):
        lines = open(path, 'r', encoding='latin-1').read().split()
        n = int(lines[2])
        vals = [int(v) for v in lines[3:3 + n * 3]]
        return [_rgb_to_gba(vals[i * 3:i * 3 + 3]) for i in range(n)]
    im = Image.open(path)
    p = im.getpalette() or []
    n = min(len(p) // 3, 16)
    return [_rgb_to_gba(p[i * 3:i * 3 + 3]) for i in range(n)]


def png_indices(path):
    """Return (w, h, indices) of a 4bpp source PNG (paletted or 4-bit greyscale)."""
    im = Image.open(path)
    w, h = im.size
    if im.mode == 'P':
        data = list(im.tobytes())
    elif im.mode in ('L', 'LA', 'I'):
        # gbagfx inverts greyscale PNGs (tools/gbagfx convert_png.c: no palette -> invert);
        # Pillow expands the 4-bit samples to 8 bit.
        data = [15 - v // 17 for v in im.convert('L').tobytes()]
    else:
        raise ValueError('unsupported PNG mode %s for %s' % (im.mode, path))
    return w, h, data


def png_to_4bpp(path, num_tiles=None):
    """gbagfx-style conversion: tiles in row-major order, low nibble = left pixel."""
    w, h, px = png_indices(path)
    out = bytearray()
    for ty in range(h // 8):
        for tx in range(w // 8):
            for y in range(8):
                row = (ty * 8 + y) * w + tx * 8
                for x in range(0, 8, 2):
                    out.append((px[row + x] & 15) | ((px[row + x + 1] & 15) << 4))
    if num_tiles is not None:
        out = out[:num_tiles * 32]
    return bytes(out)


def read_tilemap(path):
    data = open(path, 'rb').read()
    return list(struct.unpack('<%dH' % (len(data) // 2), data))


class Gba:
    """Minimal model of BG VRAM + palette RAM, enough to render text-mode BGs."""

    def __init__(self):
        self.vram = bytearray(0x10000)
        self.pal = [0] * 512  # 0..255 BG, 256..511 OBJ

    def load(self, addr, data):
        self.vram[addr:addr + len(data)] = data

    def load_pal(self, colors, offset, size_bytes):
        """LoadPalette(src, offset, size) with size in bytes."""
        n = size_bytes // 2
        for i in range(min(n, len(colors))):
            self.pal[offset + i] = colors[i]

    def get_map(self, addr, count):
        return list(struct.unpack_from('<%dH' % count, self.vram, addr))

    def put_map(self, addr, entries):
        struct.pack_into('<%dH' % len(entries), self.vram, addr, *entries)

    def put_entry(self, addr, e):
        struct.pack_into('<H', self.vram, addr, e)

    def tile_pixels(self, charbase, tile):
        base = charbase + tile * 32
        out = []
        for i in range(32):
            b = self.vram[(base + i) & 0xFFFF]
            out.append(b & 15)
            out.append(b >> 4)
        return out

    def render_bg(self, charbase, screenbase, size=0, hofs=0, vofs=0, w=SCREEN_W, h=SCREEN_H):
        """Render a 4bpp text BG (BGCNT char/screen base as byte addresses). Colour 0 transparent."""
        mw, mh = [(32, 32), (64, 32), (32, 64), (64, 64)][size]
        img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
        pix = img.load()
        cache = {}
        for sy in range(h):
            for sx in range(w):
                x = (sx + hofs) % (mw * 8)
                y = (sy + vofs) % (mh * 8)
                tx, ty = x // 8, y // 8
                block = 0
                if tx >= 32:
                    block += 1
                    tx -= 32
                if ty >= 32:
                    block += 2 if mw == 64 else 1
                    ty -= 32
                addr = screenbase + block * 0x800 + (ty * 32 + tx) * 2
                e = self.vram[addr] | (self.vram[addr + 1] << 8)
                tile, hf, vf, pn = e & 0x3FF, (e >> 10) & 1, (e >> 11) & 1, e >> 12
                if tile not in cache:
                    cache[tile] = self.tile_pixels(charbase, tile)
                tp = cache[tile]
                px, py = x & 7, y & 7
                if hf:
                    px = 7 - px
                if vf:
                    py = 7 - py
                c = tp[py * 8 + px]
                if c:
                    pix[sx, sy] = _gba_to_rgb(self.pal[pn * 16 + c]) + (255,)
        return img


def render_tilemap_piece(gba, charbase, entries, mw, left, top, width, height):
    """Render a rectangle (in tiles) of a tilemap held in a Python list with row stride mw."""
    img = Image.new('RGBA', (width * 8, height * 8), (0, 0, 0, 0))
    pix = img.load()
    for ty in range(height):
        for tx in range(width):
            e = entries[(top + ty) * mw + left + tx]
            tile, hf, vf, pn = e & 0x3FF, (e >> 10) & 1, (e >> 11) & 1, e >> 12
            tp = gba.tile_pixels(charbase, tile)
            for y in range(8):
                for x in range(8):
                    c = tp[(7 - y if vf else y) * 8 + (7 - x if hf else x)]
                    if c:
                        pix[tx * 8 + x, ty * 8 + y] = _gba_to_rgb(gba.pal[pn * 16 + c]) + (255,)
    return img


def render_sprite_png(path, pal):
    """Render a PNG (indices) with a GBA palette list; index 0 transparent."""
    w, h, px = png_indices(path)
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    p = img.load()
    for y in range(h):
        for x in range(w):
            c = px[y * w + x]
            if c:
                p[x, y] = _gba_to_rgb(pal[c]) + (255,)
    return img


def _png_tiles(path):
    w, h, px = png_indices(path)
    tiles = []
    for ty in range(h // 8):
        for tx in range(w // 8):
            t = []
            for y in range(8):
                t.extend(px[(ty * 8 + y) * w + tx * 8: (ty * 8 + y) * w + tx * 8 + 8])
            tiles.append(t)
    return tiles


def sheet_1d(path, pal, tiles_w, tiles_h, tile_index):
    """Extract a 1D-mapped OBJ frame (tiles_w x tiles_h tiles starting at tile_index)."""
    tiles = _png_tiles(path)
    img = Image.new('RGBA', (tiles_w * 8, tiles_h * 8), (0, 0, 0, 0))
    p = img.load()
    for i in range(tiles_w * tiles_h):
        t = tiles[tile_index + i]
        ox, oy = (i % tiles_w) * 8, (i // tiles_w) * 8
        for y in range(8):
            for x in range(8):
                c = t[y * 8 + x]
                if c:
                    p[ox + x, oy + y] = _gba_to_rgb(pal[c]) + (255,)
    return img


def _bbox_rect(img, region):
    """Opaque bounding box of img inside region (x, y, w, h) -> [x, y, w, h]."""
    x0, y0, w, h = region
    bb = img.crop((x0, y0, x0 + w, y0 + h)).getbbox()
    if not bb:
        return None
    return [x0 + bb[0], y0 + bb[1], bb[2] - bb[0], bb[3] - bb[1]]


# --------------------------------------------------------------------------------------
# Ruby/Sapphire text: font 3 (normal 8x16 shadowed) and font 4 (small), English rev 0
# --------------------------------------------------------------------------------------

def _c_array(src, name):
    m = re.search(name + r'\[\]\s*=\s*\{([^}]*)\}', src)
    return [int(v, 0) for v in re.findall(r'0x[0-9A-Fa-f]+|\d+', m.group(1))]


class Font:
    """text.c glyph renderer. Pixel value 1 = foreground, 2 = shadow, 0 = background."""

    def __init__(self, root, num=3):
        self.num = num
        self.charmap = {}
        for line in open(os.path.join(root, 'charmap.txt'), encoding='utf-8'):
            line = line.strip()
            m = re.match(r"^'(.+)'\s*=\s*([0-9A-Fa-f]{2})\s*$", line)
            if m and len(m.group(1)) == 1:
                self.charmap.setdefault(m.group(1), int(m.group(2), 16))
            m = re.match(r"^'\\''\s*=\s*([0-9A-Fa-f]{2})", line)
            if m:
                self.charmap["'"] = int(m.group(1), 16)
        self.charmap['{LV}'] = 0x34  # CHAR_LV
        if num == 3:
            src = open(os.path.join(root, 'src/data/text/font3_widths.h')).read()
            # "#if ENGLISH / #if REVISION >= 1 / ... / #else <rev 0> / #endif": rev 0 table
            # matches graphics/fonts/font3_lat.png (rev 1 uses font3_lat_rev1.png).
            self.widths = _c_array(src.split('#else', 1)[1], 'sFont3Widths')
            _, _, self.glyphs = png_indices(os.path.join(root, 'graphics/fonts/font3_lat.png'))
            self.gw = 128
        else:
            src = open(os.path.join(root, 'src/data/text/font4_widths.h')).read()
            self.raw_widths = _c_array(src.split('#else', 1)[1], 'sFont4Widths')
            self.map1 = _c_array(open(os.path.join(root, 'src/data/text/type1_map.h')).read(),
                                 'sFontType1Map')
            self.tiles = _png_tiles(os.path.join(root, 'graphics/fonts/font4_lat.png'))
            # GetGlyphWidth: font 4 -> sFont4Widths[sFontType1Map[2 * glyph + 1]]
            self.widths = [self.raw_widths[self.map1[2 * c + 1]] if self.map1[2 * c + 1] < len(self.raw_widths)
                           else 8 for c in range(len(self.map1) // 2)]

    def encode(self, s):
        out = []
        i = 0
        while i < len(s):
            if s[i] == '{':
                j = s.index('}', i)
                out.append(self.charmap[s[i:j + 1]])
                i = j + 1
            else:
                out.append(self.charmap[s[i]])
                i += 1
        return out

    def width(self, s):
        return sum(self.widths[c] for c in self.encode(s))

    def glyph_rows(self, c):
        """16 rows x 8 px of glyph indices."""
        if self.num == 3:
            gx, gy = (c % 16) * 8, (c // 16) * 16
            return [self.glyphs[(gy + yy) * self.gw + gx: (gy + yy) * self.gw + gx + 8] for yy in range(16)]
        # font type 1: upper/lower tiles via sFontType1Map (GetGlyphTilePointers case 1)
        up, lo = self.tiles[self.map1[2 * c]], self.tiles[self.map1[2 * c + 1]]
        return [up[y * 8:y * 8 + 8] for y in range(8)] + [lo[y * 8:y * 8 + 8] for y in range(8)]

    def draw(self, img, x, y, s, fg, shadow):
        """Draw text with its glyph-cell top-left at (x, y) (cells are 8x16)."""
        p = img.load()
        for c in self.encode(s):
            rows = self.glyph_rows(c)
            for yy in range(16):
                for xx in range(min(8, self.widths[c])):
                    v = rows[yy][xx]
                    px, py = x + xx, y + yy
                    if v in (1, 2) and 0 <= px < img.width and 0 <= py < img.height:
                        p[px, py] = tuple(fg if v == 1 else shadow) + (255,)
            x += self.widths[c]
        return x

    def cap_height(self):
        rows = self.glyph_rows(self.charmap['A'])
        ys = [yy for yy in range(16) if 1 in rows[yy]]
        return ys[-1] - ys[0] + 1, ys[0]


def _font_pal(root):
    return read_pal(_p(root, 'graphics', 'fonts', 'unknown_81E6692.pal'))


# --------------------------------------------------------------------------------------
# PC (Pokemon Storage System) screen
# --------------------------------------------------------------------------------------

def _p(root, *parts):
    return os.path.join(root, *parts)


def _num_tiles(root):
    """GFX_OPTS -num_tiles for the PSS graphics (misc.mk)."""
    counts = {}
    for m in re.finditer(r'\$\(PSSGFXDIR\)/(\w+)\.4bpp: GFX_OPTS := -num_tiles (\d+)',
                         open(_p(root, 'misc.mk')).read()):
        counts[m.group(1)] = int(m.group(2))
    return counts


def _setup_pc_vram(root):
    """Replays the PC init (pokemon_storage_system_2.c: sub_8096884 states 5-7)."""
    g = Gba()
    gfx = _p(root, 'graphics', 'pokemon_storage')
    nt = _num_tiles(root)
    font_pal = _font_pal(root)

    # sub_8097DE0: BG3 = scrolling background, BGCNT_CHARBASE(3) | SCREENBASE(30), PRIORITY(3);
    # tiles DMA'd to BG_SCREEN_ADDR(28) (= char block 3 + 0x2000 -> tile 0x100), map to
    # SCREENBASE(30), palette at 0xD0 (8 colours).  The map scrolls diagonally (sub_8097E44).
    g.load(0xE000, png_to_4bpp(_p(gfx, 'scrolling_bg.png')))
    g.put_map(0xF000, read_tilemap(_p(gfx, 'scrolling_bg_map.bin')))
    g.load_pal(read_pal(_p(gfx, 'scrolling_bg.png')), 0xD0, 0x10)

    # sub_8097E70: header gfx -> BG_SCREEN_ADDR(10) (0x5000), header tilemap into a buffer
    # whose (0,0) 10x20 block is copied to BG_SCREEN_ADDR(15) (BG1).  Palettes 0x00-0x1F,
    # 0xB0, 0xC0 and text colours in BG palette 15 (the text window palette).
    g.load(0x5000, png_to_4bpp(_p(gfx, 'header.png'), nt.get('header')))
    header_map = read_tilemap(_p(gfx, 'header.bin'))
    g.load_pal(read_pal(_p(gfx, 'menu1.pal')), 0x10, 0x20)
    g.load_pal(read_pal(_p(gfx, 'menu2.bin')), 0x00, 0x20)
    g.load_pal(read_pal(_p(gfx, '83B6D74.pal')), 0xB0, 0x20)
    g.load_pal(read_pal(_p(gfx, '83B6D94.pal')), 0xC0, 0x20)
    for src, dst, size in ((1, 0xF1, 2), (1, 0xF2, 2), (5, 0xF3, 2), (12, 0xF4, 4),
                           (10, 0xF6, 4), (2, 0xFF, 2)):
        g.load_pal(font_pal[src:], dst, size)

    # sub_8098400: misc gfx -> BG_SCREEN_ADDR(13) (0x6800), map buffer, palettes 0x20/0x30,
    # BG1CNT = PRIORITY(1) | SCREENBASE(15), char base 0.
    g.load(0x6800, png_to_4bpp(_p(gfx, 'misc1.png'), nt.get('misc1')))
    misc_map = read_tilemap(_p(gfx, 'misc1.bin'))
    g.load_pal(read_pal(_p(gfx, 'menu3.pal')), 0x20, 0x20)
    g.load_pal(read_pal(_p(gfx, 'menu4.pal')), 0x30, 0x20)
    return g, header_map, misc_map


def _copy_rect(dst, g, d_left, d_top, src, s_left, s_top, w, h):
    """sub_809D034 / sub_809D104: copy a w x h block between 32-wide tilemaps."""
    for y in range(h):
        row = src[(s_top + y) * 32 + s_left:(s_top + y) * 32 + s_left + w]
        if isinstance(dst, list):
            for x, e in enumerate(row):
                dst[(d_top + y) * 32 + d_left + x] = e
        else:
            g.put_map(dst + ((d_top + y) * 32 + d_left) * 2, row)


def _pc_text_colors(root):
    """BG palette 15 as the PC leaves it (sub_8097E70 over gFontDefaultPalette)."""
    pal = read_pal(_p(root, 'graphics', 'fonts', 'default.pal'))  # Text_LoadWindowTemplate
    fp = _font_pal(root)
    for src, dst, n in ((1, 1, 1), (1, 2, 1), (5, 3, 1), (12, 4, 2), (10, 6, 2), (2, 15, 1)):
        pal[dst:dst + n] = fp[src:src + n]
    return [list(_gba_to_rgb(c)) for c in pal]


def build_pc(root, out, font, font4):
    gfx = _p(root, 'graphics', 'pokemon_storage')
    g, header_map, misc_map = _setup_pc_vram(root)
    BG1 = 0x7800  # BG_SCREEN_ADDR(15)

    # sub_8098400 clears BG_SCREEN_ADDR(15); then header (0,0) 10x20 (sub_8097E70), and with
    # the party closed (gUnknown_0203847C == 0) misc rows 20-21 (PARTY POKeMON button) at
    # (10,0) 12x2, then sub_8098690(TRUE) copies misc (12,0) 9x2 (CLOSE BOX) to (21,0).
    g.put_map(BG1, [0] * 1024)
    _copy_rect(BG1, g, 0, 0, header_map, 0, 0, 10, 20)
    # sub_8098350 with a Pokemon displayed: header (1,0) 8x2 from (1,0) ("PKMN DATA" title;
    # with nothing displayed it copies (10,0) instead).
    _copy_rect(BG1, g, 1, 0, header_map, 1, 0, 8, 2)
    _copy_rect(BG1, g, 10, 0, misc_map, 0, 20, 12, 2)
    _copy_rect(BG1, g, 21, 0, misc_map, 12, 0, 9, 2)

    bg3 = g.render_bg(0xC000, 0xF000)
    bg1 = g.render_bg(0x0000, BG1)
    pc_bg = Image.new('RGBA', (SCREEN_W, SCREEN_H), _gba_to_rgb(g.pal[0]) + (255,))  # backdrop
    pc_bg.alpha_composite(bg3)
    pc_bg.alpha_composite(bg1)
    pc_bg.save(_p(out, 'pc_bg.png'))
    bg1.save(_p(out, 'pc_interface.png'))
    bg3.save(_p(out, 'pc_scroll_bg.png'))

    party_button = _bbox_rect(bg1, (80, 0, 88, 16))
    close_button = _bbox_rect(bg1, (168, 0, 72, 16))

    # Party panel when open (sub_8098520 end state / sub_80987DC): misc (0,0) 12x22 at BG1
    # (10,0) after sub_8098780 put the filled (12,4) or empty (16,4) 4x3 slot piece at
    # (7, (i-1)*3+1) for party slots 1..5.  Rows 20-21 fall below the screen.
    party_map = list(misc_map)
    for i in range(1, 6):
        _copy_rect(party_map, g, 7, (i - 1) * 3 + 1, misc_map, 12, 4, 4, 3)
    pc_party = render_tilemap_piece(g, 0x0000, party_map, 32, 0, 0, 12, 20)
    pc_party.save(_p(out, 'pc_party.png'))
    render_tilemap_piece(g, 0x0000, misc_map, 32, 12, 4, 4, 3).save(_p(out, 'pc_party_slot_filled.png'))
    render_tilemap_piece(g, 0x0000, misc_map, 32, 16, 4, 4, 3).save(_p(out, 'pc_party_slot_empty.png'))
    # Alternate CLOSE BOX frame sub_8098734 blinks to while the party is being closed.
    render_tilemap_piece(g, 0x0000, misc_map, 32, 12, 2, 9, 2).save(_p(out, 'pc_close_button_alt.png'))
    # CANCEL button = misc map (6..11, 17..18); its rect is where it differs from the plain
    # panel fill (tile 0x2341) / right border (0x2342) it is drawn over.
    btn = render_tilemap_piece(g, 0x0000, misc_map, 32, 6, 17, 6, 2)
    plain = render_tilemap_piece(g, 0x0000, ([0x2341] * 5 + [0x2342]) * 2, 6, 0, 0, 6, 2)
    diff = [(x, y) for y in range(16) for x in range(48) if btn.getpixel((x, y)) != plain.getpixel((x, y))]
    cx0, cy0 = min(d[0] for d in diff), min(d[1] for d in diff)
    cancel = [80 + 48 + cx0, 136 + cy0, max(d[0] for d in diff) - cx0 + 1, max(d[1] for d in diff) - cy0 + 1]

    # Hand cursor (pokemon_storage_system_4.c sub_809CC04): OBJ palette = unk_11e4[gUnknown_020384E9]
    # which is 0 = tag 0xDAC6 = HandCursorPalette (hand_cursor_pal.bin) normally; the alt
    # palette (0xDAD1) is only toggled by sub_809CD88.  32x32 frames (data/pokemon_storage_system.s
    # gSpriteAnimTable_83BBC60): tiles 0/16 = open (bobbing anim 0), 32 = grabbing (anim 2),
    # 48 = closed, holding a Pokemon (anim 3, used whenever gUnknown_020384E6 is set).
    hand_pal = read_pal(_p(gfx, 'hand_cursor_pal.bin'))
    hand_path = _p(gfx, 'hand_cursor.png')
    hand = Image.new('RGBA', (64, 32), (0, 0, 0, 0))
    hand.paste(sheet_1d(hand_path, hand_pal, 4, 4, 0), (0, 0))
    hand.paste(sheet_1d(hand_path, hand_pal, 4, 4, 48), (32, 0))
    hand.save(_p(out, 'hand.png'))
    allf = Image.new('RGBA', (128, 32), (0, 0, 0, 0))
    for i in range(4):
        allf.paste(sheet_1d(hand_path, hand_pal, 4, 4, i * 16), (i * 32, 0))
    allf.save(_p(out, 'hand_all_frames.png'))
    # Shadow: gSpriteTemplate_83BBC88, 16x16, same palette tag 0xDAC6; sub_809CB74 puts it at
    # hand (x, y + 20) (centres); visible only over the box grid (sBoxCursorArea == 0).
    render_sprite_png(_p(gfx, 'hand_cursor_shadow.png'), hand_pal).save(_p(out, 'hand_shadow.png'))

    # Box title arrows (sub_809A6DC): 8x16 OBJs at centre (0x5c + i*0x88, 0x1c); anim 0 ->
    # tile 0 (left), anim 1 -> tile 2 (right); palette PCPal_Arrow = arrow.gbapal.
    arrow_pal = read_pal(_p(gfx, 'arrow.png'))
    sheet_1d(_p(gfx, 'arrow.png'), arrow_pal, 1, 2, 0).save(_p(out, 'arrow_left.png'))
    sheet_1d(_p(gfx, 'arrow.png'), arrow_pal, 1, 2, 2).save(_p(out, 'arrow_right.png'))

    # Waveform decoration OBJs either side of "PKMN DATA" (sub_8097FB8): 16x8 at centre
    # (8 + i*63, 9); anims (pokemon_storage_system_2.c gSpriteAnimTable_83B6EEC) 0/2 = idle
    # (tiles 0 / 8), 1/3 = animated while a Pokemon is shown (sub_8098350).  Strip = 7 frames.
    wpal = read_pal(_p(gfx, 'waveform.png'))
    wave = Image.new('RGBA', (16 * 7, 8), (0, 0, 0, 0))
    for i in range(7):
        wave.paste(sheet_1d(_p(gfx, 'waveform.png'), wpal, 2, 1, i * 2), (i * 16, 0))
    wave.save(_p(out, 'pc_waveform.png'))

    # Markings (mon_markings.c sub_80F7960, 32x8 per combination, 16 combinations stacked),
    # PC uses gUnknown_083E49F4 = graphics/misc/mon_markings palette.
    mpath = _p(root, 'graphics', 'misc', 'mon_markings.png')
    render_sprite_png(mpath, read_pal(mpath)).save(_p(out, 'markings_pc.png'))

    # Standard window frame (text_window.c DrawStandardFrame): frame type 0 (default option)
    # -> graphics/text_window/1.png, 9 tiles = 3x3 in the 24x24 PNG, STD_WINDOW_PALETTE_NUM.
    frame_png = _p(root, 'graphics', 'text_window', '1.png')
    window_frame = render_sprite_png(frame_png, read_pal(frame_png))
    window_frame.save(_p(out, 'window_frame.png'))

    # Menu cursor: R/S has no triangle; InitMenu -> sub_8072D18 -> MenuCursor_Create814A5C0
    # (menu_cursor.c) builds an outline box from graphics/interface/outline_cursor_12 tiles,
    # colour index 12 = 11679 (0x2D9F), width = cursorWidth * 8.
    cur_rgb = list(_gba_to_rgb(11679))
    ctiles = _png_tiles(_p(root, 'graphics', 'interface', 'outline_cursor_12.png'))

    def cursor_piece(t0):  # 8x16 V_RECTANGLE subsprite = tiles t0 (top), t0+1 (bottom)
        im = Image.new('RGBA', (8, 16), (0, 0, 0, 0))
        for k in range(2):
            for i, v in enumerate(ctiles[t0 + k]):
                if v:
                    im.putpixel((i % 8, k * 8 + i // 8), tuple(cur_rgb) + (255,))
        return im

    pieces = [cursor_piece(0), cursor_piece(2), cursor_piece(4)]  # left cap, middle, right cap
    strip = Image.new('RGBA', (24, 16), (0, 0, 0, 0))
    for i, pc in enumerate(pieces):
        strip.paste(pc, (i * 8, 0))
    strip.save(_p(out, 'cursor_menu_pieces.png'))

    # Wallpapers: all 16, as BG2 shows them (160x144; sub_8099EB0 / CopyWallpaperTilemap:
    # tiles at BG_CHAR_ADDR(2), palettes 0x40-0x6F, palette bits + 4).
    os.makedirs(_p(out, 'wallpapers'), exist_ok=True)
    names = ['forest', 'city', 'desert', 'savanna', 'crag', 'volcano', 'snow', 'cave', 'beach',
             'seafloor', 'river', 'sky', 'polkadot', 'pokecenter', 'machine', 'plain']
    bgpal = {n: 'box_bg1' for n in names[:12]}
    bgpal.update({'polkadot': 'box_bg2', 'pokecenter': 'box_bg2', 'machine': 'box_bg3', 'plain': 'box_bg4'})
    nt = _num_tiles(root)
    for n in names:
        w = Gba()
        tiles = (png_to_4bpp(_p(gfx, n + '_frame.png'), nt.get(n + '_frame'))
                 + png_to_4bpp(_p(gfx, n + '_bg.png'), nt.get(n + '_bg')))
        w.load(0x8000, tiles)
        pals = (read_pal(_p(gfx, bgpal[n] + '.pal')) + read_pal(_p(gfx, n + '_frame.png'))
                + read_pal(_p(gfx, n + '_bg.png')))
        w.load_pal(pals, 0x40, 0x60)
        tm = read_tilemap(_p(gfx, n + '.bin'))
        ents = [((e & 0xF000) + (4 << 12)) | (e & 0xFFF) for e in tm]
        render_tilemap_piece(w, 0x8000, ents, 20, 0, 0, 20, 18).save(_p(out, 'wallpapers', n + '.png'))

    tc = _pc_text_colors(root)
    white, black = tc[15], tc[1]
    lv_w = font.widths[0x34]
    layout = {
        # CopyWallpaperTilemap: rectX = (0/8 + 10) -> tile column 10, buffer row 2.
        'wallpaper': [80, 16],
        # sub_809A23C: two 32x16 OBJs at centre (176 - w/2 + i*32, 0x1c) -> text from
        # x = 160 - w/2, glyph cell top y = 20 (gWindowTemplate_81E6D38, font 3).
        'box_name': [160, 28],
        'box_name_text': {'cell_top': 20, 'color': _rt(_gba_to_rgb(0x7FFF)),
                          'shadow': _rt(_gba_to_rgb(0x1CE7))},
        'arrows': {'left': [88, 20], 'right': [224, 20]},
        # SpawnBoxIconSprites: 32x32 icons at centre (24*j + 100, 24*i + 44).
        'grid': {'x': 84, 'y': 28, 'dx': 24, 'dy': 24, 'cols': 6, 'rows': 5},
        'party_button': party_button, 'close_button': close_button,
        # sub_809AACC: hand centre (col*24 + 100, row*24 + 32) -> top-left icon_tl + (0, -12).
        'hand_offset': [0, -12],
        # sub_809AACC area 3: centre (a1*88 + 120, 14 (8 while holding)) -> top-left (104 + 88*a1, -2)
        'hand_button_offset': [104 - party_button[0], -2 - party_button[1]],
        'hand_button_positions': {'party': [104, -2], 'close': [192, -2], 'holding_dy': -6},
        # area 2 (box title): centre (0xa2, 0x0c)
        'hand_box_title': [146, -4],
        'hand_shadow_offset': [8, 28],
        # sub_80980D4: 64x64 OBJ at centre (0x28, 0x30)
        'mon_sprite': [40, 48],
        # sub_80982B4 / sub_809C04C: Menu_PrintText at tile (x, y) -> glyph cell top-left in px.
        'mon_text': [
            {'field': 'nickname', 'x': 8, 'y': 88, 'color': white, 'shadow': black},
            # "{CLEAR_TO 7}/" + species, printed at tile (0, 13)
            {'field': 'species', 'x': 7, 'y': 104, 'prefix': '/', 'color': white, 'shadow': black},
            # tile (1, 15): CLEAR_TO 8 -> Lv glyph (0x34) at x 16, level right-aligned so it
            # ends 34 px after the line start (x 42) unless the Lv glyph is wider; then
            # CLEAR 8 and the gender symbol.
            {'field': 'level', 'x': 16, 'y': 120, 'lv_glyph': True, 'lv_glyph_width': lv_w,
             'num_right': 42, 'color': white, 'shadow': black},
            {'field': 'gender', 'x_after_level': 8, 'y': 120,
             'male': {'color': tc[4], 'shadow': tc[5]}, 'female': {'color': tc[6], 'shadow': tc[7]}},
            # tile (1, 16) with font 4 (small); its glyphs only use the lower 8 rows (y 136-143).
            {'field': 'item', 'x': 8, 'y': 128, 'font': 'small', 'color': white, 'shadow': black},
        ],
        # sub_8097F58: markings OBJ 32x8 at centre (0x28, 0x95)
        'markings': [24, 145],
        'waveform': {'left': [0, 5], 'right': [63, 5], 'size': [16, 8]},
        'party': {'x': 80, 'y': 0,
                  # sub_8099200: slot 0 icon centre (0x68, 0x40), slots 1-5 (0x98, (i-1)*24 + 0x10)
                  'slots': [[88, 48]] + [[136, (i - 1) * 24] for i in range(1, 6)],
                  # misc1 slot pieces: slot 0 at map (1,7), slots 1-5 at (7, (i-1)*3 + 1); 32x24 px.
                  'slot_rects': [[88, 56, 32, 24]] + [[136, (i - 1) * 24 + 8, 32, 24] for i in range(1, 6)],
                  'cancel': cancel,
                  # sub_809AACC area 1, a1 == 6: hand centre (0x98, 0x84)
                  'hand_cancel': [136, 116],
                  # sub_8098520: the panel slides up from below 8 px per frame over 20 frames.
                  },
        # sub_809CE84: Menu_DrawStdWindowFrame(28 - maxw, 14 - 2n, 29, 15); items at tile
        # (left + 1, top + 1 + 2i); maxw = max ceil(text_w / 8) over the items.
        'menu': {'right': 240, 'bottom': 128, 'line_height': 16, 'text_x': 8, 'padding': 8,
                 'text_y': 8, 'width_tiles_formula': 'w = (max(ceil(text_w/8)) + 2) * 8',
                 # InitMenu/RedrawMenuCursor: outline cursor OBJ at (text_left, line_top),
                 # spanning x-1 .. x + 8*maxw (inclusive), 16 px tall.
                 'cursor': {'dx': -1, 'dy': 0, 'extra_w': 2, 'h': 16, 'color': _rt(cur_rgb)}},
        # PrintStorageActionText: Menu_DrawStdWindowFrame(10, 16, 29, 19), text at tile (11, 17)
        'message': [80, 128, 160, 32],
        'message_text': [88, 136],
        # sub_8098A38: DisplayYesNoMenu(23, 10) -> frame (23,10)-(29,15)
        'yes_no': [184, 80, 56, 48],
        'text': {'color': tc[2], 'shadow': tc[3], 'background': tc[15]},
    }
    return layout


# --------------------------------------------------------------------------------------
# Summary screen (pokemon_summary_screen.c)
# --------------------------------------------------------------------------------------

def _summary_colors(root):
    """BG palette 13 after SummaryScreen_LoadPalettes (window gWindowTemplate_81E6E6C uses it)."""
    fp = _font_pal(root)
    pal13 = [0] * 16
    for src, idx, n in ((6, 1, 2), (10, 3, 2), (14, 5, 2), (6, 7, 2), (4, 9, 2), (8, 11, 2),
                        (2, 13, 1), (3, 14, 1), (1, 15, 1)):
        pal13[idx:idx + n] = fp[src:src + n]
    rgb = [list(_gba_to_rgb(c)) for c in pal13]
    # sSummaryScreenTextColors: id -> (fg, shadow) indices; default window colours 15 / 10.
    ids = {9: (1, 2), 10: (3, 4), 8: (5, 6), 11: (7, 8), 14: (9, 10), 12: (11, 12), 13: (13, 14),
           'default': (15, 10)}
    return {k: (rgb[a], rgb[b]) for k, (a, b) in ids.items()}


def build_summary(root, out, font, font4):
    gfx = _p(root, 'graphics', 'interface')
    g = Gba()
    # LoadPokemonSummaryScreenGraphics: status_screen tiles -> VRAM 0 (char base 0 for BG1-3),
    # status_screen.bin -> 0xE000 (BG3 left half), pokemon_info.bin -> 0xE800 (egg view),
    # skills -> 0x4800, battle moves -> 0x5800, contest moves -> 0x6800, palette -> 0..79.
    nt = 217  # misc.mk: $(MENUGFXDIR)/status_screen.4bpp: GFX_OPTS := -num_tiles 217
    g.load(0x0000, png_to_4bpp(_p(gfx, 'status_screen.png'), nt))
    g.put_map(0xE000, read_tilemap(_p(gfx, 'status_screen.bin')))
    g.put_map(0xE800, read_tilemap(_p(gfx, 'pokemon_info.bin')))
    g.put_map(0x4800, read_tilemap(_p(gfx, 'status_screen_pokemon_skills.bin')))
    g.put_map(0x5800, read_tilemap(_p(gfx, 'status_screen_battle_moves.bin')))
    g.put_map(0x6800, read_tilemap(_p(gfx, 'status_screen_contest_moves.bin')))
    g.load_pal(read_pal(_p(gfx, 'status_screen.pal')), 0, 160)
    # SummaryScreen_LoadPalettes (BG palettes 8, 13, 15)
    fp = _font_pal(root)
    for src, dst, size in ((14, 129, 2), (15, 136, 2), (14, 143, 2), (15, 137, 2), (6, 209, 4),
                           (10, 211, 4), (14, 213, 4), (6, 215, 4), (4, 217, 4), (8, 219, 4),
                           (2, 221, 2), (3, 222, 2), (1, 223, 2)):
        g.load_pal(fp[src:], dst, size)
    g.load_pal(read_pal(_p(root, 'graphics', 'fonts', 'default.pal')), 240, 32)
    g.load_pal(fp[3:], 249, 2)
    # Text tiles (state 9): summary_screen/text.4bpp at VRAM 0xD000, buttons.4bpp at 0xD140 ->
    # BG0 (char base 2 = 0x8000) tiles 0x280.. ; SummaryScreen_PlaceTextTile(t) uses tiles
    # 0x280 + 2t (top) / +1 (bottom).
    g.load(0xD000, png_to_4bpp(_p(root, 'graphics', 'summary_screen', 'text.png')))
    g.load(0xD140, png_to_4bpp(_p(root, 'graphics', 'summary_screen', 'buttons.png')))

    def text_tile(t, palette=15):
        im = Image.new('RGBA', (8, 16), (0, 0, 0, 0))
        for k in range(2):
            tp = g.tile_pixels(0x8000, 0x280 + t * 2 + k)
            for i, c in enumerate(tp):
                if c:
                    im.putpixel((i % 8, k * 8 + i // 8), _gba_to_rgb(g.pal[palette * 16 + c]) + (255,))
        return im

    # DrawPokerusSurvivorDot: no dot -> 0x081A at BG3 (2, 17) (both halves).
    g.put_entry(0xE444, 0x081A)
    g.put_entry(0xEC44, 0x081A)
    # sub_80A12D0(0) -> sub_80A1048 with no status: BG3 rows 18-19, cols 0-9 = entry 1.
    status_rows = [g.get_map(0xE000 + 0x480, 10), g.get_map(0xE000 + 0x4C0, 10)]
    for addr in (0xE000, 0xE800):
        g.put_map(addr + 0x480, [1] * 10)
        g.put_map(addr + 0x4C0, [1] * 10)
    # sub_80A1488(0, 0) (selectedMoveIndex == 4): rows 13-19, cols 0-9 of the battle-moves map
    # at 0x5B40 are cleared (POWER/ACCURACY panel hidden until a move is selected).
    moves_panel = [g.get_map(0x5B40 + 64 * i, 10) for i in range(7)]
    for i in range(7):
        g.put_map(0x5B40 + 64 * i, [0] * 10)
    # DrawExperienceProgressBar: 8 tiles at 0x4CAA (skills map row 18, col 21): 0x2062 + ticks.
    g.put_map(0x4CAA, [0x2062] * 8)

    colors = _summary_colors(root)
    WHITE = colors[13]
    DEF = colors['default']
    backdrop = _gba_to_rgb(g.pal[0]) + (255,)

    def nav_dots(page, first=0, last=3):
        """DrawSummaryScreenNavigationDots -> BG3 0xE016 (row 0, col 11), +0x10 for row 1."""
        arr = [0] * 8
        for i in range(4):
            if i < first:
                arr[2 * i] = arr[2 * i + 1] = 0x4040
            elif i > last:
                arr[2 * i] = arr[2 * i + 1] = 0x404A
            elif i < page:
                arr[2 * i], arr[2 * i + 1] = 0x4046, 0x4047
            elif i == page:
                base = 0x4041 if i != last else 0x404B
                arr[2 * i], arr[2 * i + 1] = base, base + 1
            else:
                base = 0x4043 if i != last else 0x4048
                arr[2 * i], arr[2 * i + 1] = base, base + 1
        g.put_map(0xE016, arr)
        g.put_map(0xE056, [a + 0x10 for a in arr])

    titles = {0: 'POKéMON INFO', 1: 'POKéMON SKILLS', 2: 'BATTLE MOVES'}
    # sub_809EC38 case 2 / ShowPokemonSummaryScreen: header action text id per page (normal / PC mode)
    actions = {0: 'CANCEL', 1: None, 2: 'INFO'}

    def page_image(page):
        nav_dots(page)
        img = Image.new('RGBA', (SCREEN_W, SCREEN_H), backdrop)
        img.alpha_composite(g.render_bg(0x0000, 0xE000, size=1, hofs=0))  # BG3 (non-egg: X = 0)
        if page == 1:   # sub_809EBC4: BG1CNT screen base 8, X = 0x100 -> 0x4800
            img.alpha_composite(g.render_bg(0x0000, 0x4000, size=1, hofs=0x100))
        elif page == 2:  # screen base 0xA, X = 0x100 -> 0x5800
            img.alpha_composite(g.render_bg(0x0000, 0x5000, size=1, hofs=0x100))
        # BG0 static text (gWindowTemplate_81E6E6C: tile (x, y) -> px (8x, 8y), font 3)
        # PrintSummaryWindowHeaderText: "{CLEAR_TO 2}" + title, white, at (0, 0)
        font.draw(img, 2, 0, titles[page], *WHITE)
        if actions[page]:
            img.alpha_composite(text_tile(5), (184, 0))  # A-button icon tiles at (23,0),(24,0)
            img.alpha_composite(text_tile(6), (192, 0))
            font.draw(img, 200, 0, actions[page], *WHITE)
        # sub_809FAC8: "No." tile 2 at (1, 2) (dex number follows at px (17, 16))
        img.alpha_composite(text_tile(2), (8, 16))
        if page == 0:
            # SummaryScreen_PrintPokemonInfoLabels: TYPE/ at (11, 6); "IDNo" tiles 0, 2 at (22,4),(23,4)
            font.draw(img, 88, 48, 'TYPE/', *DEF)
            img.alpha_composite(text_tile(0), (176, 32))
            img.alpha_composite(text_tile(2), (184, 32))
            # SummaryScreen_PrintPokemonInfo: "OT/" (white) at (11, 4), name follows
            font.draw(img, 88, 32, 'OT/', *WHITE)
        elif page == 1:
            # SummaryScreen_PrintPokemonSkillsLabels
            font.draw(img, 88, 112, 'EXP. POINTS', *WHITE)
            font.draw(img, 88, 128, 'NEXT LV.', *WHITE)
            for label, left, top, width in (('HP', 11, 7, 42), ('ATTACK', 11, 9, 42), ('DEFENSE', 11, 11, 42),
                                            ('SP. ATK', 22, 7, 36), ('SP. DEF', 22, 9, 36), ('SPEED', 22, 11, 36)):
                # Text_InitWindow_Centered: x = 8*left + (width/2 - text_w/2)
                font.draw(img, left * 8 + (width // 2 - font.width(label) // 2), top * 8, label, *WHITE)
        return img

    for page, name in ((0, 'sum_info'), (1, 'sum_skills'), (2, 'sum_moves')):
        page_image(page).save(_p(out, name + '.png'))

    # PP label tile (SummaryScreen_PlaceTextTile_White(1, 24, row)), drawn per known move.
    text_tile(1).save(_p(out, 'sum_pp_label.png'))
    # Status panel shown instead of the blank strip when the Pokemon has a status (sub_80A1048
    # copies status_screen.bin rows 18-19 cols 0-9, prints "STATUS" at (1, 18) in white).
    sp = Image.new('RGBA', (80, 16), (0, 0, 0, 0))
    sp.alpha_composite(render_tilemap_piece(g, 0, status_rows[0] + status_rows[1], 10, 0, 0, 10, 2))
    font.draw(sp, 8, 0, 'STATUS', *WHITE)
    sp.save(_p(out, 'sum_status_panel.png'))
    # Move detail panel (battle moves page after pressing A): rows 13-19 cols 0-9 + labels.
    mp = Image.new('RGBA', (80, 56), (0, 0, 0, 0))
    mp.alpha_composite(render_tilemap_piece(g, 0, sum(moves_panel, []), 10, 0, 0, 10, 7))
    font.draw(mp, 8, 120 - 104, 'POWER', *WHITE)
    font.draw(mp, 8, 136 - 104, 'ACCURACY', *WHITE)
    mp.save(_p(out, 'sum_moves_detail_panel.png'))

    # Exp bar fill colour: tile 0x2062 + 8 (full block), palette 2.
    full = g.tile_pixels(0, 0x62 + 8)
    empty = g.tile_pixels(0, 0x62)
    fill_idx = [c for c, e in zip(full, empty) if c != e]
    exp_fill = list(_gba_to_rgb(g.pal[2 * 16 + max(set(fill_idx), key=fill_idx.count)])) if fill_idx else None
    ys = sorted({i // 8 for i, (c, e) in enumerate(zip(full, empty)) if c != e}) or [0]
    exp_bar = [168, 144 + ys[0], 64, ys[-1] - ys[0] + 1]
    bar_tiles = Image.new('RGBA', (8 * 9, 8), (0, 0, 0, 0))
    for k in range(9):
        bar_tiles.alpha_composite(render_tilemap_piece(g, 0, [0x2062 + k], 1, 0, 0, 1, 1), (k * 8, 0))
    bar_tiles.save(_p(out, 'sum_exp_bar_tiles.png'))

    # Type icons (sSpriteSheet_MoveTypes, 32x16 each) with OBJ palette
    # sMoveTypeToOamPaletteNum (13/14/15 -> move_types_1/2/3.pal loaded at OBJ 13-15).
    tdir = _p(root, 'graphics', 'types')
    os.makedirs(_p(out, 'types'), exist_ok=True)
    tpal = {13: 1, 14: 2, 15: 3}
    type_pal = {'normal': 13, 'fight': 13, 'flying': 14, 'poison': 14, 'ground': 13, 'rock': 13,
                'bug': 15, 'ghost': 14, 'steel': 13, 'mystery': 15, 'fire': 13, 'water': 14,
                'grass': 15, 'electric': 13, 'psychic': 14, 'ice': 14, 'dragon': 15, 'dark': 13,
                'contest_cool': 13, 'contest_beauty': 14, 'contest_cute': 14, 'contest_smart': 15,
                'contest_tough': 13}
    for t, pn in type_pal.items():
        pal = read_pal(_p(tdir, 'move_types_%d.pal' % tpal[pn]))
        img = render_sprite_png(_p(tdir, t + '.png'), pal)
        out_name = 'fighting' if t == 'fight' else t
        img.save(_p(out, 'types', out_name + '.png'))

    # Status icons (gStatusGfx_Icons, 32x8 each: PSN PAR SLP FRZ BRN PKRS FNT).
    spath = _p(gfx, 'status_icons.png')
    spal = read_pal(spath)
    os.makedirs(_p(out, 'status'), exist_ok=True)
    for i, n in enumerate(['psn', 'par', 'slp', 'frz', 'brn', 'pkrs', 'fnt']):
        sheet_1d(spath, spal, 4, 1, i * 4).save(_p(out, 'status', n + '.png'))
    # Markings with the summary palette (sSummaryScreenMonMarkingsPalette).
    render_sprite_png(_p(root, 'graphics', 'misc', 'mon_markings.png'),
                      read_pal(_p(root, 'graphics', 'summary_screen', 'mon_markings.pal'))
                      ).save(_p(out, 'markings_summary.png'))

    W, D = [list(c) for c in WHITE], [list(c) for c in DEF]
    RED = [list(c) for c in colors[14]]
    BLUE = [list(c) for c in colors[9]]
    PINK = [list(c) for c in colors[10]]
    MALE = [list(c) for c in colors[11]]
    FEMALE = [list(c) for c in colors[12]]
    ot_x = 88 + font.width('OT/')

    def col(c):
        return {'color': c[0], 'shadow': c[1]}

    # Fields shown on every page (sub_809FAC8 / sub_80A0958 / sub_80A1D84 / sub_80A1DE8 / sub_80A1D18).
    common = [
        dict(field='dex_no', x=17, y=16, digits=3, zero_pad=True, **col(W)),
        dict(field='nickname', x=8, y=96, **col(W)),
        dict(field='species', x=7, y=112, prefix='/', **col(W)),
        dict(field='level', x=24, y=128, prefix='{LV}', lv_glyph_width=font.widths[0x34], **col(W)),
        dict(field='gender', x=56, y=128, male=col(MALE), female=col(FEMALE)),
        dict(field='ball', x=-2, y=128, w=16, h=16, center=[6, 136]),
        dict(field='markings', x=44, y=22, w=32, h=8),
        dict(field='status', x=48, y=148, w=32, h=8, panel=[0, 144, 80, 16]),
    ]
    layout = {
        'sprite': [40, 64],
        'sprite_hflip_note': 'hFlip when !IsPokeSpriteNotFlipped(species) (SummaryScreen_CreatePokemonSprite)',
        'text_default': col(D),
        'header': {'title': [2, 0], 'action_icon': [184, 0], 'action_text': [200, 0], **col(W)},
        'common': common,
        'info': [
            dict(field='ot', x=ot_x, y=32, male=col(BLUE), female=col(PINK)),
            dict(field='id', x=193, y=32, digits=5, zero_pad=True, **col(W)),
            dict(field='type1', x=120, y=48),
            dict(field='type2', x=160, y=48),
            dict(field='ability', x=88, y=72, **col(W)),
            dict(field='ability_desc', x=88, y=88, width=152, **col(D)),
            dict(field='memo', x=88, y=112, width=152, line_height=16, highlight=col(RED), **col(D),
                 note='nature name and met location in highlight colour; '
                      '"<NATURE> nature," then newline "Lv<n>, met at <LOC>." etc.'),
        ],
        'skills': [
            dict(field='item', x=88, y=32, **col(D)),
            dict(field='ribbon', x=168, y=32, **col(D)),
            dict(field='hp', x=126, y=56, cur_right=150, slash_x=150, max_right=174, **col(D)),
            dict(field='attack', x=153, y=72, align='center', **col(D)),
            dict(field='defense', x=153, y=88, align='center', **col(D)),
            dict(field='sp_atk', x=225, y=56, align='center', **col(D)),
            dict(field='sp_def', x=225, y=72, align='center', **col(D)),
            dict(field='speed', x=225, y=88, align='center', **col(D)),
            dict(field='exp_points', x=232, y=112, align='right', **col(D)),
            dict(field='next_lv', x=232, y=128, align='right', **col(D)),
            dict(field='exp_bar', rect=exp_bar, color=exp_fill, ticks=64,
                 note='8 tiles x 8 ticks; tiles in sum_exp_bar_tiles.png (0..8 ticks)'),
        ],
        'moves': [
            dict(field='moves', row_dy=16, type=[87, 32], name=dict(x=120, y=32, **col(W)),
                 pp_label=[192, 32], pp=dict(x=200, y=32, cur_right=214, slash=True, max_right=232, **col(D)),
                 empty=dict(name='-', name_x=120, pp='--', pp_x=208)),
            dict(field='move_detail', panel=[0, 104, 80, 56], power=dict(x=56, y=120, right=77, **col(D)),
                 accuracy=dict(x=56, y=136, right=77, **col(D)), description=dict(x=88, y=120, width=152, **col(D)),
                 note='only after selecting a move with A (sub_80A1334 / sub_80A04CC)'),
        ],
        'page_dots': {'rect': [88, 0, 64, 16], 'note': 'baked per page into sum_*.png (DrawSummaryScreenNavigationDots)'},
    }
    return layout


def build(decomp_root, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    font = Font(decomp_root, 3)
    font4 = Font(decomp_root, 4)
    pc_layout = build_pc(decomp_root, out_dir, font, font4)
    sum_layout = build_summary(decomp_root, out_dir, font, font4)
    tc = _pc_text_colors(decomp_root)
    h, top = font.cap_height()
    layout = {
        'font': {'color': tc[2], 'shadow': tc[3], 'height': h, 'cap_top_in_cell': top, 'cell_height': 16,
                 'shadow_offset': 'baked in glyphs (pixel 2: right/below)'},
        'pc': pc_layout,
        'summary': sum_layout,
    }
    with open(os.path.join(out_dir, 'layout.json'), 'w') as f:
        json.dump(layout, f, indent=2, ensure_ascii=False)


# --------------------------------------------------------------------------------------
# Preview (sanity check only, not part of build output)
# --------------------------------------------------------------------------------------

def preview(decomp_root, out_dir, dest):
    os.makedirs(dest, exist_ok=True)
    font = Font(decomp_root, 3)
    font4 = Font(decomp_root, 4)
    L = json.load(open(_p(out_dir, 'layout.json')))
    pc = L['pc']
    img = Image.open(_p(out_dir, 'pc_bg.png')).convert('RGBA')
    img.alpha_composite(Image.open(_p(out_dir, 'wallpapers', 'forest.png')), tuple(pc['wallpaper']))
    bn = 'BOX 1'
    font.draw(img, pc['box_name'][0] - font.width(bn) // 2, pc['box_name_text']['cell_top'], bn,
              pc['box_name_text']['color'], pc['box_name_text']['shadow'])
    img.alpha_composite(Image.open(_p(out_dir, 'arrow_left.png')), tuple(pc['arrows']['left']))
    img.alpha_composite(Image.open(_p(out_dir, 'arrow_right.png')), tuple(pc['arrows']['right']))
    gr = pc['grid']
    box = Image.new('RGBA', (32, 32), (0, 0, 0, 0))
    for yy in range(8, 32):
        for xx in range(4, 28):
            box.putpixel((xx, yy), (255, 0, 255, 120))
    for r in range(gr['rows']):
        for c in range(gr['cols']):
            img.alpha_composite(box, (gr['x'] + c * gr['dx'], gr['y'] + r * gr['dy']))
    mt = {d['field']: d for d in pc['mon_text']}
    font.draw(img, mt['nickname']['x'], mt['nickname']['y'], 'MEW', mt['nickname']['color'], mt['nickname']['shadow'])
    font.draw(img, mt['species']['x'], mt['species']['y'], '/MEW', mt['species']['color'], mt['species']['shadow'])
    x = font.draw(img, mt['level']['x'], mt['level']['y'], '{LV}', mt['level']['color'], mt['level']['shadow'])
    num = '5'
    x = max(x, mt['level']['num_right'] - font.width(num))
    x = font.draw(img, x, mt['level']['y'], num, mt['level']['color'], mt['level']['shadow'])
    font.draw(img, x + 8, mt['level']['y'], '♂', mt['gender']['male']['color'], mt['gender']['male']['shadow'])
    font4.draw(img, mt['item']['x'], mt['item']['y'], 'ORAN BERRY', mt['item']['color'], mt['item']['shadow'])
    sprite_box = Image.new('RGBA', (64, 64), (0, 0, 255, 60))
    img.alpha_composite(sprite_box, (pc['mon_sprite'][0] - 32, pc['mon_sprite'][1] - 32))
    hand = Image.open(_p(out_dir, 'hand.png')).crop((0, 0, 32, 32))
    hx, hy = gr['x'] + 2 * gr['dx'] + pc['hand_offset'][0], gr['y'] + gr['dy'] + pc['hand_offset'][1]
    img.alpha_composite(Image.open(_p(out_dir, 'hand_shadow.png')),
                        (hx + pc['hand_shadow_offset'][0], hy + pc['hand_shadow_offset'][1]))
    img.alpha_composite(hand, (hx, hy))
    # action menu with 4 items + message box, using the 9-slice frame
    fr = Image.open(_p(out_dir, 'window_frame.png'))

    def frame(rect):
        x0, y0, w, h = rect
        for ty in range(h // 8):
            for tx in range(w // 8):
                sx = 0 if tx == 0 else (16 if tx == w // 8 - 1 else 8)
                sy = 0 if ty == 0 else (16 if ty == h // 8 - 1 else 8)
                img.alpha_composite(fr.crop((sx, sy, sx + 8, sy + 8)), (x0 + tx * 8, y0 + ty * 8))

    items = ['MOVE', 'SUMMARY', 'WITHDRAW', 'CANCEL']
    m = pc['menu']
    maxw = max((font.width(s) + 7) // 8 for s in items)
    w = (maxw + 2) * 8
    h = len(items) * m['line_height'] + 2 * m['padding']
    mx, my = m['right'] - w, m['bottom'] - h
    frame([mx, my, w, h])
    for i, s in enumerate(items):
        font.draw(img, mx + m['text_x'], my + m['text_y'] + i * 16, s, L['font']['color'], L['font']['shadow'])
    cx, cy = mx + m['text_x'] + m['cursor']['dx'], my + m['text_y']
    pcs = Image.open(_p(out_dir, 'cursor_menu_pieces.png'))
    cw = maxw * 8 + m['cursor']['extra_w']
    for xx in range(0, cw - 8, 8):
        img.alpha_composite(pcs.crop((8, 0, 16, 16)), (cx + xx, cy))
    img.alpha_composite(pcs.crop((0, 0, 8, 16)), (cx, cy))
    img.alpha_composite(pcs.crop((16, 0, 24, 16)), (cx + cw - 8, cy))
    frame(pc['message'])
    font.draw(img, pc['message_text'][0], pc['message_text'][1], 'MEW is selected.', L['font']['color'], L['font']['shadow'])
    img.save(_p(dest, 'preview_pc.png'))

    # party preview
    img2 = Image.open(_p(out_dir, 'pc_bg.png')).convert('RGBA')
    img2.alpha_composite(Image.open(_p(out_dir, 'pc_party.png')), (pc['party']['x'], pc['party']['y']))
    for s in pc['party']['slots']:
        img2.alpha_composite(box, tuple(s))
    img2.alpha_composite(hand, tuple(pc['party']['hand_cancel']))
    img2.save(_p(dest, 'preview_pc_party.png'))

    S = L['summary']
    img = Image.open(_p(out_dir, 'sum_info.png')).convert('RGBA')
    img.alpha_composite(Image.new('RGBA', (64, 64), (0, 0, 255, 60)), (S['sprite'][0] - 32, S['sprite'][1] - 32))
    vals = {'dex_no': '151', 'nickname': 'MEW', 'species': '/MEW', 'level': '{LV}5',
            'ot': 'BRENDAN', 'id': '12345', 'ability': 'SYNCHRONIZE',
            'ability_desc': 'Passes on status problems.'}
    for d in S['common'] + S['info']:
        f = d['field']
        if f in vals and 'color' in d:
            font.draw(img, d['x'], d['y'], vals[f], d['color'], d['shadow'])
        elif f == 'gender':
            font.draw(img, d['x'], d['y'], '♀', d['female']['color'], d['female']['shadow'])
        elif f == 'ot':
            font.draw(img, d['x'], d['y'], vals['ot'], d['male']['color'], d['male']['shadow'])
        elif f in ('type1',):
            img.alpha_composite(Image.open(_p(out_dir, 'types', 'psychic.png')), (d['x'], d['y']))
        elif f == 'memo':
            x = font.draw(img, d['x'], d['y'], 'HASTY', d['highlight']['color'], d['highlight']['shadow'])
            font.draw(img, x, d['y'], ' nature,', d['color'], d['shadow'])
            x = font.draw(img, d['x'], d['y'] + 16, '{LV}5, met at ', d['color'], d['shadow'])
            font.draw(img, x, d['y'] + 16, 'FARAWAY ISLAND', d['highlight']['color'], d['highlight']['shadow'])
        elif f == 'markings':
            img.alpha_composite(Image.open(_p(out_dir, 'markings_summary.png')).crop((0, 8 * 5, 32, 8 * 6)),
                                (d['x'], d['y']))
    img.save(_p(dest, 'preview_summary_info.png'))

    img = Image.open(_p(out_dir, 'sum_skills.png')).convert('RGBA')
    vals = {'item': 'NONE', 'ribbon': 'NONE', 'attack': '12', 'defense': '11', 'sp_atk': '13',
            'sp_def': '12', 'speed': '14', 'exp_points': '135', 'next_lv': '44'}
    for d in S['skills']:
        f = d['field']
        if f in vals:
            s = vals[f]
            x = d['x'] - (font.width(s) // 2 if d.get('align') == 'center' else
                          font.width(s) if d.get('align') == 'right' else 0)
            font.draw(img, x, d['y'], s, d['color'], d['shadow'])
        elif f == 'hp':
            font.draw(img, d['cur_right'] - font.width('20'), d['y'], '20', d['color'], d['shadow'])
            font.draw(img, d['slash_x'], d['y'], '/', d['color'], d['shadow'])
            font.draw(img, d['max_right'] - font.width('21'), d['y'], '21', d['color'], d['shadow'])
        elif f == 'exp_bar':
            x, y, w, h = d['rect']
            for xx in range(x, x + w // 2):
                for yy in range(y, y + h):
                    img.putpixel((xx, yy), tuple(d['color']) + (255,))
    img.save(_p(dest, 'preview_summary_skills.png'))

    img = Image.open(_p(out_dir, 'sum_moves.png')).convert('RGBA')
    mv = S['moves'][0]
    pp_tile = Image.open(_p(out_dir, 'sum_pp_label.png'))
    for i, (n, t) in enumerate((('POUND', 'normal'), ('TRANSFORM', 'normal'), ('PSYCHIC', 'psychic'))):
        y = i * mv['row_dy']
        img.alpha_composite(Image.open(_p(out_dir, 'types', t + '.png')), (mv['type'][0], mv['type'][1] + y))
        font.draw(img, mv['name']['x'], mv['name']['y'] + y, n, mv['name']['color'], mv['name']['shadow'])
        img.alpha_composite(pp_tile, (mv['pp_label'][0], mv['pp_label'][1] + y))
        p = mv['pp']
        font.draw(img, p['cur_right'] - font.width('35'), p['y'] + y, '35', p['color'], p['shadow'])
        font.draw(img, p['cur_right'], p['y'] + y, '/', p['color'], p['shadow'])
        font.draw(img, p['max_right'] - font.width('35'), p['y'] + y, '35', p['color'], p['shadow'])
    y = 3 * mv['row_dy']
    font.draw(img, mv['empty']['name_x'], mv['name']['y'] + y, '-', mv['name']['color'], mv['name']['shadow'])
    font.draw(img, mv['empty']['pp_x'], mv['pp']['y'] + y, '--', mv['pp']['color'], mv['pp']['shadow'])
    img.save(_p(dest, 'preview_summary_moves.png'))


if __name__ == '__main__':
    build(sys.argv[1], sys.argv[2])
    if len(sys.argv) > 3:
        preview(sys.argv[1], sys.argv[2], sys.argv[3])
