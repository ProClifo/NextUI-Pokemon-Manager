// Minimal stand-in for the NextUI API so pkmgr-box can be built and screenshotted on a desktop.
// Screen size and UI scale come from PKMGR_WIDTH / PKMGR_HEIGHT / PKMGR_SCALE, the font from PKMGR_FONT.
// With USE_SDL12 (make desktop-sdl12) it's built on SDL 1.2 through sdl12-compat.h, with an RGB565 screen, as on
// MinUI's TrimUI Smart, and screenshots are BMPs whatever their name.
#pragma once
#include <stdbool.h>
#include <stdlib.h>
#ifdef USE_SDL12
#include <SDL/SDL.h>
#include <SDL/SDL_image.h>
#include <SDL/SDL_ttf.h>
#include "sdl12-compat.h"
#define IMG_SavePNG(surface, path) SDL_SaveBMP(surface, path)
#else
#include <SDL2/SDL.h>
#include <SDL2/SDL_image.h>
#include <SDL2/SDL_ttf.h>
#endif

#define FONT_LARGE 16
#define FONT_MEDIUM 14
#define FONT_SMALL 12
#define FONT_TINY 10
#define PADDING 10
#define PILL_SIZE 30
#define BUTTON_PADDING 12
#define BUTTON_MARGIN 5
static int desktop_scale = 2;
#define SCALE1(a) ((a) * desktop_scale)
#define CPU_SPEED_MENU 0
#define MODE_MAIN 0
enum { BTN_UP = 1, BTN_DOWN = 2, BTN_LEFT = 4, BTN_RIGHT = 8, BTN_A = 16, BTN_B = 32, BTN_L1 = 64, BTN_R1 = 128, BTN_MENU = 256, BTN_X = 512, BTN_Y = 1024 };

typedef struct { TTF_Font *large, *medium, *small, *tiny; } GFX_Fonts;
static GFX_Fonts font;

static int env_int(const char *name, int fallback)
{
    const char *v = getenv(name);
    return v ? atoi(v) : fallback;
}

static SDL_Surface *GFX_init(int mode)
{
    (void)mode;
    SDL_Init(SDL_INIT_VIDEO);
    IMG_Init(IMG_INIT_PNG);
    TTF_Init();
    desktop_scale = env_int("PKMGR_SCALE", 2);
    const char *path = getenv("PKMGR_FONT");
    if (path)
    {
        font.large = TTF_OpenFont(path, SCALE1(FONT_LARGE));
        font.medium = TTF_OpenFont(path, SCALE1(FONT_MEDIUM));
        font.small = TTF_OpenFont(path, SCALE1(FONT_SMALL));
        font.tiny = TTF_OpenFont(path, SCALE1(FONT_TINY));
    }
#ifdef USE_SDL12
    return SDL_CreateRGBSurface(SDL_SWSURFACE, env_int("PKMGR_WIDTH", 640), env_int("PKMGR_HEIGHT", 480), 16, 0xF800, 0x07E0, 0x001F, 0);
#else
    return SDL_CreateRGBSurfaceWithFormat(0, env_int("PKMGR_WIDTH", 640), env_int("PKMGR_HEIGHT", 480), 32, SDL_PIXELFORMAT_RGBA8888);
#endif
}

static void GFX_clear(SDL_Surface *s) { SDL_FillRect(s, NULL, 0); }
static void GFX_quit(void) {}
static void GFX_startFrame(void) {}
static void GFX_flip(SDL_Surface *s) { (void)s; }
static void GFX_sync(void) {}

// Draws "BUTTON label" pairs in a dark pill at the bottom of the screen, like NextUI's hint groups.
static int GFX_blitButtonGroup(char **hints, int primary, SDL_Surface *dst, int align_right)
{
    (void)primary;
    char text[256] = "";
    for (int i = 0; hints[i] && hints[i + 1]; i += 2)
    {
        if (text[0])
            strcat(text, "   ");
        strcat(text, hints[i]);
        strcat(text, " ");
        strcat(text, hints[i + 1]);
    }
    if (!font.small)
        return 0;
    SDL_Color white = {255, 255, 255, 255};
    SDL_Surface *t = TTF_RenderUTF8_Blended(font.small, text, white);
    if (!t)
        return 0;
    int h = SCALE1(PILL_SIZE);
    int w = t->w + SCALE1(PADDING) * 2;
    int x = align_right ? dst->w - w - SCALE1(PADDING) : SCALE1(PADDING);
    int y = dst->h - h - SCALE1(PADDING);
    SDL_Rect pill = {x, y, w, h};
    SDL_FillRect(dst, &pill, SDL_MapRGB(dst->format, 30, 30, 30));
    SDL_Rect r = {x + SCALE1(PADDING), y + (h - t->h) / 2, 0, 0};
    SDL_BlitSurface(t, NULL, dst, &r);
    SDL_FreeSurface(t);
    return w;
}

static void PAD_init(void) {}
static void PAD_quit(void) {}
static void PAD_poll(void) {}
static int PAD_justRepeated(int b) { (void)b; return 0; }
static int PAD_justReleased(int b) { (void)b; return 0; }
static void PWR_init(void) {}
static void PWR_quit(void) {}
static void PWR_setCPUSpeed(int s) { (void)s; }
static void PWR_update(int *d, int *s, void *a, void *b) { (void)d; (void)s; (void)a; (void)b; }
static void InitSettings(void) {}
static void QuitSettings(void) {}
