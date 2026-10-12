"""Pokémon Platinum's PC Storage System skin (pret/pokeplatinum): art from res/prebuilt/graphic/box.narc, decoded
with pc_ui.nds, and the game's fonts (res/fonts) and charmap (tools/msgenc/charmap.txt)."""
import json
import os

from PIL import Image


def load_charmap(repo):
    """{character: glyph number}: Platinum's character codes (16-bit) minus one, which is the glyph's place in the
    font sheets. Commands ({...}) and control codes are left out."""
    chars = {}
    with open(os.path.join(repo, "tools", "msgenc", "charmap.txt"), encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\n").lstrip(" \t")
            if not line or line.startswith("//") or "=" not in line:
                continue
            code, _, char = line.partition("=")
            if len(char) != 1 or char in "\\{":
                continue
            try:
                value = int(code, 16)
            except ValueError:
                continue
            if value >= 1:
                chars.setdefault(char, value - 1)
    return chars


def fonts(repo, target, font_sheet):
    """The system and message fonts as the viewer's glyph sheets (font_<name>.png/.json, foreground red and shadow
    green) and charmap.json in direct mode (characters straight to glyph numbers)."""
    names = []
    for name, file in (("normal", "font_system"), ("message", "font_message")):
        png = os.path.join(repo, "res", "fonts", file + ".png")
        meta = os.path.join(repo, "res", "fonts", file + ".json")
        if not os.path.exists(png):
            continue
        im = Image.open(png)
        px = im.load()
        widths = json.load(open(meta, encoding="utf-8"))["glyphWidths"]
        count = min(len(widths), (im.width // 16) * (im.height // 16))

        def rows(g, px=px, cols=im.width // 16):
            gx, gy = (g % cols) * 16, (g // cols) * 16
            return [[{1: 1, 2: 2}.get(px[gx + x, gy + y], 0) for x in range(16)] for y in range(16)]
        font_sheet(target, name, count, 16, widths, rows)
        names.append(name)
    chars = load_charmap(repo)
    with open(os.path.join(target, "charmap.json"), "w", encoding="utf-8") as f:
        json.dump({"direct": True, "chars": {c: [g] for c, g in chars.items()}, "names": {}}, f, ensure_ascii=False)
    return names
