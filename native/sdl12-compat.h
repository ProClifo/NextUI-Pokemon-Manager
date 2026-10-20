// The part of SDL2's surface API pkmgr-box uses, on SDL 1.2 (MinUI's TrimUI Smart; `make desktop-sdl12` on a PC).
//
// Blits follow SDL2's rules rather than SDL 1.2's: a surface's blend mode (SDL_SetSurfaceBlendMode, kept in its
// SDL_SRCALPHA flag) decides between copying a pixel and blending it, blending also blends the alpha channel (SDL 1.2
// keeps the destination's), and SDL_BlitScaled and SDL_SetSurfaceColorMod work. They're done here pixel by pixel,
// between any formats, so they can draw straight onto MinUI's RGB565 screen. SDL_Rect's w and h are 16-bit unsigned
// in SDL 1.2: a negative width wraps round, so anything over 32767 is taken as nothing.
#pragma once
#include <string.h>

typedef enum
{
    SDL_BLENDMODE_NONE,
    SDL_BLENDMODE_BLEND,
} SDL_BlendMode;

// Only the formats pkmgr-box asks for, by their byte layout (RGBA32 is SDL2's R,G,B,A in memory, little-endian).
enum
{
    SDL_PIXELFORMAT_RGBA8888 = 1,
    SDL_PIXELFORMAT_ARGB8888,
    SDL_PIXELFORMAT_RGBA32,
};

// SDL 1.2 calls SDL_Color's fourth byte "unused".
#define COLOR_ALPHA(c) ((c).unused)

static SDL_Surface *SDL_CreateRGBSurfaceWithFormat(Uint32 flags, int w, int h, int depth, Uint32 format)
{
    (void)flags;
    (void)depth;
    Uint32 r = 0xFF000000, g = 0x00FF0000, b = 0x0000FF00, a = 0x000000FF;
    if (format == SDL_PIXELFORMAT_ARGB8888)
        a = 0xFF000000, r = 0x00FF0000, g = 0x0000FF00, b = 0x000000FF;
    else if (format == SDL_PIXELFORMAT_RGBA32)
        r = 0x000000FF, g = 0x0000FF00, b = 0x00FF0000, a = 0xFF000000;
    SDL_Surface *s = SDL_CreateRGBSurface(SDL_SWSURFACE, w, h, 32, r, g, b, a);
    if (s)
    {
        memset(s->pixels, 0, s->pitch * s->h);
        SDL_SetAlpha(s, SDL_SRCALPHA, 255); // SDL2: surfaces with alpha blend
    }
    return s;
}

static int SDL_SetSurfaceBlendMode(SDL_Surface *s, SDL_BlendMode mode)
{
    if (s == NULL)
        return -1;
    if (mode == SDL_BLENDMODE_BLEND)
        s->flags |= SDL_SRCALPHA;
    else
        s->flags &= ~SDL_SRCALPHA;
    return 0;
}

// Colour mods by surface (pkmgr-box sets one on a font sheet around a blit, and back to white after).
#define COMPAT_COLOR_MODS 8
static struct
{
    SDL_Surface *surface;
    Uint8 r, g, b;
} compat_color_mods[COMPAT_COLOR_MODS];

static int SDL_SetSurfaceColorMod(SDL_Surface *s, Uint8 r, Uint8 g, Uint8 b)
{
    int free_slot = -1;
    for (int i = 0; i < COMPAT_COLOR_MODS; i++)
    {
        if (compat_color_mods[i].surface == s)
        {
            if (r == 255 && g == 255 && b == 255)
                compat_color_mods[i].surface = NULL;
            else
                compat_color_mods[i].r = r, compat_color_mods[i].g = g, compat_color_mods[i].b = b;
            return 0;
        }
        if (compat_color_mods[i].surface == NULL && free_slot < 0)
            free_slot = i;
    }
    if (r == 255 && g == 255 && b == 255)
        return 0;
    if (free_slot < 0)
        return -1;
    compat_color_mods[free_slot].surface = s;
    compat_color_mods[free_slot].r = r, compat_color_mods[free_slot].g = g, compat_color_mods[free_slot].b = b;
    return 0;
}

static Uint32 compat_read(const SDL_Surface *s, int x, int y)
{
    const Uint8 *p = (const Uint8 *)s->pixels + y * s->pitch + x * s->format->BytesPerPixel;
    switch (s->format->BytesPerPixel)
    {
    case 1:
        return *p;
    case 2:
        return *(const Uint16 *)p;
    case 3:
        return p[0] | p[1] << 8 | p[2] << 16;
    default:
        return *(const Uint32 *)p;
    }
}

static void compat_write(SDL_Surface *s, int x, int y, Uint32 v)
{
    Uint8 *p = (Uint8 *)s->pixels + y * s->pitch + x * s->format->BytesPerPixel;
    switch (s->format->BytesPerPixel)
    {
    case 1:
        *p = (Uint8)v;
        break;
    case 2:
        *(Uint16 *)p = (Uint16)v;
        break;
    case 3:
        p[0] = v, p[1] = v >> 8, p[2] = v >> 16;
        break;
    default:
        *(Uint32 *)p = v;
    }
}

static Uint8 compat_channel(Uint32 v, Uint32 mask, Uint8 shift, Uint8 loss)
{
    Uint32 c = (v & mask) >> shift;
    return loss ? (Uint8)(c << loss | c >> (8 - 2 * loss)) : (Uint8)c;
}

static void compat_rgba(const SDL_PixelFormat *f, Uint32 v, Uint8 *r, Uint8 *g, Uint8 *b, Uint8 *a)
{
    if (f->palette)
    {
        SDL_Color c = f->palette->colors[v < (Uint32)f->palette->ncolors ? v : 0];
        *r = c.r, *g = c.g, *b = c.b, *a = 255;
        return;
    }
    *r = compat_channel(v, f->Rmask, f->Rshift, f->Rloss);
    *g = compat_channel(v, f->Gmask, f->Gshift, f->Gloss);
    *b = compat_channel(v, f->Bmask, f->Bshift, f->Bloss);
    *a = f->Amask ? compat_channel(v, f->Amask, f->Ashift, f->Aloss) : 255;
}

static Uint32 compat_pixel(const SDL_PixelFormat *f, Uint8 r, Uint8 g, Uint8 b, Uint8 a)
{
    if (f->palette)
        return SDL_MapRGB((SDL_PixelFormat *)f, r, g, b);
    return (Uint32)(r >> f->Rloss) << f->Rshift | (Uint32)(g >> f->Gloss) << f->Gshift | (Uint32)(b >> f->Bloss) << f->Bshift
           | (f->Amask ? (Uint32)(a >> f->Aloss) << f->Ashift : 0);
}

static int compat_size(Uint16 v) { return v > 32767 ? 0 : v; }

// Draws src's sw x sh pixels from (sx, sy) at dst's (dx, dy) in dw x dh, nearest neighbour, clipped to both surfaces.
static void compat_draw(SDL_Surface *src, int sx, int sy, int sw, int sh, SDL_Surface *dst, int dx, int dy, int dw, int dh, SDL_Rect *out)
{
    if (out)
        out->x = dx, out->y = dy, out->w = 0, out->h = 0;
    if (src == NULL || dst == NULL || sw <= 0 || sh <= 0 || dw <= 0 || dh <= 0)
        return;
    const SDL_Rect *clip = &dst->clip_rect;
    int x0 = dx > clip->x ? dx : clip->x, y0 = dy > clip->y ? dy : clip->y;
    int x1 = dx + dw < clip->x + clip->w ? dx + dw : clip->x + clip->w;
    int y1 = dy + dh < clip->y + clip->h ? dy + dh : clip->y + clip->h;
    if (x0 >= x1 || y0 >= y1)
        return;
    if (out)
        out->x = x0, out->y = y0, out->w = x1 - x0, out->h = y1 - y0;
    bool blend = (src->flags & SDL_SRCALPHA) != 0;
    bool keyed = (src->flags & SDL_SRCCOLORKEY) != 0;
    Uint32 key = src->format->colorkey;
    Uint8 mr = 255, mg = 255, mb = 255;
    for (int i = 0; i < COMPAT_COLOR_MODS; i++)
        if (compat_color_mods[i].surface == src)
            mr = compat_color_mods[i].r, mg = compat_color_mods[i].g, mb = compat_color_mods[i].b;
    bool mod = mr != 255 || mg != 255 || mb != 255;
    for (int y = y0; y < y1; y++)
    {
        int yy = sy + (y - dy) * sh / dh;
        if (yy < 0 || yy >= src->h)
            continue;
        for (int x = x0; x < x1; x++)
        {
            int xx = sx + (x - dx) * sw / dw;
            if (xx < 0 || xx >= src->w)
                continue;
            Uint32 v = compat_read(src, xx, yy);
            if (keyed && v == key)
                continue;
            Uint8 r, g, b, a;
            compat_rgba(src->format, v, &r, &g, &b, &a);
            if (mod)
                r = r * mr / 255, g = g * mg / 255, b = b * mb / 255;
            if (blend && a != 255)
            {
                if (a == 0)
                    continue;
                Uint8 dr, dg, db, da;
                compat_rgba(dst->format, compat_read(dst, x, y), &dr, &dg, &db, &da);
                r = (r * a + dr * (255 - a)) / 255;
                g = (g * a + dg * (255 - a)) / 255;
                b = (b * a + db * (255 - a)) / 255;
                a = a + da * (255 - a) / 255;
            }
            compat_write(dst, x, y, compat_pixel(dst->format, r, g, b, a));
        }
    }
}

static int compat_blit(SDL_Surface *src, SDL_Rect *src_rect, SDL_Surface *dst, SDL_Rect *dst_rect)
{
    if (src == NULL || dst == NULL)
        return -1;
    int sx = 0, sy = 0, w = src->w, h = src->h;
    if (src_rect)
        sx = src_rect->x, sy = src_rect->y, w = compat_size(src_rect->w), h = compat_size(src_rect->h);
    int dx = dst_rect ? dst_rect->x : 0, dy = dst_rect ? dst_rect->y : 0;
    // as SDL: the part of the source rectangle inside the source
    if (sx < 0)
        w += sx, dx -= sx, sx = 0;
    if (sy < 0)
        h += sy, dy -= sy, sy = 0;
    if (sx + w > src->w)
        w = src->w - sx;
    if (sy + h > src->h)
        h = src->h - sy;
    compat_draw(src, sx, sy, w, h, dst, dx, dy, w, h, dst_rect);
    return 0;
}

static int compat_blit_scaled(SDL_Surface *src, SDL_Rect *src_rect, SDL_Surface *dst, SDL_Rect *dst_rect)
{
    if (src == NULL || dst == NULL)
        return -1;
    SDL_Rect s = src_rect ? *src_rect : (SDL_Rect){0, 0, src->w, src->h};
    SDL_Rect d = dst_rect ? *dst_rect : (SDL_Rect){0, 0, dst->w, dst->h};
    compat_draw(src, s.x, s.y, compat_size(s.w), compat_size(s.h), dst, d.x, d.y, compat_size(d.w), compat_size(d.h), dst_rect);
    return 0;
}

static int compat_fill(SDL_Surface *dst, SDL_Rect *rect, Uint32 color)
{
    if (rect && (rect->w > 32767 || rect->h > 32767))
        return 0;
    return SDL_FillRect(dst, rect, color);
}

// A copy in one of the formats above (the colour key, if any, made transparent), blending as SDL2's would. A surface
// already in that format is returned itself, with one more reference (SDL_FreeSurface drops one): RGBA PNGs load as
// RGBA32, and a DS game's sprite sheet is 22 MB, a lot to have twice in the TrimUI Smart's 120 MB.
static SDL_Surface *SDL_ConvertSurfaceFormat(SDL_Surface *src, Uint32 format, Uint32 flags)
{
    (void)flags;
    SDL_Surface *out = src ? SDL_CreateRGBSurfaceWithFormat(0, 1, 1, 32, format) : NULL;
    if (out == NULL)
        return NULL;
    const SDL_PixelFormat *a = src->format, *b = out->format;
    bool same = a->BitsPerPixel == 32 && a->Rmask == b->Rmask && a->Gmask == b->Gmask && a->Bmask == b->Bmask && a->Amask == b->Amask;
    SDL_FreeSurface(out);
    if (same && !(src->flags & SDL_SRCCOLORKEY))
    {
        src->refcount++;
        src->flags |= SDL_SRCALPHA;
        return src;
    }
    out = SDL_CreateRGBSurfaceWithFormat(0, src->w, src->h, 32, format);
    if (out == NULL)
        return NULL;
    Uint32 was = src->flags & SDL_SRCALPHA;
    src->flags &= ~SDL_SRCALPHA;
    compat_blit(src, NULL, out, NULL);
    src->flags |= was;
    return out;
}

#undef SDL_BlitSurface
#define SDL_BlitSurface compat_blit
#define SDL_BlitScaled compat_blit_scaled
#define SDL_FillRect compat_fill
