// pkmgr-box: a Gen 3 style PC for Pokémon Manager, drawn like the game's own PC and summary screens.
//
// Reads a scene description (JSON) written by pkmgr: the boxes (viewer box 0 is the party), each Pokémon's
// icon, sprite and summary text, and a skin folder with the game's PC/summary art and layout.json
// (scripts/pc_ui). Full screen, the GBA's 240x160 scaled up by a whole number.
//
//   D-pad   move the hand: box slots, the box title (left/right change box), PARTY POKéMON and CLOSE BOX
//   L1/R1   previous/next box
//   A       a Pokémon: the TRANSFER / SUMMARY / EVOLVE / CANCEL menu; PARTY POKéMON: open the party
//   B       back (closes the party, the menu, the summary, then the PC)
//
// Transfer and Evolve are written to --write-location as {"box": n, "slot": n, "action": "transfer"|"evolve"}.
// Exit codes: 0 picked, 2 back (B / CLOSE BOX), 3 back (MENU), 1 error.
//
// Built for NextUI with the device toolchains (make PLATFORM=...), or for a desktop with `make desktop`,
// which adds --screenshot out.png [--state box|party|menu|party_button|summary0|summary1|summary2].

#include <math.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <parson/parson.h>

#ifdef DESKTOP
#include "desktop.h"
#else
#include <msettings.h>
#include <SDL2/SDL_image.h>
#include <SDL2/SDL_ttf.h>
#include "defines.h"
#include "api.h"
#include "utils.h"
#endif

enum
{
    EXIT_PICKED = 0,
    EXIT_ERROR = 1,
    EXIT_BACK = 2,
    EXIT_MENU = 3,
};

#define GBA_W 240
#define GBA_H 160
#define MAX_SLOTS 36

typedef struct
{
    bool filled;
    char *icon;
    SDL_Rect icon_rect; // w == 0: the whole image (frames stacked 32x32)
    char *sprite;
    SDL_Rect sprite_rect;
    char *name;
    JSON_Object *summary; // owned by the scene's JSON
    bool can_transfer;    // greyed out in the menu when false
    bool can_evolve;
} Slot;

typedef struct
{
    char *name;
    char *wallpaper;
    int columns;
    int rows;
    Slot slots[MAX_SLOTS];
} Box;

typedef struct
{
    char *skin;
    char *font;
    Box *boxes; // [0] is the party
    int box_count;
    int box;
    int slot;
    JSON_Value *root;
    JSON_Value *layout_root;
    JSON_Object *layout;
} Scene;

// ------------------------------------------------------------------ images

typedef struct
{
    char *path;
    SDL_Surface *surface;
} CachedImage;

static CachedImage *cache = NULL;
static int cache_count = 0;
static int cache_capacity = 0;

static SDL_Surface *load_image(const char *path)
{
    if (path == NULL || path[0] == '\0')
        return NULL;
    for (int i = 0; i < cache_count; i++)
    {
        if (strcmp(cache[i].path, path) == 0)
            return cache[i].surface;
    }
    SDL_Surface *loaded = IMG_Load(path);
    SDL_Surface *converted = NULL;
    if (loaded != NULL)
    {
        converted = SDL_ConvertSurfaceFormat(loaded, SDL_PIXELFORMAT_RGBA8888, 0);
        SDL_FreeSurface(loaded);
    }
    if (cache_count == cache_capacity)
    {
        cache_capacity = cache_capacity ? cache_capacity * 2 : 64;
        cache = realloc(cache, sizeof(CachedImage) * cache_capacity);
    }
    cache[cache_count].path = strdup(path);
    cache[cache_count].surface = converted; // NULL is cached too, so missing files are only tried once
    cache_count++;
    return converted;
}

// An image from the skin folder.
static SDL_Surface *skin_image(Scene *scene, const char *name)
{
    char path[1024];
    snprintf(path, sizeof(path), "%s/%s", scene->skin ? scene->skin : ".", name);
    return load_image(path);
}

// ------------------------------------------------------------------ JSON helpers

static SDL_Rect get_rect(JSON_Object *obj, const char *key)
{
    SDL_Rect r = {0, 0, 0, 0};
    JSON_Array *a = obj ? json_object_dotget_array(obj, key) : NULL;
    if (a && json_array_get_count(a) >= 2)
    {
        r.x = (int)json_array_get_number(a, 0);
        r.y = (int)json_array_get_number(a, 1);
        if (json_array_get_count(a) >= 4)
        {
            r.w = (int)json_array_get_number(a, 2);
            r.h = (int)json_array_get_number(a, 3);
        }
    }
    return r;
}

static int get_int(JSON_Object *obj, const char *key, int fallback)
{
    return obj && json_object_dothas_value_of_type(obj, key, JSONNumber) ? (int)json_object_dotget_number(obj, key) : fallback;
}

static SDL_Color get_color(JSON_Object *obj, const char *key, SDL_Color fallback)
{
    JSON_Array *a = obj ? json_object_dotget_array(obj, key) : NULL;
    if (a == NULL || json_array_get_count(a) < 3)
        return fallback;
    return (SDL_Color){(Uint8)json_array_get_number(a, 0), (Uint8)json_array_get_number(a, 1), (Uint8)json_array_get_number(a, 2), 255};
}

static char *dup_string(JSON_Object *obj, const char *key)
{
    const char *value = json_object_get_string(obj, key);
    return value ? strdup(value) : NULL;
}

static bool load_scene(const char *path, Scene *scene)
{
    memset(scene, 0, sizeof(*scene));
    scene->root = json_parse_file(path);
    JSON_Object *obj = json_value_get_object(scene->root);
    if (obj == NULL)
        return false;

    scene->skin = dup_string(obj, "skin");
    scene->font = dup_string(obj, "font");
    scene->box = (int)json_object_get_number(obj, "box");
    scene->slot = (int)json_object_get_number(obj, "slot");

    char layout_path[1024];
    snprintf(layout_path, sizeof(layout_path), "%s/layout.json", scene->skin ? scene->skin : ".");
    scene->layout_root = json_parse_file(layout_path);
    scene->layout = json_value_get_object(scene->layout_root);
    if (scene->layout == NULL)
    {
        fprintf(stderr, "could not read %s\n", layout_path);
        return false;
    }

    JSON_Array *boxes = json_object_get_array(obj, "boxes");
    scene->box_count = boxes ? (int)json_array_get_count(boxes) : 0;
    if (scene->box_count < 2)
        return false;
    scene->boxes = calloc(scene->box_count, sizeof(Box));
    for (int b = 0; b < scene->box_count; b++)
    {
        JSON_Object *bo = json_array_get_object(boxes, b);
        Box *box = &scene->boxes[b];
        box->name = dup_string(bo, "name");
        box->wallpaper = dup_string(bo, "wallpaper");
        box->columns = b == 0 ? 6 : (int)json_object_get_number(bo, "columns");
        box->rows = b == 0 ? 1 : (int)json_object_get_number(bo, "rows");
        if (box->columns <= 0)
            box->columns = 6;
        if (box->rows <= 0)
            box->rows = 5;
        if (box->columns * box->rows > MAX_SLOTS)
            box->rows = MAX_SLOTS / box->columns;
        JSON_Array *slots = json_object_get_array(bo, "slots");
        int count = slots ? (int)json_array_get_count(slots) : 0;
        for (int s = 0; s < count && s < box->columns * box->rows; s++)
        {
            JSON_Object *so = json_array_get_object(slots, s);
            if (so == NULL || json_object_get_string(so, "name") == NULL)
                continue;
            Slot *slot = &box->slots[s];
            slot->filled = true;
            slot->icon = dup_string(so, "icon");
            slot->icon_rect = get_rect(so, "icon_rect");
            slot->sprite = dup_string(so, "sprite");
            slot->sprite_rect = get_rect(so, "sprite_rect");
            slot->name = dup_string(so, "name");
            slot->summary = json_object_get_object(so, "summary");
            slot->can_transfer = json_object_get_boolean(so, "can_transfer") != 0; // missing (-1) = allowed
            slot->can_evolve = json_object_get_boolean(so, "can_evolve") != 0;
        }
    }
    if (scene->box < 0 || scene->box >= scene->box_count)
        scene->box = 1;
    Box *first = &scene->boxes[scene->box];
    if (scene->slot < 0 || scene->slot >= first->columns * first->rows)
        scene->slot = 0;
    return true;
}

// ------------------------------------------------------------------ drawing basics

typedef struct
{
    int scale;
    int origin_x;
    int origin_y;
} Layout;

static Layout make_layout(SDL_Surface *screen)
{
    Layout l;
    int sx = screen->w / GBA_W, sy = screen->h / GBA_H;
    l.scale = sx < sy ? sx : sy;
    if (l.scale < 1)
        l.scale = 1;
    l.origin_x = (screen->w - GBA_W * l.scale) / 2;
    l.origin_y = (screen->h - GBA_H * l.scale) / 2;
    return l;
}

static void blit_scaled(SDL_Surface *src, SDL_Rect *src_rect, SDL_Surface *dst, const Layout *l, int x, int y)
{
    if (src == NULL)
        return;
    int w = src_rect ? src_rect->w : src->w;
    int h = src_rect ? src_rect->h : src->h;
    SDL_Rect d = {l->origin_x + x * l->scale, l->origin_y + y * l->scale, w * l->scale, h * l->scale};
    SDL_BlitScaled(src, src_rect, dst, &d);
}

static void fill_logical(SDL_Surface *dst, const Layout *l, int x, int y, int w, int h, SDL_Color c)
{
    SDL_Rect r = {l->origin_x + x * l->scale, l->origin_y + y * l->scale, w * l->scale, h * l->scale};
    SDL_FillRect(dst, &r, SDL_MapRGB(dst->format, c.r, c.g, c.b));
}

// ------------------------------------------------------------------ text

// The game's text, in NextUI's font sized so a capital letter is as tall as the game's.
static TTF_Font *text_font = NULL;
static int text_cap_offset = 0; // screen pixels from the font's top to the top of a capital letter
static int font_height = 10;    // GBA pixels of a capital letter
static SDL_Color text_color = {96, 96, 96, 255};
static SDL_Color text_shadow = {208, 208, 200, 255};

static void open_text_font(Scene *scene, const Layout *l)
{
    const char *path = scene->font;
#ifdef DESKTOP
    if (path == NULL)
        path = getenv("PKMGR_FONT");
#endif
    font_height = get_int(scene->layout, "font.height", 10);
    text_color = get_color(scene->layout, "font.color", text_color);
    text_shadow = get_color(scene->layout, "font.shadow", text_shadow);
    if (path == NULL)
        return;
    TTF_Font *probe = TTF_OpenFont(path, 100);
    if (probe == NULL)
        return;
    int minx, maxx, miny, maxy, advance;
    int probe_cap = TTF_GlyphMetrics(probe, 'H', &minx, &maxx, &miny, &maxy, &advance) == 0 && maxy > 0 ? maxy : 70;
    TTF_CloseFont(probe);
    text_font = TTF_OpenFont(path, 100 * font_height * l->scale / probe_cap);
    if (text_font && TTF_GlyphMetrics(text_font, 'H', &minx, &maxx, &miny, &maxy, &advance) == 0)
        text_cap_offset = TTF_FontAscent(text_font) - maxy;
}

static int text_width(const char *text, const Layout *l)
{
    int w = 0;
    if (text_font && text && text[0])
        TTF_SizeUTF8(text_font, text, &w, NULL);
    return (w + l->scale - 1) / l->scale;
}

// Draws text with the top of its capital letters at GBA (x, y), with the game's shadow (one GBA pixel right,
// down and diagonal). align: 0 left, 1 centre, 2 right (x is the right edge). Returns the width in GBA pixels.
static int draw_text_c(SDL_Surface *dst, const Layout *l, const char *text, int x, int y, int align, SDL_Color color, SDL_Color shadow)
{
    if (text_font == NULL || text == NULL || text[0] == '\0')
        return 0;
    int w = text_width(text, l);
    if (align == 1)
        x -= w / 2;
    else if (align == 2)
        x -= w;
    int px = l->origin_x + x * l->scale, py = l->origin_y + y * l->scale - text_cap_offset;
    SDL_Surface *s = shadow.a ? TTF_RenderUTF8_Blended(text_font, text, shadow) : NULL;
    SDL_Surface *t = TTF_RenderUTF8_Blended(text_font, text, color);
    if (s)
    {
        static const int offsets[3][2] = {{1, 0}, {0, 1}, {1, 1}};
        for (int i = 0; i < 3; i++)
        {
            SDL_Rect r = {px + offsets[i][0] * l->scale, py + offsets[i][1] * l->scale, 0, 0};
            SDL_BlitSurface(s, NULL, dst, &r);
        }
        SDL_FreeSurface(s);
    }
    if (t)
    {
        SDL_Rect r = {px, py, 0, 0};
        SDL_BlitSurface(t, NULL, dst, &r);
        SDL_FreeSurface(t);
    }
    return w;
}

static int draw_text(SDL_Surface *dst, const Layout *l, const char *text, int x, int y, int align)
{
    return draw_text_c(dst, l, text, x, y, align, text_color, text_shadow);
}

// A layout field: {"field": name, "x", "y", "color"?, "shadow"?, "align"?}.
static JSON_Object *find_field(JSON_Object *layout, const char *list, const char *name)
{
    JSON_Array *fields = json_object_dotget_array(layout, list);
    for (size_t i = 0; fields && i < json_array_get_count(fields); i++)
    {
        JSON_Object *f = json_array_get_object(fields, i);
        const char *n = json_object_get_string(f, "field");
        if (n && strcmp(n, name) == 0)
            return f;
    }
    return NULL;
}

static int field_align(JSON_Object *f)
{
    const char *a = json_object_get_string(f, "align");
    return a == NULL ? 0 : strcmp(a, "center") == 0 ? 1 : strcmp(a, "right") == 0 ? 2 : 0;
}

static void draw_field(SDL_Surface *dst, const Layout *l, JSON_Object *layout, const char *list, const char *name, const char *text)
{
    JSON_Object *f = find_field(layout, list, name);
    if (f == NULL || text == NULL)
        return;
    draw_text_c(dst, l, text, get_int(f, "x", 0), get_int(f, "y", 0), field_align(f),
                get_color(f, "color", text_color), get_color(f, "shadow", text_shadow));
}

// The trainer memo: lines split at \n, parts in { } in the highlight colour.
static void draw_memo(SDL_Surface *dst, const Layout *l, JSON_Object *f, const char *memo, SDL_Color highlight, SDL_Color highlight_shadow)
{
    if (f == NULL || memo == NULL)
        return;
    int x0 = get_int(f, "x", 0), y = get_int(f, "y", 0), line_height = get_int(f, "line_height", 13);
    SDL_Color normal = get_color(f, "color", text_color), shadow = get_color(f, "shadow", text_shadow);
    bool hl = false;
    int x = x0;
    char part[256];
    int n = 0;
    for (const char *p = memo;; p++)
    {
        char c = *p;
        if (c == '{' || c == '}' || c == '\n' || c == '\0')
        {
            part[n] = '\0';
            if (n)
                x += draw_text_c(dst, l, part, x, y, 0, hl ? highlight : normal, hl ? highlight_shadow : shadow);
            n = 0;
            if (c == '{')
                hl = true;
            else if (c == '}')
                hl = false;
            else if (c == '\n')
            {
                x = x0;
                y += line_height;
            }
            else
                break;
        }
        else if (n < (int)sizeof(part) - 1)
            part[n++] = c;
    }
}

// The standard window: window_frame.png is 3x3 tiles (corners, edges, centre fill).
static void draw_window(SDL_Surface *dst, Scene *scene, const Layout *l, int x, int y, int w, int h)
{
    SDL_Surface *frame = skin_image(scene, "window_frame.png");
    if (frame == NULL)
    {
        fill_logical(dst, l, x, y, w, h, (SDL_Color){96, 96, 96, 255});
        fill_logical(dst, l, x + 1, y + 1, w - 2, h - 2, (SDL_Color){255, 255, 255, 255});
        return;
    }
    int t = frame->w / 3;
    for (int ty = y; ty < y + h; ty += t)
    {
        for (int tx = x; tx < x + w; tx += t)
        {
            int col = tx == x ? 0 : tx + t >= x + w ? 2 : 1;
            int row = ty == y ? 0 : ty + t >= y + h ? 2 : 1;
            SDL_Rect src = {col * t, row * t, t, t};
            blit_scaled(frame, &src, dst, l, tx, ty);
        }
    }
}

// ------------------------------------------------------------------ PC screen

typedef enum
{
    TARGET_SLOT,
    TARGET_TITLE,
    TARGET_PARTY_BUTTON,
    TARGET_CLOSE_BUTTON,
} Target;

typedef enum
{
    MODE_BOX,
    MODE_PARTY,
    MODE_MENU,
    MODE_SUMMARY,
} Mode;

typedef struct
{
    Mode mode;
    Mode menu_from; // MODE_BOX or MODE_PARTY: where the menu and summary return to
    Target target;
    int party_slot; // 0-5, 6 = CANCEL
    int menu_item;
    int page;
} State;

static const char *MENU_ITEMS[] = {"TRANSFER", "SUMMARY", "EVOLVE", "CANCEL"};
#define MENU_COUNT 4

// Transfer and Evolve are greyed out (and do nothing) when the Pokémon can't be transferred or evolved.
static bool menu_enabled(Slot *slot, int item)
{
    return item == 0 ? slot->can_transfer : item == 2 ? slot->can_evolve : true;
}

static void draw_icon(SDL_Surface *screen, const Layout *l, Slot *slot, int x, int y, bool animate, Uint32 ticks)
{
    SDL_Surface *icon = load_image(slot->icon);
    if (icon == NULL)
        return;
    SDL_Rect cell = slot->icon_rect.w ? slot->icon_rect : (SDL_Rect){0, 0, 32, icon->h};
    int frame = animate && cell.h >= 64 ? (int)((ticks / 200) % 2) : 0;
    SDL_Rect src = {cell.x, cell.y + frame * 32, 32, 32};
    blit_scaled(icon, &src, screen, l, x, y);
}

static void draw_sprite(SDL_Surface *screen, const Layout *l, Slot *slot, int cx, int cy)
{
    SDL_Surface *sprite = load_image(slot->sprite);
    if (sprite == NULL)
        return;
    SDL_Rect src = slot->sprite_rect.w ? slot->sprite_rect : (SDL_Rect){0, 0, sprite->w, sprite->h};
    if (src.w > 64)
    {
        src.x += (src.w - 64) / 2;
        src.w = 64;
    }
    if (src.h > 64)
    {
        src.y += src.h - 64;
        src.h = 64;
    }
    blit_scaled(sprite, &src, screen, l, cx - src.w / 2, cy - src.h / 2);
}

static bool party_open(State *st)
{
    return st->mode == MODE_PARTY || (st->mode != MODE_BOX && st->menu_from == MODE_PARTY);
}

// The slot the hand is on, or NULL.
static Slot *hovered(Scene *scene, State *st)
{
    if (party_open(st))
        return st->party_slot < 6 ? &scene->boxes[0].slots[st->party_slot] : NULL;
    if (st->target != TARGET_SLOT)
        return NULL;
    return &scene->boxes[scene->box].slots[scene->slot];
}

static bool party_slot_xy(JSON_Object *lo, int i, int *x, int *y)
{
    JSON_Array *slots = json_object_dotget_array(lo, "pc.party.slots");
    JSON_Array *xy = slots && i < (int)json_array_get_count(slots) ? json_array_get_array(slots, i) : NULL;
    if (xy == NULL)
        return false;
    *x = (int)json_array_get_number(xy, 0);
    *y = (int)json_array_get_number(xy, 1);
    return true;
}

static void draw_pc(SDL_Surface *screen, Scene *scene, State *st, const Layout *l, Uint32 ticks)
{
    JSON_Object *lo = scene->layout;
    blit_scaled(skin_image(scene, "pc_bg.png"), NULL, screen, l, 0, 0);

    // Wallpaper, box name and arrows.
    Box *box = &scene->boxes[scene->box];
    SDL_Rect wp = get_rect(lo, "pc.wallpaper");
    blit_scaled(load_image(box->wallpaper), NULL, screen, l, wp.x, wp.y);
    SDL_Rect name = get_rect(lo, "pc.box_name");
    draw_text_c(screen, l, box->name, name.x, name.y - font_height / 2, 1,
                get_color(lo, "pc.box_name_color", text_color), get_color(lo, "pc.box_name_shadow", text_shadow));
    int bob = (int)((ticks / 250) % 2);
    SDL_Rect al = get_rect(lo, "pc.arrows.left"), ar = get_rect(lo, "pc.arrows.right");
    blit_scaled(skin_image(scene, "arrow_left.png"), NULL, screen, l, al.x - bob, al.y);
    blit_scaled(skin_image(scene, "arrow_right.png"), NULL, screen, l, ar.x + bob, ar.y);

    // Icons.
    int gx = get_int(lo, "pc.grid.x", 0), gy = get_int(lo, "pc.grid.y", 0);
    int dx = get_int(lo, "pc.grid.dx", 24), dy = get_int(lo, "pc.grid.dy", 24);
    for (int i = 0; i < box->columns * box->rows; i++)
    {
        if (box->slots[i].filled)
            draw_icon(screen, l, &box->slots[i], gx + (i % box->columns) * dx, gy + (i / box->columns) * dy,
                      st->mode == MODE_BOX && st->target == TARGET_SLOT && i == scene->slot, ticks);
    }

    // The party, when open.
    if (party_open(st))
    {
        blit_scaled(skin_image(scene, "pc_party.png"), NULL, screen, l, get_int(lo, "pc.party.x", 0), get_int(lo, "pc.party.y", 0));
        for (int i = 0; i < 6; i++)
        {
            int x, y;
            if (scene->boxes[0].slots[i].filled && party_slot_xy(lo, i, &x, &y))
                draw_icon(screen, l, &scene->boxes[0].slots[i], x, y, st->mode == MODE_PARTY && i == st->party_slot, ticks);
        }
    }

    // PKMN DATA: the Pokémon under the hand.
    Slot *cur = hovered(scene, st);
    if (cur && cur->filled)
    {
        SDL_Rect sp = get_rect(lo, "pc.mon_sprite");
        draw_sprite(screen, l, cur, sp.x, sp.y);
        JSON_Object *s = cur->summary;
        draw_field(screen, l, lo, "pc.mon_text", "nickname", json_object_get_string(s, "nickname"));
        draw_field(screen, l, lo, "pc.mon_text", "species", json_object_get_string(s, "species"));
        draw_field(screen, l, lo, "pc.mon_text", "level", json_object_get_string(s, "level"));
        const char *item = json_object_get_string(s, "item");
        if (item && strcmp(item, "NONE") != 0)
            draw_field(screen, l, lo, "pc.mon_text", "item", item);
    }

    // The hand: over the slot, the box title or a button, bobbing like the game's.
    SDL_Surface *hand = skin_image(scene, "hand.png");
    int hx = 0, hy = 0;
    SDL_Rect ho = get_rect(lo, "pc.hand_offset"), hbo = get_rect(lo, "pc.hand_button_offset");
    if (party_open(st))
    {
        if (st->party_slot >= 6 || !party_slot_xy(lo, st->party_slot, &hx, &hy))
        {
            SDL_Rect c = get_rect(lo, "pc.party.cancel");
            hx = c.x + c.w / 2 - 16 + hbo.x;
            hy = c.y + hbo.y;
        }
        else
        {
            hx += ho.x;
            hy += ho.y;
        }
    }
    else if (st->target == TARGET_SLOT)
    {
        hx = gx + (scene->slot % box->columns) * dx + ho.x;
        hy = gy + (scene->slot / box->columns) * dy + ho.y;
    }
    else if (st->target == TARGET_TITLE)
    {
        hx = name.x - 16;
        hy = name.y - 24;
    }
    else
    {
        SDL_Rect b = get_rect(lo, st->target == TARGET_PARTY_BUTTON ? "pc.party_button" : "pc.close_button");
        hx = b.x + b.w / 2 - 16 + hbo.x;
        hy = b.y + hbo.y;
    }
    int hand_bob = st->mode == MODE_MENU ? 0 : (int)(2.0 * sin(ticks / 160.0)) + 2;
    if (hand)
    {
        SDL_Rect src = {0, 0, hand->w >= 64 ? 32 : hand->w, hand->h >= 32 ? 32 : hand->h};
        blit_scaled(hand, &src, screen, l, hx, hy + hand_bob);
    }

    // The action menu and its message.
    if (st->mode == MODE_MENU && cur && cur->filled)
    {
        int line = get_int(lo, "pc.menu.line_height", 16), pad = get_int(lo, "pc.menu.padding", 8);
        int text_x = get_int(lo, "pc.menu.text_x", 16);
        int w = 0;
        for (int i = 0; i < MENU_COUNT; i++)
        {
            int tw = text_width(MENU_ITEMS[i], l);
            if (tw > w)
                w = tw;
        }
        w = ((text_x + w + pad + 7) / 8) * 8;
        int h = ((MENU_COUNT * line + pad * 2 + 7) / 8) * 8;
        int x = get_int(lo, "pc.menu.right", GBA_W - 8) - w, y = get_int(lo, "pc.menu.bottom", 120) - h;
        draw_window(screen, scene, l, x, y, w, h);
        for (int i = 0; i < MENU_COUNT; i++)
        {
            int ty = y + pad + i * line + (line - font_height) / 2;
            if (menu_enabled(cur, i))
                draw_text(screen, l, MENU_ITEMS[i], x + text_x, ty, 0);
            else
                draw_text_c(screen, l, MENU_ITEMS[i], x + text_x, ty, 0, get_color(lo, "pc.menu.disabled_color", (SDL_Color){176, 176, 176, 255}),
                            get_color(lo, "pc.menu.disabled_shadow", (SDL_Color){224, 224, 224, 255}));
            if (i != st->menu_item)
                continue;
            // The game's black triangle cursor.
            SDL_Surface *arrow = skin_image(scene, "cursor_menu.png");
            if (arrow)
                blit_scaled(arrow, NULL, screen, l, x + text_x - arrow->w - 1, ty + (font_height - arrow->h) / 2);
            else
            {
                for (int r = 0; r < 7; r++)
                    fill_logical(screen, l, x + text_x - 8, ty + (font_height - 7) / 2 + r, r < 4 ? r + 1 : 7 - r, 1, text_color);
            }
        }
        SDL_Rect m = get_rect(lo, "pc.message");
        draw_window(screen, scene, l, m.x, m.y, m.w, m.h);
        char msg[160];
        const char *nick = json_object_get_string(cur->summary, "nickname");
        snprintf(msg, sizeof(msg), "%s is selected.", nick ? nick : cur->name);
        draw_text(screen, l, msg, m.x + 8, m.y + (m.h - font_height) / 2, 0);
    }
}

// ------------------------------------------------------------------ summary screen

static void draw_type(SDL_Surface *screen, Scene *scene, const Layout *l, const char *type, int x, int y)
{
    if (type == NULL)
        return;
    char name[96];
    snprintf(name, sizeof(name), "types/%s.png", type);
    blit_scaled(skin_image(scene, name), NULL, screen, l, x, y);
}

static void draw_summary(SDL_Surface *screen, Scene *scene, State *st, const Layout *l)
{
    JSON_Object *lo = scene->layout;
    Slot *cur = hovered(scene, st);
    if (cur == NULL || !cur->filled)
        return;
    JSON_Object *s = cur->summary;
    bool egg = json_object_get_boolean(s, "egg") == 1;
    int page = egg ? 0 : st->page;
    static const char *PAGES[] = {"sum_info.png", "sum_skills.png", "sum_moves.png"};
    static const char *LISTS[] = {"summary.info", "summary.skills", "summary.moves"};
    blit_scaled(skin_image(scene, PAGES[page]), NULL, screen, l, 0, 0);
    const char *list = LISTS[page];

    // The left column, shown on every page.
    SDL_Rect sp = get_rect(lo, "summary.sprite");
    draw_sprite(screen, l, cur, sp.x, sp.y);
    draw_field(screen, l, lo, list, "nickname", json_object_get_string(s, "nickname"));
    draw_field(screen, l, lo, list, "species", json_object_get_string(s, "species"));
    draw_field(screen, l, lo, list, "level", json_object_get_string(s, "level"));
    const char *gender = json_object_get_string(s, "gender");
    JSON_Object *g = find_field(lo, list, "gender");
    if (gender && g)
    {
        bool male = strcmp(gender, "male") == 0;
        draw_text_c(screen, l, male ? "♂" : "♀", get_int(g, "x", 0), get_int(g, "y", 0), field_align(g),
                    get_color(g, male ? "male" : "female", male ? (SDL_Color){48, 80, 200, 255} : (SDL_Color){224, 8, 8, 255}),
                    get_color(g, male ? "male_shadow" : "female_shadow", text_shadow));
    }

    if (page == 0)
    {
        JSON_Object *ot = find_field(lo, list, "ot");
        if (ot)
        {
            bool female = json_object_get_boolean(s, "ot_female") == 1;
            draw_text_c(screen, l, json_object_get_string(s, "ot"), get_int(ot, "x", 0), get_int(ot, "y", 0), field_align(ot),
                        get_color(ot, female ? "female" : "male", female ? (SDL_Color){248, 56, 32, 255} : (SDL_Color){48, 184, 248, 255}),
                        get_color(ot, female ? "female_shadow" : "male_shadow", text_shadow));
        }
        draw_field(screen, l, lo, list, "id", json_object_get_string(s, "id"));
        JSON_Array *types = json_object_get_array(s, "types");
        JSON_Object *f1 = find_field(lo, list, "type1"), *f2 = find_field(lo, list, "type2");
        if (types && f1)
            draw_type(screen, scene, l, json_array_get_string(types, 0), get_int(f1, "x", 0), get_int(f1, "y", 0));
        if (types && f2 && json_array_get_count(types) > 1)
            draw_type(screen, scene, l, json_array_get_string(types, 1), get_int(f2, "x", 0), get_int(f2, "y", 0));
        draw_field(screen, l, lo, list, "ability", json_object_get_string(s, "ability"));
        draw_field(screen, l, lo, list, "ability_desc", json_object_get_string(s, "ability_desc"));
        draw_memo(screen, l, find_field(lo, list, "memo"), json_object_get_string(s, "memo"),
                  get_color(lo, "summary.memo_highlight", (SDL_Color){248, 0, 0, 255}),
                  get_color(lo, "summary.memo_highlight_shadow", (SDL_Color){248, 184, 112, 255}));
    }
    else if (page == 1)
    {
        static const char *FIELDS[] = {"item", "ribbon", "hp", "attack", "defense", "sp_atk", "sp_def", "speed", "exp_points", "next_lv"};
        for (size_t i = 0; i < sizeof(FIELDS) / sizeof(FIELDS[0]); i++)
            draw_field(screen, l, lo, list, FIELDS[i], json_object_get_string(s, FIELDS[i]));
        JSON_Object *bar = find_field(lo, list, "exp_bar");
        if (bar)
        {
            int w = (int)(get_int(bar, "w", 64) * json_object_get_number(s, "exp_fill"));
            fill_logical(screen, l, get_int(bar, "x", 0), get_int(bar, "y", 0), w, get_int(bar, "h", 2),
                         get_color(bar, "color", (SDL_Color){64, 200, 248, 255}));
        }
    }
    else
    {
        JSON_Object *m = find_field(lo, list, "moves");
        JSON_Array *moves = json_object_get_array(s, "moves");
        int row_dy = get_int(m, "row_dy", 16);
        for (int i = 0; m && moves && i < 4 && i < (int)json_array_get_count(moves); i++)
        {
            JSON_Object *mv = json_array_get_object(moves, i);
            int y = i * row_dy;
            draw_type(screen, scene, l, json_object_get_string(mv, "type"), get_int(m, "type_x", 0), get_int(m, "type_y", 0) + y);
            draw_text(screen, l, json_object_get_string(mv, "name"), get_int(m, "name_x", 0), get_int(m, "name_y", 0) + y, 0);
            char pp[32];
            if (json_object_has_value(mv, "max_pp"))
                snprintf(pp, sizeof(pp), "PP%2d/%2d", (int)json_object_get_number(mv, "pp"), (int)json_object_get_number(mv, "max_pp"));
            else
                snprintf(pp, sizeof(pp), "--");
            draw_text(screen, l, pp, get_int(m, "pp_x", 0), get_int(m, "pp_y", 0) + y, 2);
        }
    }
}

// ------------------------------------------------------------------ input

static void box_move(Scene *scene, State *st, int dx, int dy)
{
    Box *box = &scene->boxes[scene->box];
    if (dy != 0)
    {
        // Vertical order, wrapping: the buttons, the box title, rows 0..n-1.
        int row = st->target == TARGET_SLOT ? scene->slot / box->columns + 2 : st->target == TARGET_TITLE ? 1 : 0;
        int col = st->target == TARGET_SLOT ? scene->slot % box->columns : 0;
        int rows = box->rows + 2;
        row = (row + dy + rows) % rows;
        if (row == 0)
            st->target = st->target == TARGET_SLOT && col >= box->columns / 2 ? TARGET_CLOSE_BUTTON : TARGET_PARTY_BUTTON;
        else if (row == 1)
            st->target = TARGET_TITLE;
        else
        {
            if (st->target == TARGET_PARTY_BUTTON)
                col = 1;
            else if (st->target == TARGET_CLOSE_BUTTON)
                col = box->columns - 2;
            else if (st->target == TARGET_TITLE)
                col = scene->slot % box->columns;
            st->target = TARGET_SLOT;
            scene->slot = (row - 2) * box->columns + col;
        }
        return;
    }
    if (st->target == TARGET_SLOT)
    {
        int col = scene->slot % box->columns, row = scene->slot / box->columns;
        col = (col + dx + box->columns) % box->columns;
        scene->slot = row * box->columns + col;
    }
    else if (st->target == TARGET_PARTY_BUTTON || st->target == TARGET_CLOSE_BUTTON)
        st->target = st->target == TARGET_PARTY_BUTTON ? TARGET_CLOSE_BUTTON : TARGET_PARTY_BUTTON;
}

static void change_box(Scene *scene, int delta)
{
    int pc_boxes = scene->box_count - 1;
    scene->box = 1 + ((scene->box - 1 + delta) % pc_boxes + pc_boxes) % pc_boxes;
}

// The party panel: slot 0 on the left, 1-5 down the right, CANCEL (6) below them.
static void party_move(State *st, int dx, int dy)
{
    int s = st->party_slot;
    if (dx < 0)
        s = 0;
    else if (dx > 0 && s == 0)
        s = 1;
    else if (dy != 0)
    {
        if (s == 0)
            s = 6;
        else
            s = s + dy < 1 ? 6 : s + dy > 6 ? 1 : s + dy;
    }
    st->party_slot = s;
}

// The next filled slot in the same box or party, for flicking through summaries.
static void summary_step(Scene *scene, State *st, int delta)
{
    bool party = st->menu_from == MODE_PARTY;
    Box *box = party ? &scene->boxes[0] : &scene->boxes[scene->box];
    int count = party ? 6 : box->columns * box->rows;
    int at = party ? st->party_slot : scene->slot;
    for (int i = 1; i < count; i++)
    {
        int next = ((at + delta * i) % count + count) % count;
        if (box->slots[next].filled)
        {
            if (party)
                st->party_slot = next;
            else
                scene->slot = next;
            return;
        }
    }
}

static void write_result(const char *path, Scene *scene, State *st, const char *action)
{
    if (path == NULL)
        return;
    FILE *f = fopen(path, "w");
    if (f == NULL)
        return;
    bool party = st->menu_from == MODE_PARTY;
    fprintf(f, "{\"box\": %d, \"slot\": %d, \"action\": \"%s\"}\n", party ? 0 : scene->box, party ? st->party_slot : scene->slot, action);
    fclose(f);
}

static void draw(SDL_Surface *screen, Scene *scene, State *st, const Layout *l, Uint32 ticks)
{
    SDL_FillRect(screen, NULL, SDL_MapRGB(screen->format, 0, 0, 0));
    if (st->mode == MODE_SUMMARY)
        draw_summary(screen, scene, st, l);
    else
        draw_pc(screen, scene, st, l, ticks);
}

int main(int argc, char *argv[])
{
    const char *scene_path = NULL;
    const char *write_location = NULL;
    const char *screenshot = NULL;
    const char *state_name = "box";
    for (int i = 1; i < argc; i++)
    {
        if (strcmp(argv[i], "--scene") == 0 && i + 1 < argc)
            scene_path = argv[++i];
        else if (strcmp(argv[i], "--write-location") == 0 && i + 1 < argc)
            write_location = argv[++i];
        else if (strcmp(argv[i], "--screenshot") == 0 && i + 1 < argc)
            screenshot = argv[++i];
        else if (strcmp(argv[i], "--state") == 0 && i + 1 < argc)
            state_name = argv[++i];
    }
    if (scene_path == NULL)
    {
        fprintf(stderr, "usage: pkmgr-box --scene scene.json --write-location out.json\n");
        return EXIT_ERROR;
    }

    Scene scene;
    if (!load_scene(scene_path, &scene))
    {
        fprintf(stderr, "could not read %s\n", scene_path);
        return EXIT_ERROR;
    }

    PWR_setCPUSpeed(CPU_SPEED_MENU);
    SDL_Surface *screen = GFX_init(MODE_MAIN);
    PAD_init();
    PWR_init();
    InitSettings();

    Layout layout = make_layout(screen);
    open_text_font(&scene, &layout);

    State st = {MODE_BOX, MODE_BOX, TARGET_SLOT, 0, 0, 0};
    if (scene.box == 0)
    {
        // Back from a party Pokémon: the party is open again.
        st.mode = st.menu_from = MODE_PARTY;
        st.party_slot = scene.slot < 6 ? scene.slot : 0;
        scene.box = 1;
        scene.slot = 0;
    }

#ifdef DESKTOP
    if (screenshot)
    {
        if (strcmp(state_name, "party") == 0)
            st.mode = st.menu_from = MODE_PARTY;
        else if (strcmp(state_name, "menu") == 0)
            st.mode = MODE_MENU;
        else if (strncmp(state_name, "summary", 7) == 0)
        {
            st.mode = MODE_SUMMARY;
            st.page = atoi(state_name + 7);
        }
        else if (strcmp(state_name, "party_button") == 0)
            st.target = TARGET_PARTY_BUTTON;
        draw(screen, &scene, &st, &layout, 0);
        IMG_SavePNG(screen, screenshot);
        return EXIT_PICKED;
    }
#else
    (void)screenshot;
    (void)state_name;
#endif

    int exit_code = EXIT_BACK;
    const char *action = NULL;
    bool quitting = false;
    int dirty = 1;
    int show_setting = 0;
    Uint32 last_anim = 0;
    while (!quitting)
    {
        GFX_startFrame();
        PWR_update(&dirty, &show_setting, NULL, NULL);
        PAD_poll();

        int dx = PAD_justRepeated(BTN_LEFT) ? -1 : PAD_justRepeated(BTN_RIGHT) ? 1 : 0;
        int dy = PAD_justRepeated(BTN_UP) ? -1 : PAD_justRepeated(BTN_DOWN) ? 1 : 0;
        bool a = PAD_justReleased(BTN_A), b = PAD_justReleased(BTN_B);
        bool l1 = PAD_justRepeated(BTN_L1), r1 = PAD_justRepeated(BTN_R1);
        if (dx || dy || a || b || l1 || r1)
            dirty = 1;

        if (PAD_justReleased(BTN_MENU))
        {
            exit_code = EXIT_MENU;
            quitting = true;
        }
        else if (st.mode == MODE_BOX)
        {
            if (l1 || (st.target == TARGET_TITLE && dx < 0))
                change_box(&scene, -1);
            else if (r1 || (st.target == TARGET_TITLE && dx > 0))
                change_box(&scene, 1);
            else if (dx || dy)
                box_move(&scene, &st, dx, dy);
            else if (a && st.target == TARGET_SLOT && scene.boxes[scene.box].slots[scene.slot].filled)
            {
                st.mode = MODE_MENU;
                st.menu_from = MODE_BOX;
                st.menu_item = 0;
            }
            else if (a && st.target == TARGET_PARTY_BUTTON)
            {
                st.mode = st.menu_from = MODE_PARTY;
                st.party_slot = 0;
            }
            else if ((a && st.target == TARGET_CLOSE_BUTTON) || b)
            {
                exit_code = EXIT_BACK;
                quitting = true;
            }
        }
        else if (st.mode == MODE_PARTY)
        {
            if (dx || dy)
                party_move(&st, dx, dy);
            else if (a && st.party_slot < 6 && scene.boxes[0].slots[st.party_slot].filled)
            {
                st.mode = MODE_MENU;
                st.menu_from = MODE_PARTY;
                st.menu_item = 0;
            }
            else if ((a && st.party_slot == 6) || b)
            {
                st.mode = st.menu_from = MODE_BOX;
                st.target = TARGET_PARTY_BUTTON;
            }
        }
        else if (st.mode == MODE_MENU)
        {
            if (dy)
                st.menu_item = (st.menu_item + dy + MENU_COUNT) % MENU_COUNT;
            else if (a && (st.menu_item == 0 || st.menu_item == 2) && menu_enabled(hovered(&scene, &st), st.menu_item))
            {
                action = st.menu_item == 0 ? "transfer" : "evolve";
                exit_code = EXIT_PICKED;
                quitting = true;
            }
            else if (a && st.menu_item == 1)
            {
                st.mode = MODE_SUMMARY;
                st.page = 0;
            }
            else if ((a && st.menu_item == 3) || b)
                st.mode = st.menu_from;
        }
        else if (st.mode == MODE_SUMMARY)
        {
            if (dx || l1 || r1)
                st.page = (st.page + (dx ? dx : l1 ? -1 : 1) + 3) % 3;
            else if (dy)
                summary_step(&scene, &st, dy);
            else if (b)
                st.mode = st.menu_from;
        }

        // Animate at ~12 fps (icons, hand, arrows) without redrawing every frame.
        Uint32 now = SDL_GetTicks();
        if (now - last_anim >= 80)
        {
            last_anim = now;
            dirty = 1;
        }
        if (dirty && !quitting)
        {
            draw(screen, &scene, &st, &layout, now);
            GFX_flip(screen);
            dirty = 0;
        }
        else
        {
            GFX_sync();
        }
    }

    if (exit_code == EXIT_PICKED && action)
        write_result(write_location, &scene, &st, action);

    QuitSettings();
    PWR_quit();
    PAD_quit();
    GFX_quit();
    return exit_code;
}
