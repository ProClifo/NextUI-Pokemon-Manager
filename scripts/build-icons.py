#!/usr/bin/env python3
"""Builds the Legal/Illegal icons shown at the right of list items.

Usage: scripts/build-icons.py <noto-emoji checkout> <output dir> [font]

The fonts the menus use have no emoji, so ✅ and ☠️ are drawn as images: Noto Emoji's 128 px PNGs
(googlefonts/noto-emoji, Apache 2.0), scaled to the list's text height for each UI scale (minui-list's
items are 16 px times the scale). Writes legal-<scale>x.png and illegal-<scale>x.png, and with a font (NextUI's)
legal-label-<scale>x.png / illegal-label-<scale>x.png: the icon followed by "Legal" or "Illegal" in white, the
status minui-list draws in its bottom-left pill for the highlighted item. Requires Pillow.
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ICONS = {"legal": "emoji_u2705.png", "illegal": "emoji_u2620.png"}  # ✅ WHITE HEAVY CHECK MARK, ☠ SKULL AND CROSSBONES
TEXT_PX = 14  # a little under minui-list's 16 px items, so the icon sits within the text's height
WORDS = {"legal": "Legal", "illegal": "Illegal"}


def main():
    src, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    for name, file in ICONS.items():
        image = Image.open(os.path.join(src, "2D", "png", "128", file)).convert("RGBA")
        for scale in (2, 3):
            size = TEXT_PX * scale
            icon = image.resize((size, size), Image.LANCZOS)
            icon.save(os.path.join(out, f"{name}-{scale}x.png"))
            if len(sys.argv) > 3 and os.path.exists(sys.argv[3]):
                # as tall as a list row (16 px times the scale) so minui-list draws it unscaled, the icon and the
                # word's capitals centred on the row like the item's own text
                row = 16 * scale
                font = ImageFont.truetype(sys.argv[3], 14 * scale)
                word = WORDS[name]
                left, top, right, bottom = font.getbbox(word)
                cap_top = font.getbbox("L")[1]
                small = image.resize((12 * scale, 12 * scale), Image.LANCZOS)
                gap = 3 * scale
                label = Image.new("RGBA", (small.width + gap + right, row), (0, 0, 0, 0))
                label.alpha_composite(small, (0, (row - small.height) // 2))
                cap_h = font.getbbox("L")[3] - cap_top
                ImageDraw.Draw(label).text((small.width + gap, (row - cap_h) // 2 - cap_top), word, font=font, fill=(255, 255, 255, 255))
                label.save(os.path.join(out, f"{name}-label-{scale}x.png"))
    print(f"{len(ICONS)} icons")


if __name__ == "__main__":
    main()
