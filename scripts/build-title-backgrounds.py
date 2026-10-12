#!/usr/bin/env python3
"""Draws menu backgrounds for the games without title art in assets/backgrounds, from the pret decompilations.

Usage: scripts/build-title-backgrounds.py <decomp dir> <output dir>

Red, Blue, Green and Yellow: the game's Super Game Boy border (gfx/sgb, its palettes from data/sgb/sgb_border.asm)
around the title screen's Game Boy picture without the logo: the version's Pokémon (Charizard, Blastoise,
Venusaur, Pikachu) and the player, coloured with their SGB palettes (data/pokemon/palettes.asm). Green's border
art is in pokered but its palettes aren't (it was only sold in Japan), so Red's are turned green.

Diamond, Pearl, Platinum, HeartGold and SoulSilver, whose title screens are 3D: the cover legendary's DS sprite
(pokeplatinum res/pokemon, twice its size) over a backdrop in the title's colours; SoulSilver's is the sea at
dusk from HeartGold/SoulSilver's opening (pokeheartgold files/demo/opening). Black and White have no
decompilation to take art from.

Writes <game>.png (256x224 for the Game Boy games, 256x192 for DS); scripts/build-backgrounds.py scales them.
"""
import colorsys
import os
import re
import struct
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pc_ui import nds  # noqa: E402


def rgb5(r, g, b):
    return (r * 255 // 31, g * 255 // 31, b * 255 // 31)


def border_palettes(path, version=None):
    """The border's palettes (four colours each) from data/sgb/sgb_border.asm: in pokered, the IF DEF(_RED) or
    IF DEF(_BLUE) ones, four colours apiece; in pokeyellow, 16 apiece of which the first four are used."""
    colours, block, padded = [], None, False
    for line in open(path, encoding="utf-8"):
        code = line.split(";")[0].strip()
        if code.startswith("IF DEF("):
            block = code[len("IF DEF(_"):-1].lower()
        elif code == "ENDC":
            block = None
        elif code.startswith("ds $18"):
            padded = True
        elif code.startswith("RGB") and (block is None or block == version):
            colours.append(rgb5(*(int(v) for v in re.findall(r"\d+", code)[:3])))
    stride = 4 if padded else 16
    return [colours[i:i + 4] for i in range(0, len(colours), stride)]


def gb_tiles(path):
    """A Game Boy PNG's 8x8 tiles as colour numbers (white 0 ... black 3)."""
    im = Image.open(path).convert("L")
    px = im.load()
    return [[3 - px[tx * 8 + x, ty * 8 + y] // 85 for y in range(8) for x in range(8)]
            for ty in range(im.height // 8) for tx in range(im.width // 8)]


def gb_image(path, palette):
    """A Game Boy picture in four colours, colour 0 see-through."""
    im = Image.open(path).convert("L")
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    src, dst = im.load(), out.load()
    for y in range(im.height):
        for x in range(im.width):
            c = 3 - src[x, y] // 85
            if c:
                dst[x, y] = palette[c] + (255,)
    return out


def sgb_palettes(repo):
    """{PAL_NAME: four colours} from data/sgb/sgb_palettes.asm."""
    pals = {}
    for line in open(os.path.join(repo, "data", "sgb", "sgb_palettes.asm"), encoding="utf-8"):
        m = re.match(r"\s*RGB\s+([\d, ]+);\s*(PAL_\w+)", line)
        if m:
            v = [int(x) for x in m.group(1).replace(" ", "").split(",") if x]
            pals[m.group(2)] = [rgb5(*v[i:i + 3]) for i in range(0, 12, 3)]
    return pals


def greener(colour):
    h, l, s = colorsys.rgb_to_hls(*(c / 255 for c in colour))
    r, g, b = colorsys.hls_to_rgb((h + 1 / 3) % 1, l, s)
    return (round(r * 255), round(g * 255), round(b * 255))


def gb_background(repo, border, version, mon, mon_pal, green=False):
    pals = border_palettes(os.path.join(repo, "data", "sgb", "sgb_border.asm"), version)
    if green:
        pals = [[greener(c) for c in p] for p in pals]
    tiles = gb_tiles(os.path.join(repo, "gfx", "sgb", border + ".png"))
    data = open(os.path.join(repo, "gfx", "sgb", border + ".tilemap"), "rb").read()
    sgb = sgb_palettes(repo)
    backdrop = sgb[mon_pal][0]
    img = Image.new("RGBA", (256, 224), backdrop + (255,))
    px = img.load()
    for n, e in enumerate(struct.unpack("<%dH" % (len(data) // 2), data)):
        t, p, hf, vf = e & 0x3FF, (e >> 10) & 7, e & 0x4000, e & 0x8000
        if t >= len(tiles) or p < 4 or p - 4 >= len(pals):
            continue
        for y in range(8):
            for x in range(8):
                c = tiles[t][(7 - y if vf else y) * 8 + (7 - x if hf else x)]
                if c:
                    px[(n % 32) * 8 + x, (n // 32) * 8 + y] = pals[p - 4][c] + (255,)
    # The Game Boy screen (160x144 at 48,40): the title screen's Pokémon and player, standing on the same line.
    screen = Image.new("RGBA", (160, 144), backdrop + (255,))
    pokemon = gb_image(os.path.join(repo, "gfx", "pokemon", "front", mon + ".png"), sgb[mon_pal])
    player = gb_image(os.path.join(repo, "gfx", "title", "player.png"), sgb["PAL_REDMON"])
    ground = 116
    screen.alpha_composite(pokemon, (28, ground - pokemon.height))
    screen.alpha_composite(player, (100, ground - player.height))
    img.alpha_composite(screen, (48, 40))
    return img


def jasc(path):
    lines = open(path, encoding="ascii").read().split("\n")[3:]
    return [tuple(int(v) for v in line.split()) for line in lines if line.strip()]


def ds_sprite(folder, front="male_front.png", pal="normal.pal"):
    """A Gen 4 front sprite's first 80x80 frame, colour 0 see-through."""
    im = Image.open(os.path.join(folder, front))
    colours = jasc(os.path.join(folder, pal))
    out = Image.new("RGBA", (80, 80), (0, 0, 0, 0))
    src, dst = im.load(), out.load()
    for y in range(80):
        for x in range(80):
            c = src[x, y]
            if c:
                dst[x, y] = colours[c] + (255,)
    return out


def gradient(top, bottom, size=(256, 192)):
    img = Image.new("RGBA", size)
    px = img.load()
    for y in range(size[1]):
        t = y / (size[1] - 1)
        # in 8-pixel bands, as a DS background would be
        t = (int(y / 8) * 8) / (size[1] - 1)
        c = tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,)
        for x in range(size[0]):
            px[x, y] = c
    return img


def hgss_sea(repo):
    """The sea at dusk from HeartGold/SoulSilver's opening (gs_opening: tiles 9, palette 3, screen 22)."""
    d = os.path.join(repo, "files", "demo", "opening", "gs_opening")
    if not os.path.isdir(d):
        return None

    def load(n):
        return open(os.path.join(d, f"gs_opening_{n:08d}.{'NCLR' if n < 4 else 'NCGR' if n < 10 else 'NSCR'}"), "rb").read()
    img = nds.render_screen(nds.Nscr(load(22)), nds.Ncgr(load(9)), nds.nclr(load(3)), transparent0=False)
    return img.crop((0, 0, 256, 192))


def ds_background(backdrop, sprite, x=None, y=None):
    big = sprite.resize((160, 160), Image.NEAREST)
    img = backdrop.copy()
    # kept off the edges: the screen-filling scale extends the edge pixels outwards
    img.alpha_composite(big, (x if x is not None else 72, y if y is not None else 192 - 160 - 8))
    return img


def main():
    decomp, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    made = []
    red, yellow = os.path.join(decomp, "pokered"), os.path.join(decomp, "pokeyellow")
    if os.path.isdir(os.path.join(red, "gfx", "sgb")):
        for game, version, mon, pal in (("red", "red", "charizard", "PAL_REDMON"), ("blue", "blue", "blastoise", "PAL_CYANMON"),
                                        ("green", "red", "venusaur", "PAL_GREENMON")):
            border = game + "_border"
            gb_background(red, border, version, mon, pal, green=game == "green").save(os.path.join(out, game + ".png"))
            made.append(game)
    if os.path.isdir(os.path.join(yellow, "gfx", "sgb")):
        gb_background(yellow, "border", None, "pikachu", "PAL_YELLOWMON").save(os.path.join(out, "yellow.png"))
        made.append("yellow")

    mons = os.path.join(decomp, "pokeplatinum", "res", "pokemon")
    if os.path.isdir(mons):
        sea = hgss_sea(os.path.join(decomp, "pokeheartgold"))
        scenes = {
            "diamond": (gradient((16, 24, 56), (90, 120, 170)), ds_sprite(os.path.join(mons, "dialga"))),
            "pearl": (gradient((120, 70, 120), (250, 205, 225)), ds_sprite(os.path.join(mons, "palkia"))),
            "platinum": (gradient((8, 8, 16), (110, 24, 32)),
                         ds_sprite(os.path.join(mons, "giratina", "forms", "origin"), "front.png")),
            "heartgold": (gradient((250, 150, 60), (255, 230, 150)), ds_sprite(os.path.join(mons, "ho_oh"))),
            "soulsilver": (sea if sea is not None else gradient((20, 30, 80), (120, 160, 210)),
                           ds_sprite(os.path.join(mons, "lugia"))),
        }
        for game, (backdrop, sprite) in scenes.items():
            ds_background(backdrop, sprite).save(os.path.join(out, game + ".png"))
            made.append(game)
    print(f"title backgrounds: {', '.join(made) or 'none'}")


if __name__ == "__main__":
    main()
