#!/usr/bin/env python3
"""Builds the PC and summary screen skins of Ruby/Sapphire, Emerald and FireRed/LeafGreen for pkmgr-box.

Usage: scripts/pc_ui/build.py <decomp dir> <output dir>

<decomp dir> holds pret checkouts named pokeruby, pokeemerald and pokefirered. Each game's module
(scripts/pc_ui/<game>.py) renders its art and a detailed layout (kept as layout_full.json); this script then
writes layout.json, the same layout in the one form the viewer reads:

  font     {color, shadow, height, cap_top}
  pc       wallpaper, box_name [cx, top], box_name_color/_shadow, arrows, grid, party_button, close_button,
           hand_offset, hand_button_offset, hand_title, hand_cancel, mon_sprite, mon_text (fields),
           mon_text_bg {rect, color}, party {x, y, slots, cancel}, menu {right, bottom, line_height, text_x,
           text_y, color, shadow}, message, message_text, message_frame
  summary  sprite, info / skills / moves (fields)

A field is {field, x, y, align?, color, shadow, prefix?, male?, female?, width?, line_height?}; text fields are
drawn with the top of their glyph cell at y. Special fields: type1/type2/ball {x, y}, exp_bar/hp_bar
{x, y, w, h, color | green/yellow/red}, moves {row_dy, type, name, pp, pp_label?, empty}.
"""
import copy
import importlib
import json
import os
import shutil
import sys

GAMES = {"e": "pokeemerald", "rs": "pokeruby", "frlg": "pokefirered"}
TOKENS = {"{LV}": "Lv", "{LV_2}": "Lv", "{NO}": "No", "{PP}": "PP"}


def text(value):
    for token, replacement in TOKENS.items():
        value = value.replace(token, replacement)
    return value


def field(f, color, shadow):
    """A text/position field with its colours filled in and its prefix spelled out."""
    out = {k: v for k, v in f.items() if k in ("field", "x", "y", "align", "width", "line_height", "w", "h")}
    out["color"] = f.get("color", color)
    out["shadow"] = f.get("shadow", shadow)
    if "prefix" in f:
        out["prefix"] = text(f["prefix"])
    for gender in ("male", "female"):
        if gender in f:
            out[gender] = f[gender]
    if "highlight" in f:
        out["highlight"] = f["highlight"]
    return out


def gender_and_level(fields, color, shadow):
    """The PC panel's level line: '<gender> Lv<level>' from x; the gender glyph is 6 px plus a 3 px space."""
    out = []
    for f in fields:
        if f["field"] == "level" and "num_right" in f:  # Ruby/Sapphire: Lv, number right-aligned, then the gender
            out.append({**field(f, color, shadow), "x": f["num_right"], "align": "right",
                        "prefix": "Lv", "prefix_x": f["x"]})
            gender = next((g for g in fields if g["field"] == "gender"), None)
            if gender:
                out.append({**field(gender, color, shadow), "x": f["num_right"] + gender.get("x_after_level", 8)})
            continue
        if f["field"] == "gender" and "x_after_level" in f:
            continue
        if f["field"] == "level" and ("male" in f or "text" in f or "format" in f):
            level = field(f, color, shadow)
            gender = {"field": "gender", "x": f["x"], "y": f["y"], "color": color, "shadow": shadow}
            for g in ("male", "female"):
                if g in f:
                    gender[g] = f[g]
            level.pop("male", None)
            level.pop("female", None)
            level["x"] = f["x"] + f.get("gender_width", 6) + 3
            level["prefix"] = "Lv"
            out += [gender, level]
        elif f["field"] == "gender" and any(o["field"] == "gender" for o in out):
            for g in ("male", "female"):
                if g in f:
                    next(o for o in out if o["field"] == "gender")[g] = f[g]
        elif f["field"] == "gender" and any(x["field"] == "level" and "text" in x for x in fields):
            continue  # merged with the level line above
        else:
            out.append(field(f, color, shadow))
    # Emerald lists the gender separately from a level whose text includes it.
    return out


def xy(value):
    return {"x": value[0], "y": value[1]} if isinstance(value, list) else {"x": value["x"], "y": value["y"]}


def moves(m, color, shadow):
    pp = m["pp"]
    if isinstance(m.get("empty"), dict):  # Ruby/Sapphire
        pp = {**pp, "empty_text": m["empty"].get("pp", "--"), "empty_x": m["empty"].get("pp_x", pp.get("x", 0))}
    states = pp.get("states", {})
    rename = {"normal": "high", "empty": "zero"}
    out = {
        "field": "moves",
        "row_dy": m["row_dy"],
        "type": xy(m["type"]),
        "name": field({"field": "name", **m["name"]}, color, shadow),
        "states": {rename.get(k, k): v for k, v in states.items()},
        "empty": {"text": pp.get("empty_text", "--"), "x": pp.get("empty_x", pp.get("empty_center", pp.get("x", 0)))},
    }
    if "pp_label" in m:  # FireRed/LeafGreen, Ruby/Sapphire: "PP" then "cur/max" right-aligned
        label = m["pp_label"]
        out["pp_label"] = {**xy(label), "text": text(label.get("text", "PP") if isinstance(label, dict) else "PP")}
        if isinstance(label, list):  # Ruby/Sapphire draw a small "PP" tile, narrower than any font's PP
            out["pp_label"]["image"] = "sum_pp_label.png"
        out["pp"] = {"x": pp["max_right"], "y": pp["y"], "align": "right"}
        if "color" in pp and not out["states"]:
            out["states"] = {"high": {"color": pp["color"], "shadow": pp.get("shadow", shadow)}}
    else:
        out["pp"] = {"x": pp["x"], "y": pp["y"], "align": pp.get("align", "right"), "prefix": text(pp.get("prefix", ""))}
    return out


def bar(f):
    rect = f.get("rect", [f.get("x", 0), f.get("y", 0), f.get("w", 0), f.get("h", 0)])
    out = {"field": f["field"], "x": rect[0], "y": rect[1], "w": rect[2], "h": rect[3]}
    colors = f.get("colors", f.get("fill_colors"))
    if isinstance(colors, dict):
        for band in ("green", "yellow", "red"):
            out[band] = colors[band][1] if len(colors[band]) > 1 else colors[band][0]
    elif colors:
        out["color"] = colors[0]
    elif "color" in f:
        out["color"] = f["color"]
    return out


def summary_fields(fields, color, shadow):
    out = []
    for f in fields:
        name = f["field"]
        if name == "moves":
            out.append(moves(f, color, shadow))
        elif name in ("exp_bar", "hp_bar"):
            out.append(bar(f))
        elif name in ("type1", "type2", "ball"):
            out.append({"field": name, "x": f["x"], "y": f["y"]})
        elif name == "dex_number":
            out.append(field({**f, "field": "dex_no"}, color, shadow))
        elif name in ("markings", "status", "shiny_star", "pokerus_cured", "move_description"):
            continue  # not shown yet
        else:
            out.append(field(f, color, shadow))
    return out


def hand_button(pc, which):
    positions = pc.get("hand_button_positions", {})
    if which in positions:
        return positions[which]
    rect = pc["party_button" if which == "party" else "close_button"]
    return [rect[0] + pc["hand_button_offset"][0], rect[1] + pc["hand_button_offset"][1]]


def normalize(full):
    font = full["font"]
    color, shadow = font["color"], font["shadow"]
    pc, summary = full["pc"], full["summary"]
    name_text = pc.get("box_name_text", {})
    mon_color, mon_shadow = pc.get("mon_text_color", [255, 255, 255]), pc.get("mon_text_shadow", [0, 0, 0])
    menu = dict(pc["menu"])
    message, message_text = pc["message"], pc.get("message_text", [0, 1])
    if "width_tiles_formula" in menu:  # Ruby/Sapphire give the window with its frame
        menu["right"], menu["bottom"], menu["text_y"] = menu["right"] - 8, menu["bottom"] - 8, menu.get("text_y", 8) - 8
        message = [message[0] + 8, message[1] + 8, message[2] - 16, message[3] - 16]
        message_text = [message_text[0] - message[0], message_text[1] - message[1]]
    out = {
        # NextUI's font is wider than Ruby/Sapphire's tall, narrow one; at more than 9 px it overflows the boxes.
        "font": {"color": color, "shadow": shadow, "height": min(font.get("height", 9), 9), "cap_top": font.get("cap_top_in_cell", font.get("cap_top", 2))},
        "pc": {
            "wallpaper": pc["wallpaper"],
            "box_name": [pc["box_name"][0], name_text.get("top", name_text.get("cell_top", pc.get("box_name_text_top", pc["box_name"][1] - 7)))],
            "box_name_color": name_text.get("color", pc.get("box_name_color", [255, 255, 255])),
            "box_name_shadow": name_text.get("shadow", pc.get("box_name_shadow", [57, 57, 57])),
            "arrows": pc["arrows"],
            "grid": pc["grid"],
            "party_button": pc["party_button"],
            "close_button": pc["close_button"],
            "hand_offset": pc["hand_offset"],
            "hand_button_offset": pc["hand_button_offset"],
            "hand_title": pc.get("hand_box_title", [146, -4]),
            "hand_cancel": pc.get("hand_party_cancel", pc["party"].get("hand_cancel", [136, 116])),
            # Over the buttons: the button's left + 24, 2 above it (Emerald, FireRed/LeafGreen; R/S list them).
            "hand_party_button": hand_button(pc, "party"),
            "hand_close_button": hand_button(pc, "close"),
            "hand_shadow_offset": pc.get("hand_shadow_offset", [8, 28]),
            "mon_sprite": pc["mon_sprite"],
            "mon_text": gender_and_level(pc["mon_text"], mon_color, mon_shadow),
            "party": {k: pc["party"][k] for k in ("x", "y", "slots", "cancel")},
            "menu": {
                "right": menu["right"], "bottom": menu["bottom"], "line_height": menu["line_height"],
                "text_x": menu["text_x"], "text_y": menu.get("text_y", 1),
                "color": menu.get("color", color), "shadow": menu.get("shadow", shadow),
            },
            "message": message,
            "message_text": message_text,
            "message_frame": pc.get("message_frame", "window_frame.png"),
        },
        "summary": {
            "sprite": summary["sprite"],
            # Ruby/Sapphire list the side panel (number, name, level, ball) once for every page.
            "info": summary_fields(summary.get("common", []) + summary["info"], color, shadow),
            "skills": summary_fields(summary.get("common", []) + summary["skills"], color, shadow),
            "moves": summary_fields(summary.get("common", []) + summary["moves"], color, shadow),
        },
    }
    if "mon_text_rect" in pc:
        out["pc"]["mon_text_bg"] = {"rect": pc["mon_text_rect"], "color": pc.get("mon_text_bg", [148, 148, 172])}
    return out


WALLPAPERS = ["forest", "city", "desert", "savanna", "crag", "volcano", "snow", "cave",
              "beach", "seafloor", "river", "sky", "polkadot", "pokecenter", "machine", "plain"]


def frlg_wallpaper(folder):
    """A FireRed/LeafGreen wallpaper: tiles.png (two 16-colour palettes: the frame's, then the background's) and
    a 20x18 tilemap.bin whose palettes 1 and 2 are those; colour 0 and palette 0 stay see-through."""
    import struct
    from PIL import Image
    img = Image.open(os.path.join(folder, "tiles.png"))
    w, h = img.size
    px = img.load()
    tiles = [[[px[tx * 8 + x, ty * 8 + y] & 0xF for x in range(8)] for y in range(8)]
             for ty in range(h // 8) for tx in range(w // 8)]
    pal = img.getpalette()
    palettes = {1: [tuple(pal[i * 3:i * 3 + 3]) for i in range(16)], 2: [tuple(pal[i * 3:i * 3 + 3]) for i in range(16, 32)]}
    data = open(os.path.join(folder, "tilemap.bin"), "rb").read()
    out = Image.new("RGBA", (160, 144), (0, 0, 0, 0))
    o = out.load()
    for i, e in enumerate(struct.unpack("<%dH" % (len(data) // 2), data)):
        tile, hflip, vflip, p = e & 0x3FF, (e >> 10) & 1, (e >> 11) & 1, e >> 12
        if p not in palettes or tile >= len(tiles):
            continue
        cx, cy = (i % 20) * 8, (i // 20) * 8
        for y in range(8):
            for x in range(8):
                c = tiles[tile][7 - y if vflip else y][7 - x if hflip else x]
                if c:
                    o[cx + x, cy + y] = palettes[p][c] + (255,)
    return out


def game_wallpapers(decomp, out):
    """Each game's own 16 box wallpapers as ui/<game>/wallpapers/<00-15>.png, in the games' order (Emerald's are
    res/box/wallpapers, from scripts/build-box-assets.py)."""
    rs = os.path.join(out, "rs", "wallpapers")
    if os.path.isdir(rs):  # pc_ui/rs.py names them
        for i, name in enumerate(WALLPAPERS):
            if os.path.exists(os.path.join(rs, f"{name}.png")):
                os.replace(os.path.join(rs, f"{name}.png"), os.path.join(rs, f"{i:02d}.png"))
    src = os.path.join(decomp, "pokefirered", "graphics", "pokemon_storage", "wallpapers")
    if os.path.isdir(src) and os.path.isdir(os.path.join(out, "frlg")):
        dst = os.path.join(out, "frlg", "wallpapers")
        os.makedirs(dst, exist_ok=True)
        # sWallpapers: FireRed/LeafGreen have STARS, TILES and SIMPLE where Ruby/Sapphire/Emerald have
        # POLKA-DOT, MACHINE and PLAIN
        names = WALLPAPERS[:12] + ["stars", "pokecenter", "tiles", "simple"]
        for i, name in enumerate(names):
            frlg_wallpaper(os.path.join(src, name)).save(os.path.join(dst, f"{i:02d}.png"))


def main():
    decomp, out = sys.argv[1], sys.argv[2]
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    for game, repo in GAMES.items():
        try:
            module = importlib.import_module(f"pc_ui.{game}")
        except ImportError as e:
            print(f"pc_ui: no {game} module ({e})")
            continue
        target = os.path.join(out, game)
        module.build(os.path.join(decomp, repo), target)
        layout_path = os.path.join(target, "layout.json")
        with open(layout_path, encoding="utf-8") as f:
            full = json.load(f)
        with open(os.path.join(target, "layout_full.json"), "w", encoding="utf-8") as f:
            json.dump(full, f, indent=1)
        with open(layout_path, "w", encoding="utf-8") as f:
            json.dump(normalize(copy.deepcopy(full)), f, indent=1)
        for preview in [p for p in os.listdir(target) if p.startswith("preview_")]:
            os.remove(os.path.join(target, preview))
        print(f"pc_ui: {game}")
    game_wallpapers(decomp, out)
    # Ruby/Sapphire's ball icons are Emerald's.
    rs_balls, e_balls = os.path.join(out, "rs", "balls"), os.path.join(out, "e", "balls")
    if os.path.isdir(e_balls) and os.path.isdir(os.path.join(out, "rs")) and not os.path.isdir(rs_balls):
        shutil.copytree(e_balls, rs_balls)


if __name__ == "__main__":
    main()
