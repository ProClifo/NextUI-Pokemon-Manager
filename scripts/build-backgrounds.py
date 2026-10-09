#!/usr/bin/env python3
"""Renders the per-game menu backgrounds at each NextUI screen size.

Usage: scripts/build-backgrounds.py <source dir> <output dir>

Each <game>.png (GBA-sized, 240x160) becomes <game>/<W>x<H>.png for every supported screen,
plus <game>/original.png. Images are scaled by a whole number so pixels stay sharp: the
largest scale that crops at most 15% of the art on either axis, centred; any remaining space
is filled with the art's own edge colours. Panoramas (at least twice as wide as tall, like the
Gold/Silver/Crystal title strips) are scaled to fill the screen's height instead and cropped at the
sides, so their scenery isn't smeared outwards. Requires Pillow.
"""
import os
import sys
from collections import Counter

from PIL import Image

SCREENS = [(640, 480), (720, 480), (720, 720), (1024, 768), (1280, 720)]
MAX_CROP = 0.15


def pick_scale(w, h, sw, sh):
    best = 1
    for k in range(1, 20):
        crop_x = max(0, w * k - sw) / (w * k)
        crop_y = max(0, h * k - sh) / (h * k)
        if crop_x <= MAX_CROP and crop_y <= MAX_CROP:
            best = k
    return best


def render(src, sw, sh):
    src = src.convert("RGBA")
    w, h = src.size
    k = max(1, sh // h) if w >= 2 * h else pick_scale(w, h, sw, sh)
    scaled = src.resize((w * k, h * k), Image.NEAREST)
    out = Image.new("RGBA", (sw, sh))
    ox, oy = (sw - scaled.width) // 2, (sh - scaled.height) // 2
    out.paste(scaled, (ox, oy))

    px = out.load()
    top, bottom = max(oy, 0), min(oy + scaled.height, sh)
    left, right = max(ox, 0), min(ox + scaled.width, sw)
    # Left/right: carry each row's edge colour outwards (keeps horizontal gradients intact).
    for y in range(top, bottom):
        for x in range(0, left):
            px[x, y] = px[left, y]
        for x in range(right, sw):
            px[x, y] = px[right - 1, y]
    # Top: repeat the top row; bottom: the bottom row's most common colour (art may touch the edge).
    for y in range(0, top):
        for x in range(sw):
            px[x, y] = px[x, top]
    if bottom < sh:
        fill = Counter(px[x, bottom - 1] for x in range(sw)).most_common(1)[0][0]
        for y in range(bottom, sh):
            for x in range(sw):
                px[x, y] = fill
    return out.convert("RGB")


def main():
    src_dir, out_dir = sys.argv[1], sys.argv[2]
    count = 0
    for name in sorted(os.listdir(src_dir)):
        if not name.lower().endswith(".png"):
            continue
        game = os.path.splitext(name)[0].lower()
        image = Image.open(os.path.join(src_dir, name))
        target = os.path.join(out_dir, game)
        os.makedirs(target, exist_ok=True)
        image.convert("RGB").save(os.path.join(target, "original.png"))
        for sw, sh in SCREENS:
            render(image, sw, sh).save(os.path.join(target, f"{sw}x{sh}.png"), optimize=True)
        count += 1
    print(f"{count} backgrounds x {len(SCREENS)} screen sizes")


if __name__ == "__main__":
    main()
