#!/usr/bin/env python3
"""Builds the Legal/Illegal icons shown next to the highlighted list item.

Usage: scripts/build-icons.py <noto-emoji checkout> <output dir>

The fonts the menus use have no emoji, so ✅ and ☠️ are drawn as images: Noto Emoji's 128 px PNGs
(googlefonts/noto-emoji, Apache 2.0), scaled to the list's text height for each UI scale (minui-list's
items are 16 px times the scale). Writes legal-<scale>x.png and illegal-<scale>x.png. Requires Pillow.
"""
import os
import sys

from PIL import Image

ICONS = {"legal": "emoji_u2705.png", "illegal": "emoji_u2620.png"}  # ✅ WHITE HEAVY CHECK MARK, ☠ SKULL AND CROSSBONES
TEXT_PX = 14  # a little under minui-list's 16 px items, so the icon sits within the text's height


def main():
    src, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    for name, file in ICONS.items():
        image = Image.open(os.path.join(src, "2D", "png", "128", file)).convert("RGBA")
        for scale in (2, 3):
            size = TEXT_PX * scale
            image.resize((size, size), Image.LANCZOS).save(os.path.join(out, f"{name}-{scale}x.png"))
    print(f"{len(ICONS)} icons")


if __name__ == "__main__":
    main()
