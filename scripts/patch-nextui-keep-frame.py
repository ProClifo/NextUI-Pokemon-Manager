#!/usr/bin/env python3
"""Keeps the last frame on screen between Pokémon Manager's screens (a NextUI checkout, patched in place).

Usage: scripts/patch-nextui-keep-frame.py <NextUI checkout>

Every screen of the pak is its own program (minui-list, minui-presenter, pkmgr-box), and NextUI's video code
blanks the display when one quits (PLAT_quitVideo: three cleared frames, then /dev/fb0 zeroed), so the screen stays
black while the pak works out the next one. With PKMGR_KEEP_FRAME set (by the pak's launch.sh) those clears are
skipped: the old screen stays up until the next program starts. When a program starts, GFX_init ends with
PLAT_clearAll, which shows two black frames (its flips also hand the GPU back to SDL: without them a program's first
frame isn't shown). With the variable it clears without showing anything, and the program's first frame is shown
twice instead, so the old screen goes straight to the new one. Without the variable the programs behave exactly as
NextUI's. With PKMGR_FRAME_LOG set they also write the time their first frame was shown there, for the pak's
timing log. Running it again changes nothing.
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
        ("SDL_Surface *GFX_init(int mode)\n",
         "static int pkmgr_show_first_frame_twice;\n"
         "// PKMGR_FRAME_LOG: when the first frame is shown, for the pak's screen timing log (Ui/ScreenTiming.cs)\n"
         "static void pkmgr_log_first_frame(void) {\n"
         "\tstatic int logged;\n"
         "\tconst char *path = getenv(\"PKMGR_FRAME_LOG\");\n"
         "\tif (logged || !path) return;\n"
         "\tlogged = 1;\n"
         "\tFILE *f = fopen(path, \"a\");\n"
         "\tif (!f) return;\n"
         "\tstruct timespec ts;\n"
         "\tclock_gettime(CLOCK_MONOTONIC, &ts);\n"
         "\tfprintf(f, \"%d %lld\\n\", (int)getpid(), (long long)ts.tv_sec * 1000000000LL + ts.tv_nsec);\n"
         "\tfclose(f);\n"
         "}\n"
         "SDL_Surface *GFX_init(int mode)\n"),
        ("\tPLAT_clearAll();\n\n\treturn gfx.screen;",
         "\tif (getenv(\"PKMGR_KEEP_FRAME\")) {\n"
         "\t\tPLAT_clearLayers(0);\n"
         "\t\tPLAT_clearVideo(gfx.screen);\n"
         "\t\tpkmgr_show_first_frame_twice = 1;\n"
         "\t} else PLAT_clearAll();\n\n\treturn gfx.screen;"),
        ("\tPLAT_flip(screen, 0);\n\n\tperf.fps = current_fps;",
         "\tPLAT_flip(screen, 0);\n"
         "\tif (pkmgr_show_first_frame_twice) {\n"
         "\t\tpkmgr_show_first_frame_twice = 0;\n"
         "\t\tPLAT_flip(screen, 0);\n"
         "\t}\n"
         "\tpkmgr_log_first_frame();\n\n\tperf.fps = current_fps;"),
    ])


if __name__ == "__main__":
    main()
