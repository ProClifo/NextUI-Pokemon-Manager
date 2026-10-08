#!/usr/bin/env python3
"""Builds the PC box art used by the box viewer from the pokeemerald decompilation.

Usage: scripts/build-box-assets.py <pokeemerald checkout> <output dir>

Writes:
  wallpapers/<00-15>.png  the 16 Emerald box wallpapers (160x144, transparent where the game shows nothing)
  icons/<national dex>.png  Gen 3 box icons, both animation frames (32x64), plus egg.png and unknown.png
  icons/201-<form>.png      Unown letters (A-Z, !, ?)
  sprites/<dex>[-shiny].png front sprites for the info panel (64x64), Unown as 201-<form>[-shiny].png, egg.png
  background.png            the PC's scrolling background pattern (256x256 tile)
  cursor.png                the hand cursor (32x32, first frame)

No Nintendo artwork is stored in this repository; it is generated at build time from pret/pokeemerald.
Requires Pillow.
"""
import os
import re
import struct
import sys

from PIL import Image

WALLPAPERS = ["forest", "city", "desert", "savanna", "crag", "volcano", "snow", "cave",
              "beach", "seafloor", "river", "sky", "polkadot", "pokecenter", "machine", "plain"]


def tiles_of(path, limit=None):
    """8x8 tiles of an indexed PNG in GBA order (left to right, top to bottom), plus its palette."""
    img = Image.open(path)
    assert img.mode == "P", path
    w, h = img.size
    px = img.load()
    tiles = []
    for ty in range(h // 8):
        for tx in range(w // 8):
            tiles.append([[px[tx * 8 + x, ty * 8 + y] & 0xF for x in range(8)] for y in range(8)])
    if limit is not None:
        tiles = tiles[:limit]
    pal = img.getpalette()[:48]
    return tiles, [tuple(pal[i * 3:i * 3 + 3]) for i in range(16)]


def frame_tile_limits(rules_path):
    """`-num_tiles N` options from graphics_file_rules.mk for each wallpaper's frame.png."""
    limits = {}
    lines = open(rules_path, encoding="utf-8").read().splitlines()
    for i, line in enumerate(lines):
        m = re.match(r"\$\(WALLPAPERGFXDIR\)/(\w+)/frame\.4bpp:", line)
        if m and i + 1 < len(lines):
            n = re.search(r"-num_tiles (\d+)", lines[i + 1])
            if n:
                limits[m.group(1)] = int(n.group(1))
    return limits


def build_wallpaper(folder, frame_limit):
    frame_tiles, frame_pal = tiles_of(os.path.join(folder, "frame.png"), frame_limit)
    bg_tiles, bg_pal = tiles_of(os.path.join(folder, "bg.png"))
    tiles = frame_tiles + bg_tiles
    palettes = {1: frame_pal, 2: bg_pal}
    data = open(os.path.join(folder, "tilemap.bin"), "rb").read()
    entries = struct.unpack("<%dH" % (len(data) // 2), data)
    out = Image.new("RGBA", (160, 144), (0, 0, 0, 0))
    px = out.load()
    for i, e in enumerate(entries):
        tile, hflip, vflip, pal = e & 0x3FF, (e >> 10) & 1, (e >> 11) & 1, e >> 12
        if pal not in palettes or tile >= len(tiles):
            continue
        cx, cy = (i % 20) * 8, (i // 20) * 8
        for y in range(8):
            for x in range(8):
                c = tiles[tile][7 - y if vflip else y][7 - x if hflip else x]
                if c == 0:
                    continue  # colour 0 is transparent on GBA backgrounds layered over the box UI
                r, g, b = palettes[pal][c]
                px[cx + x, cy + y] = (r, g, b, 255)
    return out


def save_sprite(gfx_dir, pal_dir, out_dir, name, shiny=True):
    """First 64x64 frame of a front sprite, in its normal (and shiny) palette."""
    for file in ("anim_front.png", "front.png"):
        path = os.path.join(gfx_dir, file)
        if os.path.exists(path):
            break
    else:
        return False
    variants = [("normal.pal", "")] + ([("shiny.pal", "-shiny")] if shiny else [])
    for pal_file, suffix in variants:
        pal_path = os.path.join(pal_dir, pal_file)
        if not os.path.exists(pal_path):
            continue
        indexed_to_rgba(path, read_jasc(pal_path)).crop((0, 0, 64, 64)).save(os.path.join(out_dir, f"{name}{suffix}.png"))
    return True


def build_tilemap(png, tilemap, palette, width_tiles):
    """Renders a single-palette GBA tilemap (used for the PC's scrolling background)."""
    tiles, _ = tiles_of(png)
    data = open(tilemap, "rb").read()
    entries = struct.unpack("<%dH" % (len(data) // 2), data)
    height_tiles = len(entries) // width_tiles
    out = Image.new("RGBA", (width_tiles * 8, height_tiles * 8), palette[0] + (255,))
    px = out.load()
    for i, e in enumerate(entries):
        tile, hflip, vflip = e & 0x3FF, (e >> 10) & 1, (e >> 11) & 1
        if tile >= len(tiles):
            continue
        cx, cy = (i % width_tiles) * 8, (i // width_tiles) * 8
        for y in range(8):
            for x in range(8):
                c = tiles[tile][7 - y if vflip else y][7 - x if hflip else x]
                px[cx + x, cy + y] = palette[c] + (255,)
    return out


def indexed_to_rgba(path, palette=None):
    img = Image.open(path)
    pal = palette or [tuple(img.getpalette()[i * 3:i * 3 + 3]) for i in range(16)]
    w, h = img.size
    src = img.load()
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    dst = out.load()
    for y in range(h):
        for x in range(w):
            c = src[x, y] & 0xF
            if c:
                dst[x, y] = pal[c] + (255,)
    return out


def read_jasc(path):
    lines = open(path, encoding="utf-8").read().split()
    count = int(lines[2])
    vals = list(map(int, lines[3:3 + count * 3]))
    return [tuple(vals[i * 3:i * 3 + 3]) for i in range(count)]


def icon_palette_indices(root):
    """species name -> icon palette index, from src/pokemon_icon.c."""
    text = open(os.path.join(root, "src", "pokemon_icon.c"), encoding="utf-8").read()
    table = text[text.index("gMonIconPaletteIndices"):]
    return {m.group(1): int(m.group(2)) for m in re.finditer(r"\[SPECIES_(\w+)\]\s*=\s*(\d)", table)}


def main():
    root, out = sys.argv[1], sys.argv[2]
    wp_dir = os.path.join(root, "graphics", "pokemon_storage", "wallpapers")
    limits = frame_tile_limits(os.path.join(root, "graphics_file_rules.mk"))
    os.makedirs(os.path.join(out, "wallpapers"), exist_ok=True)
    for i, name in enumerate(WALLPAPERS):
        build_wallpaper(os.path.join(wp_dir, name), limits.get(name)).save(os.path.join(out, "wallpapers", f"{i:02d}.png"))
    print(f"{len(WALLPAPERS)} wallpapers")

    cursor = indexed_to_rgba(os.path.join(root, "graphics", "pokemon_storage", "hand_cursor.png"))
    cursor.crop((0, 0, 32, 32)).save(os.path.join(out, "cursor.png"))

    os.makedirs(os.path.join(out, "icons"), exist_ok=True)
    pals = [read_jasc(os.path.join(root, "graphics", "pokemon", "icon_palettes", f"icon_palette_{i}.pal")) for i in range(3)]
    pal_index = icon_palette_indices(root)
    dex = national_dex(root)
    count = 0
    for number, (folder, species) in dex.items():
        path = os.path.join(root, "graphics", "pokemon", folder, "icon.png")
        if not os.path.exists(path):
            continue
        indexed_to_rgba(path, pals[pal_index.get(species, 0)]).save(os.path.join(out, "icons", f"{number}.png"))
        count += 1
    # Unown: one icon per letter, in PKHeX's form order (A-Z, !, ?). Plain 201.png is A.
    letters = [chr(c) for c in range(ord("a"), ord("z") + 1)] + ["exclamation_mark", "question_mark"]
    for form, letter in enumerate(letters):
        path = os.path.join(root, "graphics", "pokemon", "unown", letter, "icon.png")
        if os.path.exists(path):
            icon = indexed_to_rgba(path, pals[pal_index.get("UNOWN", 0)])
            icon.save(os.path.join(out, "icons", f"201-{form}.png"))
            if form == 0:
                icon.save(os.path.join(out, "icons", "201.png"))
                count += 1
    for name, species, target in (("egg", "EGG", "egg.png"), ("question_mark", "NONE", "unknown.png")):
        path = os.path.join(root, "graphics", "pokemon", name, "icon.png")
        if os.path.exists(path):
            indexed_to_rgba(path, pals[pal_index.get(species, 0)]).save(os.path.join(out, "icons", target))
    print(f"{count} icons")

    os.makedirs(os.path.join(out, "sprites"), exist_ok=True)
    sprites = 0
    for number, (folder, _) in dex.items():
        base = os.path.join(root, "graphics", "pokemon", folder)
        if save_sprite(base, base, os.path.join(out, "sprites"), str(number)):
            sprites += 1
    sprites += 1  # Unown, saved per form below
    unown = os.path.join(root, "graphics", "pokemon", "unown")
    for form, letter in enumerate(letters):
        save_sprite(os.path.join(unown, letter), unown, os.path.join(out, "sprites"), f"201-{form}")
    save_sprite(os.path.join(unown, "a"), unown, os.path.join(out, "sprites"), "201")
    # Castform keeps a sprite per weather form (PKHeX form order: normal, sunny, rainy, snowy).
    castform = os.path.join(root, "graphics", "pokemon", "castform")
    for form, name in enumerate(["normal", "sunny", "rainy", "snowy"]):
        folder = os.path.join(castform, name)
        if save_sprite(folder, folder, os.path.join(out, "sprites"), f"351-{form}") and form == 0:
            save_sprite(folder, folder, os.path.join(out, "sprites"), "351")
            sprites += 1
    egg = os.path.join(root, "graphics", "pokemon", "egg")
    save_sprite(egg, egg, os.path.join(out, "sprites"), "egg", shiny=False)
    print(f"{sprites} front sprites")

    storage = os.path.join(root, "graphics", "pokemon_storage")
    build_tilemap(os.path.join(storage, "scrolling_bg.png"), os.path.join(storage, "scrolling_bg.bin"),
                  read_jasc(os.path.join(storage, "scrolling_bg.pal")), 32).save(os.path.join(out, "background.png"))


def national_dex(root):
    """National dex number -> (graphics folder, SPECIES_ name), using pokeemerald's species/dex constants."""
    species_h = open(os.path.join(root, "include", "constants", "species.h"), encoding="utf-8").read()
    pokedex_h = open(os.path.join(root, "include", "constants", "pokedex.h"), encoding="utf-8").read()
    species = [n for n, _ in re.findall(r"#define SPECIES_(\w+)\s+(\d+)\b", species_h)]
    dex_names = re.findall(r"^\s*NATIONAL_DEX_(\w+),", pokedex_h, re.M)
    result = {}
    for number, name in enumerate(dex_names):  # dex_names[0] is NATIONAL_DEX_NONE
        if name == "NONE":
            continue
        if name not in species:
            continue
        folder = name.lower()
        if not os.path.isdir(os.path.join(root, "graphics", "pokemon", folder)):
            alt = folder.replace("_", "")
            folder = alt if os.path.isdir(os.path.join(root, "graphics", "pokemon", alt)) else folder
        result[number] = (folder, name)
    return result


if __name__ == "__main__":
    main()
