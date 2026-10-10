"""Pokémon Emerald PC (box) + summary screen UI assets, extracted from pokeemerald.

    build(decomp_root, out_dir)

decomp_root: a full pret/pokeemerald checkout. Uses only Python 3 + Pillow.
All positions are GBA pixels, cited from the C sources (src/pokemon_storage_system.c,
src/pokemon_summary_screen.c, src/menu.c, src/text_window.c) in the comments.
"""

import json
import os
import sys

from PIL import Image

try:
    from . import gba
except ImportError:  # run as a script
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    from pc_ui import gba


TYPES = [  # TYPE_* order (include/constants/pokemon.h), file name in graphics/types
    ("normal", "normal"), ("fighting", "fight"), ("flying", "flying"), ("poison", "poison"),
    ("ground", "ground"), ("rock", "rock"), ("bug", "bug"), ("ghost", "ghost"),
    ("steel", "steel"), ("mystery", "mystery"), ("fire", "fire"), ("water", "water"),
    ("grass", "grass"), ("electric", "electric"), ("psychic", "psychic"), ("ice", "ice"),
    ("dragon", "dragon"), ("dark", "dark"),
]
# sMoveTypeToOamPaletteNum (pokemon_summary_screen.c): OBJ palette 13/14/15 =
# move_types_1/2/3.pal (gMoveTypes_Pal loaded at OBJ_PLTT_ID(13), 3 palettes)
TYPE_PAL = {
    "normal": 13, "fighting": 13, "flying": 14, "poison": 14, "ground": 13, "rock": 13,
    "bug": 15, "ghost": 14, "steel": 13, "mystery": 15, "fire": 13, "water": 14,
    "grass": 15, "electric": 13, "psychic": 14, "ice": 14, "dragon": 15, "dark": 13,
}
BALLS = ["poke", "great", "ultra", "master", "safari", "net", "dive", "nest",
         "repeat", "timer", "luxury", "premier"]
STATUS = ["poison", "paralysis", "sleep", "freeze", "burn", "pokerus", "fainted"]


def _p(root, *parts):
    return os.path.join(root, *parts)


def _rgb(c):
    return [int(c[0]), int(c[1]), int(c[2])]


# ---------------------------------------------------------------------------
# PC
# ---------------------------------------------------------------------------

def _pc_palettes(root):
    """BG palette RAM in the PC (InitPalettesAndSprites / InitSupplementalTilemaps):
    0 = interface.pal, 1 = party_menu.pal, 2 = pkmn_data_gray.pal,
    3 = scrolling_bg.pal, 15 = text_windows.pal."""
    g = _p(root, "graphics", "pokemon_storage")
    pals = [None] * 16
    pals[0] = gba.read_jasc(_p(g, "interface.pal"))
    pals[1] = gba.read_jasc(_p(g, "party_menu.pal"))
    pals[2] = gba.read_jasc(_p(g, "pkmn_data_gray.pal"))
    pals[3] = gba.read_jasc(_p(g, "scrolling_bg.pal"))
    pals[15] = gba.read_jasc(_p(g, "text_windows.pal"))
    return pals


def _build_pc(root, out, font, layout):
    g = _p(root, "graphics", "pokemon_storage")
    pals = _pc_palettes(root)
    # BG1 (charBaseIndex 1, baseTile 0x100): gStorageSystemMenu_Gfx (menu.png) is
    # loaded by DecompressAndLoadBgGfxUsingHeap(1, ...) at tile 0x100, so map
    # tile T -> menu.png tile T - 0x100.
    menu_tiles = gba.load_tiles(_p(g, "menu.png"))
    MENU_BASE = 0x100

    # --- pc_bg.png -------------------------------------------------------
    img = Image.new("RGBA", (240, 160), tuple(pals[0][0]) + (255,))  # backdrop = BG pal 0 col 0
    # BG3: scrolling background (SetScrollingBackground), char base 3, palette 3, scroll 0
    bg3 = gba.BgLayer()
    bg3.map = gba.read_tilemap(_p(g, "scrolling_bg.bin"))[:1024]
    bg3.render(img, gba.load_tiles(_p(g, "scrolling_bg.png")), pals)

    # BG1: interface. LoadPokeStorageMenuGfx sets display_menu.bin (32x20) as BG1's buffer,
    # then the TilemapUtil pieces are drawn over it (InitSupplementalTilemaps):
    bg1 = gba.BgLayer(fill=0x100)
    bg1.copy_rect(gba.read_tilemap(_p(g, "display_menu.bin")), 32, 0, 0)
    # "PKMN DATA" (TILEMAPID_PKMN_DATA, 8x4 map at (1,0)); rows 0-1 = coloured
    # version used while a Pokémon is displayed (UpdateWaveformAnimation).
    pkmn_data = gba.read_tilemap(_p(g, "pkmn_data.bin"))
    bg1.copy_rect(pkmn_data, 8, 1, 0, 0, 0, 8, 2)
    # Party menu (12x22, at (10,0)); closed => only rows 20-21 (the PARTY POKéMON button)
    party_map = gba.read_tilemap(_p(g, "party_menu.bin"))
    bg1.copy_rect(party_map, 12, 10, 0, 0, 20, 12, 2)
    # Close box button (9x4 at (21,0)), rows 0-1 = normal (UpdateCloseBoxButtonTilemap(TRUE))
    close_map = gba.read_tilemap(_p(g, "close_box_button.bin"))
    bg1.copy_rect(close_map, 9, 21, 0, 0, 0, 9, 2)
    bg1.render(img, menu_tiles, pals, tile_base=MENU_BASE)
    # WIN_DISPLAY_INFO (bg1, tilemapLeft 0, top 11, 9x7, palette 3) is filled with
    # PIXEL_FILL(1) by PrintDisplayMonInfo -> palette 3 colour 1 behind the text.
    info_fill = tuple(pals[3][1]) + (255,)
    img.paste(Image.new("RGBA", (9 * 8, 7 * 8), info_fill), (0, 11 * 8))
    # Waveform sprites (CreateWaveformSprites: 16x8 at centre (i*63+8, 9), palette
    # PALTAG_MISC_2 = waveform.png). Static first frame of the "on" animations
    # (tile 2 left, tile 10 right).
    wave_tiles = gba.load_tiles(_p(g, "waveform.png"))
    wave_pal = gba.png_palette(_p(g, "waveform.png"))
    gba.draw_sprite(img, wave_tiles, 2, 2, 1, 8 - 8, 9 - 4, wave_pal)
    gba.draw_sprite(img, wave_tiles, 10, 2, 1, 63 + 8 - 8, 9 - 4, wave_pal)
    img.save(_p(out, "pc_bg.png"))

    # Waveform animation strips (left on: 2,4,6; right on: 10,4,12; 8 frames each)
    strip = gba.new_screen(16 * 3, 16)
    for k, (lt, rt) in enumerate(((2, 10), (4, 4), (6, 12))):
        gba.draw_sprite(strip, wave_tiles, lt, 2, 1, k * 16, 0, wave_pal)
        gba.draw_sprite(strip, wave_tiles, rt, 2, 1, k * 16, 8, wave_pal)
    strip.save(_p(out, "pc_waveform.png"))

    # --- party panel -----------------------------------------------------
    # Fully shown party menu = rows 0..19 of the 12x22 map at (10,0) (ShowPartyMenu
    # grows the rect to 22 rows from destY 0; rows 20-21 fall off-screen).
    # SetPartySlotTilemaps: slot p (1..5) is a 4x3 piece at (7, 3*(p-1)+1) in the map.
    filled = gba.read_tilemap(_p(g, "party_slot_filled.bin"))
    empty = gba.read_tilemap(_p(g, "party_slot_empty.bin"))
    pm = list(party_map)
    for p in range(1, 6):
        for j in range(3):
            for i in range(4):
                pm[(3 * (p - 1) + 1 + j) * 12 + 7 + i] = filled[j * 4 + i]
    party = gba.new_screen(96, 160)
    gba.draw_tilemap(party, pm, 12, menu_tiles, pals, 0, 0, 0, 0, 12, 20, tile_base=MENU_BASE)
    party.save(_p(out, "pc_party.png"))
    for name, data in (("pc_party_slot_filled.png", filled), ("pc_party_slot_empty.png", empty)):
        im = gba.new_screen(32, 24)
        gba.draw_tilemap(im, data, 4, menu_tiles, pals, tile_base=MENU_BASE)
        im.save(_p(out, name))

    # --- hand cursor -----------------------------------------------------
    # CreateCursorSprites: 32x32, anims BOUNCE (tiles 0/16), OPEN (32), FIST (48).
    # The normal cursor uses PALTAG_MISC_2 (= sWaveform_Pal, white hand);
    # PALTAG_MISC_1 (hand_cursor.png's own palette, yellow) only with auto-action on.
    hand_tiles = gba.load_tiles(_p(g, "hand_cursor.png"))
    hand = gba.new_screen(64, 32)
    hand.paste(gba.sprite_image(hand_tiles, 0, 4, 4, wave_pal), (0, 0))    # pointing (normal)
    hand.paste(gba.sprite_image(hand_tiles, 48, 4, 4, wave_pal), (32, 0))  # fist (holding a mon)
    hand.save(_p(out, "hand.png"))
    allf = gba.new_screen(128, 32)
    for k in range(4):
        allf.paste(gba.sprite_image(hand_tiles, k * 16, 4, 4, wave_pal), (k * 32, 0))
    allf.save(_p(out, "hand_frames.png"))
    hand_yellow = gba.new_screen(64, 32)
    ypal = gba.png_palette(_p(g, "hand_cursor.png"))
    hand_yellow.paste(gba.sprite_image(hand_tiles, 0, 4, 4, ypal), (0, 0))
    hand_yellow.paste(gba.sprite_image(hand_tiles, 48, 4, 4, ypal), (32, 0))
    hand_yellow.save(_p(out, "hand_auto.png"))
    shadow_tiles = gba.load_tiles(_p(g, "hand_cursor_shadow.png"))
    gba.sprite_image(shadow_tiles, 0, 2, 2, wave_pal).save(_p(out, "hand_shadow.png"))

    # --- box title arrows (sSpriteTemplate_Arrow 8x16, PALTAG_MISC_2; left tile 0, right tile 2)
    arrow_tiles = gba.load_tiles(_p(g, "arrow.png"))
    gba.sprite_image(arrow_tiles, 0, 1, 2, wave_pal).save(_p(out, "arrow_left.png"))
    gba.sprite_image(arrow_tiles, 2, 1, 2, wave_pal).save(_p(out, "arrow_right.png"))

    # --- standard window frame -------------------------------------------
    # Both the action menu (AddMenu: DrawStdFrameWithCustomTileAndPalette(.., 11, 14))
    # and the message box (InitPokeStorageBg0 / DrawTextBorderOuter(WIN_MESSAGE, 2, 14))
    # use LoadUserWindowBorderGfx = the player's frame type (default type 0 =
    # graphics/text_window/1.png with its own palette). Interior = window fill
    # PIXEL_FILL(1) of palette 15 (text_windows.pal colour 1).
    tw = _p(root, "graphics", "text_window")
    frame_tiles = gba.load_tiles(_p(tw, "1.png"))
    frame_pal = gba.png_palette(_p(tw, "1.png"))
    fill = tuple(pals[15][1]) + (255,)
    frame = gba.new_screen(24, 24)
    for k in range(9):
        if k == 4:
            frame.paste(Image.new("RGBA", (8, 8), fill), (8, 8))
            continue
        gba.draw_tile(frame, frame_tiles[k], (k % 3) * 8, (k // 3) * 8, frame_pal)
    frame.save(_p(out, "window_frame.png"))
    # all 20 user-selectable frames, same layout, for completeness
    os.makedirs(_p(out, "window_frames"), exist_ok=True)
    for n in range(1, 21):
        f = _p(tw, "%d.png" % n)
        if not os.path.exists(f):
            continue
        ts, pl = gba.load_tiles(f), gba.png_palette(f)
        im = gba.new_screen(24, 24)
        for k in range(9):
            if k == 4:
                im.paste(Image.new("RGBA", (8, 8), fill), (8, 8))
            else:
                gba.draw_tile(im, ts[k], (k % 3) * 8, (k // 3) * 8, pl)
        im.save(_p(out, "window_frames", "%d.png" % n))

    # Menu cursor: Menu_MoveCursor prints gText_SelectorArrow3 ("▶") with FONT_NORMAL
    # at (0, 1 + 16*pos) in the window, default colours (fg 2 / shadow 3 of pal 15).
    fg, sh = pals[15][2], pals[15][3]
    cur = gba.new_screen(font.width("▶"), 16)
    font.draw(cur, "▶", 0, 0, fg, sh)
    cur.save(_p(out, "cursor_menu.png"))

    # --- markings (mon_markings.png: 16 combos of 32x8, row = markings bitmask) ---
    mk = _p(root, "graphics", "interface", "mon_markings.png")
    mk_tiles = gba.load_tiles(mk)
    mk_pal = gba.png_palette(mk)
    strip = gba.new_screen(32, 128)
    for c in range(16):
        gba.draw_sprite(strip, mk_tiles, c * 4, 4, 1, 0, c * 8, mk_pal)
    strip.save(_p(out, "markings_pc.png"))

    # --- layout ------------------------------------------------------------
    # Box icons: CreateMonIconSprite(.., 8*(3*j)+100, 8*(3*i)+44) = sprite centre of
    # a 32x32 icon -> top-left (84 + 24*col, 28 + 24*row).
    # Cursor in box: GetCursorCoordsByPos -> centre (100 + 24*col, 32 + 24*row) of a
    # 32x32 sprite -> top-left (84+24c, 16+24r) = icon top-left + (0, -12).
    # Buttons: cursor centre (120 + 88*pos, 14) -> top-left (104 / 192, -2).
    # Party: CreatePartyMonsSprites centres (104, 64) and (152, 24*(i-1)+16);
    #   cursor (104,52) / (152, 24*(i-1)+4) / CANCEL (152,132).
    win_msg = (11 * 8, 17 * 8, 18 * 8, 2 * 8)  # sWindowTemplates[WIN_MESSAGE]
    party_slots = [[104 - 16, 64 - 16]] + [[152 - 16, 24 * (i - 1) + 16 - 16] for i in range(1, 6)]
    layout["pc"] = {
        # Wallpaper: BG2 (screenSize 1), wallpaper tilemap 20x18 drawn at tile (11,2)
        # (DrawWallpaper with x offset 0) -> 160x144 image at (88, 16).
        "wallpaper": [88, 16],
        "wallpaper_size": [160, 144],
        # Box title: 2 sprites of 32x16 (sSpriteTemplate_BoxTitle), y centre 28;
        # text is centred on the title area by GetBoxTitleBaseX: x = 176 - width/2.
        "box_name": [176, 28],
        "box_name_font": "normal",
        "arrows": {"left": [92 - 4, 28 - 8], "right": [228 - 4, 28 - 8]},  # CreateBoxScrollArrows
        "grid": {"x": 84, "y": 28, "dx": 24, "dy": 24, "cols": 6, "rows": 5},
        "icon_size": [32, 32],
        "party_button": [80, 0, 96, 16],
        "close_button": [168, 0, 72, 16],
        "hand_offset": [0, -12],
        "hand_button_offset": [104 - 80, -2],  # relative to party_button; close: 192-168 = 24 too
        "hand_party_offset": [0, -12],        # hand top-left relative to a party icon top-left
        "hand_party_cancel": [152 - 16, 132 - 16],
        "hand_shadow_offset": [8, 28],        # SpriteCB_CursorShadow: (x, y+20), 16x16; box only
        "hand_frames": {"point": 0, "grab": 1},
        # CreateDisplayMonSprite: 64x64 sprite centred at (40, 48)
        "mon_sprite": [40, 48],
        # PrintDisplayMonInfo, WIN_DISPLAY_INFO at (0, 88), palette 3: fg colour 2, shadow 3
        "mon_text": [
            {"field": "nickname", "x": 6, "y": 88 + 0, "font": "normal"},
            {"field": "species", "x": 6, "y": 88 + 15, "font": "short", "prefix": "/"},
            {"field": "level", "x": 10, "y": 88 + 29, "font": "short",
             "format": "gender symbol then {LV}<level> (gDisplayMonGenderLvlText)"},
            {"field": "item", "x": 6, "y": 88 + 43, "font": "small"},
        ],
        "mon_text_color": _rgb(pals[3][2]),
        "mon_text_shadow": _rgb(pals[3][3]),
        "markings": [40 - 16, 150 - 4],  # CreateMarkingComboSprite: 32x8 centred (40,150)
        "party": {
            "x": 80, "y": 0,
            "slots": party_slots,
            "cancel": [80 + 6 * 8, 17 * 8, 6 * 8, 3 * 8],
            "slot_rects": [[80 + 7 * 8, (3 * (p - 1) + 1) * 8, 32, 24] for p in range(1, 6)],
        },
        # AddMenu: window width = (longest item length in chars) + 2 tiles,
        # height = 2*items tiles, tilemapLeft = 29 - width, tilemapTop = 15 - height.
        # PrintMenuTable text at (8, 16*i + 1); cursor "▶" at (0, 16*i + 1).
        # Frame is drawn 8 px outside the window rect.
        "menu": {"right": 29 * 8, "bottom": 15 * 8, "line_height": 16, "text_x": 8,
                 "text_y": 1, "cursor_x": 0, "padding": 8,
                 "width_tiles": "max(len(item))+2", "height_tiles": "2*items"},
        # PrintMessage: text at (0, 1) inside WIN_MESSAGE; frame 8px outside
        "message": list(win_msg),
        "message_text": [0, 1],
        "window_fill": _rgb(pals[15][1]),
    }


# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

def _sum_palettes(root):
    """LoadCompressedPalette(gSummaryScreen_Pal (tiles.png), BG 0, 8 palettes) +
    LoadPalette(gPPTextPalette, BG_PLTT_ID(8) + 1, 15 colours)."""
    pals = gba.split16(gba.png_palette(_p(root, "graphics", "summary_screen", "tiles.png")))[:8]
    pals += [None] * (16 - len(pals))
    pp = gba.read_jasc(_p(root, "graphics", "battle_interface", "text_pp.pal"))
    pals[8] = [(0, 0, 0)] + pp[:15]
    return pals


def _pagination(page, min_page=0, max_page=3, count=4):
    """DrawPagination: 8x2 tilemap drawn on BG3 at (11, 0)."""
    t = [0] * (count * 2 * 2)
    for i in range(count):
        j = i * 2
        if i < min_page:
            a, b, c, d = 0x40, 0x40, 0x50, 0x50
        elif i > max_page:
            a, b, c, d = 0x4A, 0x4A, 0x5A, 0x5A
        elif i < page:
            a, b, c, d = 0x46, 0x47, 0x56, 0x57
        elif i == page:
            a, b, c, d = (0x41, 0x42, 0x51, 0x52) if i != max_page else (0x4B, 0x4C, 0x5B, 0x5C)
        elif i != max_page:
            a, b, c, d = 0x43, 0x44, 0x53, 0x54
        else:
            a, b, c, d = 0x48, 0x49, 0x58, 0x59
        t[j], t[j + 1], t[j + 2 * count], t[j + 2 * count + 1] = a, b, c, d
    return t


def _build_summary(root, out, font, layout):
    s = _p(root, "graphics", "summary_screen")
    pals = _sum_palettes(root)
    tiles = gba.load_tiles(_p(s, "tiles.png"))  # BG1-3 share charBaseIndex 2, offset 0

    # sTextColors (pokemon_summary_screen.c): {bg, fg, shadow} colour indices
    TC = [(0, 1, 2), (0, 3, 4), (0, 5, 6), (0, 7, 8), (0, 9, 10), (0, 11, 12), (0, 13, 14),
          (0, 7, 8), (13, 15, 14), (0, 1, 2), (0, 3, 4), (0, 5, 6), (0, 7, 8)]

    def col(pal, cid):
        _, f, sh = TC[cid]
        return pals[pal][f], pals[pal][sh]

    def text(img, win, s_, x, y, cid, pal=6):
        """PrintTextOnWindow(win, s, x, y, 0, colorId) with win = (left, top) in tiles."""
        f, sh = col(pal, cid)
        font.draw(img, s_, win[0] * 8 + x, win[1] * 8 + y, f, sh)

    info_map = gba.read_tilemap(_p(s, "page_info.bin"))
    # SetDefaultTilemaps -> PositionStatusSlidingWindow(0, 0xFF) when the mon has no
    # status: CopyNColumnsToTilemap fills sStatusSlidingWindow1 (left 0, top 18,
    # 10x2) with its defaultTile 1 -> the bottom-left status box is hidden.
    for y in range(18, 20):
        for x in range(0, 10):
            info_map[y * 32 + x] = 1
    # DrawPokerusCuredSymbol: entry 0x223 = 0x81A when not cured (static default)
    info_map[0x223] = 0x81A
    # SetMonPicBackgroundPalette(FALSE): rect (1,4) 8x8 -> palette 0
    for y in range(4, 12):
        for x in range(1, 9):
            info_map[y * 32 + x] &= 0x0FFF
    skills_map = gba.read_tilemap(_p(s, "page_skills.bin"))
    moves_map = gba.read_tilemap(_p(s, "page_battle_moves.bin"))
    # PositionPowerAccSlidingWindow(0, 0xFF) (SetDefaultTilemaps): sPowerAccSlidingWindow
    # (left 0, top 45 of the 64x32 buffer = row 13 of the right screen block, 10x7)
    # filled with defaultTile 0 -> the POWER/ACCURACY "EFFECT" box is hidden until a
    # move is selected.
    for y in range(13, 20):
        for x in range(0, 10):
            moves_map[y * 32 + x] = 0

    a_btn = gba.load_tiles(_p(s, "a_button.png"))

    def base(page):
        """Compose BG3 (info) + the page's BG on top, as left after PssScrollRightEnd.
        Skills/moves .bin are decompressed into bgTilemapBuffers[page][1] (the right
        screen block) and scrolled into view; they sit above the info BG."""
        img = Image.new("RGBA", (240, 160), tuple(pals[0][0]) + (255,))
        m3 = list(info_map)
        pg = _pagination(page)
        for j in range(2):
            for i in range(8):
                # CopyToBgTilemapBufferRect_ChangePalette(.., 16): palette 16 keeps
                # the destination entry's palette/flip bits (CopyTileMapEntry).
                k = j * 32 + 11 + i
                m3[k] = (m3[k] & 0xFC00) | (pg[j * 8 + i] & 0x3FF)
        gba.draw_tilemap(img, m3, 32, tiles, pals, w=30, h=20)
        if page >= 1:
            gba.draw_tilemap(img, skills_map, 32, tiles, pals, w=30, h=20)
        if page >= 2:
            gba.draw_tilemap(img, moves_map, 32, tiles, pals, w=30, h=20)
        return img

    def title(img, s_):
        # PrintPageNamesAndStats: title windows at (0,0), text (2,1), colorId 1, pal 6
        text(img, (0, 0), s_, 2, 1, 1)

    def prompt(img, s_):
        # PROMPT window (22,0) 8x2 palette 7: right-aligned in 62, A button 16 px left of it
        sx = 62 - font.width(s_)
        ix = max(sx - 16, 0)
        a_pal = pals[7]
        gba.draw_sprite(img, a_btn, 0, 2, 2, 22 * 8 + ix, 0, a_pal)
        text(img, (22, 0), s_, sx, 1, 0, pal=7)

    # ----- INFO page --------------------------------------------------------
    img = base(0)
    title(img, "POKéMON INFO")
    prompt(img, "CANCEL")
    text(img, (11, 6), "TYPE/", 0, 1, 0)                # PSS_LABEL_WINDOW_POKEMON_INFO_TYPE
    text(img, (11, 4), "OT/", 0, 1, 1)                  # PrintMonOTName
    idw = font.width("{ID}{NO}00000")
    text(img, (22, 4), "{ID}{NO}", 56 - idw, 1, 1)      # PrintMonOTID (right-aligned in 56)
    img.save(_p(out, "sum_info.png"))

    # ----- SKILLS page ------------------------------------------------------
    img = base(1)
    title(img, "POKéMON SKILLS")
    L, R = (10, 7), (22, 7)
    for k, s_ in enumerate(["HP", "ATTACK", "DEFENSE"]):
        text(img, L, s_, 6 + (42 - font.width(s_)) // 2, 1 + 16 * k, 1)
    for k, s_ in enumerate(["SP. ATK", "SP. DEF", "SPEED"]):
        text(img, R, s_, 2 + (36 - font.width(s_)) // 2, 1 + 16 * k, 1)
    text(img, (10, 14), "EXP. POINTS", 6, 1, 1)
    text(img, (10, 14), "NEXT LV.", 6, 17, 1)
    img.save(_p(out, "sum_skills.png"))

    # ----- BATTLE MOVES page --------------------------------------------------
    img = base(2)
    title(img, "BATTLE MOVES")
    prompt(img, "INFO")
    img.save(_p(out, "sum_moves.png"))

    # Exp bar: DrawExperienceProgressBar writes 8 tiles at skills map 0x255
    # (row 18, col 21) with palette 2: 0x2062 + ticks (0..7), 0x206A = full tile.
    t_empty, t_full = tiles[0x62], tiles[0x6A]
    rows = sorted({k // 8 for k in range(64) if t_empty[k] != t_full[k]})
    fill_idx = [t_full[r * 8 + 4] for r in rows]
    exp_bar = [21 * 8, 18 * 8 + rows[0], 64, len(rows)]
    exp_cols = [_rgb(pals[2][c]) for c in fill_idx]
    bar = gba.new_screen(8, 8)
    gba.draw_tile(bar, t_full, 0, 0, pals[2])
    bar.save(_p(out, "exp_bar_tile_full.png"))

    # ----- type icons (32x16, OBJ palettes 13-15 = move_types_1..3.pal) -------
    tdir = _p(root, "graphics", "types")
    tp = {13: gba.read_jasc(_p(tdir, "move_types_1.pal")),
          14: gba.read_jasc(_p(tdir, "move_types_2.pal")),
          15: gba.read_jasc(_p(tdir, "move_types_3.pal"))}
    os.makedirs(_p(out, "types"), exist_ok=True)
    for name, fn in TYPES:
        ts = gba.load_tiles(_p(tdir, fn + ".png"))
        # move_types.4bpp is built from the 32x16 PNGs; the OBJ uses 1D mapping, and
        # gbagfx emits the PNG's tiles row-major, so the 8 tiles map 1:1.
        gba.sprite_image(ts, 0, 4, 2, tp[TYPE_PAL[name]]).save(_p(out, "types", name + ".png"))

    # ----- balls (gBallSpriteTemplates, 16x16, frame 0 of graphics/balls/*.png) ---
    os.makedirs(_p(out, "balls"), exist_ok=True)
    for b in BALLS:
        f = _p(root, "graphics", "balls", b + ".png")
        if os.path.exists(f):
            gba.sprite_image(gba.load_tiles(f), 0, 2, 2, gba.png_palette(f)).save(
                _p(out, "balls", b + ".png"))

    # ----- status icons (status_icons.png, 32x8 each, anim frames 0,4,..24) ------
    f = _p(root, "graphics", "interface", "status_icons.png")
    os.makedirs(_p(out, "status"), exist_ok=True)
    st_tiles, st_pal = gba.load_tiles(f), gba.png_palette(f)
    for k, nm in enumerate(STATUS):
        gba.sprite_image(st_tiles, k * 4, 4, 1, st_pal).save(_p(out, "status", nm + ".png"))

    # ----- markings on the summary (CreateMonMarkingAllCombosSprite with markings.pal)
    mk = _p(root, "graphics", "interface", "mon_markings.png")
    mk_tiles = gba.load_tiles(mk)
    mpal = gba.read_jasc(_p(s, "markings.pal"))
    strip = gba.new_screen(32, 128)
    for c in range(16):
        gba.draw_sprite(strip, mk_tiles, c * 4, 4, 1, 0, c * 8, mpal)
    strip.save(_p(out, "markings_summary.png"))

    # ----- layout -------------------------------------------------------------
    def c(pal, cid):
        f, sh = col(pal, cid)
        return {"color": _rgb(f), "shadow": _rgb(sh)}

    digit = font.width("0")
    spacer = font.width([0x77])  # CHAR_SPACER used by STR_CONV_MODE_RIGHT_ALIGN
    slash = font.width("/")
    left_x = 16 * 8 + 4   # PrintLeftColumnStats: window (16,7) x 4
    right_x = 27 * 8 + 2  # PrintRightColumnStats: window (27,7) x 2
    hp_right = left_x + 3 * digit + slash + 3 * digit
    common = [
        # PrintNotEggInfo: PORTRAIT_NICKNAME (1,12), PORTRAIT_SPECIES (1,14), colorId 1
        dict(field="nickname", x=8, y=12 * 8 + 1, **c(6, 1)),
        dict(field="species", x=8, y=14 * 8 + 1, prefix="/", **c(6, 1)),
        dict(field="level", x=8 + 24, y=14 * 8 + 17, prefix="{LV}", **c(6, 1)),
        dict(field="gender", x=8 + 57, y=14 * 8 + 17, male=c(6, 3), female=c(6, 4)),
        dict(field="dex_number", x=8, y=2 * 8 + 1, prefix="No", note="PORTRAIT_DEX_NUMBER, palette 7", **c(7, 1)),
        dict(field="ball", x=16 - 8, y=136 - 8, w=16, h=16),          # CreateCaughtBallSprite
        dict(field="markings", x=60 - 16, y=26 - 4, w=32, h=8),       # CreateMonMarkingsSprite
        dict(field="status", x=64 - 16, y=152 - 4, w=32, h=8),        # CreateSetStatusSprite
    ]
    otw = font.width("OT/")
    info = common + [
        dict(field="ot", x=88 + otw, y=33, male=c(6, 5), female=c(6, 6)),       # PrintMonOTName
        dict(field="id", x=176 + 56, y=33, align="right", digits=5, zero_pad=True, **c(6, 1)),
        dict(field="type1", x=120, y=48, w=32, h=16),                           # SetMonTypeIcons
        dict(field="type2", x=160, y=48, w=32, h=16),
        dict(field="ability", x=88, y=9 * 8 + 1, **c(6, 1)),                     # PrintMonAbilityName
        dict(field="ability_desc", x=88, y=9 * 8 + 17, **c(6, 0)),               # ...Description
        dict(field="memo", x=88, y=14 * 8 + 1, width=18 * 8, line_height=16,      # PrintMonTrainerMemo
             highlight={"color": _rgb(pals[6][5]), "shadow": _rgb(pals[6][6])},
             **c(6, 0)),
    ]
    skills = common + [
        dict(field="item", x=80 + 6 + 36, y=33, align="center", **c(6, 0)),      # PrintHeldItemName
        dict(field="ribbon", x=160 + 6 + 35, y=33, align="center", **c(6, 0)),   # PrintRibbonCount
        dict(field="hp", x=hp_right, y=57, align="right", format="%3d/%3d", **c(6, 0)),
        dict(field="attack", x=left_x + 7 * digit, y=57 + 16, align="right", **c(6, 0)),
        dict(field="defense", x=left_x + 7 * digit, y=57 + 32, align="right", **c(6, 0)),
        dict(field="sp_atk", x=right_x + 3 * digit, y=57, align="right", **c(6, 0)),
        dict(field="sp_def", x=right_x + 3 * digit, y=57 + 16, align="right", **c(6, 0)),
        dict(field="speed", x=right_x + 3 * digit, y=57 + 32, align="right", **c(6, 0)),
        dict(field="exp_points", x=24 * 8 + 2 + 42, y=14 * 8 + 1, align="right", **c(6, 0)),
        dict(field="next_lv", x=24 * 8 + 2 + 42, y=14 * 8 + 17, align="right", **c(6, 0)),
        dict(field="exp_bar", rect=exp_bar, colors=exp_cols, ticks=64),
    ]
    moves = common + [
        dict(field="moves", row_dy=16,
             type={"x": 85, "y": 32, "w": 32, "h": 16},                          # SetMoveTypeIcons
             name=dict(x=15 * 8, y=4 * 8 + 1, empty="-", **c(6, 1)),             # PrintMoveNameAndPP
             pp=dict(x=24 * 8 + 44, y=4 * 8 + 1, align="right", prefix="{PP}", format="%2d/%2d",
                     states={"full": c(8, 12), "normal": c(8, 9), "low": c(8, 10), "empty": c(8, 11)},
                     empty_text="--", empty_center=24 * 8 + 22, empty_color=c(8, 12))),
        dict(field="move_description", x=80, y=15 * 8 + 1, width=160, line_height=16, **c(6, 0)),
    ]
    layout["summary"] = {
        "sprite": [40, 64],  # CreateMonSprite: CreateSprite(&gMultiuseSpriteTemplate, 40, 64)
        "info": info,
        "skills": skills,
        "moves": moves,
        "text_font": "normal",
        "pp_text_note": "PP colour state from GetCurrentPPToMaxPPState: 3=full, 0=normal, 1=low, 2=empty",
    }


def build(decomp_root, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    cm = gba.Charmap(decomp_root)
    font = gba.Font(decomp_root, "normal", cm)
    layout = {}
    pc_pals = _pc_palettes(decomp_root)
    layout["font"] = {
        # FONT_NORMAL defaults fg 2 / shadow 3 on text_windows.pal (BG palette 15)
        "color": _rgb(pc_pals[15][2]),
        "shadow": _rgb(pc_pals[15][3]),
        "height": font.cap_height(),
        "line_height": 16,
    }
    _build_pc(decomp_root, out_dir, font, layout)
    _build_summary(decomp_root, out_dir, font, layout)
    with open(os.path.join(out_dir, "layout.json"), "w") as f:
        json.dump(layout, f, indent=1)


if __name__ == "__main__":
    build(sys.argv[1], sys.argv[2])
