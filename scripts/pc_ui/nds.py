"""Nintendo DS graphics formats (Python 3 + Pillow): NARC archives, LZ77 (type 0x10/0x11) compression, and the
Nitro files they hold: NCLR palettes, NCGR tiles, NSCR screens (tilemaps) and NCER cells (sprite layouts)."""
import struct

from PIL import Image


def lz_decompress(data):
    """BIOS LZ77 (0x10) or LZ11 (0x11) data, or the data itself when it isn't compressed."""
    if not data or data[0] not in (0x10, 0x11):
        return data
    kind = data[0]
    size = data[1] | data[2] << 8 | data[3] << 16
    out = bytearray()
    i = 4
    try:
        while len(out) < size:
            flags = data[i]
            i += 1
            for bit in range(8):
                if len(out) >= size:
                    break
                if flags & (0x80 >> bit):
                    if kind == 0x10:
                        b1, b2 = data[i], data[i + 1]
                        i += 2
                        length = (b1 >> 4) + 3
                        disp = ((b1 & 0xF) << 8 | b2) + 1
                    else:
                        b1 = data[i]
                        ind = b1 >> 4
                        if ind == 0:
                            length = ((b1 & 0xF) << 4 | data[i + 1] >> 4) + 0x11
                            disp = ((data[i + 1] & 0xF) << 8 | data[i + 2]) + 1
                            i += 3
                        elif ind == 1:
                            length = ((b1 & 0xF) << 12 | data[i + 1] << 4 | data[i + 2] >> 4) + 0x111
                            disp = ((data[i + 2] & 0xF) << 8 | data[i + 3]) + 1
                            i += 4
                        else:
                            length = ind + 1
                            disp = ((b1 & 0xF) << 8 | data[i + 1]) + 1
                            i += 2
                    for _ in range(length):
                        out.append(out[-disp])
                else:
                    out.append(data[i])
                    i += 1
    except IndexError:
        return data  # not really compressed
    return bytes(out[:size])


def narc_files(data):
    """The files of a NARC archive, decompressed."""
    assert data[:4] == b"NARC", "not a NARC"
    off = struct.unpack_from("<H", data, 0x0C)[0]
    blocks = {}
    while off < len(data):
        magic = data[off:off + 4]
        size = struct.unpack_from("<I", data, off + 4)[0]
        blocks[magic] = (off, size)
        off += size
    fat_off, _ = blocks[b"BTAF"]
    count = struct.unpack_from("<H", data, fat_off + 8)[0]
    img_off, _ = blocks[b"GMIF"]
    files = []
    for n in range(count):
        start, end = struct.unpack_from("<II", data, fat_off + 12 + n * 8)
        files.append(lz_decompress(data[img_off + 8 + start:img_off + 8 + end]))
    return files


def _sections(data):
    """Nitro file sections: {magic: payload} (payload after the 8-byte section header)."""
    header_size = struct.unpack_from("<H", data, 0x0C)[0]
    n = struct.unpack_from("<H", data, 0x0E)[0]
    off = header_size
    out = {}
    for _ in range(n):
        magic = data[off:off + 4]
        size = struct.unpack_from("<I", data, off + 4)[0]
        out[magic] = data[off + 8:off + size]
        off += size
    return out


def kind(data):
    return data[:4].decode("latin-1") if len(data) >= 4 else "?"


def rgb15(v):
    r, g, b = v & 31, (v >> 5) & 31, (v >> 10) & 31
    return (r << 3 | r >> 2, g << 3 | g >> 2, b << 3 | b >> 2)


def nclr(data):
    """NCLR: a flat list of RGB colours (16 per palette)."""
    s = _sections(data)[b"TTLP"]
    size = struct.unpack_from("<I", s, 8)[0]
    off = struct.unpack_from("<I", s, 12)[0]
    raw = s[off:off + size] if off + size <= len(s) else s[16:16 + size]
    return [rgb15(struct.unpack_from("<H", raw, i)[0]) for i in range(0, len(raw) - 1, 2)]


class Ncgr:
    """NCGR tiles: .tiles is a list of 64 colour indices per 8x8 tile; .bpp 4 or 8; .w/.h in tiles (or -1)."""

    def __init__(self, data):
        s = _sections(data)[b"RAHC"]
        self.h, self.w = struct.unpack_from("<hh", s, 0)
        depth = struct.unpack_from("<I", s, 4)[0]
        self.bpp = 8 if depth == 4 else 4
        self.mapping = struct.unpack_from("<I", s, 8)[0]
        self.scanned = struct.unpack_from("<I", s, 12)[0] & 1  # 1 = bitmap (not tiled)
        size, off = struct.unpack_from("<II", s, 16)
        raw = s[off - 8 + 8: off - 8 + 8 + size] if off else s[24:24 + size]
        raw = s[24:24 + size] if off == 0x18 or not off else raw
        self.tiles = []
        if self.bpp == 4:
            px = []
            for b in raw:
                px.append(b & 0xF)
                px.append(b >> 4)
        else:
            px = list(raw)
        if self.scanned and self.w > 0:
            W = self.w * 8
            H = len(px) // W
            for ty in range(H // 8):
                for tx in range(self.w):
                    self.tiles.append([px[(ty * 8 + y) * W + tx * 8 + x] for y in range(8) for x in range(8)])
        else:
            for t in range(len(px) // 64):
                self.tiles.append(px[t * 64:(t + 1) * 64])


class Nscr:
    """NSCR screen: .w/.h in tiles, .entries (tile | hflip<<10 | vflip<<11 | palette<<12)."""

    def __init__(self, data):
        s = _sections(data)[b"NRCS"]
        wpx, hpx = struct.unpack_from("<HH", s, 0)
        self.w, self.h = wpx // 8, hpx // 8
        size = struct.unpack_from("<I", s, 8)[0]
        self.entries = [struct.unpack_from("<H", s, 12 + i * 2)[0] for i in range(size // 2)]


def draw_tile(img, tile, x, y, palette, hflip=False, vflip=False, transparent0=True):
    px = img.load()
    W, H = img.size
    for j in range(8):
        sy = 7 - j if vflip else j
        if not 0 <= y + j < H:
            continue
        for i in range(8):
            sx = 7 - i if hflip else i
            if not 0 <= x + i < W:
                continue
            c = tile[sy * 8 + sx]
            if c == 0 and transparent0:
                continue
            if c < len(palette):
                px[x + i, y + j] = palette[c] + (255,)


def render_screen(scr, chars, colors, tile_base=0, transparent0=True, bpp=None):
    """An NSCR drawn with an NCGR's tiles and an NCLR's colours (palette slot = entry >> 12)."""
    img = Image.new("RGBA", (scr.w * 8, scr.h * 8), (0, 0, 0, 0))
    bpp = bpp or chars.bpp
    for n, e in enumerate(scr.entries):
        t = (e & 0x3FF) - tile_base
        if not 0 <= t < len(chars.tiles):
            continue
        pal = (e >> 12) & 0xF
        palette = colors[pal * 16:pal * 16 + 16] if bpp == 4 else colors
        draw_tile(img, chars.tiles[t], (n % scr.w) * 8, (n // scr.w) * 8, palette, bool(e & 0x400), bool(e & 0x800), transparent0)
    return img


def render_tiles(chars, colors, columns=16, palette=0):
    """Every tile of an NCGR in a grid (for looking at a file)."""
    rows = (len(chars.tiles) + columns - 1) // columns
    img = Image.new("RGBA", (columns * 8, max(rows, 1) * 8), (0, 0, 0, 0))
    pal = colors[palette * 16:palette * 16 + 16] if chars.bpp == 4 else colors
    for t, tile in enumerate(chars.tiles):
        draw_tile(img, tile, (t % columns) * 8, (t // columns) * 8, pal)
    return img


class Ncer:
    """NCER cells: .cells is a list of OAM lists [(x, y, w, h, tile, hflip, vflip, palette)] in pixels/tiles."""
    SIZES = {(0, 0): (8, 8), (0, 1): (16, 16), (0, 2): (32, 32), (0, 3): (64, 64),
             (1, 0): (16, 8), (1, 1): (32, 8), (1, 2): (32, 16), (1, 3): (64, 32),
             (2, 0): (8, 16), (2, 1): (8, 32), (2, 2): (16, 32), (2, 3): (32, 64)}

    def __init__(self, data):
        s = _sections(data)[b"KBEC"]
        count, ext = struct.unpack_from("<HH", s, 0)
        data_off = struct.unpack_from("<I", s, 4)[0]
        mapping = struct.unpack_from("<I", s, 8)[0]
        self.shift = {0: 0, 1: 1, 2: 2, 3: 3, 4: 4}.get(mapping & 7, 0)
        entry = 16 if ext == 1 else 8
        self.cells = []
        base = data_off
        oam_base = base + count * entry
        for c in range(count):
            n, attr = struct.unpack_from("<HH", s, base + c * entry)
            oam_off = struct.unpack_from("<I", s, base + c * entry + 4)[0]
            oams = []
            for k in range(n):
                a0, a1, a2 = struct.unpack_from("<HHH", s, oam_base + oam_off + k * 6)
                y = a0 & 0xFF
                y = y - 256 if y >= 128 else y
                x = a1 & 0x1FF
                x = x - 512 if x >= 256 else x
                shape, size = a0 >> 14, a1 >> 14
                w, h = self.SIZES.get((shape, size), (8, 8))
                eight = bool(a0 & 0x2000)
                oams.append((x, y, w, h, (a2 & 0x3FF) << self.shift, bool(a1 & 0x1000), bool(a1 & 0x2000), a2 >> 12, eight))
            self.cells.append(oams)


def render_cell(cell, chars, colors, mapping_1d=True):
    """One NCER cell drawn from 1D-mapped tiles, cropped to its bounds; returns (image, x0, y0)."""
    if not cell:
        return None, 0, 0
    x0 = min(o[0] for o in cell)
    y0 = min(o[1] for o in cell)
    x1 = max(o[0] + o[2] for o in cell)
    y1 = max(o[1] + o[3] for o in cell)
    img = Image.new("RGBA", (x1 - x0, y1 - y0), (0, 0, 0, 0))
    for x, y, w, h, tile, hf, vf, pal, eight in reversed(cell):
        part = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        palette = colors if eight else colors[pal * 16:pal * 16 + 16]
        step = 2 if eight and chars.bpp == 4 else 1
        n = tile
        for ty in range(h // 8):
            for tx in range(w // 8):
                if n < len(chars.tiles):
                    draw_tile(part, chars.tiles[n], tx * 8, ty * 8, palette)
                n += step
        if hf:
            part = part.transpose(Image.FLIP_LEFT_RIGHT)
        if vf:
            part = part.transpose(Image.FLIP_TOP_BOTTOM)
        img.alpha_composite(part, (x - x0, y - y0))
    return img, x0, y0
