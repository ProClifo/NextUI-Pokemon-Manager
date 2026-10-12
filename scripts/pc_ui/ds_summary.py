"""Platinum's summary screen (pret/pokeplatinum src/applications/pokemon_summary_screen) for the DS skins: the
pages' backgrounds (res/graphics/pokemon_summary_screen: tiles_main.png + .pal, page_*.NSCR) with the page tabs
baked in, the Gen 4 type icons and Poké Balls, and the layout of the text the game prints over them (window.c's
window templates; TEXT_COLOR(fg, shadow, bg) on BG palette 15).

Pages: POKéMON INFO, TRAINER MEMO, POKéMON SKILLS, BATTLE MOVES (the viewer's; Condition, Contest Moves and
Ribbons aren't shown). The tab bar shows those four, placed as PokemonSummaryScreen_UpdatePageTabSprites does.
"""
import json
import os

from PIL import Image

from pc_ui import nds

SCREEN = (256, 192)
# main.h: SUMMARY_TEXT_* = TEXT_COLOR(fg, shadow, bg) on BG palette 15 of tiles_main.pal
BLACK, WHITE, BLUE, RED = (1, 2), (15, 14), (3, 4), (5, 6)
# The viewer's pages: (name, NSCR, tab animation = SUMMARY_PAGE_*, title)
PAGES = [("info", "page_info", 0, "POKéMON INFO"), ("memo", "page_memo", 1, "TRAINER MEMO"),
         ("skills", "page_skills", 2, "POKéMON SKILLS"), ("moves", "page_battle_moves", 3, "BATTLE MOVES")]
# sprites.c: CENTERED_TAB_POS_X, FOCUSED_TAB_WIDTH, UNFOCUSED_TAB_WIDTH; the tabs' y (sSummaryScreenSpriteTemplates)
TAB_CENTRE, TAB_FOCUSED, TAB_UNFOCUSED, TAB_Y = 188, 24, 16, 24
# sprites.c sBallIDToPaletteNum (balls_<n>.pal) for the balls' files
BALLS = {"master": 0, "ultra": 2, "great": 2, "poke": 0, "safari": 1, "net": 1, "dive": 1, "nest": 1, "repeat": 2,
         "timer": 2, "luxury": 2, "premier": 2, "dusk": 3, "heal": 3, "quick": 2, "cherish": 0}
# type_icon.c sMoveTypeIconPaletteIndex (rows of type_icons/shared.pal)
TYPE_PAL = {"normal": 0, "fighting": 0, "flying": 1, "poison": 1, "ground": 0, "rock": 0, "bug": 2, "ghost": 1,
            "steel": 0, "mystery": 2, "fire": 0, "water": 1, "grass": 2, "electric": 0, "psychic": 1, "ice": 1,
            "dragon": 2, "dark": 0}


def _jasc(path):
    lines = open(path, encoding="ascii").read().split("\n")[3:]
    return [tuple(int(v) for v in line.split()) for line in lines if line.strip()]


class _Chars:
    """An indexed PNG's 8x8 tiles in reading order, as nds.render_screen takes them (4 bits per pixel)."""

    def __init__(self, path):
        im = Image.open(path)
        w, h = im.size
        px = im.load()
        self.bpp = 4
        self.tiles = [[px[tx * 8 + i, ty * 8 + j] & 15 for j in range(8) for i in range(8)]
                      for ty in range(h // 8) for tx in range(w // 8)]


def _indexed(path, palette):
    """An indexed PNG drawn with a 16-colour palette, colour 0 see-through."""
    im = Image.open(path)
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    src, dst = im.load(), out.load()
    for y in range(im.height):
        for x in range(im.width):
            c = src[x, y] & 15
            if c:
                dst[x, y] = tuple(palette[c]) + (255,)
    return out


def _tabs(img, g, current, tab_chars, sprite_pal):
    """PokemonSummaryScreen_UpdatePageTabSprites for the four pages: the current one focused (24 wide)."""
    base = TAB_CENTRE - (TAB_FOCUSED + (len(PAGES) - 1) * TAB_UNFOCUSED) // 2
    cells = json.load(open(os.path.join(g, "tabs_cell.json")))["cells"]
    for k, (_, _, anim, _) in enumerate(PAGES):
        x = base + k * TAB_UNFOCUSED if current >= k else base + TAB_FOCUSED + (k - 1) * TAB_UNFOCUSED
        cell = cells[anim + 8 if current == k else anim]
        for oam in cell["OAM"]:
            ox, oy = oam["Attr1"]["XCoordinate"], oam["Attr0"]["YCoordinate"]
            ox, oy = ox - 512 if ox >= 256 else ox, oy - 256 if oy >= 128 else oy
            # shape 0 size 1 = 16x16, shape 2 size 0 = 8x16 (vertical)
            w, h = (16, 16) if oam["Attr0"]["Shape"] == 0 else (8, 16)
            pal = sprite_pal[oam["Attr2"]["Palette"] * 16:oam["Attr2"]["Palette"] * 16 + 16]
            t = oam["Attr2"]["CharName"]
            for ty in range(h // 8):
                for tx in range(w // 8):
                    nds.draw_tile(img, tab_chars.tiles[t], x + ox + tx * 8, TAB_Y + oy + ty * 8, pal)
                    t += 1


def _bar(chars, colors, base_tile, palette):
    """The rows and colour a bar's tiles fill (tile base + n has n of its 8 columns filled)."""
    empty, full = chars.tiles[base_tile], chars.tiles[base_tile + 8]
    rows = sorted({k // 8 for k in range(64) if empty[k] != full[k]})
    if not rows:
        return 0, 0, [0, 0, 0]
    k = rows[0] * 8 + 4
    return rows[0], len(rows), list(colors[palette * 16 + full[k]])


def build(platinum_repo, target, text_width):
    """Writes sum_<page>.png, types/, balls/, sum_lv.png into target; returns the layout's "summary"."""
    g = os.path.join(platinum_repo, "res", "graphics", "pokemon_summary_screen")
    if not os.path.isdir(g):
        return None
    chars = _Chars(os.path.join(g, "tiles_main.png"))
    colors = _jasc(os.path.join(g, "tiles_main.pal"))
    sprite_pal = _jasc(os.path.join(g, "sprites.pal"))
    tab_chars = _Chars(os.path.join(g, "tabs.png"))
    p15 = colors[15 * 16:16 * 16]

    def tc(c):
        return {"color": list(p15[c[0]]), "shadow": list(p15[c[1]])}

    for k, (name, scr, _, _) in enumerate(PAGES):
        img = Image.new("RGBA", SCREEN, colors[0] + (255,))
        img.alpha_composite(nds.render_screen(nds.Nscr(open(os.path.join(g, scr + ".NSCR"), "rb").read()), chars, colors,
                                              transparent0=False).crop((0, 0) + SCREEN))
        _tabs(img, g, k, tab_chars, sprite_pal)
        img.save(os.path.join(target, f"sum_{name}.png"))

    # FontSpecialChars_DrawPartyScreenLevelText: the small "Lv." of font_special_chars.png (x 88-103), in black
    sc = os.path.join(platinum_repo, "res", "fonts", "font_special_chars.png")
    if os.path.exists(sc):
        im = Image.open(sc)
        lv = Image.new("RGBA", (16, 8), (0, 0, 0, 0))
        for y in range(8):
            for x in range(16):
                c = im.getpixel((88 + x, y))
                if c in (1, 2):
                    lv.putpixel((x, y), p15[BLACK[0] if c == 1 else BLACK[1]] + (255,))
        lv.save(os.path.join(target, "sum_lv.png"))

    types = os.path.join(platinum_repo, "res", "graphics", "battle", "type_icons")
    if os.path.isdir(types):
        os.makedirs(os.path.join(target, "types"), exist_ok=True)
        shared = _jasc(os.path.join(types, "shared.pal"))
        for name, row in TYPE_PAL.items():
            _indexed(os.path.join(types, name + ".png"), shared[row * 16:row * 16 + 16]).save(
                os.path.join(target, "types", name + ".png"))
    os.makedirs(os.path.join(target, "balls"), exist_ok=True)
    for name, pal in BALLS.items():
        png = os.path.join(g, "balls", name + "_ball.png")
        if os.path.exists(png):
            _indexed(png, _jasc(os.path.join(g, "balls", f"balls_{pal}.pal"))).save(os.path.join(target, "balls", name + ".png"))

    # main.c: HEALTHBAR_BASE_X/Y (6 tiles from 0xC0/0xE0/0x100, palette 10), EXPBAR_BASE_X/Y (7 tiles from 0xAC)
    hp_row, hp_h, green = _bar(chars, colors, 0xC0, 10)
    _, _, yellow = _bar(chars, colors, 0xE0, 10)
    _, _, red = _bar(chars, colors, 0x100, 10)
    exp_row, exp_h, exp_color = _bar(chars, colors, 0xAC, 10)

    def label(text, x, y, color=WHITE, align=None):
        f = {"field": "label", "text": text, "x": x, "y": y, **tc(color)}
        if align:
            f["align"] = align
        return f

    def centred(field, window_x, window_w, y, color=BLACK, **extra):
        return {"field": field, "x": window_x + window_w // 2, "y": y, "align": "center", **tc(color), **extra}

    # Every page: the title, the Pokémon's nickname and gender, level, ball and held item (the static windows).
    def common(title):
        return [
            label(title, 8, 0),                                               # SUMMARY_WINDOW_LABEL_<page> (1,0)
            {"field": "nickname", "x": 24, "y": 24, **tc(WHITE)},            # MON_NICKNAME_GENDER (3,3) 9 wide
            {"field": "gender", "x": 24 + 72, "y": 24, "align": "right", "male": tc(BLUE), "female": tc(RED)},
            {"field": "label", "image": "sum_lv.png", "x": 8, "y": 45},      # MON_LEVEL (1,5): "Lv." at (0,5)
            {"field": "level", "x": 24, "y": 40, **tc(BLACK)},              # the number at (16,0)
            {"field": "ball", "x": 16 - 8, "y": 32 - 8},                       # SUMMARY_SPRITE_CAUGHT_BALL (16,32)
            label("Item", 8, 160),                                            # LABEL_ITEM (1,20)
            {"field": "item", "x": 8, "y": 176, **tc(BLACK)},               # ITEM_NAME (1,22)
        ]

    info = common(PAGES[0][3]) + [
        label("Pokédex No.", 112, 40), label("Name", 112, 56), label("Type", 112, 72), label("OT", 112, 88),
        label("ID No.", 112, 104), label("Exp. Points", 112, 120), label("To Next Lv.", 112, 152),
        centred("dex_no", 192, 48, 40),                                        # DEX_NUM (24,5) 6 wide
        centred("species", 184, 64, 56),                                       # SPECIES_NAME (23,7) 8 wide
        {"field": "ot", "x": 184 + 32, "y": 88, "align": "center", "male": tc(BLUE), "female": tc(RED)},
        centred("id", 200, 32, 104),                                           # OT_ID (25,13) 4 wide
        centred("exp_points", 192, 48, 136),                                   # EXP (24,17)
        centred("next_lv", 192, 48, 168),                                      # EXP_NEXT_LV (24,21)
        # INFO_MON_TYPE_ICON_1/2_X, INFO_SOLO_MON_TYPE_ICON_X, _Y: the icons' centres (32x16)
        {"field": "type1", "x": 199 - 16, "y": 80 - 8, "solo": [216 - 16, 80 - 8]},
        {"field": "type2", "x": 233 - 16, "y": 80 - 8},
        {"field": "exp_bar", "x": 23 * 8, "y": 23 * 8 + exp_row, "w": 7 * 8, "h": exp_h, "color": exp_color},
    ]
    memo = common(PAGES[1][3]) + [
        # SUMMARY_WINDOW_MEMO (14,5) 17x18: PrintTrainerMemo, {COLOR 2} parts in red
        {"field": "memo", "x": 112, "y": 40, "width": 136, "line_height": 16, **tc(BLACK), "highlight": tc(RED)},
    ]
    skills = common(PAGES[2][3]) + [
        label("HP", 144, 32), label("Attack", 128, 56), label("Defense", 128, 72), label("Sp. Atk", 128, 88),
        label("Sp. Def", 128, 104), label("Speed", 128, 120), label("Ability", 112, 144),
        # PrintCurrentAndMaxInfo: the slash centred in SUMMARY_WINDOW_HP (23,4) 7 wide
        {"field": "hp", "x": 184, "y": 32, "slash_center": 184 + 28, **tc(BLACK)},
        {"field": "hp_bar", "x": 24 * 8, "y": 6 * 8 + hp_row, "w": 6 * 8, "h": hp_h,
         "green": green, "yellow": yellow, "red": red},
    ] + [{"field": f, "x": 224, "y": y, "align": "right", **tc(BLACK)}   # (25,7..15) 3 wide, right-aligned
         for f, y in (("attack", 56), ("defense", 72), ("sp_atk", 88), ("sp_def", 104), ("speed", 120))] + [
        {"field": "ability", "x": 168, "y": 144, **tc(BLACK)},              # ABILITY (21,18)
        {"field": "ability_desc", "x": 112, "y": 160, "width": 144, "line_height": 16, **tc(BLACK)},
    ]
    dashes = text_width("--")
    moves = common(PAGES[3][3]) + [
        label("INFO", 208, 0),                                                 # BUTTON_PROMPT (26,0)
        {"field": "moves", "row_dy": 32,
         # MOVE_TYPE_ICON_X, MOVE_TYPE_ICON_BASE_Y: centres; PrintMoveNameAndPP in BATTLE_MOVE_n (21, 4+4n)
         "type": {"x": 151 - 16, "y": 42 - 8},
         "name": {"field": "name", "x": 168 + 1, "y": 32 + 2, **tc(WHITE)},
         "pp_label": {"x": 168 + 16, "y": 32 + 16, "text": "PP"},
         "pp": {"x": 168 + 60, "y": 32 + 16, "slash_center": 168 + 60},
         "states": {"high": tc(BLACK)},
         "empty": {"text": "--", "x": 168 + 60 - dashes // 2}},
    ]
    return {
        "sprite": [52, 104],  # 3d_anim.c: PokemonSpriteManager_CreateSprite(..., 52, 104, ...)
        "info": info, "memo": memo, "skills": skills, "moves": moves,
        "pages": [{"image": f"sum_{name}.png", "list": f"summary.{name}"} for name, _, _, _ in PAGES],
    }
