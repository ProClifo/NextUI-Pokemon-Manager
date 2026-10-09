#!/usr/bin/env python3
"""Builds the player's overworld sprite of each game, shown next to the trainer on the main menu.

Usage: scripts/build-trainers.py <decomp dir> <output dir> [preview.png]

<decomp dir> holds checkouts of pret/pokered, pokeyellow, pokegold, pokecrystal, pokeemerald, pokefirered,
pokeplatinum and pokeheartgold. Writes <set>-<m|f>-<scale>x.png: the sprite standing and facing the camera,
trimmed and scaled by the smallest whole number that makes it at least as tall as the list text (16 px times
the UI scale).

Sets: rb, y, gs (Gold/Silver have only a boy), c, rs (Ruby/Sapphire's Brendan and May, which Emerald keeps),
e, frlg (Red and Leaf), dp (Diamond/Pearl's outfits, which Platinum keeps), pt and hgss (Ethan and Lyra, decoded
from HeartGold's DS textures). Black/White have no decompilation to take sprites from. Requires Pillow.
"""
import os
import re
import struct
import sys

from PIL import Image

TEXT_PX = 16
STORE = {}


def gb_rgb(values):
    return tuple(v * 255 // 31 for v in values)


def game_boy(path, colors):
    """The first 16x16 frame (standing, facing down) in four colours, white see-through."""
    img = Image.open(path).convert("L").crop((0, 0, 16, 16))
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    for y in range(16):
        for x in range(16):
            shade = 3 - round(img.getpixel((x, y)) / 85)
            if shade:
                out.putpixel((x, y), colors[shade] + (255,))
    return out


def day_palette(src, name):
    """A palette of gfx/overworld/npc_sprites.pal's "day" block (red, blue...)."""
    text = open(os.path.join(src, "gfx/overworld/npc_sprites.pal"), encoding="utf-8").read()
    day = text[text.index("; day"):]
    for line in day.splitlines():
        m = re.match(r"\s*RGB\s+([\d,\s]+);\s*(\w+)", line)
        if m and m.group(2) == name:
            v = list(map(int, re.findall(r"\d+", m.group(1))))
            return [gb_rgb(v[i:i + 3]) for i in range(0, 12, 3)]
    raise KeyError(name)


def indexed_frame(path, box):
    """A frame of an indexed PNG in its own palette, colour 0 see-through."""
    img = Image.open(path)
    pal = img.getpalette()
    frame = img.crop(box)
    out = Image.new("RGBA", frame.size, (0, 0, 0, 0))
    for y in range(frame.height):
        for x in range(frame.width):
            c = frame.getpixel((x, y))
            if c:
                out.putpixel((x, y), tuple(pal[c * 3:c * 3 + 3]) + (255,))
    return out


def nsbtx_frame(path, texture):
    """One texture of a Nitro BTX0 file (4 bpp, palette 0), colour 0 see-through."""
    d = open(path, "rb").read()
    t = d[struct.unpack_from("<I", d, 0x10)[0]:]

    def block(off):
        count = t[off + 1]
        p = off + 4
        p += struct.unpack_from("<HH", t, p)[1] - 4  # unknown block; its size includes the entry header
        esize = struct.unpack_from("<H", t, p)[0]
        p += 4
        entries = [t[p + i * esize:p + (i + 1) * esize] for i in range(count)]
        p += esize * count
        names = [t[p + i * 16:p + i * 16 + 16].split(b"\0")[0].decode("latin1") for i in range(count)]
        return dict(zip(names, entries))

    tex_data, pal_data = struct.unpack_from("<I", t, 0x14)[0], struct.unpack_from("<I", t, 0x38)[0]
    entry = block(struct.unpack_from("<H", t, 0x0E)[0])[texture]
    pal_off = pal_data + (struct.unpack_from("<H", next(iter(block(struct.unpack_from("<I", t, 0x34)[0]).values())), 0)[0] << 3)
    offs, params = struct.unpack_from("<HH", entry, 0)
    w, h = 8 << ((params >> 4) & 7), 8 << ((params >> 7) & 7)
    assert (params >> 10) & 7 == 3, "expected 4 bpp"
    colors = [gb_rgb([(c := struct.unpack_from("<H", t, pal_off + 2 * i)[0]) & 31, (c >> 5) & 31, (c >> 10) & 31]) for i in range(16)]
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    base = tex_data + (offs << 3)
    for i in range(w * h):
        v = (t[base + i // 2] >> ((i % 2) * 4)) & 15
        if v:
            out.putpixel((i % w, i // w), colors[v] + (255,))
    return out


def save(out_dir, name, img):
    img = img.crop(img.getbbox())
    STORE[name] = img
    for scale in (2, 3):
        factor = max(1, -(-TEXT_PX * scale // img.height))  # rounded up: at least the text's height
        img.resize((img.width * factor, img.height * factor), Image.NEAREST).save(os.path.join(out_dir, f"{name}-{scale}x.png"))


def main():
    src, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    gray = [(255, 255, 255), (168, 168, 168), (88, 88, 88), (24, 24, 24)]
    save(out, "rb-m", game_boy(os.path.join(src, "pokered/gfx/sprites/red.png"), gray))
    save(out, "y-m", game_boy(os.path.join(src, "pokeyellow/gfx/sprites/red.png"), gray))
    save(out, "gs-m", game_boy(os.path.join(src, "pokegold/gfx/sprites/chris.png"), day_palette(os.path.join(src, "pokegold"), "red")))
    crystal = os.path.join(src, "pokecrystal")
    save(out, "c-m", game_boy(os.path.join(crystal, "gfx/sprites/chris.png"), day_palette(crystal, "red")))
    save(out, "c-f", game_boy(os.path.join(crystal, "gfx/sprites/kris.png"), day_palette(crystal, "blue")))
    people = os.path.join(src, "pokeemerald/graphics/object_events/pics/people")
    for name, folder in (("rs-m", "ruby_sapphire_brendan"), ("rs-f", "ruby_sapphire_may"), ("e-m", "brendan"), ("e-f", "may")):
        save(out, name, indexed_frame(os.path.join(people, folder, "walking.png"), (0, 0, 16, 32)))
    people = os.path.join(src, "pokefirered/graphics/object_events/pics/people")
    save(out, "frlg-m", indexed_frame(os.path.join(people, "red_normal.png"), (0, 0, 16, 32)))
    save(out, "frlg-f", indexed_frame(os.path.join(people, "green_normal.png"), (0, 0, 16, 32)))
    player = os.path.join(src, "pokeplatinum/res/graphics/field_sprites/player")
    for name, file in (("dp-m", "dp_player_m.png"), ("dp-f", "dp_player_f.png"), ("pt-m", "player_m.png"), ("pt-f", "player_f.png")):
        save(out, name, indexed_frame(os.path.join(player, file), (0, 128, 32, 160)))  # frame 4 faces the camera
    mmodel = os.path.join(src, "pokeheartgold/files/data/mmodel/mmodel")
    save(out, "hgss-m", nsbtx_frame(os.path.join(mmodel, "mmodel_00000069.NSBTX"), "hero.5"))     # MMODEL_HERO
    save(out, "hgss-f", nsbtx_frame(os.path.join(mmodel, "mmodel_00000070.NSBTX"), "heroine.5"))  # MMODEL_HEROINE
    if len(sys.argv) > 3:
        sheet = Image.new("RGBA", (len(STORE) * 36, 40), (90, 120, 90, 255))
        for i, img in enumerate(STORE.values()):
            sheet.alpha_composite(img, (i * 36 + (36 - img.width) // 2, 40 - img.height - 2))
        sheet.resize((sheet.width * 3, sheet.height * 3), Image.NEAREST).save(sys.argv[3])
    print(f"{len(STORE)} trainer sprites")


if __name__ == "__main__":
    main()
