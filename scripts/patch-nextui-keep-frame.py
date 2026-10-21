#!/usr/bin/env python3
"""Keeps the last frame on screen between Pokémon Manager's screens (a NextUI checkout, patched in place).

Usage: scripts/patch-nextui-keep-frame.py <NextUI checkout>

Every screen of the pak is its own program (minui-list, minui-presenter, pkmgr-box), and NextUI's video code
blanks the display both when one quits (PLAT_quitVideo: three cleared frames, then /dev/fb0 zeroed) and when the next
starts (GFX_init ends with PLAT_clearAll), so moving between screens flashes black. With PKMGR_KEEP_FRAME set (by the
pak's launch.sh) those clears are skipped: the old screen stays up until the new one draws over it. Without it the
programs behave exactly as NextUI's. Running it again changes nothing.
"""
import os
import sys

GUARD = 'if (!getenv("PKMGR_KEEP_FRAME")) '


def patch(path, edits):
    text = open(path, encoding="utf-8").read()
    changed = text
    for old, new in edits:
        if new in changed:
            continue  # already patched
        if old not in changed:
            sys.exit(f"{path}: expected code not found: {old.strip()!r}")
        changed = changed.replace(old, new, 1)
    if changed != text:
        open(path, "w", encoding="utf-8").write(changed)
        print(f"patched {path}")


def main():
    root = sys.argv[1]
    common = os.path.join(root, "workspace", "all", "common")
    patch(os.path.join(common, "generic_video.c"), [
        ("void PLAT_quitVideo(void) {\n\tclearVideo();",
         "void PLAT_quitVideo(void) {\n\t" + GUARD + "clearVideo();"),
        ('\tSDL_QuitSubSystem(SDL_INIT_VIDEO);\n\tsystem("cat /dev/zero > /dev/fb0 2>/dev/null");',
         '\tSDL_QuitSubSystem(SDL_INIT_VIDEO);\n\t' + GUARD + 'system("cat /dev/zero > /dev/fb0 2>/dev/null");'),
    ])
    patch(os.path.join(common, "api.c"), [
        ("\tPLAT_clearAll();\n\n\treturn gfx.screen;", "\t" + GUARD + "PLAT_clearAll();\n\n\treturn gfx.screen;"),
    ])


if __name__ == "__main__":
    main()
