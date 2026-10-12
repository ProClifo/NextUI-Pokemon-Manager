#!/usr/bin/env python3
"""Draws the cartridge icons shown in front of the games' names on the saves list.

Usage: scripts/build-carts.py <output dir>

Pixel art, 16 pixels high (minui-list scales it by a whole number to its text height): a Game Boy cartridge for
Red/Blue/Green/Yellow, a Game Boy Color one for Gold/Silver/Crystal, a Game Boy Advance one for Ruby to
LeafGreen and a DS card for Diamond to White 2, each in its game's colours: the shell where the real
cartridge's is coloured (Red, Blue, Yellow, Gold, Silver, Crystal and the Advance games), the label otherwise.
Writes cart-<game>.png. Requires Pillow.
"""
import os
import sys

from PIL import Image

# o outline, h/b/s the shell's highlight/face/shadow, d moulded detail, l/L the label and its highlight,
# a the label's second colour, p the board seen through a clear shell.
SHAPES = {
    "gb": [
        "ooooooooooo...",
        "ohhhhhhhhhhso.",
        "ohdddddddddbso",
        "ohbbbbbbbbbbbo",
        "ohbllllllllbso",
        "ohblLLLLLllbso",
        "ohbllllllllbso",
        "ohbllllllllbso",
        "ohbllllllllbso",
        "ohbaaaaaaaabso",
        "ohbbbbbbbbbbso",
        "ohbbbbbdbbbbso",
        "ohbbbbdddbbbso",
        "ohbbbbbbbbbbso",
        "ohssssssssssso",
        "oooooooooooooo",
    ],
    # The Color cartridges' moulded top stands above the face.
    "gbc": [
        "ooooooooooo...",
        "ohhhhhhhhhhso.",
        "ohddddddddddso",
        "osssssssssssso",
        "ohbllllllllbso",
        "ohblLLLLLllbso",
        "ohbllllllllbso",
        "ohbllllllllbso",
        "ohbllllllllbso",
        "ohbaaaaaaaabso",
        "ohbbbbbbbbbbso",
        "ohbpppppppbbso",
        "ohbpppppppbbso",
        "ohbbbbbbbbbbso",
        "ohssssssssssso",
        "oooooooooooooo",
    ],
    "gba": [
        "................",
        "................",
        "....oooooooo....",
        ".oooohhhhhhoooo.",
        "ohhhhbbbbbbbbbso",
        "ohbllllllllllbso",
        "ohblLLLLLLlllbso",
        "ohbllllllllllbso",
        "ohbllllllllllbso",
        "ohbaaaaaaaaaabso",
        "ohbbppppppppbbso",
        "ohbbbbbbbbbbbbso",
        "ohssssssssssssso",
        "oooooooooooooooo",
        "................",
        "................",
    ],
    "ds": [
        "..oooooooooo",
        ".ohhhhhhhhso",
        "ohbdbdbdbbso",
        "ohbbbbbbbbso",
        "ohllllllllso",
        "ohlLLLLlllso",
        "ohllllllllso",
        "ohllllllllso",
        "ohllllllllso",
        "ohllllllllso",
        "ohaaaaaaaaso",
        "ohbbbbbbbbso",
        "ohbbbbbbbbso",
        "ohssssssssso",
        "oooooooooooo",
        "............",
    ],
}

GREY = (170, 170, 178)

# game: (shape, shell, label, second label colour or None, clear shell)
GAMES = {
    "red": ("gb", (208, 40, 40), (252, 120, 96), (130, 16, 22), False),
    "blue": ("gb", (40, 84, 208), (112, 160, 252), (20, 44, 130), False),
    "green": ("gb", GREY, (48, 166, 72), (24, 110, 44), False),  # the Japanese Green cartridge is grey
    "yellow": ("gb", (240, 196, 36), (255, 232, 110), (226, 120, 40), False),
    "gold": ("gbc", (208, 162, 44), (236, 128, 44), (176, 64, 36), False),
    "silver": ("gbc", (186, 192, 204), (96, 112, 156), (60, 70, 110), False),
    "crystal": ("gbc", (116, 188, 236), (64, 124, 210), (190, 230, 250), True),
    "ruby": ("gba", (204, 32, 52), (244, 96, 96), (130, 16, 30), True),
    "sapphire": ("gba", (40, 82, 204), (104, 152, 244), (20, 46, 130), True),
    "emerald": ("gba", (30, 162, 92), (110, 224, 146), (16, 104, 56), True),
    "firered": ("gba", (234, 84, 40), (252, 156, 64), (170, 40, 20), True),
    "leafgreen": ("gba", (100, 190, 60), (176, 232, 104), (54, 130, 34), True),
    "diamond": ("ds", GREY, (96, 156, 230), (206, 228, 252), False),
    "pearl": ("ds", GREY, (232, 140, 182), (252, 214, 232), False),
    "platinum": ("ds", GREY, (124, 124, 140), (224, 182, 72), False),
    "heartgold": ("ds", GREY, (226, 168, 40), (252, 222, 124), False),
    "soulsilver": ("ds", GREY, (168, 180, 204), (232, 236, 246), False),
    "black": ("ds", GREY, (44, 44, 52), (96, 96, 108), False),
    "white": ("ds", GREY, (238, 238, 242), (186, 186, 198), False),
    "black2": ("ds", GREY, (40, 46, 62), (64, 124, 224), False),
    "white2": ("ds", GREY, (236, 232, 228), (232, 92, 52), False),
}


def mix(c, d, t):
    return tuple(round(a + (b - a) * t) for a, b in zip(c, d))


def palette(shell, label, second, clear):
    black, white, board = (0, 0, 0), (255, 255, 255), (40, 84, 52)
    face = shell
    colours = {
        "o": mix(shell, black, 0.7),
        "h": mix(shell, white, 0.4),
        "b": face,
        "s": mix(shell, black, 0.3),
        "d": mix(shell, black, 0.18),
        "l": label,
        "L": mix(label, white, 0.45),
        "a": second or label,
        # through a clear shell the board shows; an opaque one has a recess there
        "p": mix(face, board, 0.45) if clear else mix(shell, black, 0.12),
    }
    return colours


def draw(shape, colours):
    rows = SHAPES[shape]
    width = max(len(r) for r in rows)
    img = Image.new("RGBA", (width, len(rows)), (0, 0, 0, 0))
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch in colours:
                img.putpixel((x, y), colours[ch] + (255,))
    return img


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    for game, (shape, shell, label, second, clear) in GAMES.items():
        draw(shape, palette(shell, label, second, clear)).save(os.path.join(out, f"cart-{game}.png"))
    print(f"{len(GAMES)} cartridge icons")


if __name__ == "__main__":
    main()
