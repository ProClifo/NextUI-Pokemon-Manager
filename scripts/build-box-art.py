#!/usr/bin/env python3
"""Builds each game's own PC box icons and front sprites from the pret decompilations.

Usage: scripts/build-box-art.py <decomp dir> <output dir> [preview.png]

<decomp dir> holds checkouts of pret/pokered, pokeyellow, pokegold, pokecrystal, pokeruby, pokeemerald,
pokefirered and pokeplatinum. Each art set is packed into two sheets, so the SD card holds a few files
rather than thousands: <output dir>/<set>/icons.png, sprites.png and index.json, which gives each image's
place in its sheet:

  icons   "<dex>[-<form>]" and "egg": [x, y] of the 32x64 icon (both animation frames)
  sprites "<dex>[-<form>][-f][-shiny]" and "egg": [x, y, w, h] of the front sprite (trimmed; -f = female)

Sets: rb (Red/Blue), y (Yellow), gold, silver, c (Crystal), rs (Ruby/Sapphire), e (Emerald, the fallback
for anything a set lacks), frlg (FireRed/LeafGreen; LeafGreen's Deoxys is 386-leafgreen) and pt
(Platinum, used for all of Gen 4).

Game Boy art is drawn the way the games do: Red/Blue/Yellow sprites in each species' Super Game Boy
palette and their party icons in grey (Gen 1 icons are assembled from overworld sprite tiles, mirrored
like the game's OAM code); Gold/Silver/Crystal sprites in their Game Boy Color palettes and party icons
in the party menu's palette. Game Boy icons stay 16x16, at the bottom of a Gen 3 sized cell. Requires Pillow.
"""
import json
import os
import re
import sys

from PIL import Image

# ------------------------------------------------------------------ helpers


def read_jasc(path):
    lines = open(path, encoding="utf-8").read().split()
    count = int(lines[2])
    vals = list(map(int, lines[3:3 + count * 3]))
    return [tuple(vals[i * 3:i * 3 + 3]) for i in range(count)]


def gb_rgb(values):
    """Game Boy Color RGB (0-31 per channel) to 8-bit."""
    return tuple(v * 255 // 31 for v in values)


def indexed(path, palette=None, transparent=0):
    """An indexed PNG as RGBA, with colour index `transparent` see-through."""
    img = Image.open(path)
    pal = palette or [tuple(img.getpalette()[i * 3:i * 3 + 3]) for i in range(len(img.getpalette()) // 3)]
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    src, dst = img.load(), out.load()
    for y in range(img.height):
        for x in range(img.width):
            c = src[x, y]
            if c != transparent and c < len(pal):
                dst[x, y] = pal[c] + (255,)
    return out


def gray4(path, colors, keep_white=False):
    """A 4-shade Game Boy image (white, light, dark, black) in the given 4 colours. White is see-through,
    or with keep_white only the white around the picture (see clear_background)."""
    img = Image.open(path).convert("L")
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    src, dst = img.load(), out.load()
    for y in range(img.height):
        for x in range(img.width):
            shade = 3 - round(src[x, y] / 85)  # 255 -> 0 (white) ... 0 -> 3 (black)
            if shade or keep_white:
                dst[x, y] = colors[shade] + (255,)
    return clear_background(out) if keep_white else out


def clear_background(img):
    """Makes the background see-through: pixels of the corner's colour connected to the edge. Game Boy
    sprites have no transparent colour; white inside a Pokémon is drawn white, only the surround isn't."""
    px = img.load()
    w, h = img.size
    background = px[0, 0]
    stack = [(x, y) for x in range(w) for y in (0, h - 1)] + [(x, y) for y in range(h) for x in (0, w - 1)]
    seen = set()
    while stack:
        x, y = stack.pop()
        if (x, y) in seen or not (0 <= x < w and 0 <= y < h) or px[x, y] != background:
            continue
        seen.add((x, y))
        px[x, y] = (0, 0, 0, 0)
        stack.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
    return img


def trimmed(img):
    box = img.getbbox()
    return img.crop(box) if box else img


STORE = {}


def save(img, path):
    """Collects an image for its set's sheets (path = <set dir>/<icons|sprites>/<name>.png)."""
    set_dir, kind = os.path.dirname(os.path.dirname(path)), os.path.basename(os.path.dirname(path))
    STORE.setdefault(set_dir, {"icons": {}, "sprites": {}})[kind][os.path.basename(path)[:-4]] = img.copy()


def pack(set_dir, images):
    """Writes icons.png (32x64 cells, 16 across), sprites.png (shelf-packed) and index.json."""
    os.makedirs(set_dir, exist_ok=True)
    index = {"icons": {}, "sprites": {}}
    names = sorted(images["icons"])
    sheet = Image.new("RGBA", (16 * 32, max(1, (len(names) + 15) // 16) * 64), (0, 0, 0, 0))
    for i, name in enumerate(names):
        x, y = (i % 16) * 32, (i // 16) * 64
        sheet.alpha_composite(images["icons"][name], (x, y))
        index["icons"][name] = [x, y]
    sheet.save(os.path.join(set_dir, "icons.png"), optimize=True)

    width, x, y, row = 1024, 0, 0, 0
    places = {}
    for name in sorted(images["sprites"], key=lambda n: (-images["sprites"][n].height, n)):
        img = images["sprites"][name]
        if x + img.width > width:
            x, y, row = 0, y + row, 0
        places[name] = (x, y)
        x, row = x + img.width, max(row, img.height)
    sheet = Image.new("RGBA", (width, max(1, y + row)), (0, 0, 0, 0))
    for name, (px, py) in places.items():
        img = images["sprites"][name]
        sheet.alpha_composite(img, (px, py))
        index["sprites"][name] = [px, py, img.width, img.height]
    sheet.save(os.path.join(set_dir, "sprites.png"), optimize=True)
    with open(os.path.join(set_dir, "index.json"), "w", encoding="utf-8") as f:
        json.dump(index, f, separators=(",", ":"))


def icon_frames(frame_a, frame_b, top=None):
    """Two frames stacked into the 32x64 icon the viewer animates, in 32x32 cells: centred, or centred
    across and `top` pixels down (Game Boy icons sit low in the cell, where a Gen 3 icon's Pokémon is)."""
    out = Image.new("RGBA", (32, 64), (0, 0, 0, 0))
    for i, frame in enumerate((frame_a, frame_b)):
        y = (32 - frame.height) // 2 if top is None else top
        out.alpha_composite(frame, ((32 - frame.width) // 2, i * 32 + y))
    return out


GB_ICON_TOP = 12


def key(name):
    return re.sub(r"[^a-z0-9]", "", name.lower())


def national_dex(species_txt):
    """Dex number by normalised species name, from pokeplatinum generated/species.txt (dex order)."""
    names = [l.strip()[len("SPECIES_"):] for l in open(species_txt, encoding="utf-8") if l.startswith("SPECIES_")]
    return {key(n): i for i, n in enumerate(names) if i and n not in ("EGG", "BAD_EGG")}


def folders_by_dex(root, dex):
    """Species folders of a decomp's graphics directory, by dex number."""
    found = {}
    for name in os.listdir(root):
        number = dex.get(key(name))
        if number is not None and os.path.isdir(os.path.join(root, name)) and number not in found:
            found[number] = os.path.join(root, name)
    return found


# ------------------------------------------------------------------ Game Boy


def sgb_palettes(src):
    """PAL_ name -> 4 colours from the SuperPalettes in data/sgb/sgb_palettes.asm (the Super Game Boy colours;
    Yellow's Game Boy Color palettes are built from the DMG shades at run time and come out washed out here)."""
    pals = {}
    lines = open(os.path.join(src, "data/sgb/sgb_palettes.asm"), encoding="utf-8").read().splitlines()
    if any(l.startswith("CGBBasePalettes:") for l in lines):
        lines = lines[:next(i for i, l in enumerate(lines) if l.startswith("CGBBasePalettes:"))]
    for line in lines:
        m = re.match(r"\s*RGB\s+([\d,\s]+);\s*(PAL_\w+)", line)
        if m and m.group(2) not in pals:
            v = list(map(int, re.findall(r"\d+", m.group(1))))
            if len(v) == 12:
                pals[m.group(2)] = [gb_rgb(v[i:i + 3]) for i in range(0, 12, 3)]
    return pals


def gen1(src, out, dex):
    """Red/Blue or Yellow: SGB-coloured front sprites and party icons."""
    pals = sgb_palettes(src)
    order = [m.group(1) for m in re.finditer(r"db (PAL_\w+)\s*;\s*(\S+)", open(os.path.join(src, "data/pokemon/palettes.asm"), encoding="utf-8").read())]
    # MonsterPalettes starts with MISSINGNO, then dex order.
    species_pal = {n: pals[p] for n, p in enumerate(order) if n and p in pals}
    sprites = 0
    front = os.path.join(src, "gfx/pokemon/front")
    for file in os.listdir(front):
        number = dex.get(key(file[:-4]))
        if number and number <= 151:
            colors = species_pal.get(number, pals["PAL_MEWMON"])
            save(trimmed(gray4(os.path.join(front, file), colors, keep_white=True)), os.path.join(out, "sprites", f"{number}.png"))
            sprites += 1

    types = re.findall(r"nybble ICON_(\w+)", open(os.path.join(src, "data/pokemon/menu_icons.asm"), encoding="utf-8").read())
    ink = [(255, 255, 255), (168, 168, 168), (88, 88, 88), (24, 24, 24)]

    def sheet_tiles(path):
        img = gray4(path, ink)
        return [img.crop((x, y, x + 8, y + 8)) for y in range(0, img.height, 8) for x in range(0, img.width, 8)]

    def symmetric(top, bottom):
        """The game's symmetric icon OAM: the left column of tiles, mirrored onto the right."""
        frame = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
        frame.alpha_composite(top, (0, 0))
        frame.alpha_composite(top.transpose(Image.FLIP_LEFT_RIGHT), (8, 0))
        frame.alpha_composite(bottom, (0, 8))
        frame.alpha_composite(bottom.transpose(Image.FLIP_LEFT_RIGHT), (8, 8))
        return frame

    # data/icon_pointers.asm: the tiles each icon loads for its two frames (the second block is
    # ICONOFFSET, frame B). Whole overworld sprites (4 tiles: TL, TR, BL, BR) and the 1-tile-wide icons
    # (gfx/icons, one tile per call) are both drawn with the symmetric OAM: left column, mirrored.
    sprites_asm = open(os.path.join(src, "gfx/sprites.asm"), encoding="utf-8").read()
    sources = {m.group(1): m.group(2) for m in re.finditer(r'(\w+)::\s*INCBIN "(gfx/[\w/]+)\.2bpp"', sprites_asm)}
    for m in re.finditer(r'(\w+IconFrame[12]):\s*INCBIN "(gfx/icons/\w+)\.2bpp",\s*INC_FRAME_([12])',
                         open(os.path.join(src, "engine/gfx/mon_icons.asm"), encoding="utf-8").read()):
        sources[m.group(1)] = (m.group(2), int(m.group(3)) - 1)
    loaded = {}
    for m in re.finditer(r"mon_icon_header\s+(\w+),\s*(\d+),\s*(\d+),\s*(ICONOFFSET \+ )?ICON_(\w+) << 2( \+ 2)?",
                         open(os.path.join(src, "data/icon_pointers.asm"), encoding="utf-8").read()):
        label, offset, count, frame_b, icon, second = m.group(1), int(m.group(2)), int(m.group(3)), bool(m.group(4)), m.group(5), bool(m.group(6))
        source = sources.get(label)
        if source is None:
            continue
        slot = loaded.setdefault(icon, [[None] * 4, [None] * 4])[1 if frame_b else 0]
        if isinstance(source, tuple):  # one tile of a 1-tile-wide icon: top (slot 0) or bottom (slot 2)
            path, frame = source
            tiles = sheet_tiles(os.path.join(src, f"{path}.png"))
            slot[2 if second else 0] = tiles[frame * 2 + offset]
        else:
            tiles = sheet_tiles(os.path.join(src, f"{source}.png"))
            for i in range(min(count, 4)):
                if offset + i < len(tiles):
                    slot[i] = tiles[offset + i]
    # The Poké Ball icon loads 8 tiles, running on into the fossil sprite: those are the helix icon's.
    if "HELIX" not in loaded and "PokeBallSprite" in sources and "FossilSprite" in sources:
        fossil_tiles = sheet_tiles(os.path.join(src, f"{sources['FossilSprite']}.png"))
        loaded["HELIX"] = [fossil_tiles[:4], fossil_tiles[:4]]
    frames = {}
    for icon, (a, b) in loaded.items():
        if icon == "HELIX":  # the only asymmetric icon: TL, TR, BL, BR
            def whole(t):
                f = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
                for i, tile in enumerate(t):
                    if tile is not None:
                        f.alpha_composite(tile, ((i % 2) * 8, (i // 2) * 8))
                return f
            frames[icon] = (whole(a), whole(a))
        elif a[0] is not None and a[2] is not None:
            fb = b if b[0] is not None and b[2] is not None else a
            frames[icon] = (symmetric(a[0], a[2]), symmetric(fb[0], fb[2]))
    # Ball and fossil icons only bob up and down.
    for icon in ("BALL", "HELIX"):
        if icon not in frames:
            continue
        a = frames[icon][0]
        b = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
        b.alpha_composite(a.crop((0, 0, 16, 15)), (0, 1))
        frames[icon] = (a, b)
    for number, icon in enumerate(types, start=1):
        if icon in frames:
            save(icon_frames(*frames[icon], top=GB_ICON_TOP), os.path.join(out, "icons", f"{number}.png"))
    return sprites


def gen2(src, out, dex, front_file):
    """Gold, Silver or Crystal: coloured front sprites (and shiny palettes) and party icons."""
    gfx = os.path.join(src, "gfx/pokemon")
    sprites = 0
    for number, folder in folders_by_dex(gfx, dex).items():
        if number > 251:
            continue
        path = os.path.join(folder, front_file)
        if not os.path.exists(path):
            path = os.path.join(folder, "front.png")  # Gold and Silver share sprites that didn't change
        if not os.path.exists(path):
            continue
        img = Image.open(path)
        size = img.width  # Crystal stacks its animation frames vertically; the first is square
        normal = [tuple(img.getpalette()[i * 3:i * 3 + 3]) for i in range(4)]
        frame = clear_background(indexed(path, normal, transparent=None).crop((0, 0, size, size)))
        name = f"{number}" if os.path.basename(folder) != "unown" else "201"
        save(trimmed(frame), os.path.join(out, "sprites", f"{name}.png"))
        shiny_pal = os.path.join(folder, "shiny.pal")
        if os.path.exists(shiny_pal):
            light, dark = [gb_rgb(map(int, re.findall(r"\d+", l))) for l in open(shiny_pal) if "RGB" in l][:2]
            shiny = clear_background(indexed(path, [normal[0], light, dark, normal[3]], transparent=None).crop((0, 0, size, size)))
            save(trimmed(shiny), os.path.join(out, "sprites", f"{name}-shiny.png"))
        sprites += 1
    # Unown letters (gfx/pokemon/unown_a..z), forms 0-25.
    for form in range(26):
        folder = os.path.join(gfx, f"unown_{chr(97 + form)}")
        path = os.path.join(folder, front_file)
        if not os.path.exists(path):
            path = os.path.join(folder, "front.png")
        if os.path.exists(path):
            img = Image.open(path)
            normal = [tuple(img.getpalette()[i * 3:i * 3 + 3]) for i in range(4)]
            letter = trimmed(clear_background(indexed(path, normal, transparent=None).crop((0, 0, img.width, img.width))))
            save(letter, os.path.join(out, "sprites", f"201-{form}.png"))
            if form == 0:
                save(letter, os.path.join(out, "sprites", "201.png"))
    egg = next((os.path.join(gfx, "egg", f) for f in (front_file, "front.png", "egg.png") if os.path.exists(os.path.join(gfx, "egg", f))), "")
    if os.path.exists(egg):
        img = Image.open(egg)
        normal = [tuple(img.getpalette()[i * 3:i * 3 + 3]) for i in range(4)] if img.mode == "P" else None
        frame = indexed(egg, normal, transparent=None) if normal else gray4(egg, [(255, 255, 255), (168, 168, 168), (88, 88, 88), (24, 24, 24)], keep_white=True)
        save(trimmed(clear_background(frame.crop((0, 0, img.width, img.width)))), os.path.join(out, "sprites", "egg.png"))

    # The party menu draws icons in the first of its object palettes.
    pal = re.findall(r"RGB\s+(\d+),\s*(\d+),\s*(\d+)", open(os.path.join(src, "gfx/stats/party_menu_ob.pal"), encoding="utf-8").read())
    ink = [gb_rgb(list(map(int, c))) for c in pal[:4]]
    icons = re.findall(r"db ICON_(\w+)", open(os.path.join(src, "data/pokemon/menu_icons.asm"), encoding="utf-8").read())
    for number, icon in enumerate(icons, start=1):
        path = os.path.join(src, "gfx/icons", f"{icon.lower()}.png")
        if os.path.exists(path):
            img = gray4(path, ink)
            save(icon_frames(img.crop((0, 0, 16, 16)), img.crop((0, 16, 16, 32)), top=GB_ICON_TOP), os.path.join(out, "icons", f"{number}.png"))
    egg_icon = os.path.join(src, "gfx/icons/egg.png")
    if os.path.exists(egg_icon):
        img = gray4(egg_icon, ink)
        save(icon_frames(img.crop((0, 0, 16, 16)), img.crop((0, 16, 16, 32)) if img.height >= 32 else img.crop((0, 0, 16, 16)), top=GB_ICON_TOP),
             os.path.join(out, "icons", "egg.png"))
    return sprites


# ------------------------------------------------------------------ GBA


def gen3(root, out, dex, icon_pals, icon_index):
    """Ruby/Sapphire or FireRed/LeafGreen: front sprites in normal and shiny palettes, and box icons."""
    gfx = os.path.join(root, "graphics/pokemon")
    sprites = 0
    for number, folder in folders_by_dex(gfx, dex).items():
        if number > 386:
            continue
        name = os.path.basename(folder)
        if gba_sprite(folder, "front.png", folder, out, str(number)):
            sprites += 1
        icon = os.path.join(folder, "icon.png")
        if os.path.exists(icon):
            save(indexed(icon, icon_pals[icon_index.get(key(name), 0)]), os.path.join(out, "icons", f"{number}.png"))
    unown = os.path.join(gfx, "unown")
    letters = [chr(c) for c in range(ord("a"), ord("z") + 1)] + ["exclamation_mark", "question_mark"]
    for form, letter in enumerate(letters):
        # pokefirered: unown/<letter>/front.png; pokeruby: unown/front_<letter>.png
        for folder, file in ((os.path.join(unown, letter), "front.png"), (unown, f"front_{letter}.png")):
            if gba_sprite(folder, file, unown, out, f"201-{form}"):
                break
        icon_path = next((p for p in (os.path.join(unown, letter, "icon.png"), os.path.join(unown, f"icon_{letter}.png")) if os.path.exists(p)), None)
        if icon_path:
            save(indexed(icon_path, icon_pals[icon_index.get("unown", 0)]), os.path.join(out, "icons", f"201-{form}.png"))
    castform = os.path.join(gfx, "castform")
    for form, weather in enumerate(["normal", "sunny", "rainy", "snowy"]):
        for folder, file in ((os.path.join(castform, weather), "front.png"), (castform, f"front_{weather}_form.png")):
            if gba_sprite(folder, file, folder if os.path.isdir(folder) and os.path.exists(os.path.join(folder, "normal.pal")) else castform, out, f"351-{form}"):
                break
    # Plain 201/351 are Unown A and normal Castform.
    store = STORE.get(out, {"icons": {}, "sprites": {}})
    for base, first in (("201", "201-0"), ("351", "351-0")):
        for kind in ("icons", "sprites"):
            for suffix in ("", "-shiny"):
                if base + suffix not in store[kind] and first + suffix in store[kind]:
                    save(store[kind][first + suffix], os.path.join(out, kind, f"{base}{suffix}.png"))
    deoxys = os.path.join(gfx, "deoxys")
    gba_sprite(deoxys, "front_def.png", deoxys, out, "386-leafgreen")
    egg = os.path.join(gfx, "egg")
    if not gba_sprite(egg, "front.png", egg, out, "egg", shiny=False) and os.path.exists(os.path.join(egg, "pic.png")):
        # pokeruby: egg/pic.png with egg/palette.pal
        save(trimmed(indexed(os.path.join(egg, "pic.png"), read_jasc(os.path.join(egg, "palette.pal"))).crop((0, 0, 64, 64))),
             os.path.join(out, "sprites", "egg.png"))
    egg_icon = os.path.join(egg, "icon.png")
    if os.path.exists(egg_icon):
        save(indexed(egg_icon, icon_pals[icon_index.get("egg", 1)]), os.path.join(out, "icons", "egg.png"))
    return sprites


def gba_sprite(folder, file, pal_dir, out, name, shiny=True):
    path = os.path.join(folder, file)
    if not os.path.exists(path):
        alt = os.path.join(folder, "anim_front.png")
        if file == "front.png" and os.path.exists(alt):
            path = alt
        else:
            return False
    wrote = False
    for pal_file, suffix in [("normal.pal", "")] + ([("shiny.pal", "-shiny")] if shiny else []):
        pal_path = os.path.join(pal_dir, pal_file)
        if os.path.exists(pal_path):
            img = indexed(path, read_jasc(pal_path)).crop((0, 0, 64, 64))
            save(trimmed(img), os.path.join(out, "sprites", f"{name}{suffix}.png"))
            wrote = True
    return wrote


def gen3_icon_palettes(root):
    pals = [read_jasc(os.path.join(root, "graphics/pokemon/icon_palettes", f"icon_palette_{i}.pal")) for i in range(3)]
    text = open(os.path.join(root, "src/pokemon_icon.c"), encoding="utf-8").read()
    table = text[text.index("gMonIconPaletteIndices"):]
    return pals, {key(m.group(1)): int(m.group(2)) for m in re.finditer(r"\[SPECIES_(\w+)\]\s*=\s*(\d)", table)}


# ------------------------------------------------------------------ NDS

# PKHeX form order -> Platinum form folder ("base" is form 0).
PT_FORMS = {
    201: ["base"] + [chr(c) for c in range(ord("b"), ord("z") + 1)] + ["exc", "que"],
    351: ["base", "sunny", "rainy", "snowy"],
    386: ["base", "attack", "defense", "speed"],
    412: ["base", "sandy", "trash"],
    413: ["base", "sandy", "trash"],
    421: ["base", "sunny"],
    422: ["base", "east_sea"],
    423: ["base", "east_sea"],
    479: ["base", "heat", "wash", "frost", "fan", "mow"],
    487: ["base", "origin"],
    492: ["base", "sky"],
    493: ["base", "fighting", "flying", "poison", "ground", "rock", "bug", "ghost", "steel", "mystery",
          "fire", "water", "grass", "electric", "psychic", "ice", "dragon", "dark"],
}


def gen4(root, out, dex):
    """Platinum: front sprites (first frame, female variants, shiny palettes), box icons and forms."""
    gfx = os.path.join(root, "res/pokemon")
    shared = read_jasc(os.path.join(gfx, ".shared", "pl_poke_icon.pal"))
    icon_pals = [shared[i * 16:(i + 1) * 16] for i in range(len(shared) // 16)]
    sprites = 0

    def icon_palette(folder, fallback, form="base"):
        """data.json's icon_palette: a number, or [[form, palette], ...] for species whose forms differ."""
        data = os.path.join(folder, "data.json")
        if os.path.exists(data):
            try:
                value = json.load(open(data, encoding="utf-8")).get("icon_palette", fallback)
            except ValueError:
                return fallback
            if isinstance(value, list):
                return dict((f, p) for f, p in value).get(form, fallback)
            return value
        return fallback

    def sprite(folder, pal_dir, file, name):
        path = os.path.join(folder, file)
        if not os.path.exists(path):
            return False
        for pal_file, suffix in (("normal.pal", ""), ("shiny.pal", "-shiny")):
            pal_path = os.path.join(pal_dir, pal_file)
            if os.path.exists(pal_path):
                img = Image.open(path)
                frame = indexed(path, read_jasc(pal_path)).crop((0, 0, img.height, img.height))  # frames side by side
                save(trimmed(frame), os.path.join(out, "sprites", f"{name}{suffix}.png"))
        return True

    for number, folder in folders_by_dex(gfx, dex).items():
        if number > 493:
            continue
        pal = icon_palette(folder, 0)
        # Female-only species (Nidoran F, Chansey, Wormadam...) only have a female sprite.
        if (sprite(folder, folder, "male_front.png", str(number)) or sprite(folder, folder, "front.png", str(number))
                or sprite(folder, folder, "female_front.png", str(number))):
            sprites += 1
        sprite(folder, folder, "female_front.png", f"{number}-f")
        if os.path.exists(os.path.join(folder, "icon.png")):
            save(indexed(os.path.join(folder, "icon.png"), icon_pals[pal]), os.path.join(out, "icons", f"{number}.png"))
        for form, form_name in enumerate(PT_FORMS.get(number, [])):
            if form == 0:
                continue
            form_dir = os.path.join(folder, "forms", form_name)
            if not os.path.isdir(form_dir):
                continue
            pal_dir = form_dir if os.path.exists(os.path.join(form_dir, "normal.pal")) else folder
            sprite(form_dir, pal_dir, "front.png", f"{number}-{form}") or sprite(form_dir, pal_dir, "male_front.png", f"{number}-{form}")
            if os.path.exists(os.path.join(form_dir, "icon.png")):
                save(indexed(os.path.join(form_dir, "icon.png"), icon_pals[icon_palette(folder, icon_palette(form_dir, pal), form_name)]),
                     os.path.join(out, "icons", f"{number}-{form}.png"))
    egg = os.path.join(gfx, "egg")
    sprite(egg, egg, "front.png", "egg") or sprite(egg, egg, "male_front.png", "egg")
    if os.path.exists(os.path.join(egg, "icon.png")):
        save(indexed(os.path.join(egg, "icon.png"), icon_pals[icon_palette(egg, 1)]), os.path.join(out, "icons", "egg.png"))
    return sprites


# ------------------------------------------------------------------ main


def preview(path, species=("25", "1", "7", "150", "201-7")):
    """A few species' icons and sprites from every set, for checking."""
    sets = sorted(STORE)
    sheet = Image.new("RGBA", (len(species) * 90 + 60, len(sets) * 150), (200, 216, 232, 255))
    for row, set_dir in enumerate(sets):
        for col, name in enumerate(species):
            icon = STORE[set_dir]["icons"].get(name)
            sprite = STORE[set_dir]["sprites"].get(name)
            if icon:
                sheet.alpha_composite(icon.crop((0, 0, 32, 32)), (col * 90 + 4, row * 150 + 4))
            if sprite:
                sheet.alpha_composite(sprite, (col * 90 + 4, row * 150 + 40))
    sheet.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST).save(path)


def main():
    src, out = sys.argv[1], sys.argv[2]
    dex = national_dex(os.path.join(src, "pokeplatinum/generated/species.txt"))
    emerald_pals, emerald_index = gen3_icon_palettes(os.path.join(src, "pokeemerald"))
    counts = {
        "rb": gen1(os.path.join(src, "pokered"), os.path.join(out, "rb"), dex),
        "y": gen1(os.path.join(src, "pokeyellow"), os.path.join(out, "y"), dex),
        "gold": gen2(os.path.join(src, "pokegold"), os.path.join(out, "gold"), dex, "front_gold.png"),
        "silver": gen2(os.path.join(src, "pokegold"), os.path.join(out, "silver"), dex, "front_silver.png"),
        "c": gen2(os.path.join(src, "pokecrystal"), os.path.join(out, "c"), dex, "front.png"),
        # Ruby/Sapphire's box icons and icon palettes are Emerald's.
        "rs": gen3(os.path.join(src, "pokeruby"), os.path.join(out, "rs"), dex, emerald_pals, emerald_index),
        "e": gen3(os.path.join(src, "pokeemerald"), os.path.join(out, "e"), dex, emerald_pals, emerald_index),
        "frlg": gen3(os.path.join(src, "pokefirered"), os.path.join(out, "frlg"), dex, *gen3_icon_palettes(os.path.join(src, "pokefirered"))),
        "pt": gen4(os.path.join(src, "pokeplatinum"), os.path.join(out, "pt"), dex),
    }
    if len(sys.argv) > 3:
        preview(sys.argv[3])
    for set_dir, images in STORE.items():
        pack(set_dir, images)
    print("box art: " + ", ".join(f"{k} ({v} sprites)" for k, v in counts.items()))


if __name__ == "__main__":
    main()
