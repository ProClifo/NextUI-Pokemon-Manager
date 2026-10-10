"""PC box + summary screen UI assets for Pokemon Ruby/Sapphire (pret/pokeruby).

build(decomp_root, out_dir) renders the static layers of the Pokemon Storage System
box screen and the summary pages straight from the decomp's graphics, emulating the
VRAM/palette loads the C code performs, and writes layout.json with the coordinates
taken from the source (functions cited inline).

Only Python 3 + Pillow. All helpers live in this module (no shared gba.py).
"""

import json
import os
import re
import struct

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
        # gbagfx inverts greyscale PNGs (tools/gbagfx/main.c: invertColors = !hasPalette);
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
                if (charbase, tile) not in cache:
                    cache[(charbase, tile)] = self.tile_pixels(charbase, tile)
                tp = cache[(charbase, tile)]
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


def render_sprite_png(path, pal, frame_w=None, frame_h=None):
    """Render a sprite sheet PNG (indices) with a GBA palette list; index 0 transparent."""
    w, h, px = png_indices(path)
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    p = img.load()
    for y in range(h):
        for x in range(w):
            c = px[y * w + x]
            if c:
                p[x, y] = _gba_to_rgb(pal[c]) + (255,)
    return img


def sheet_1d(path, pal, tiles_w, tiles_h, tile_index):
    """Extract a sprite frame from a 1D-mapped sheet stored as an 8-px or wider column PNG."""
    w, h, px = png_indices(path)
    tiles = []
    for ty in range(h // 8):
        for tx in range(w // 8):
            t = []
            for y in range(8):
                t.extend(px[(ty * 8 + y) * w + tx * 8: (ty * 8 + y) * w + tx * 8 + 8])
            tiles.append(t)
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


# --------------------------------------------------------------------------------------
# Ruby/Sapphire text (font 3, the normal 8x16 proportional font)
# --------------------------------------------------------------------------------------

class Font:
    def __init__(self, root):
        self.charmap = {}
        for line in open(os.path.join(root, 'charmap.txt'), encoding='utf-8'):
            m = re.match(r"^'(.+)'\s*=\s*([0-9A-Fa-f]{2})\s*$", line.strip())
            if m and len(m.group(1)) == 1:
                self.charmap.setdefault(m.group(1), int(m.group(2), 16))
            m = re.match(r"^'\\''\s*=\s*([0-9A-Fa-f]{2})", line.strip())
            if m:
                self.charmap["'"] = int(m.group(1), 16)
        src = open(os.path.join(root, 'src/data/text/font3_widths.h')).read()
        # English revision 0 table is the one after the first "#else".
        block = src.split('#else', 1)[1]
        nums = re.findall(r'\{([^}]*)\}', block)[0]
        self.widths = [int(v) for v in re.findall(r'\d+', nums)]
        _, _, self.glyphs = png_indices(os.path.join(root, 'graphics/fonts/font3_lat.png'))
        self.gw = 128

    def encode(self, s):
        return [self.charmap[ch] for ch in s]

    def width(self, s):
        return sum(self.widths[c] for c in self.encode(s))

    def draw(self, img, x, y, s, fg, shadow):
        """Draw text with its top-left at (x, y): glyph cells are 8x16, index1=fg, index2=shadow."""
        p = img.load()
        for c in self.encode(s):
            gx, gy = (c % 16) * 8, (c // 16) * 16
            for yy in range(16):
                for xx in range(8):
                    v = self.glyphs[(gy + yy) * self.gw + gx + xx]
                    px, py = x + xx, y + yy
                    if v and 0 <= px < img.width and 0 <= py < img.height and xx < self.widths[c]:
                        p[px, py] = tuple(fg if v == 1 else shadow) + (255,)
            x += self.widths[c]
        return x

    def cap_height(self):
        c = self.charmap['A']
        gx, gy = (c % 16) * 8, (c // 16) * 16
        rows = [yy for yy in range(16)
                if any(self.glyphs[(gy + yy) * self.gw + gx + xx] == 1 for xx in range(8))]
        return rows[-1] - rows[0] + 1, rows[0]


# --------------------------------------------------------------------------------------
# PC (Pokemon Storage System) screen
# --------------------------------------------------------------------------------------

def _p(root, *parts):
    return os.path.join(root, *parts)


def _setup_pc_vram(root):
    """Replays the PC init (pokemon_storage_system_2.c: sub_8096884 states 5-7)."""
    g = Gba()
    gfx = _p(root, 'graphics', 'pokemon_storage')
    font_pal = read_pal(_p(root, 'graphics', 'fonts', 'unknown_81E6692.pal'))

    # sub_8097DE0: BG3 = scrolling background. BGCNT_CHARBASE(3) | SCREENBASE(30);
    # tiles DMA'd to BG_SCREEN_ADDR(28) (=char block 3 + 0x2000), map to SCREENBASE(30),
    # palette at 0xD0 (8 colours).
    g.load(0xE000, png_to_4bpp(_p(gfx, 'scrolling_bg.png')))
    g.put_map(0xF000, read_tilemap(_p(gfx, 'scrolling_bg_map.bin')))
    g.load_pal(read_pal(_p(gfx, 'scrolling_bg.png')), 0xD0, 0x10)

    # sub_8097E70: header gfx -> BG_SCREEN_ADDR(10) (0x5000), header tilemap (32 wide) into a
    # buffer whose (0,0) 10x20 block is queued to BG_SCREEN_ADDR(15).
    g.load(0x5000, png_to_4bpp(_p(gfx, 'header.png'), 47))
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
    g.load(0x6800, png_to_4bpp(_p(gfx, 'misc1.png'), 91))
    misc_map = read_tilemap(_p(gfx, 'misc1.bin'))
    g.load_pal(read_pal(_p(gfx, 'menu3.pal')), 0x20, 0x20)
    g.load_pal(read_pal(_p(gfx, 'menu4.pal')), 0x30, 0x20)

    # sub_8098780: party slot pieces, filled (12,4) or empty (16,4) 4x3 blocks copied into
    # misc map at (7, (i-1)*3+1) for party slots 1..5.  We keep the pristine map too.
    return g, header_map, misc_map


def _copy_rect(dst_addr_or_list, g, d_left, d_top, src, s_left, s_top, w, h):
    """sub_809D034 / sub_809D104: copy a w x h block between 32-wide tilemaps."""
    for y in range(h):
        row = src[(s_top + y) * 32 + s_left:(s_top + y) * 32 + s_left + w]
        if isinstance(dst_addr_or_list, list):
            for x, e in enumerate(row):
                dst_addr_or_list[(d_top + y) * 32 + d_left + x] = e
        else:
            g.put_map(dst_addr_or_list + ((d_top + y) * 32 + d_left) * 2, row)


def build_pc(root, out, font):
    gfx = _p(root, 'graphics', 'pokemon_storage')
    g, header_map, misc_map = _setup_pc_vram(root)
    BG1 = 0x7800  # BG_SCREEN_ADDR(15)

    # sub_8098400 clears BG_SCREEN_ADDR(15) right away; the queued copies (sub_809D034) of
    # sub_8097E70/sub_8098400 then land in VBlank: header (0,0) 10x20, then, party closed
    # (gUnknown_0203847C == 0), misc rows 20-21 (the PARTY POKeMON / CLOSE BOX buttons) at
    # (10,0) 12x2, then sub_8098690(TRUE) copies misc (12,0) 9x2 to (21,0).
    g.put_map(BG1, [0] * 1024)
    _copy_rect(BG1, g, 0, 0, header_map, 0, 0, 10, 20)
    # sub_8098350 with a Pokemon displayed: header (1,0) 8x2 from (1,0) (same as initial).
    _copy_rect(BG1, g, 1, 0, header_map, 1, 0, 8, 2)
    _copy_rect(BG1, g, 10, 0, misc_map, 0, 20, 12, 2)
    _copy_rect(BG1, g, 21, 0, misc_map, 12, 0, 9, 2)

    bg3 = g.render_bg(0xC000, 0xF000)
    bg1 = g.render_bg(0x0000, BG1)
    pc_bg = Image.new('RGBA', (SCREEN_W, SCREEN_H), (0, 0, 0, 255))
    # Backdrop = BG palette colour 0 (menu2.bin[0]).
    pc_bg.paste(_gba_to_rgb(g.pal[0]) + (255,), (0, 0, SCREEN_W, SCREEN_H))
    pc_bg.alpha_composite(bg3)
    pc_bg.alpha_composite(bg1)
    pc_bg.save(_p(out, 'pc_bg.png'))
    bg1.save(_p(out, 'pc_interface.png'))
    bg3.save(_p(out, 'pc_scroll_bg.png'))

    # Party panel (party open, sub_80987DC / sub_8098520 end state): misc (0,0) 12x22 at
    # dest (10,0) after sub_8098780 picked the filled piece for every slot.  Rows 20-21 are
    # off-screen (y >= 160) so the panel is the 12x20 block at (80,0).
    party_map = list(misc_map)
    for i in range(1, 6):
        _copy_rect(party_map, g, 7, (i - 1) * 3 + 1, misc_map, 12, 4, 4, 3)
    pc_party = render_tilemap_piece(g, 0x0000, party_map, 32, 0, 0, 12, 20)
    pc_party.save(_p(out, 'pc_party.png'))
    render_tilemap_piece(g, 0x0000, misc_map, 32, 12, 4, 4, 3).save(_p(out, 'pc_party_slot_filled.png'))
    render_tilemap_piece(g, 0x0000, misc_map, 32, 16, 4, 4, 3).save(_p(out, 'pc_party_slot_empty.png'))
    # Buttons strip as drawn with party closed (misc (0,20) 12x2 + (12,0) 9x2), and the
    # alternate CLOSE BOX frame that sub_8098734 blinks to (misc (12,2) 9x2).
    render_tilemap_piece(g, 0x0000, misc_map, 32, 12, 2, 9, 2).save(_p(out, 'pc_close_button_alt.png'))

    # Hand cursor: gHandCursorSpritePalettes[0] = HandCursorPalette (tag 0xDAC6) is the one
    # used normally (sub_809CC04: paletteNum = unk_11e4[gUnknown_020384E9], 0 by default;
    # the alt palette is toggled by sub_809CD88).  Anim frames: 0/16 = open (bobbing),
    # 32 = grabbing, 48 = holding (gSpriteAnim_83BBC3C..58).
    hand_pal = read_pal(_p(gfx, 'hand_cursor_pal.bin'))
    hand_path = _p(gfx, 'hand_cursor.png')
    hand = Image.new('RGBA', (64, 32), (0, 0, 0, 0))
    hand.paste(sheet_1d(hand_path, hand_pal, 4, 4, 0), (0, 0))
    hand.paste(sheet_1d(hand_path, hand_pal, 4, 4, 32), (32, 0))
    hand.save(_p(out, 'hand.png'))
    allf = Image.new('RGBA', (128, 32), (0, 0, 0, 0))
    for i in range(4):
        allf.paste(sheet_1d(hand_path, hand_pal, 4, 4, i * 16), (i * 32, 0))
    allf.save(_p(out, 'hand_all_frames.png'))
    render_sprite_png(_p(gfx, 'hand_cursor_shadow.png'), hand_pal).save(_p(out, 'hand_shadow.png'))

    # Box title arrows (sub_809A6DC): 8x16 sprites, anim 0 -> tile 0 (left), anim 1 -> tile 2.
    arrow_pal = read_pal(_p(gfx, 'arrow.png'))
    sheet_1d(_p(gfx, 'arrow.png'), arrow_pal, 1, 2, 0).save(_p(out, 'arrow_left.png'))
    sheet_1d(_p(gfx, 'arrow.png'), arrow_pal, 1, 2, 2).save(_p(out, 'arrow_right.png'))

    # Waveform decoration sprites at the top of PKMN DATA (sub_8097FB8), 16x8 frames.
    wpal = read_pal(_p(gfx, 'waveform.png'))
    wave = Image.new('RGBA', (16 * 7, 8), (0, 0, 0, 0))
    for i in range(7):
        wave.paste(sheet_1d(_p(gfx, 'waveform.png'), wpal, 2, 1, i * 2), (i * 16, 0))
    wave.save(_p(out, 'pc_waveform.png'))

    # Standard window frame (text_window.c): frame type 0 -> graphics/text_window/1.png,
    # palette slot 14; 9 tiles laid out 3x3 in the 24x24 PNG.
    frame_pal = read_pal(_p(root, 'graphics', 'text_window', '1.png'))
    render_sprite_png(_p(root, 'graphics', 'text_window', '1.png'), frame_pal).save(_p(out, 'window_frame.png'))

    # Wallpapers: all 16, as BG2 shows them (160x144, sub_8099EB0 / CopyWallpaperTilemap).
    os.makedirs(_p(out, 'wallpapers'), exist_ok=True)
    names = ['forest', 'city', 'desert', 'savanna', 'crag', 'volcano', 'snow', 'cave', 'beach',
             'seafloor', 'river', 'sky', 'polkadot', 'pokecenter', 'machine', 'plain']
    bgpal = {n: 'box_bg1' for n in names[:12]}
    bgpal.update({'polkadot': 'box_bg2', 'pokecenter': 'box_bg2', 'machine': 'box_bg3', 'plain': 'box_bg4'})
    frame_tiles = _frame_tile_counts(root)
    for n in names:
        w = Gba()
        tiles = png_to_4bpp(_p(gfx, n + '_frame.png'), frame_tiles.get(n)) + png_to_4bpp(_p(gfx, n + '_bg.png'))
        w.load(0x8000, tiles)
        pals = (read_pal(_p(gfx, bgpal[n] + '.pal')) + read_pal(_p(gfx, n + '_frame.png'))
                + read_pal(_p(gfx, n + '_bg.png')))
        w.load_pal(pals, 0x40, 0x60)
        tm = read_tilemap(_p(gfx, n + '.bin'))
        ents = [((e & 0xF000) + (4 << 12)) | (e & 0xFFF) for e in tm]
        img = render_tilemap_piece(w, 0x8000, ents, 20, 0, 0, 20, 18)
        img.save(_p(out, 'wallpapers', n + '.png'))

    layout = {
        'wallpaper': [80, 16],
        'box_name': [160, 28],
        'arrows': {'left': [88, 20], 'right': [224, 20]},
        'grid': {'x': 84, 'y': 28, 'dx': 24, 'dy': 24, 'cols': 6, 'rows': 5},
        'party_button': None, 'close_button': None,
        'hand_offset': [0, -12],
        'mon_sprite': [40, 48],
        'mon_text': [],
        'markings': [24, 141],
        'party': {'x': 80, 'y': 0, 'slots': [], 'cancel': None},
    }
    return layout, g, misc_map


def _frame_tile_counts(root):
    counts = {}
    txt = open(_p(root, 'misc.mk')).read()
    for m in re.finditer(r'pokemon_storage\)?/?(\w+)_frame\.4bpp: GFX_OPTS := -num_tiles (\d+)', txt):
        counts[m.group(1)] = int(m.group(2))
    for m in re.finditer(r'\$\(PSSGFXDIR\)/(\w+)_frame\.4bpp: GFX_OPTS := -num_tiles (\d+)', txt):
        counts[m.group(1)] = int(m.group(2))
    return counts


def build(decomp_root, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    font = Font(decomp_root)
    pc_layout, _, _ = build_pc(decomp_root, out_dir, font)
    with open(os.path.join(out_dir, 'layout.json'), 'w') as f:
        json.dump({'pc': pc_layout}, f, indent=2)
