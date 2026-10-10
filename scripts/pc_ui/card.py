"""The Trainer Card art of Ruby/Sapphire, Emerald and FireRed/LeafGreen (src/trainer_card.c), for pkmgr-box --card.

Writes into the skin folder:
  card_front_<stars>_<m|f>.png, card_back_<stars>_<m|f>.png  the 240x160 screen: the background behind the card
      (whose palette is the female one for a girl) and the card itself, coloured by the star count (0-4)
  card_icon_<stars>_<m|f>.png  the back's 8x16 stat icon (Emerald, FireRed/LeafGreen)
  card_nodex_<stars>.png       Ruby/Sapphire: what covers the card's printed POKeDEX label before the Pokedex
  card_pic_<m|f>.png           the player's 64x64 picture
  card_badges.png              the eight 16x16 badges
  card_star.png                the 8x8 star
"""
import os

from PIL import Image

from pc_ui import gba

STARS = 5


def _image(w, h):
    return Image.new("RGBA", (w, h), (0, 0, 0, 0))


def _palettes(slots):
    """16 palettes from a {slot: 16 colours} dict (missing slots are None)."""
    return [slots.get(i) for i in range(16)]


def _pic(path, palette):
    w, h, data = gba.png_indices(path)
    img = _image(w, h)
    px = img.load()
    for i, c in enumerate(data):
        if c:
            px[i % w, i // w] = palette[c] + (255,)
    return img


def _tiles_image(tiles, numbers, palette, columns=1):
    """Tiles stacked into an image (columns wide), index 0 transparent."""
    rows = (len(numbers) + columns - 1) // columns
    img = _image(8 * columns, 8 * rows)
    for k, n in enumerate(numbers):
        gba.draw_tile(img, tiles[n], (k % columns) * 8, (k // columns) * 8, palette)
    return img


def _save(img, target, name):
    img.save(os.path.join(target, name))


def _gen3e_like(repo, target, star_files, pic_files, icon_slot):
    """Emerald and FireRed/LeafGreen (whose back icons use palette 0 and 1): BG2 bg.bin (palette 1) behind BG0 front.bin/back.bin (palette 0), both
    30x20 maps of tiles.png; slots 0-2 are the star count's 48-colour palette, slot 1 female_bg.pal for a girl."""
    tc = os.path.join(repo, "graphics", "trainer_card")
    tiles = gba.load_tiles(os.path.join(tc, "tiles.png"))
    bg = gba.read_tilemap(os.path.join(tc, "bg.bin"))
    front = gba.read_tilemap(os.path.join(tc, "front.bin"))
    back = gba.read_tilemap(os.path.join(tc, "back.bin"))
    female = gba.read_jasc(os.path.join(tc, "female_bg.pal"))[:16]
    star_pal = gba.read_jasc(os.path.join(tc, "star.pal"))[:16]
    for stars, name in enumerate(star_files):
        base = gba.split16(gba.read_jasc(os.path.join(tc, name)))
        for g in ("m", "f"):
            slots = {0: base[0], 1: female if g == "f" else base[1], 2: base[2]}
            pals = _palettes(slots)
            for side, card in (("front", front), ("back", back)):
                img = _image(240, 160)
                gba.draw_tilemap(img, bg, 30, tiles, pals, w=30, h=20)
                gba.draw_tilemap(img, card, 30, tiles, pals, w=30, h=20)
                _save(img, target, f"card_{side}_{stars}_{g}.png")
            _save(_tiles_image(tiles, [141, 157], slots[icon_slot]), target, f"card_icon_{stars}_{g}.png")
    _save(_tiles_image(tiles, [143], star_pal), target, "card_star.png")
    badges = os.path.join(tc, "badges.png")
    _save(_pic(badges, gba.png_palette(badges)), target, "card_badges.png")
    for g, (png, pal) in pic_files.items():
        _save(_pic(png, gba.read_jasc(pal)), target, f"card_pic_{g}.png")


def _rs(repo, target):
    """Ruby/Sapphire: BG3 trainer_card_background.map.bin behind BG2 trainer_card_front/back.map.bin (32x20 maps of
    trainer_card.png); BG2 is scrolled 4 pixels down. Slots 0-2 are trainer_card_<n>star.pal, slot 1 83B5F0C.pal
    for a girl; the stars use 83B5F4C.pal. The front's labels are part of the card."""
    tc = os.path.join(repo, "graphics", "trainer_card")
    tiles = gba.load_tiles(os.path.join(tc, "trainer_card.png"))
    bg = gba.read_tilemap(os.path.join(tc, "trainer_card_background.map.bin"))
    front = gba.read_tilemap(os.path.join(tc, "trainer_card_front.map.bin"))
    back = gba.read_tilemap(os.path.join(tc, "trainer_card_back.map.bin"))
    female = gba.read_jasc(os.path.join(tc, "83B5F0C.pal"))[:16]
    star_pal = gba.read_jasc(os.path.join(tc, "83B5F4C.pal"))[:16]
    for stars in range(STARS):
        base = gba.split16(gba.read_jasc(os.path.join(tc, f"trainer_card_{stars}star.pal")))
        for g in ("m", "f"):
            slots = {0: base[0], 1: female if g == "f" else base[1], 2: base[2] if len(base) > 2 else None}
            pals = _palettes(slots)
            for side, card in (("front", front), ("back", back)):
                img = _image(240, 160)
                gba.draw_tilemap(img, bg, 32, tiles, pals, w=30, h=20)
                layer = _image(240, 168)
                gba.draw_tilemap(layer, card, 32, tiles, pals, w=30, h=20)
                img.alpha_composite(layer.crop((0, 0, 240, 156)), (0, 4))
                _save(img, target, f"card_{side}_{stars}_{g}.png")
        # TrainerCard_ClearPokedexLabel: BG2 columns 3-16 of rows 10-11 become tile 1
        patch = _image(112, 16)
        for j in range(2):
            for i in range(14):
                gba.draw_tile(patch, tiles[1], i * 8, j * 8, base[0], opaque0=True)
        _save(patch, target, f"card_nodex_{stars}.png")
    _save(_tiles_image(tiles, [0x8F], star_pal), target, "card_star.png")
    badges = os.path.join(tc, "badges.png")
    _save(_pic(badges, gba.png_palette(badges)), target, "card_badges.png")
    for g, name in (("m", "brendan.png"), ("f", "may.png")):
        png = os.path.join(repo, "graphics", "trainers", name)
        _save(_pic(png, gba.png_palette(png)), target, f"card_pic_{g}.png")


def build(game, repo, target):
    """Writes the card art for the game; False when the decomp doesn't have it."""
    try:
        if game == "e":
            pics = os.path.join(repo, "graphics", "trainers")
            _gen3e_like(repo, target, ["green.pal", "bronze.pal", "copper.pal", "silver.pal", "gold.pal"],
                        {"m": (os.path.join(pics, "front_pics", "brendan.png"), os.path.join(pics, "palettes", "brendan.pal")),
                         "f": (os.path.join(pics, "front_pics", "may.png"), os.path.join(pics, "palettes", "may.pal"))}, 0)
        elif game == "frlg":
            pics = os.path.join(repo, "graphics", "trainers")
            _gen3e_like(repo, target, ["blue.pal", "green.pal", "bronze.pal", "silver.pal", "gold.pal"],
                        {"m": (os.path.join(pics, "front_pics", "red_front_pic.png"), os.path.join(pics, "palettes", "red.pal")),
                         "f": (os.path.join(pics, "front_pics", "leaf_front_pic.png"), os.path.join(pics, "palettes", "leaf.pal"))}, 1)
        elif game == "rs":
            _rs(repo, target)
        else:
            return False
    except (OSError, ValueError, IndexError) as e:
        print(f"pc_ui: no trainer card for {game} ({e})")
        return False
    return True
