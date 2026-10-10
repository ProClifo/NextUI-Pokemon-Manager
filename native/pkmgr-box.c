// pkmgr-box: the PC for Pokémon Manager, drawn like the games' own PC and summary screens (Gen 3 here; Gen 1/2 in
// pkmgr-box-gb.c).
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
    char *transfer_why; // why Transfer/Evolve can't be used, shown when the greyed-out item is picked
    char *evolve_why;
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
            slot->transfer_why = dup_string(so, "transfer_why");
            slot->evolve_why = dup_string(so, "evolve_why");
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

// The game's text, in NextUI's font sized so a capital letter is as tall as the game's. Layout positions are
// the top of the game's 16-pixel glyph cell; capitals start font.cap_top pixels below it.
static TTF_Font *text_font = NULL;
static int text_cap_offset = 0; // screen pixels from the TTF line's top to the top of a capital letter
static int font_height = 10;    // GBA pixels of a capital letter
static int cap_top = 2;         // GBA pixels from the glyph cell's top to a capital's top
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
    cap_top = get_int(scene->layout, "font.cap_top", 2);
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

// Draws text in a glyph cell whose top is GBA y, with the game's shadow one GBA pixel right, down and diagonal.
// align: 0 left, 1 centre, 2 right (x is the right edge). Returns the width in GBA pixels.
static int draw_text_c(SDL_Surface *dst, const Layout *l, const char *text, int x, int y, int align, SDL_Color color, SDL_Color shadow)
{
    if (text_font == NULL || text == NULL || text[0] == '\0')
        return 0;
    int w = text_width(text, l);
    if (align == 1)
        x -= w / 2;
    else if (align == 2)
        x -= w;
    int px = l->origin_x + x * l->scale, py = l->origin_y + (y + cap_top) * l->scale - text_cap_offset;
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

// A layout field: {"field": name, "x", "y", "color", "shadow", "align"?, "prefix"?, "prefix_x"?, "male"/"female"?}.
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

// A text field: its prefix ("/", "Lv") and value. A prefix_x draws the prefix there and the value on its own.
static void draw_field(SDL_Surface *dst, const Layout *l, JSON_Object *layout, const char *list, const char *name, const char *value)
{
    JSON_Object *f = find_field(layout, list, name);
    if (f == NULL || value == NULL || value[0] == '\0')
        return;
    SDL_Color color = get_color(f, "color", text_color), shadow = get_color(f, "shadow", text_shadow);
    const char *prefix = json_object_get_string(f, "prefix");
    char buf[256];
    if (prefix && json_object_has_value(f, "prefix_x"))
    {
        draw_text_c(dst, l, prefix, get_int(f, "prefix_x", 0), get_int(f, "y", 0), 0, color, shadow);
        snprintf(buf, sizeof(buf), "%s", value);
    }
    else
        snprintf(buf, sizeof(buf), "%s%s", prefix ? prefix : "", value);
    draw_text_c(dst, l, buf, get_int(f, "x", 0), get_int(f, "y", 0), field_align(f), color, shadow);
}

// The gender symbol in the field's male/female colours.
static void draw_gender(SDL_Surface *dst, const Layout *l, JSON_Object *layout, const char *list, const char *gender)
{
    JSON_Object *f = find_field(layout, list, "gender");
    if (f == NULL || gender == NULL)
        return;
    bool male = strcmp(gender, "male") == 0;
    SDL_Color fallback = male ? (SDL_Color){66, 206, 255, 255} : (SDL_Color){255, 156, 148, 255};
    draw_text_c(dst, l, male ? "♂" : "♀", get_int(f, "x", 0), get_int(f, "y", 0), 0,
                get_color(f, male ? "male.color" : "female.color", fallback), get_color(f, male ? "male.shadow" : "female.shadow", text_shadow));
}

// Multi-line text: lines split at \n, parts in { } in the highlight colour.
static void draw_lines(SDL_Surface *dst, const Layout *l, JSON_Object *f, const char *text)
{
    if (f == NULL || text == NULL)
        return;
    int x0 = get_int(f, "x", 0), y = get_int(f, "y", 0), line_height = get_int(f, "line_height", 16);
    SDL_Color normal = get_color(f, "color", text_color), shadow = get_color(f, "shadow", text_shadow);
    SDL_Color hl = get_color(f, "highlight.color", (SDL_Color){231, 8, 8, 255}), hl_shadow = get_color(f, "highlight.shadow", shadow);
    bool highlight = false;
    int x = x0;
    char part[256];
    int n = 0;
    for (const char *p = text;; p++)
    {
        char c = *p;
        if (c == '{' || c == '}' || c == '\n' || c == '\0')
        {
            part[n] = '\0';
            if (n)
                x += draw_text_c(dst, l, part, x, y, 0, highlight ? hl : normal, highlight ? hl_shadow : shadow);
            n = 0;
            if (c == '{')
                highlight = true;
            else if (c == '}')
                highlight = false;
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

// A window: the frame image's 3x3 tiles drawn one tile outside the inner rect, which is filled from the centre.
static void draw_window(SDL_Surface *dst, Scene *scene, const Layout *l, const char *frame_name, int x, int y, int w, int h)
{
    SDL_Surface *frame = skin_image(scene, frame_name ? frame_name : "window_frame.png");
    if (frame == NULL)
    {
        fill_logical(dst, l, x - 2, y - 2, w + 4, h + 4, (SDL_Color){96, 96, 96, 255});
        fill_logical(dst, l, x, y, w, h, (SDL_Color){255, 255, 255, 255});
        return;
    }
    int t = frame->w / 3;
    for (int ty = y - t; ty < y + h + t; ty += t)
    {
        for (int tx = x - t; tx < x + w + t; tx += t)
        {
            int col = tx < x ? 0 : tx >= x + w ? 2 : 1;
            int row = ty < y ? 0 : ty >= y + h ? 2 : 1;
            SDL_Rect src = {col * t, row * t, t, t};
            blit_scaled(frame, &src, dst, l, tx, ty);
        }
    }
}

// ------------------------------------------------------------------ PC screen

static int wrap_two_lines(const char *text, int max_w, int (*width)(const char *, const void *), const void *ctx, char lines[2][160]);

static int measure_text(const char *text, const void *ctx)
{
    return text_width(text, (const Layout *)ctx);
}

// The message box at the bottom; a second line makes it one line taller, upwards.
static void draw_message(SDL_Surface *screen, Scene *scene, const Layout *l, const char *text)
{
    JSON_Object *lo = scene->layout;
    SDL_Rect m = get_rect(lo, "pc.message"), mt = get_rect(lo, "pc.message_text");
    SDL_Color color = get_color(lo, "pc.menu.color", text_color), shadow = get_color(lo, "pc.menu.shadow", text_shadow);
    char lines[2][160];
    int n = wrap_two_lines(text, m.w - 2 * mt.x - 2, measure_text, l, lines);
    if (n == 2)
    {
        m.y -= 16;
        m.h += 16;
    }
    draw_window(screen, scene, l, json_object_dotget_string(lo, "pc.message_frame"), m.x, m.y, m.w, m.h);
    for (int i = 0; i < n; i++)
        draw_text_c(screen, l, lines[i], m.x + mt.x, m.y + mt.y + i * 16, 0, color, shadow);
}

typedef enum
{
    TARGET_SLOT,
    TARGET_TITLE,
    TARGET_PARTY_BUTTON,
    TARGET_CLOSE_BUTTON,
} Target;

typedef enum
{
    VIEW_BOX,
    VIEW_PARTY,
    VIEW_MENU,
    VIEW_SUMMARY,
} Mode;

typedef struct
{
    Mode mode;
    Mode menu_from; // VIEW_BOX or VIEW_PARTY: where the menu and summary return to
    Target target;
    int party_slot; // 0-5, 6 = CANCEL
    int menu_item;
    int page;
    const char *message; // an explanation in the message box, until a button is pressed
} State;

static const char *MENU_ITEMS[] = {"TRANSFER", "SUMMARY", "EVOLVE", "CANCEL"};
#define MENU_COUNT 4

// Transfer and Evolve are greyed out when the Pokémon can't be transferred or evolved; picking them explains why.
static bool menu_enabled(Slot *slot, int item)
{
    return item == 0 ? slot->can_transfer : item == 2 ? slot->can_evolve : true;
}

static const char *menu_why(Slot *slot, int item)
{
    if (item == 0)
        return slot->transfer_why ? slot->transfer_why : "It can't be transferred!";
    return slot->evolve_why ? slot->evolve_why : "It can't evolve by trading!";
}

// Splits text into at most two lines of at most max_w pixels (measured by width()), at spaces.
static int wrap_two_lines(const char *text, int max_w, int (*width)(const char *, const void *), const void *ctx, char lines[2][160])
{
    lines[0][0] = lines[1][0] = '\0';
    snprintf(lines[0], 160, "%s", text);
    if (width(lines[0], ctx) <= max_w)
        return 1;
    // the last space that keeps the first line inside the box
    int split = -1;
    for (int i = 0; text[i]; i++)
    {
        if (text[i] != ' ')
            continue;
        char head[160];
        snprintf(head, sizeof(head), "%.*s", i, text);
        if (width(head, ctx) <= max_w)
            split = i;
        else
            break;
    }
    if (split < 0)
        return 1;
    snprintf(lines[0], 160, "%.*s", split, text);
    snprintf(lines[1], 160, "%s", text + split + 1);
    return 2;
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

// The front picture centred on (cx, cy); the summary screens mirror it unless the species is one the games
// never flip (IsMonSpriteNotFlipped).
static void draw_sprite(SDL_Surface *screen, const Layout *l, Slot *slot, int cx, int cy, bool flip)
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
    if (!flip)
    {
        blit_scaled(sprite, &src, screen, l, cx - src.w / 2, cy - src.h / 2);
        return;
    }
    SDL_Surface *part = SDL_CreateRGBSurfaceWithFormat(0, src.w, src.h, 32, SDL_PIXELFORMAT_ARGB8888);
    SDL_Surface *mirror = SDL_CreateRGBSurfaceWithFormat(0, src.w, src.h, 32, SDL_PIXELFORMAT_ARGB8888);
    if (part && mirror)
    {
        SDL_SetSurfaceBlendMode(sprite, SDL_BLENDMODE_NONE);
        SDL_BlitSurface(sprite, &src, part, NULL);
        SDL_SetSurfaceBlendMode(sprite, SDL_BLENDMODE_BLEND);
        for (int y = 0; y < src.h; y++)
        {
            Uint32 *from = (Uint32 *)((Uint8 *)part->pixels + y * part->pitch);
            Uint32 *to = (Uint32 *)((Uint8 *)mirror->pixels + y * mirror->pitch);
            for (int x = 0; x < src.w; x++)
                to[x] = from[src.w - 1 - x];
        }
        SDL_SetSurfaceBlendMode(mirror, SDL_BLENDMODE_BLEND);
        blit_scaled(mirror, NULL, screen, l, cx - src.w / 2, cy - src.h / 2);
    }
    SDL_FreeSurface(part);
    SDL_FreeSurface(mirror);
}

static bool party_open(State *st)
{
    return st->mode == VIEW_PARTY || (st->mode != VIEW_BOX && st->menu_from == VIEW_PARTY);
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
    draw_text_c(screen, l, box->name, name.x, name.y, 1,
                get_color(lo, "pc.box_name_color", (SDL_Color){255, 255, 255, 255}), get_color(lo, "pc.box_name_shadow", (SDL_Color){57, 57, 57, 255}));
    int bob = (int)((ticks / 250) % 2);
    SDL_Rect al = get_rect(lo, "pc.arrows.left"), ar = get_rect(lo, "pc.arrows.right");
    blit_scaled(skin_image(scene, "arrow_left.png"), NULL, screen, l, al.x - bob, al.y);
    blit_scaled(skin_image(scene, "arrow_right.png"), NULL, screen, l, ar.x + bob, ar.y);

    // The hand's position: over the slot, the box title or a button.
    int gx = get_int(lo, "pc.grid.x", 0), gy = get_int(lo, "pc.grid.y", 0);
    int dx = get_int(lo, "pc.grid.dx", 24), dy = get_int(lo, "pc.grid.dy", 24);
    SDL_Rect ho = get_rect(lo, "pc.hand_offset");
    int hx = 0, hy = 0;
    bool in_box = false;
    if (party_open(st))
    {
        if (st->party_slot >= 6 || !party_slot_xy(lo, st->party_slot, &hx, &hy))
        {
            SDL_Rect c = get_rect(lo, "pc.hand_cancel");
            hx = c.x;
            hy = c.y;
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
        in_box = true;
    }
    else
    {
        SDL_Rect at = get_rect(lo, st->target == TARGET_TITLE ? "pc.hand_title" : st->target == TARGET_PARTY_BUTTON ? "pc.hand_party_button" : "pc.hand_close_button");
        hx = at.x;
        hy = at.y;
    }
    int hand_bob = st->mode == VIEW_MENU ? 0 : (int)((ticks / 300) % 2) * 2;

    // In the box the hand's shadow falls on the wallpaper, under the icons.
    if (in_box)
    {
        SDL_Rect so = get_rect(lo, "pc.hand_shadow_offset");
        blit_scaled(skin_image(scene, "hand_shadow.png"), NULL, screen, l, hx + so.x, hy + so.y);
    }

    for (int i = 0; i < box->columns * box->rows; i++)
    {
        if (box->slots[i].filled)
            draw_icon(screen, l, &box->slots[i], gx + (i % box->columns) * dx, gy + (i / box->columns) * dy,
                      st->mode == VIEW_BOX && st->target == TARGET_SLOT && i == scene->slot, ticks);
    }

    // The party, when open, over the wallpaper.
    if (party_open(st))
    {
        blit_scaled(skin_image(scene, "pc_party.png"), NULL, screen, l, get_int(lo, "pc.party.x", 0), get_int(lo, "pc.party.y", 0));
        for (int i = 0; i < 6; i++)
        {
            int x, y;
            if (scene->boxes[0].slots[i].filled && party_slot_xy(lo, i, &x, &y))
                draw_icon(screen, l, &scene->boxes[0].slots[i], x, y, st->mode == VIEW_PARTY && i == st->party_slot, ticks);
        }
    }

    // PKMN DATA: the Pokémon under the hand.
    Slot *cur = hovered(scene, st);
    if (cur && cur->filled)
    {
        SDL_Rect bg = get_rect(lo, "pc.mon_text_bg.rect");
        if (bg.w)
            fill_logical(screen, l, bg.x, bg.y, bg.w, bg.h, get_color(lo, "pc.mon_text_bg.color", (SDL_Color){148, 148, 172, 255}));
        SDL_Rect sp = get_rect(lo, "pc.mon_sprite");
        draw_sprite(screen, l, cur, sp.x, sp.y, false);
        JSON_Object *s = cur->summary;
        draw_field(screen, l, lo, "pc.mon_text", "nickname", json_object_get_string(s, "nickname"));
        draw_field(screen, l, lo, "pc.mon_text", "species", json_object_get_string(s, "species"));
        draw_field(screen, l, lo, "pc.mon_text", "level", json_object_get_string(s, "level"));
        draw_gender(screen, l, lo, "pc.mon_text", json_object_get_string(s, "gender"));
        const char *item = json_object_get_string(s, "item");
        if (item && strcmp(item, "NONE") != 0)
            draw_field(screen, l, lo, "pc.mon_text", "item", item);
    }

    SDL_Surface *hand = skin_image(scene, "hand.png");
    if (hand)
    {
        SDL_Rect src = {0, 0, hand->w >= 64 ? 32 : hand->w, hand->h >= 32 ? 32 : hand->h};
        blit_scaled(hand, &src, screen, l, hx, hy + hand_bob);
    }

    // The action menu, in a window whose bottom-right corner is fixed, and its message.
    if (st->mode == VIEW_MENU && cur && cur->filled)
    {
        int line = get_int(lo, "pc.menu.line_height", 16), text_x = get_int(lo, "pc.menu.text_x", 8), text_y = get_int(lo, "pc.menu.text_y", 1);
        SDL_Color color = get_color(lo, "pc.menu.color", text_color), shadow = get_color(lo, "pc.menu.shadow", text_shadow);
        int widest = 0;
        for (int i = 0; i < MENU_COUNT; i++)
        {
            int tw = text_width(MENU_ITEMS[i], l);
            if (tw > widest)
                widest = tw;
        }
        int w = ((widest + 7) / 8 + 2) * 8, h = MENU_COUNT * line;
        int x = get_int(lo, "pc.menu.right", 232) - w, y = get_int(lo, "pc.menu.bottom", 120) - h;
        draw_window(screen, scene, l, NULL, x, y, w, h);
        for (int i = 0; i < MENU_COUNT; i++)
        {
            int ty = y + text_y + i * line;
            if (menu_enabled(cur, i))
                draw_text_c(screen, l, MENU_ITEMS[i], x + text_x, ty, 0, color, shadow);
            else
                draw_text_c(screen, l, MENU_ITEMS[i], x + text_x, ty, 0, (SDL_Color){176, 176, 176, 255}, (SDL_Color){224, 224, 224, 255});
            if (i != st->menu_item)
                continue;
            SDL_Surface *arrow = skin_image(scene, "cursor_menu.png");
            if (arrow)
                blit_scaled(arrow, NULL, screen, l, x, ty);
            else
            {
                for (int r = 0; r < 7; r++)
                    fill_logical(screen, l, x + 1, ty + cap_top + (font_height - 7) / 2 + r, r < 4 ? r + 1 : 7 - r, 1, color);
            }
        }
        char msg[160];
        const char *nick = json_object_get_string(cur->summary, "nickname");
        snprintf(msg, sizeof(msg), "%s is selected.", nick ? nick : cur->name);
        draw_message(screen, scene, l, msg);
    }
    else if (st->message)
        draw_message(screen, scene, l, st->message);
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

// The game's PP colours (GetCurrentPPToMaxPPState): full, half, a quarter or none left.
static const char *pp_state(int pp, int max)
{
    if (pp == max)
        return "high";
    if (max <= 2)
        return pp > 1 ? "high" : pp == 1 ? "quarter" : "zero";
    if (max <= 7)
        return pp > 2 ? "high" : pp == 2 ? "half" : pp == 1 ? "quarter" : "zero";
    if (pp == 0)
        return "zero";
    if (pp <= max / 4)
        return "quarter";
    return pp > max / 2 ? "high" : "half";
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

    // The Pokémon's picture, name, species, level, gender and ball (on every page where the layout has them).
    SDL_Rect sp = get_rect(lo, "summary.sprite");
    draw_sprite(screen, l, cur, sp.x, sp.y, json_object_get_boolean(s, "flip") == 1);
    static const char *TEXT[] = {"dex_no", "nickname", "species", "level", "ot", "id", "ability", "item", "ribbon",
                                 "hp", "attack", "defense", "sp_atk", "sp_def", "speed", "exp_points", "next_lv"};
    for (size_t i = 0; i < sizeof(TEXT) / sizeof(TEXT[0]); i++)
    {
        if (strcmp(TEXT[i], "ot") == 0)
        {
            JSON_Object *ot = find_field(lo, list, "ot");
            if (ot)
            {
                bool female = json_object_get_boolean(s, "ot_female") == 1;
                draw_text_c(screen, l, json_object_get_string(s, "ot"), get_int(ot, "x", 0), get_int(ot, "y", 0), field_align(ot),
                            get_color(ot, female ? "female.color" : "male.color", get_color(ot, "color", text_color)),
                            get_color(ot, female ? "female.shadow" : "male.shadow", get_color(ot, "shadow", text_shadow)));
            }
            continue;
        }
        draw_field(screen, l, lo, list, TEXT[i], json_object_get_string(s, TEXT[i]));
    }
    draw_gender(screen, l, lo, list, json_object_get_string(s, "gender"));
    JSON_Object *ball = find_field(lo, list, "ball");
    if (ball && json_object_get_string(s, "ball"))
    {
        char name[96];
        snprintf(name, sizeof(name), "balls/%s.png", json_object_get_string(s, "ball"));
        blit_scaled(skin_image(scene, name), NULL, screen, l, get_int(ball, "x", 0), get_int(ball, "y", 0));
    }
    JSON_Array *types = json_object_get_array(s, "types");
    JSON_Object *t1 = find_field(lo, list, "type1"), *t2 = find_field(lo, list, "type2");
    if (types && t1)
        draw_type(screen, scene, l, json_array_get_string(types, 0), get_int(t1, "x", 0), get_int(t1, "y", 0));
    if (types && t2 && json_array_get_count(types) > 1)
        draw_type(screen, scene, l, json_array_get_string(types, 1), get_int(t2, "x", 0), get_int(t2, "y", 0));
    draw_lines(screen, l, find_field(lo, list, "ability_desc"), json_object_get_string(s, "ability_desc"));
    draw_lines(screen, l, find_field(lo, list, "memo"), json_object_get_string(s, "memo"));

    // HP and EXP bars.
    JSON_Object *exp = find_field(lo, list, "exp_bar");
    if (exp)
        fill_logical(screen, l, get_int(exp, "x", 0), get_int(exp, "y", 0), (int)(get_int(exp, "w", 64) * json_object_get_number(s, "exp_fill")),
                     get_int(exp, "h", 3), get_color(exp, "color", (SDL_Color){64, 200, 248, 255}));
    JSON_Object *hpbar = find_field(lo, list, "hp_bar");
    int hp_max = (int)json_object_get_number(s, "hp_max"), hp_cur = (int)json_object_get_number(s, "hp_cur");
    if (hpbar && hp_max > 0)
    {
        const char *band = hp_cur * 2 > hp_max ? "green" : hp_cur * 5 > hp_max ? "yellow" : "red";
        int w = get_int(hpbar, "w", 48);
        fill_logical(screen, l, get_int(hpbar, "x", 0), get_int(hpbar, "y", 0), (w * hp_cur + hp_max - 1) / hp_max, get_int(hpbar, "h", 3),
                     get_color(hpbar, band, (SDL_Color){90, 214, 132, 255}));
    }

    // Moves: type, name and PP (coloured by how much is left).
    JSON_Object *m = find_field(lo, list, "moves");
    JSON_Array *moves = json_object_get_array(s, "moves");
    int row_dy = get_int(m, "row_dy", 16);
    for (int i = 0; m && moves && i < 4 && i < (int)json_array_get_count(moves); i++)
    {
        JSON_Object *mv = json_array_get_object(moves, i);
        int y = i * row_dy;
        draw_type(screen, scene, l, json_object_get_string(mv, "type"), get_int(m, "type.x", 0), get_int(m, "type.y", 0) + y);
        draw_text_c(screen, l, json_object_get_string(mv, "name"), get_int(m, "name.x", 0), get_int(m, "name.y", 0) + y, 0,
                    get_color(m, "name.color", text_color), get_color(m, "name.shadow", text_shadow));
        if (!json_object_has_value(mv, "max_pp"))
        {
            draw_text_c(screen, l, json_object_dotget_string(m, "empty.text"), get_int(m, "empty.x", 0), get_int(m, "pp.y", 0) + y, 0,
                        get_color(m, "states.high.color", text_color), get_color(m, "states.high.shadow", text_shadow));
            continue;
        }
        int pp = (int)json_object_get_number(mv, "pp"), max = (int)json_object_get_number(mv, "max_pp");
        char key[64], key2[64];
        snprintf(key, sizeof(key), "states.%s.color", pp_state(pp, max));
        snprintf(key2, sizeof(key2), "states.%s.shadow", pp_state(pp, max));
        SDL_Color c = get_color(m, key, get_color(m, "states.high.color", text_color)), sh = get_color(m, key2, get_color(m, "states.high.shadow", text_shadow));
        if (json_object_dothas_value(m, "pp_label.image"))
            blit_scaled(skin_image(scene, json_object_dotget_string(m, "pp_label.image")), NULL, screen, l, get_int(m, "pp_label.x", 0),
                        get_int(m, "pp_label.y", 0) + y);
        else if (json_object_has_value(m, "pp_label"))
            draw_text_c(screen, l, json_object_dotget_string(m, "pp_label.text"), get_int(m, "pp_label.x", 0), get_int(m, "pp_label.y", 0) + y, 0, c, sh);
        const char *prefix = json_object_dotget_string(m, "pp.prefix");
        char text[48];
        snprintf(text, sizeof(text), "%s%2d/%2d", prefix ? prefix : "", pp, max);
        draw_text_c(screen, l, text, get_int(m, "pp.x", 0), get_int(m, "pp.y", 0) + y, 2, c, sh);
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
    bool party = st && st->menu_from == VIEW_PARTY; // no state: the Game Boy PC, where scene->box/slot are current
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
    bool party = st->menu_from == VIEW_PARTY;
    fprintf(f, "{\"box\": %d, \"slot\": %d, \"action\": \"%s\"}\n", party ? 0 : scene->box, party ? st->party_slot : scene->slot, action);
    fclose(f);
}

static void draw(SDL_Surface *screen, Scene *scene, State *st, const Layout *l, Uint32 ticks)
{
    SDL_FillRect(screen, NULL, SDL_MapRGB(screen->format, 0, 0, 0));
    if (st->mode == VIEW_SUMMARY)
        draw_summary(screen, scene, st, l);
    else
        draw_pc(screen, scene, st, l, ticks);
}

#include "pkmgr-box-gb.c"

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

    // Gen 1/2 saves: the Game Boy PC (pkmgr-box-gb.c).
    const char *style = json_object_get_string(scene.layout, "style");
    if (style && strcmp(style, "gb") == 0)
    {
        int gb_exit = EXIT_BACK;
#ifdef DESKTOP
        if (screenshot)
        {
            gb_screenshot(screen, &scene, state_name, screenshot);
            return EXIT_PICKED;
        }
#endif
        gb_exit = gb_run(screen, &scene, write_location);
        QuitSettings();
        PWR_quit();
        PAD_quit();
        GFX_quit();
        return gb_exit;
    }

    Layout layout = make_layout(screen);
    open_text_font(&scene, &layout);

    State st = {VIEW_BOX, VIEW_BOX, TARGET_SLOT, 0, 0, 0, NULL};
    if (scene.box == 0)
    {
        // Back from a party Pokémon: the party is open again.
        st.mode = st.menu_from = VIEW_PARTY;
        st.party_slot = scene.slot < 6 ? scene.slot : 0;
        scene.box = 1;
        scene.slot = 0;
    }

#ifdef DESKTOP
    if (screenshot)
    {
        if (strcmp(state_name, "party") == 0)
            st.mode = st.menu_from = VIEW_PARTY;
        else if (strcmp(state_name, "menu") == 0)
            st.mode = VIEW_MENU;
        else if (strncmp(state_name, "summary", 7) == 0)
        {
            st.mode = VIEW_SUMMARY;
            st.page = atoi(state_name + 7);
        }
        else if (strcmp(state_name, "party_button") == 0)
            st.target = TARGET_PARTY_BUTTON;
        else if (strcmp(state_name, "why") == 0 && hovered(&scene, &st))
            st.message = menu_why(hovered(&scene, &st), hovered(&scene, &st)->can_transfer ? 2 : 0);
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
        else if (st.message && (dx || dy || a || b || l1 || r1))
            st.message = NULL; // a button closes the explanation
        else if (st.mode == VIEW_BOX)
        {
            if (l1 || (st.target == TARGET_TITLE && dx < 0))
                change_box(&scene, -1);
            else if (r1 || (st.target == TARGET_TITLE && dx > 0))
                change_box(&scene, 1);
            else if (dx || dy)
                box_move(&scene, &st, dx, dy);
            else if (a && st.target == TARGET_SLOT && scene.boxes[scene.box].slots[scene.slot].filled)
            {
                st.mode = VIEW_MENU;
                st.menu_from = VIEW_BOX;
                st.menu_item = 0;
            }
            else if (a && st.target == TARGET_PARTY_BUTTON)
            {
                st.mode = st.menu_from = VIEW_PARTY;
                st.party_slot = 0;
            }
            else if ((a && st.target == TARGET_CLOSE_BUTTON) || b)
            {
                exit_code = EXIT_BACK;
                quitting = true;
            }
        }
        else if (st.mode == VIEW_PARTY)
        {
            if (dx || dy)
                party_move(&st, dx, dy);
            else if (a && st.party_slot < 6 && scene.boxes[0].slots[st.party_slot].filled)
            {
                st.mode = VIEW_MENU;
                st.menu_from = VIEW_PARTY;
                st.menu_item = 0;
            }
            else if ((a && st.party_slot == 6) || b)
            {
                st.mode = st.menu_from = VIEW_BOX;
                st.target = TARGET_PARTY_BUTTON;
            }
        }
        else if (st.mode == VIEW_MENU)
        {
            if (dy)
                st.menu_item = (st.menu_item + dy + MENU_COUNT) % MENU_COUNT;
            else if (a && (st.menu_item == 0 || st.menu_item == 2) && menu_enabled(hovered(&scene, &st), st.menu_item))
            {
                action = st.menu_item == 0 ? "transfer" : "evolve";
                exit_code = EXIT_PICKED;
                quitting = true;
            }
            else if (a && (st.menu_item == 0 || st.menu_item == 2))
            {
                // greyed out: the menu closes and the message box says why
                st.message = menu_why(hovered(&scene, &st), st.menu_item);
                st.mode = st.menu_from;
            }
            else if (a && st.menu_item == 1)
            {
                st.mode = VIEW_SUMMARY;
                st.page = 0;
            }
            else if ((a && st.menu_item == 3) || b)
                st.mode = st.menu_from;
        }
        else if (st.mode == VIEW_SUMMARY)
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
