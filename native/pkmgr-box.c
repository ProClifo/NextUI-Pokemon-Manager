// pkmgr-box: a Gen 3 style PC box viewer for Pokémon Manager.
//
// Reads a scene description (JSON) written by pkmgr, shows the boxes with their wallpapers and
// Pokémon icons, and lets the player move a hand cursor around like the in-game PC:
//
//   D-pad   move the cursor (wraps around the box)
//   L1/R1   previous/next box
//   A       pick the Pokémon under the cursor
//   B       back
//
// Icons and sprites can be pictures within a sheet ("icon_rect" / "sprite_rect": [x, y, w, h]), and
// "font" / "font_em" name the game's font, drawn at one font pixel per GBA pixel (text it can't show
// falls back to the NextUI font).
//
// The result is written to --write-location as {"box": n, "slot": n}.
// Exit codes: 0 picked, 2 back (B), 3 back (MENU), 1 error.
//
// Built for NextUI with the device toolchains (make PLATFORM=...), or for a desktop with
// `make desktop`, which adds --screenshot to render a single frame to a PNG for testing.

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

// The game's screen and box layout, in GBA pixels (the viewer scales it up by a whole number).
#define GBA_W 240
#define GBA_H 160
#define WALLPAPER_X 80
#define WALLPAPER_Y 16
#define WALLPAPER_W 160
#define WALLPAPER_H 144
#define SLOT_SIZE 24
#define ICON_X 4  // icon top-left within the wallpaper, slot (0,0)
#define ICON_Y 12
#define CURSOR_Y 0 // hand cursor top-left within the wallpaper, slot (0,0)
#define MAX_LINES 6
#define MAX_SLOTS 36

typedef struct
{
    bool filled;
    char *icon;
    SDL_Rect icon_rect; // w == 0: the whole image (frames stacked 32x32)
    char *sprite;
    SDL_Rect sprite_rect;
    char *name;
    char *lines[MAX_LINES];
    int line_count;
} Slot;

typedef struct
{
    char *name;
    char *wallpaper;
    int columns;
    int rows;
    int offset_x; // extra grid offset in GBA pixels, e.g. to centre the party
    int offset_y;
    Slot slots[MAX_SLOTS];
} Box;

typedef struct
{
    char *title;
    char *background;
    char *cursor;
    char *font;
    int font_em;
    Box *boxes;
    int box_count;
    int box;
    int slot;
} Scene;

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

static SDL_Rect get_rect(JSON_Object *obj, const char *key)
{
    SDL_Rect r = {0, 0, 0, 0};
    JSON_Array *a = json_object_get_array(obj, key);
    if (a && json_array_get_count(a) == 4)
    {
        r.x = (int)json_array_get_number(a, 0);
        r.y = (int)json_array_get_number(a, 1);
        r.w = (int)json_array_get_number(a, 2);
        r.h = (int)json_array_get_number(a, 3);
    }
    return r;
}

static char *dup_string(JSON_Object *obj, const char *key)
{
    const char *value = json_object_get_string(obj, key);
    return value ? strdup(value) : NULL;
}

static bool load_scene(const char *path, Scene *scene)
{
    JSON_Value *root = json_parse_file(path);
    if (root == NULL)
        return false;
    JSON_Object *obj = json_value_get_object(root);
    if (obj == NULL)
        return false;

    memset(scene, 0, sizeof(*scene));
    scene->title = dup_string(obj, "title");
    scene->background = dup_string(obj, "background");
    scene->cursor = dup_string(obj, "cursor");
    scene->font = dup_string(obj, "font");
    scene->font_em = (int)json_object_get_number(obj, "font_em");
    scene->box = (int)json_object_get_number(obj, "box");
    scene->slot = (int)json_object_get_number(obj, "slot");

    JSON_Array *boxes = json_object_get_array(obj, "boxes");
    scene->box_count = boxes ? (int)json_array_get_count(boxes) : 0;
    if (scene->box_count == 0)
        return false;
    scene->boxes = calloc(scene->box_count, sizeof(Box));

    for (int b = 0; b < scene->box_count; b++)
    {
        JSON_Object *bo = json_array_get_object(boxes, b);
        Box *box = &scene->boxes[b];
        box->name = dup_string(bo, "name");
        box->wallpaper = dup_string(bo, "wallpaper");
        box->columns = (int)json_object_get_number(bo, "columns");
        box->rows = (int)json_object_get_number(bo, "rows");
        box->offset_x = (int)json_object_get_number(bo, "offset_x");
        box->offset_y = (int)json_object_get_number(bo, "offset_y");
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
            JSON_Array *lines = json_object_get_array(so, "lines");
            int n = lines ? (int)json_array_get_count(lines) : 0;
            for (int l = 0; l < n && l < MAX_LINES; l++)
            {
                const char *line = json_array_get_string(lines, l);
                if (line)
                    slot->lines[slot->line_count++] = strdup(line);
            }
        }
    }

    if (scene->box < 0 || scene->box >= scene->box_count)
        scene->box = 0;
    Box *first = &scene->boxes[scene->box];
    if (scene->slot < 0 || scene->slot >= first->columns * first->rows)
        scene->slot = 0;
    json_value_free(root);
    return true;
}

// ------------------------------------------------------------------ drawing

typedef struct
{
    int scale;
    int origin_x;
    int origin_y;
    int screen_w;
    int screen_h;
} Layout;

static Layout make_layout(SDL_Surface *screen, int hint_band)
{
    Layout l;
    l.screen_w = screen->w;
    l.screen_h = screen->h;
    int avail_h = screen->h - hint_band;
    int sx = screen->w / GBA_W;
    int sy = avail_h / GBA_H;
    l.scale = sx < sy ? sx : sy;
    if (l.scale < 1)
        l.scale = 1;
    l.origin_x = (screen->w - GBA_W * l.scale) / 2;
    l.origin_y = (avail_h - GBA_H * l.scale) / 2;
    if (l.origin_y < 0)
        l.origin_y = 0;
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

static void fill_logical(SDL_Surface *dst, const Layout *l, int x, int y, int w, int h, Uint32 color)
{
    SDL_Rect r = {l->origin_x + x * l->scale, l->origin_y + y * l->scale, w * l->scale, h * l->scale};
    SDL_FillRect(dst, &r, color);
}

// The game's font, if the scene names one, and its shadow offset (one font pixel).
static TTF_Font *game_font = NULL;
static int game_font_pixel = 1;

// The game font if it has every character of the text, else the NextUI font.
static TTF_Font *font_for(const char *text, TTF_Font *fallback)
{
    if (game_font == NULL || text == NULL)
        return fallback;
    const unsigned char *p = (const unsigned char *)text;
    while (*p)
    {
        Uint32 c;
        int n;
        if (*p < 0x80) { c = *p; n = 1; }
        else if ((*p & 0xE0) == 0xC0) { c = *p & 0x1F; n = 2; }
        else if ((*p & 0xF0) == 0xE0) { c = *p & 0x0F; n = 3; }
        else return fallback; // outside the Basic Multilingual Plane: no game font has it
        for (int i = 1; i < n; i++)
        {
            if ((p[i] & 0xC0) != 0x80)
                return fallback;
            c = (c << 6) | (p[i] & 0x3F);
        }
        if (c >= 0x20 && !TTF_GlyphIsProvided(game_font, (Uint16)c))
            return fallback;
        p += n;
    }
    return game_font;
}

// Draws text with a dark shadow, like the game's box title. Returns the rendered width.
static int draw_text(SDL_Surface *dst, TTF_Font *f, const char *text, int x, int y, int max_w, SDL_Color color, bool centered)
{
    if (f == NULL || text == NULL || text[0] == '\0')
        return 0;
    char truncated[300];
    snprintf(truncated, sizeof(truncated), "%s", text);
    int tw = 0, th = 0;
    TTF_SizeUTF8(f, truncated, &tw, &th);
    while (max_w > 0 && tw > max_w && strlen(truncated) > 1)
    {
        size_t len = strlen(truncated);
        // drop a whole UTF-8 character, then add an ellipsis
        do
            len--;
        while (len > 0 && (truncated[len] & 0xC0) == 0x80);
        truncated[len] = '\0';
        char with_dots[sizeof(truncated) + 4];
        snprintf(with_dots, sizeof(with_dots), "%s...", truncated);
        TTF_SizeUTF8(f, with_dots, &tw, &th);
        if (tw <= max_w)
        {
            memcpy(truncated, with_dots, strlen(with_dots) + 1 <= sizeof(truncated) ? strlen(with_dots) + 1 : sizeof(truncated));
            truncated[sizeof(truncated) - 1] = '\0';
            break;
        }
    }
    if (centered)
        x -= tw / 2;

    SDL_Color shadow = {56, 56, 56, 255};
    SDL_Surface *s = TTF_RenderUTF8_Blended(f, truncated, shadow);
    SDL_Surface *t = TTF_RenderUTF8_Blended(f, truncated, color);
    if (s)
    {
        int off = f == game_font ? game_font_pixel : (th / 12 > 0 ? th / 12 : 1);
        SDL_Rect r = {x + off, y + off, 0, 0};
        SDL_BlitSurface(s, NULL, dst, &r);
        SDL_FreeSurface(s);
    }
    if (t)
    {
        SDL_Rect r = {x, y, 0, 0};
        SDL_BlitSurface(t, NULL, dst, &r);
        SDL_FreeSurface(t);
    }
    return tw;
}

// A small pixel-art arrow, like the ones beside the box title.
static void draw_arrow(SDL_Surface *dst, const Layout *l, int x, int y, bool right, Uint32 color)
{
    for (int row = 0; row < 7; row++)
    {
        int width = row < 4 ? row + 1 : 7 - row;
        int start = right ? x : x + 4 - width;
        fill_logical(dst, l, start, y + row, width, 1, color);
    }
}

static void draw_frame(SDL_Surface *screen, Scene *scene, const Layout *l, Uint32 ticks, int scroll)
{
    SDL_PixelFormat *fmt = screen->format;
    Box *box = &scene->boxes[scene->box];

    // Scrolling Poké Ball background, tiled over the whole screen like the game's PC.
    SDL_Surface *bg = load_image(scene->background);
    if (bg)
    {
        int tw = bg->w * l->scale, th = bg->h * l->scale;
        int ox = -(scroll % tw), oy = -(scroll % th);
        for (int y = oy; y < l->screen_h; y += th)
        {
            for (int x = ox; x < l->screen_w; x += tw)
            {
                SDL_Rect d = {x, y, tw, th};
                SDL_BlitScaled(bg, NULL, screen, &d);
            }
        }
    }
    else
    {
        SDL_FillRect(screen, NULL, SDL_MapRGB(fmt, 255, 226, 206));
    }

    // Wallpaper, title and arrows.
    blit_scaled(load_image(box->wallpaper), NULL, screen, l, WALLPAPER_X, WALLPAPER_Y);
    Uint32 arrow = SDL_MapRGB(fmt, 255, 255, 255);
    int bob = ((ticks / 250) % 2);
    draw_arrow(screen, l, WALLPAPER_X + 10 - bob, WALLPAPER_Y + 6, false, arrow);
    draw_arrow(screen, l, WALLPAPER_X + WALLPAPER_W - 15 + bob, WALLPAPER_Y + 6, true, arrow);
    SDL_Color white = {255, 255, 255, 255};
    TTF_Font *title_font = font_for(box->name, font.medium ? font.medium : font.large);
    int title_h = TTF_FontHeight(title_font);
    draw_text(screen, title_font, box->name,
              l->origin_x + (WALLPAPER_X + WALLPAPER_W / 2) * l->scale,
              l->origin_y + (WALLPAPER_Y + 10) * l->scale - title_h / 2,
              100 * l->scale, white, true);

    // Pokémon icons. The one under the cursor animates, as in the party screen.
    int count = box->columns * box->rows;
    for (int i = 0; i < count; i++)
    {
        Slot *slot = &box->slots[i];
        if (!slot->filled)
            continue;
        SDL_Surface *icon = load_image(slot->icon);
        if (icon == NULL)
            continue;
        SDL_Rect cell = slot->icon_rect.w ? slot->icon_rect : (SDL_Rect){0, 0, 32, icon->h};
        int frame = (i == scene->slot && cell.h >= 64) ? (int)((ticks / 200) % 2) : 0;
        SDL_Rect src = {cell.x, cell.y + frame * 32, 32, 32};
        int col = i % box->columns, row = i / box->columns;
        blit_scaled(icon, &src, screen, l, WALLPAPER_X + box->offset_x + ICON_X + col * SLOT_SIZE,
                    WALLPAPER_Y + box->offset_y + ICON_Y + row * SLOT_SIZE);
    }

    // Hand cursor, bobbing.
    SDL_Surface *hand = load_image(scene->cursor);
    int col = scene->slot % box->columns, row = scene->slot / box->columns;
    int hand_bob = (int)(2.0 * sin(ticks / 160.0));
    if (hand)
        blit_scaled(hand, NULL, screen, l, WALLPAPER_X + box->offset_x + ICON_X + col * SLOT_SIZE,
                    WALLPAPER_Y + box->offset_y + CURSOR_Y + row * SLOT_SIZE + hand_bob);

    // Left panel: front sprite and details of the Pokémon under the cursor.
    Uint32 panel = SDL_MapRGBA(fmt, 248, 248, 248, 255);
    Uint32 border = SDL_MapRGB(fmt, 112, 112, 128);
    fill_logical(screen, l, 3, 3, 74, 154, border);
    fill_logical(screen, l, 4, 4, 72, 152, panel);
    fill_logical(screen, l, 4, 4, 72, 72, SDL_MapRGB(fmt, 208, 216, 232));

    Slot *current = &box->slots[scene->slot];
    if (current->filled)
    {
        // The sprite, centred in the 72x72 picture area (sheets hold sprites trimmed to their pixels).
        SDL_Surface *sprite = load_image(current->sprite);
        if (sprite)
        {
            SDL_Rect src = current->sprite_rect.w ? current->sprite_rect : (SDL_Rect){0, 0, sprite->w, sprite->h};
            if (src.w > 72)
            {
                src.x += (src.w - 72) / 2;
                src.w = 72;
            }
            if (src.h > 72)
            {
                src.y += src.h - 72;
                src.h = 72;
            }
            blit_scaled(sprite, &src, screen, l, 4 + (72 - src.w) / 2, 4 + (72 - src.h) / 2);
        }
        else
        {
            SDL_Surface *icon = load_image(current->icon);
            if (icon)
            {
                SDL_Rect src = {current->icon_rect.x, current->icon_rect.y, 32, 32};
                SDL_Rect d = {l->origin_x + 8 * l->scale, l->origin_y + 8 * l->scale, 64 * l->scale, 64 * l->scale};
                SDL_BlitScaled(icon, &src, screen, &d);
            }
        }

        SDL_Color dark = {40, 40, 48, 255};
        int x = l->origin_x + 7 * l->scale;
        int y = l->origin_y + 79 * l->scale;
        int max_w = 66 * l->scale;
        TTF_Font *nextui_name = font.small ? font.small : font.medium;
        TTF_Font *nextui_line = font.tiny ? font.tiny : nextui_name;
        TTF_Font *name_font = font_for(current->name, nextui_name);
        int name_w = 0;
        if (current->name)
            TTF_SizeUTF8(name_font, current->name, &name_w, NULL);
        int name_x = x;
        if (name_font == game_font && name_w > max_w && name_w <= 72 * l->scale)
            name_x = l->origin_x + 4 * l->scale + (72 * l->scale - name_w) / 2; // Game Boy names fill the panel
        else if (name_w > max_w)
            name_font = nextui_line; // long names (UMBREON, nicknames) use the smaller font before truncating
        draw_text(screen, name_font, current->name, name_x, y, 72 * l->scale, dark, false);
        y += TTF_FontLineSkip(name_font) + l->scale;
        for (int i = 0; i < current->line_count; i++)
        {
            TTF_Font *line_font = font_for(current->lines[i], nextui_line);
            if (y + TTF_FontHeight(line_font) > l->origin_y + 156 * l->scale)
                break;
            draw_text(screen, line_font, current->lines[i], x, y, max_w, dark, false);
            y += TTF_FontLineSkip(line_font) + (line_font == game_font ? game_font_pixel : 0); // room for the shadow
        }
    }

    // Top line: which save this is.
    if (scene->title && l->origin_y + 0 >= 0)
    {
        TTF_Font *tf = font_for(scene->title, font.tiny ? font.tiny : font.small);
        int h = TTF_FontHeight(tf);
        if (h <= WALLPAPER_Y * l->scale)
            draw_text(screen, tf, scene->title, l->origin_x + (WALLPAPER_X + WALLPAPER_W / 2) * l->scale,
                      l->origin_y + (WALLPAPER_Y * l->scale - h) / 2, WALLPAPER_W * l->scale, white, true);
    }
}

static void draw_hints(SDL_Surface *screen)
{
    GFX_blitButtonGroup((char *[]){"L/R", "BOX", NULL}, 0, screen, 0);
    GFX_blitButtonGroup((char *[]){"B", "BACK", "A", "SELECT", NULL}, 1, screen, 1);
}

// ------------------------------------------------------------------ input

static void move_cursor(Scene *scene, int dx, int dy)
{
    Box *box = &scene->boxes[scene->box];
    int col = scene->slot % box->columns, row = scene->slot / box->columns;
    col = (col + dx + box->columns) % box->columns;
    row = (row + dy + box->rows) % box->rows;
    scene->slot = row * box->columns + col;
}

static void change_box(Scene *scene, int delta)
{
    Box *old = &scene->boxes[scene->box];
    int col = scene->slot % old->columns, row = scene->slot / old->columns;
    scene->box = (scene->box + delta + scene->box_count) % scene->box_count;
    Box *box = &scene->boxes[scene->box];
    if (col >= box->columns)
        col = box->columns - 1;
    if (row >= box->rows)
        row = box->rows - 1;
    scene->slot = row * box->columns + col;
}

static void write_result(const char *path, Scene *scene)
{
    if (path == NULL)
        return;
    FILE *f = fopen(path, "w");
    if (f == NULL)
        return;
    fprintf(f, "{\"box\": %d, \"slot\": %d}\n", scene->box, scene->slot);
    fclose(f);
}

static int hint_band_height(void)
{
    return SCALE1(PADDING * 2 + PILL_SIZE);
}

int main(int argc, char *argv[])
{
    const char *scene_path = NULL;
    const char *write_location = NULL;
    const char *screenshot = NULL;
    for (int i = 1; i < argc; i++)
    {
        if (strcmp(argv[i], "--scene") == 0 && i + 1 < argc)
            scene_path = argv[++i];
        else if (strcmp(argv[i], "--write-location") == 0 && i + 1 < argc)
            write_location = argv[++i];
        else if (strcmp(argv[i], "--screenshot") == 0 && i + 1 < argc)
            screenshot = argv[++i];
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

    Layout layout = make_layout(screen, hint_band_height());
    if (scene.font && scene.font_em > 0)
    {
        game_font = TTF_OpenFont(scene.font, scene.font_em * layout.scale);
        game_font_pixel = layout.scale;
        if (game_font == NULL)
            fprintf(stderr, "could not open %s: %s\n", scene.font, TTF_GetError());
    }

#ifdef DESKTOP
    if (screenshot)
    {
        GFX_clear(screen);
        draw_frame(screen, &scene, &layout, 0, 0);
        draw_hints(screen);
        IMG_SavePNG(screen, screenshot);
        return EXIT_PICKED;
    }
#else
    (void)screenshot;
#endif

    int exit_code = EXIT_BACK;
    bool quitting = false;
    int dirty = 1;
    int show_setting = 0;
    Uint32 last_anim = 0;
    while (!quitting)
    {
        GFX_startFrame();
        PWR_update(&dirty, &show_setting, NULL, NULL);
        PAD_poll();

        Box *box = &scene.boxes[scene.box];
        if (PAD_justRepeated(BTN_UP)) { move_cursor(&scene, 0, -1); dirty = 1; }
        else if (PAD_justRepeated(BTN_DOWN)) { move_cursor(&scene, 0, 1); dirty = 1; }
        else if (PAD_justRepeated(BTN_LEFT)) { move_cursor(&scene, -1, 0); dirty = 1; }
        else if (PAD_justRepeated(BTN_RIGHT)) { move_cursor(&scene, 1, 0); dirty = 1; }
        else if (PAD_justRepeated(BTN_L1)) { change_box(&scene, -1); dirty = 1; }
        else if (PAD_justRepeated(BTN_R1)) { change_box(&scene, 1); dirty = 1; }
        else if (PAD_justReleased(BTN_A) && box->slots[scene.slot].filled)
        {
            exit_code = EXIT_PICKED;
            quitting = true;
        }
        else if (PAD_justReleased(BTN_B))
        {
            exit_code = EXIT_BACK;
            quitting = true;
        }
        else if (PAD_justReleased(BTN_MENU))
        {
            exit_code = EXIT_MENU;
            quitting = true;
        }

        // Animate at ~12 fps (icons, cursor, background) without redrawing every frame.
        Uint32 now = SDL_GetTicks();
        if (now - last_anim >= 80)
        {
            last_anim = now;
            dirty = 1;
        }

        if (dirty && !quitting)
        {
            GFX_clear(screen);
            draw_frame(screen, &scene, &layout, now, (int)(now / 40));
            draw_hints(screen);
            GFX_flip(screen);
            dirty = 0;
        }
        else
        {
            GFX_sync();
        }
    }

    if (exit_code == EXIT_PICKED)
        write_result(write_location, &scene);

    QuitSettings();
    PWR_quit();
    PAD_quit();
    GFX_quit();
    return exit_code;
}
