"""Small GBA graphics helpers (Python 3 + Pillow only).

Tiles, JASC palettes, tilemaps (.bin, 16-bit entries) and the pret "latfont" text
fonts, rendered the way the hardware / the game's text engine would draw them.
Everything renders onto RGBA Pillow images; colour index 0 of a 4bpp tile is
transparent.
"""

import os
import re

from PIL import Image


# --------------------------------------------------------------------------
# Colours / palettes
# --------------------------------------------------------------------------

def gba_rgb(r, g, b):
    """Quantise an 8-bit colour to the GBA's 5-bit channels and expand it back
    the way the hardware/emulators show it ((c5 << 3) | (c5 >> 2))."""
    def q(v):
        c = (int(v) >> 3) & 31
        return (c << 3) | (c >> 2)
    return (q(r), q(g), q(b))


def read_jasc(path):
    """Read a JASC-PAL file -> list of (r, g, b) (GBA-quantised)."""
    with open(path) as f:
        lines = [ln.strip() for ln in f if ln.strip()]
    if lines[0] != "JASC-PAL":
        raise ValueError("not a JASC palette: %s" % path)
    n = int(lines[2])
    cols = []
    for ln in lines[3:3 + n]:
        r, g, b = (int(v) for v in ln.split()[:3])
        cols.append(gba_rgb(r, g, b))
    return cols


def png_palette(path):
    """Palette embedded in an indexed PNG (what gbagfx's .gbapal produces)."""
    im = Image.open(path)
    pal = im.getpalette() or []
    cols = [gba_rgb(*pal[i:i + 3]) for i in range(0, len(pal), 3)]
    return cols


def split16(cols):
    """Split a flat colour list into 16-colour palettes (padding the last)."""
    out = []
    for i in range(0, max(len(cols), 16), 16):
        p = list(cols[i:i + 16])
        p += [(0, 0, 0)] * (16 - len(p))
        out.append(p)
    return out


# --------------------------------------------------------------------------
# Tiles
# --------------------------------------------------------------------------

def png_indices(path, bpp=4):
    """Return (width, height, list-of-indices) of a PNG as gbagfx would see it.

    Indexed images give their palette index (masked to the bit depth);
    1/2/4-bit greyscale images (Pillow expands them to 0..255) are reduced back
    to their raw sample value."""
    im = Image.open(path)
    w, h = im.size
    mask = (1 << bpp) - 1
    if im.mode == "P":
        data = [v & mask for v in im.getdata()]
    elif im.mode in ("L", "I", "I;16"):
        raw = list(im.convert("L").getdata())
        levels = sorted(set(raw))
        # 4-bit greyscale stored as multiples of 17 by Pillow
        if all(v % 17 == 0 for v in levels):
            data = [(v // 17) & mask for v in raw]
        else:
            data = [(v >> (8 - bpp)) & mask for v in raw]
    elif im.mode == "1":
        data = [1 if v else 0 for v in im.getdata()]
    else:
        raise ValueError("unsupported PNG mode %s for %s" % (im.mode, path))
    return w, h, data


def load_tiles(path, bpp=4):
    """Cut a PNG into 8x8 tiles in gbagfx order (row-major, no metatiles).
    Returns a list of 64-entry index lists."""
    w, h, data = png_indices(path, bpp)
    tiles = []
    for ty in range(h // 8):
        for tx in range(w // 8):
            t = []
            for y in range(8):
                row = (ty * 8 + y) * w + tx * 8
                t.extend(data[row:row + 8])
            tiles.append(t)
    return tiles


def draw_tile(img, tile, x, y, palette, hflip=False, vflip=False, opaque0=False):
    """Draw one 8x8 tile at (x, y) on an RGBA image. Index 0 is transparent
    unless opaque0."""
    px = img.load()
    W, H = img.size
    for j in range(8):
        sy = 7 - j if vflip else j
        dy = y + j
        if dy < 0 or dy >= H:
            continue
        for i in range(8):
            sx = 7 - i if hflip else i
            dx = x + i
            if dx < 0 or dx >= W:
                continue
            c = tile[sy * 8 + sx]
            if c == 0 and not opaque0:
                continue
            r, g, b = palette[c]
            px[dx, dy] = (r, g, b, 255)


def draw_sprite(img, tiles, first_tile, w_tiles, h_tiles, x, y, palette):
    """Draw a 1D-mapped OBJ (w_tiles x h_tiles tiles starting at first_tile)."""
    n = first_tile
    for ty in range(h_tiles):
        for tx in range(w_tiles):
            draw_tile(img, tiles[n], x + tx * 8, y + ty * 8, palette)
            n += 1


def sprite_image(tiles, first_tile, w_tiles, h_tiles, palette):
    img = Image.new("RGBA", (w_tiles * 8, h_tiles * 8), (0, 0, 0, 0))
    draw_sprite(img, tiles, first_tile, w_tiles, h_tiles, 0, 0, palette)
    return img


# --------------------------------------------------------------------------
# Tilemaps
# --------------------------------------------------------------------------

def read_tilemap(path):
    """Read a raw (uncompressed) text-BG tilemap: list of u16 entries."""
    with open(path, "rb") as f:
        b = f.read()
    return [b[i] | (b[i + 1] << 8) for i in range(0, len(b) - 1, 2)]


def entry(e):
    """Decode a tilemap entry -> (tile, hflip, vflip, palette)."""
    return e & 0x3FF, bool(e & 0x400), bool(e & 0x800), (e >> 12) & 0xF


def draw_tilemap(img, entries, map_w, tiles, palettes, dest_x=0, dest_y=0,
                 src_x=0, src_y=0, w=None, h=None, tile_base=0, opaque0=False,
                 skip_missing=True):
    """Draw a rectangle of a tilemap onto img.

    entries/map_w: the tilemap and its width in tiles.
    tiles: tile list as loaded into the BG's char block, indexed so that tile
           number T in the map is tiles[T - tile_base] (tile_base lets you
           model graphics loaded at an offset inside the char block).
    palettes: list of 16 palettes (each 16 RGB) = BG palette RAM.
    dest_x/dest_y: destination in tiles. src_*/w/h: source rect in tiles."""
    map_h = len(entries) // map_w
    if w is None:
        w = map_w - src_x
    if h is None:
        h = map_h - src_y
    for j in range(h):
        for i in range(w):
            idx = (src_y + j) * map_w + (src_x + i)
            if idx >= len(entries):
                continue
            t, hf, vf, p = entry(entries[idx])
            ti = t - tile_base
            if ti < 0 or ti >= len(tiles):
                if skip_missing:
                    continue
                raise IndexError("tile %d out of range" % t)
            pal = palettes[p] if p < len(palettes) and palettes[p] else None
            if pal is None:
                continue
            draw_tile(img, tiles[ti], (dest_x + i) * 8, (dest_y + j) * 8, pal,
                      hf, vf, opaque0)


class BgLayer:
    """A 32-tile-wide text BG tilemap buffer, mirroring the game's
    CopyToBgTilemapBufferRect / FillBgTilemapBufferRect helpers."""

    def __init__(self, width=32, height=32, fill=0):
        self.w = width
        self.h = height
        self.map = [fill] * (width * height)

    def copy_rect(self, src, src_w, dest_x, dest_y, src_x=0, src_y=0, w=None, h=None):
        if w is None:
            w = src_w - src_x
        if h is None:
            h = len(src) // src_w - src_y
        for j in range(h):
            for i in range(w):
                dx, dy = dest_x + i, dest_y + j
                if 0 <= dx < self.w and 0 <= dy < self.h:
                    self.map[dy * self.w + dx] = src[(src_y + j) * src_w + src_x + i]

    def fill_rect(self, value, x, y, w, h):
        for j in range(h):
            for i in range(w):
                if 0 <= x + i < self.w and 0 <= y + j < self.h:
                    self.map[(y + j) * self.w + x + i] = value

    def render(self, img, tiles, palettes, tile_base=0, scroll_x=0, scroll_y=0):
        """Render the visible 30x20 screen (scroll in tiles) onto img."""
        sw, sh = img.size[0] // 8, img.size[1] // 8
        for j in range(sh):
            for i in range(sw):
                mx = (i + scroll_x) % self.w
                my = (j + scroll_y) % self.h
                t, hf, vf, p = entry(self.map[my * self.w + mx])
                ti = t - tile_base
                if 0 <= ti < len(tiles):
                    draw_tile(img, tiles[ti], i * 8, j * 8, palettes[p], hf, vf)


def screen_map_64(entries_left, entries_right):
    """Join two 32x32 screen blocks into a 64x32 map (screenSize 1)."""
    out = []
    for y in range(32):
        out += entries_left[y * 32:(y + 1) * 32]
        out += entries_right[y * 32:(y + 1) * 32]
    return out


# --------------------------------------------------------------------------
# Text (pret latfont fonts + charmap)
# --------------------------------------------------------------------------

FONT_FILES = {
    # name: (png, widths array, glyph height used by the text engine)
    "normal": ("latin_normal.png", "gFontNormalLatinGlyphWidths", 15),
    "short": ("latin_short.png", "gFontShortLatinGlyphWidths", 14),
    "small": ("latin_small.png", "gFontSmallLatinGlyphWidths", 13),
    "narrow": ("latin_narrow.png", "gFontNarrowLatinGlyphWidths", 15),
}


class Charmap:
    """Subset of pret's charmap.txt: single characters and {BRACED} names."""

    def __init__(self, decomp_root):
        self.chars = {}
        self.names = {}
        rx_char = re.compile(r"^'(.+)'\s*=\s*((?:[0-9A-Fa-f]{2}\s*)+)")
        rx_name = re.compile(r"^([A-Z_0-9]+)\s*=\s*((?:[0-9A-Fa-f]{2}\s*)+)")
        with open(os.path.join(decomp_root, "charmap.txt"), encoding="utf-8") as f:
            for ln in f:
                ln = ln.split("@")[0].rstrip()
                m = rx_char.match(ln)
                if m:
                    ch = m.group(1)
                    if ch == "\\'":
                        ch = "'"
                    elif ch.startswith("\\") and len(ch) == 2:
                        ch = {"n": "\n", "l": "\x0b", "p": "\x0c"}.get(ch[1], ch[1])
                    if ch not in self.chars:
                        self.chars[ch] = [int(x, 16) for x in m.group(2).split()]
                    continue
                m = rx_name.match(ln)
                if m and m.group(1) not in self.names:
                    self.names[m.group(1)] = [int(x, 16) for x in m.group(2).split()]

    def encode(self, s):
        out = []
        i = 0
        while i < len(s):
            c = s[i]
            if c == "{":
                j = s.index("}", i)
                name = s[i + 1:j]
                out += self.names[name]
                i = j + 1
                continue
            if c == "\n":
                out.append(0xFE)
            else:
                out += self.chars[c]
            i += 1
        return out


class Font:
    """Renders text with a pret latin font exactly like the game's text
    printer: glyph advance = per-glyph width (+ letterSpacing 0), 0xFE = new
    line (line height = font max height + lineSpacing), F9 xx = extra symbol
    (glyph 0x100 + xx)."""

    LINE_HEIGHT = {"normal": 16, "short": 14, "small": 12, "narrow": 16}

    def __init__(self, decomp_root, name="normal", charmap=None):
        png, warr, height = FONT_FILES[name]
        self.name = name
        self.height = height
        self.charmap = charmap or Charmap(decomp_root)
        w, h, data = png_indices(os.path.join(decomp_root, "graphics", "fonts", png), 2)
        self.png_w = w
        self.data = data
        src = open(os.path.join(decomp_root, "src", "fonts.c")).read()
        m = re.search(r"%s\[\]\s*=\s*\{(.*?)\};" % warr, src, re.S)
        self.widths = [int(v) for v in re.findall(r"\d+", m.group(1))]

    def glyphs(self, s):
        codes = self.charmap.encode(s) if isinstance(s, str) else list(s)
        out = []
        i = 0
        while i < len(codes):
            c = codes[i]
            if c == 0xF9:
                out.append(0x100 | codes[i + 1])
                i += 2
                continue
            if c == 0xFF:
                break
            out.append(c)
            i += 1
        return out

    def width(self, s):
        """GetStringWidth for a single line."""
        best = cur = 0
        for g in self.glyphs(s):
            if g == 0xFE:
                best = max(best, cur)
                cur = 0
                continue
            cur += self.widths[g]
        return max(best, cur)

    def draw(self, img, s, x, y, fg, shadow, line_spacing=0):
        """Draw text with its top-left at (x, y) (the AddTextPrinter x/y plus
        window origin). fg/shadow are RGB tuples (None to skip)."""
        px = img.load()
        W, H = img.size
        cx, cy = x, y
        for g in self.glyphs(s):
            if g == 0xFE:
                cx = x
                cy += self.LINE_HEIGHT[self.name] + line_spacing
                continue
            gw = self.widths[g]
            gx0 = (g % 16) * 16
            gy0 = (g // 16) * 16
            for j in range(self.height):
                for i in range(gw):
                    v = self.data[(gy0 + j) * self.png_w + gx0 + i]
                    col = fg if v == 1 else shadow if v == 2 else None
                    if col is None:
                        continue
                    dx, dy = cx + i, cy + j
                    if 0 <= dx < W and 0 <= dy < H:
                        px[dx, dy] = tuple(col[:3]) + (255,)
            cx += gw
        return cx

    def cap_height(self):
        """Pixel height of a capital letter ('A')."""
        g = self.charmap.chars["A"][0]
        gx0, gy0 = (g % 16) * 16, (g // 16) * 16
        rows = [j for j in range(16)
                if any(self.data[(gy0 + j) * self.png_w + gx0 + i] == 1 for i in range(16))]
        return (max(rows) - min(rows) + 1) if rows else 0


# --------------------------------------------------------------------------
# Misc
# --------------------------------------------------------------------------

def new_screen(w=240, h=160):
    return Image.new("RGBA", (w, h), (0, 0, 0, 0))


def crop(img, x, y, w, h):
    return img.crop((x, y, x + w, y + h))
