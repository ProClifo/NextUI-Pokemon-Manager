"""Black/White's 24 box wallpapers in Platinum's PC frame (res/box/ui/pt/wallpapers-bw/NN.png).

No decompilation has Black/White's PC art, so the wallpapers come from a ripped sheet (assets/gen5/wallpapers.png,
ripped by Barubary): four across, each a 112x19 name banner over a framed 156x114 box (the game draws the frame
around them itself). Platinum's wallpaper image is its frame and name plate with the wallpaper inside, and the
insides are the same size: the box's 152x112 (Black/White's, a pixel off each side) and the plate's 110x17. Each
Black/White wallpaper goes into Platinum's wallpaper 0, its bevel recoloured to match.
"""
import os
from collections import Counter

from PIL import Image

PITCH_X, PITCH_Y, LEFT, TOP = 172, 164, 4, 11
# in a sheet cell (from its left and its banner's top) and in Platinum's 168x160 wallpaper
BW_BANNER, PT_BANNER = (29, 1, 110, 17), (29, 12, 110, 17)
BW_BOX, PT_BOX = (8, 29, 152, 112), (8, 40, 152, 112)
PT_LIGHT, PT_DARK = (206, 255, 198), (123, 165, 107)  # Platinum wallpaper 0's bevel colours


def build(sheet_path, pt_dir):
    frame_path = os.path.join(pt_dir, "wallpapers", "00.png")
    if not (os.path.exists(sheet_path) and os.path.exists(frame_path)):
        print("bw wallpapers: skipped (no sheet or no Platinum wallpaper)")
        return 0
    sheet = Image.open(sheet_path).convert("RGBA")
    frame = Image.open(frame_path).convert("RGBA")
    out = os.path.join(pt_dir, "wallpapers-bw")
    os.makedirs(out, exist_ok=True)
    for n in range(24):
        x0, y0 = LEFT + (n % 4) * PITCH_X, TOP + (n // 4) * PITCH_Y
        box = sheet.crop((x0 + BW_BOX[0], y0 + BW_BOX[1], x0 + BW_BOX[0] + BW_BOX[2], y0 + BW_BOX[1] + BW_BOX[3]))
        banner = sheet.crop((x0 + BW_BANNER[0], y0 + BW_BANNER[1], x0 + BW_BANNER[0] + BW_BANNER[2],
                             y0 + BW_BANNER[1] + BW_BANNER[3]))
        # the bevel: the box's own border colour for its shadow, its main colour lightened for its highlight
        dark = sheet.getpixel((x0 + 6, y0 + 60))[:3]
        main = Counter(box.convert("RGB").getdata()).most_common(1)[0][0]
        light = tuple(min(255, c + 66) for c in main)
        img = frame.copy()
        px = img.load()
        for y in range(img.height):
            for x in range(img.width):
                p = px[x, y][:3]
                if p == PT_LIGHT:
                    px[x, y] = light + (255,)
                elif p == PT_DARK:
                    px[x, y] = dark + (255,)
        img.paste(box, PT_BOX[:2])
        img.paste(banner, PT_BANNER[:2])
        img.save(os.path.join(out, f"{n:02}.png"))
    print("bw wallpapers: 24")
    return 24
