// Game Boy PC screens for pkmgr-box (included by pkmgr-box.c): Gen 1's and Gen 2's BILL'S PC, drawn on the Game
// Boy's 160x144 screen of 8x8 tiles at the positions the games use (pokered engine/pokemon/bills_pc.asm and
// home/list_menu.asm, pokecrystal engine/pokemon/bills_pc.asm), with the same menus as the Gen 3 PC:
// TRANSFER / STATS / EVOLVE / CANCEL. Text is NextUI's font, one character per tile like the games' own.
//
//   Up/Down     move through the list (the last entry is CANCEL)
//   Left/Right  previous/next box; the party is left of box 1
//   A           a Pokémon: the menu; CANCEL: leave
//   B           back (closes the stats, the menu, then the PC)
//
// The skin (scripts/gb_ui/build.py) has the games' tiles: frame.png (┌─┐│└┘), the glyphs for <LV>, <ID>, №,
// <PK><MN>, ♂/♀, ▶/▷, ▼, the box arrows, the held item and mail icons, Gen 2's PC selection outline and the HP
// and EXP bars; layout.json says which game it is ("gen") and its colours.

#define GB_W 160
#define GB_H 144

typedef enum
{
    GB_LIST,
    GB_MENU,
    GB_STATS,
} GbMode;

typedef struct
{
    GbMode mode;
    int cursor; // index in the list; == count is CANCEL
    int scroll;
    int menu_item;
    int page;
    int last_box; // Gen 1's BOX No. while the party is shown
    const char *message; // why Transfer/Evolve can't be used, until a button is pressed
} GbState;

static const char *GB_MENU_ITEMS[] = {"TRANSFER", "STATS", "EVOLVE", "CANCEL"};

static int gb_gen = 2;
static SDL_Color gb_bg = {247, 243, 247, 255};
static SDL_Color gb_fg = {0, 0, 0, 255};
static SDL_Color gb_grey = {165, 165, 165, 255};

static Layout gb_layout(SDL_Surface *screen)
{
    Layout l;
    int sx = screen->w / GB_W, sy = screen->h / GB_H;
    l.scale = sx < sy ? sx : sy;
    if (l.scale < 1)
        l.scale = 1;
    l.origin_x = (screen->w - GB_W * l.scale) / 2;
    l.origin_y = (screen->h - GB_H * l.scale) / 2;
    return l;
}

static int gb_count(Box *box)
{
    int n = 0;
    while (n < box->columns * box->rows && box->slots[n].filled)
        n++;
    return n;
}

static int gb_visible(void)
{
    return gb_gen == 1 ? 4 : 5;
}

// ------------------------------------------------------------------ tiles and text

static void gb_image(SDL_Surface *dst, Scene *scene, const Layout *l, const char *name, int x, int y)
{
    blit_scaled(skin_image(scene, name), NULL, dst, l, x, y);
}

// One of frame.png's six tiles (┌ ─ ┐ │ └ ┘) at a tile position.
static void gb_frame_tile(SDL_Surface *dst, Scene *scene, const Layout *l, int piece, int col, int row)
{
    SDL_Surface *frame = skin_image(scene, "frame.png");
    if (frame == NULL)
        return;
    SDL_Rect src = {piece * 8, 0, 8, 8};
    blit_scaled(frame, &src, dst, l, col * 8, row * 8);
}

// A text box whose border's top-left tile is (col, row), w x h tiles including the border, filled inside.
static void gb_box(SDL_Surface *dst, Scene *scene, const Layout *l, int col, int row, int w, int h)
{
    fill_logical(dst, l, col * 8, row * 8, w * 8, h * 8, gb_bg);
    for (int y = row; y < row + h; y++)
    {
        for (int x = col; x < col + w; x++)
        {
            bool top = y == row, bottom = y == row + h - 1, left = x == col, right = x == col + w - 1;
            int piece = top ? (left ? 0 : right ? 2 : 1) : bottom ? (left ? 4 : right ? 5 : 1) : (left || right) ? 3 : -1;
            if (piece >= 0)
                gb_frame_tile(dst, scene, l, piece, x, y);
        }
    }
}

// The glyph images that stand for the games' special characters.
static const struct
{
    const char *token;
    const char *image;
    int tiles;
} GB_TOKENS[] = {
    {"<LV>", "lv.png", 1},     {"<ID>", "id.png", 1},         {"<No>", "no.png", 1},
    {"<PKMN>", "pkmn.png", 2}, {"♂", "male.png", 1},          {"♀", "female.png", 1},
    {"▶", "cursor.png", 1},    {"▷", "cursor_empty.png", 1},  {"▼", "arrow_down.png", 1},
    {"<ITEM>", "item.png", 1}, {"<MAIL>", "mail.png", 1},    {"<BOXL>", "arrow_left.png", 1},
    {"<BOXR>", "arrow_right.png", 1},
};

// Text one character per tile from (col, row) on, in colour; returns the tiles used.
static int gb_print_c(SDL_Surface *dst, Scene *scene, const Layout *l, const char *text, int col, int row, SDL_Color color)
{
    int x = col;
    for (const char *p = text; p && *p;)
    {
        bool token = false;
        for (size_t i = 0; i < sizeof(GB_TOKENS) / sizeof(GB_TOKENS[0]); i++)
        {
            size_t n = strlen(GB_TOKENS[i].token);
            if (strncmp(p, GB_TOKENS[i].token, n) == 0)
            {
                gb_image(dst, scene, l, GB_TOKENS[i].image, x * 8, row * 8);
                x += GB_TOKENS[i].tiles;
                p += n;
                token = true;
                break;
            }
        }
        if (token)
            continue;
        // one UTF-8 character
        int n = (*p & 0x80) == 0 ? 1 : (*p & 0xE0) == 0xC0 ? 2 : (*p & 0xF0) == 0xE0 ? 3 : 4;
        char ch[5] = {0};
        memcpy(ch, p, n);
        p += n;
        if (ch[0] != ' ' && text_font)
        {
            SDL_Surface *g = TTF_RenderUTF8_Blended(text_font, ch, color);
            if (g)
            {
                // centred in its tile, capitals filling the tile's top seven rows as the games' letters do
                int cell = 8 * l->scale;
                SDL_Rect r = {l->origin_x + x * cell + (cell - g->w) / 2, l->origin_y + (row * 8 + cap_top) * l->scale - text_cap_offset, 0, 0};
                SDL_BlitSurface(g, NULL, dst, &r);
                SDL_FreeSurface(g);
            }
        }
        x++;
    }
    return x - col;
}

static int gb_print(SDL_Surface *dst, Scene *scene, const Layout *l, const char *text, int col, int row)
{
    return gb_print_c(dst, scene, l, text, col, row, gb_fg);
}

// Text that ends at tile column right_col (inclusive), for right-aligned numbers.
static void gb_print_right(SDL_Surface *dst, Scene *scene, const Layout *l, const char *text, int right_col, int row)
{
    int len = 0;
    for (const char *p = text; *p; p++)
        if ((*p & 0xC0) != 0x80)
            len++;
    gb_print(dst, scene, l, text, right_col - len + 1, row);
}

static const char *gb_str(JSON_Object *o, const char *key)
{
    const char *s = o ? json_object_get_string(o, key) : NULL;
    return s ? s : "";
}

// PrintLevel: <LV> and the level left-aligned in two tiles, or a three-digit level without the glyph.
static void gb_level(SDL_Surface *dst, Scene *scene, const Layout *l, const char *level, int col, int row)
{
    char text[16];
    if (strlen(level) >= 3)
        snprintf(text, sizeof(text), "%s", level);
    else
        snprintf(text, sizeof(text), "<LV>%s", level);
    gb_print(dst, scene, l, text, col, row);
}

// The front picture in its 7x7-tile square: centred and standing on the square's bottom, as the games pad
// smaller pictures.
static void gb_pic(SDL_Surface *dst, const Layout *l, Slot *slot, int x, int y, bool flip)
{
    SDL_Surface *sprite = load_image(slot->sprite);
    if (sprite == NULL)
        return;
    SDL_Rect src = slot->sprite_rect.w ? slot->sprite_rect : (SDL_Rect){0, 0, sprite->w, sprite->h};
    if (src.w > 56)
    {
        src.x += (src.w - 56) / 2;
        src.w = 56;
    }
    if (src.h > 56)
    {
        src.y += src.h - 56;
        src.h = 56;
    }
    int px = x + (56 - src.w) / 2, py = y + 56 - src.h;
    if (!flip)
    {
        blit_scaled(sprite, &src, dst, l, px, py);
        return;
    }
    SDL_Surface *mirror = SDL_CreateRGBSurfaceWithFormat(0, src.w, src.h, 32, SDL_PIXELFORMAT_RGBA8888);
    if (mirror == NULL)
        return;
    SDL_SetSurfaceBlendMode(sprite, SDL_BLENDMODE_NONE);
    SDL_Surface *part = SDL_CreateRGBSurfaceWithFormat(0, src.w, src.h, 32, SDL_PIXELFORMAT_RGBA8888);
    if (part)
    {
        SDL_BlitSurface(sprite, &src, part, NULL);
        for (int yy = 0; yy < src.h; yy++)
        {
            Uint32 *from = (Uint32 *)((Uint8 *)part->pixels + yy * part->pitch);
            Uint32 *to = (Uint32 *)((Uint8 *)mirror->pixels + yy * mirror->pitch);
            for (int xx = 0; xx < src.w; xx++)
                to[xx] = from[src.w - 1 - xx];
        }
        SDL_FreeSurface(part);
    }
    SDL_SetSurfaceBlendMode(sprite, SDL_BLENDMODE_BLEND);
    SDL_SetSurfaceBlendMode(mirror, SDL_BLENDMODE_BLEND);
    blit_scaled(mirror, NULL, dst, l, px, py);
    SDL_FreeSurface(mirror);
}

static int gb_measure(const char *text, const void *ctx)
{
    (void)ctx;
    int n = 0;
    for (const char *p = text; *p; p++)
        if ((*p & 0xC0) != 0x80)
            n++;
    return n;
}

// The games' two-line text box (0,12)-(19,17), lines at rows 14 and 16, 18 tiles wide.
static void gb_message(SDL_Surface *dst, Scene *scene, const Layout *l, const char *text)
{
    char lines[2][160];
    int n = wrap_two_lines(text, 18, gb_measure, NULL, lines);
    gb_box(dst, scene, l, 0, 12, 20, 6);
    for (int i = 0; i < n; i++)
        gb_print(dst, scene, l, lines[i], 1, 14 + i * 2);
}

// ------------------------------------------------------------------ the PC list

static const char *gb_box_name(Scene *scene, int b)
{
    if (b == 0)
        return gb_gen == 1 ? "PARTY" : "PARTY <PKMN>";
    return scene->boxes[b].name ? scene->boxes[b].name : "BOX";
}

static void gb_list_entry_name(Box *box, int index, int count, char *out, size_t size)
{
    if (index < count)
    {
        JSON_Object *s = box->slots[index].summary;
        const char *nick = gb_str(s, "nickname");
        snprintf(out, size, "%s", nick[0] ? nick : (box->slots[index].name ? box->slots[index].name : ""));
    }
    else if (index == count)
        snprintf(out, size, "CANCEL");
    else
        out[0] = '\0';
}

// Gen 2's selection outline: the OAM of BillsPC_UpdateSelectionCursor (tiles 0 and 1 of gfx/pc/pc.png), moved
// 16 pixels down per row. Entries: x tile, y tile, x px, y px, tile, flags (1 x flip, 2 y flip).
static const int GB_SELECT_OAM[][6] = {
    {10, 4, 0, 6, 0, 0}, {11, 4, 0, 6, 0, 0}, {12, 4, 0, 6, 0, 0}, {13, 4, 0, 6, 0, 0}, {14, 4, 0, 6, 0, 0},
    {15, 4, 0, 6, 0, 0}, {16, 4, 0, 6, 0, 0}, {17, 4, 0, 6, 0, 0}, {18, 4, 0, 6, 0, 0}, {18, 4, 7, 6, 0, 0},
    {10, 7, 0, 1, 0, 2}, {11, 7, 0, 1, 0, 2}, {12, 7, 0, 1, 0, 2}, {13, 7, 0, 1, 0, 2}, {14, 7, 0, 1, 0, 2},
    {15, 7, 0, 1, 0, 2}, {16, 7, 0, 1, 0, 2}, {17, 7, 0, 1, 0, 2}, {18, 7, 0, 1, 0, 2}, {18, 7, 7, 1, 0, 2},
    {9, 5, 6, 6, 1, 0},  {9, 6, 6, 1, 1, 2},  {19, 5, 1, 6, 1, 1}, {19, 6, 1, 1, 1, 3},
};

static void gb_select_outline(SDL_Surface *dst, Scene *scene, const Layout *l, int row_index)
{
    SDL_Surface *tiles = skin_image(scene, "select.png");
    if (tiles == NULL)
        return;
    for (size_t i = 0; i < sizeof(GB_SELECT_OAM) / sizeof(GB_SELECT_OAM[0]); i++)
    {
        const int *o = GB_SELECT_OAM[i];
        // OAM positions are 8 px right of and 16 px below the screen's
        int x = o[0] * 8 + o[2] - 8, y = o[1] * 8 + o[3] - 16 + row_index * 16;
        SDL_Surface *tile = SDL_CreateRGBSurfaceWithFormat(0, 8, 8, 32, SDL_PIXELFORMAT_RGBA8888);
        if (tile == NULL)
            continue;
        SDL_SetSurfaceBlendMode(tiles, SDL_BLENDMODE_NONE);
        SDL_BlitSurface(tiles, &(SDL_Rect){o[4] * 8, 0, 8, 8}, tile, NULL);
        SDL_SetSurfaceBlendMode(tiles, SDL_BLENDMODE_BLEND);
        if (o[5])
        {
            Uint32 copy[64];
            for (int yy = 0; yy < 8; yy++)
                for (int xx = 0; xx < 8; xx++)
                    copy[yy * 8 + xx] = ((Uint32 *)((Uint8 *)tile->pixels + yy * tile->pitch))[xx];
            for (int yy = 0; yy < 8; yy++)
                for (int xx = 0; xx < 8; xx++)
                {
                    int sx = (o[5] & 1) ? 7 - xx : xx, sy = (o[5] & 2) ? 7 - yy : yy;
                    ((Uint32 *)((Uint8 *)tile->pixels + yy * tile->pitch))[xx] = copy[sy * 8 + sx];
                }
        }
        SDL_SetSurfaceBlendMode(tile, SDL_BLENDMODE_BLEND);
        blit_scaled(tile, NULL, dst, l, x, y);
        SDL_FreeSurface(tile);
    }
}

// The menu over the list: TRANSFER / STATS / EVOLVE / CANCEL, the ones that can't be done greyed out.
static void gb_draw_menu(SDL_Surface *dst, Scene *scene, const Layout *l, GbState *st, Slot *slot, int col, int row, int w)
{
    gb_box(dst, scene, l, col, row, w, 10);
    for (int i = 0; i < 4; i++)
    {
        bool enabled = menu_enabled(slot, i == 1 ? 1 : i);
        gb_print_c(dst, scene, l, GB_MENU_ITEMS[i], col + 2, row + 2 + i * 2, enabled ? gb_fg : gb_grey);
    }
    gb_print(dst, scene, l, "▶", col + 1, row + 2 + st->menu_item * 2);
}

static void gb_draw_gen2(SDL_Surface *dst, Scene *scene, const Layout *l, GbState *st)
{
    Box *box = &scene->boxes[scene->box];
    int count = gb_count(box);

    // BillsPC_BoxName (with Move PKMN w/o Mail's box arrows, as Left/Right change the box)
    gb_box(dst, scene, l, 8, 0, 12, 3);
    gb_print(dst, scene, l, "<BOXL>", 8, 1);
    gb_print(dst, scene, l, "<BOXR>", 19, 1);
    gb_print(dst, scene, l, gb_box_name(scene, scene->box), 10, 1);

    // BillsPC_RefreshTextboxes: the list box joined to the box name's
    gb_box(dst, scene, l, 8, 2, 12, 12);
    gb_frame_tile(dst, scene, l, 4, 8, 2);
    gb_frame_tile(dst, scene, l, 5, 19, 2);
    for (int i = 0; i < 5; i++)
    {
        char name[64];
        gb_list_entry_name(box, st->scroll + i, count, name, sizeof(name));
        gb_print(dst, scene, l, name, 9, 4 + i * 2);
    }

    // PCMonInfo: the highlighted Pokémon's picture, level, gender, item and species
    if (st->cursor < count)
    {
        Slot *slot = &box->slots[st->cursor];
        JSON_Object *s = slot->summary;
        bool egg = json_object_get_boolean(s, "egg") == 1;
        gb_pic(dst, l, slot, 8, 32, false);
        if (!egg)
        {
            gb_print(dst, scene, l, gb_str(s, "species"), 1, 14);
            gb_level(dst, scene, l, gb_str(s, "level"), 1, 12);
            const char *gender = gb_str(s, "gender");
            if (strcmp(gender, "male") == 0)
                gb_print(dst, scene, l, "♂", 5, 12);
            else if (strcmp(gender, "female") == 0)
                gb_print(dst, scene, l, "♀", 5, 12);
            const char *item = gb_str(s, "item_icon");
            if (item[0])
                gb_print(dst, scene, l, strcmp(item, "mail") == 0 ? "<MAIL>" : "<ITEM>", 7, 12);
        }
    }

    if (st->mode == GB_LIST && count + 1 > 0)
        gb_select_outline(dst, scene, l, st->cursor - st->scroll);

    if (st->message)
        gb_message(dst, scene, l, st->message);
    else if ((gb_box(dst, scene, l, 0, 15, 20, 3), st->mode == GB_MENU))
    {
        gb_print(dst, scene, l, "What's up?", 1, 16);
        gb_draw_menu(dst, scene, l, st, &box->slots[st->cursor], 9, 4, 11);
    }
    else
        gb_print(dst, scene, l, "Choose a <PKMN>.", 1, 16);
}

static void gb_draw_gen1(SDL_Surface *dst, Scene *scene, const Layout *l, GbState *st)
{
    Box *box = &scene->boxes[scene->box];
    int count = gb_count(box);

    // BillsPCMenu, with WITHDRAW (boxes) or DEPOSIT (the party) picked
    gb_box(dst, scene, l, 0, 0, 14, 12);
    static const char *PC_MENU[] = {"WITHDRAW <PKMN>", "DEPOSIT <PKMN>", "RELEASE <PKMN>", "CHANGE BOX", "SEE YA!"};
    for (int i = 0; i < 5; i++)
        gb_print(dst, scene, l, PC_MENU[i], 2, 2 + i * 2);
    gb_print(dst, scene, l, "▷", 1, scene->box == 0 ? 4 : 2);

    // "What?" and the BOX No. box
    gb_box(dst, scene, l, 0, 12, 20, 6);
    gb_print(dst, scene, l, "What?", 1, 14);
    gb_box(dst, scene, l, 9, 14, 11, 4);
    char number[32];
    int b = scene->box == 0 ? st->last_box : scene->box;
    snprintf(number, sizeof(number), "BOX No.%d", b);
    gb_print(dst, scene, l, number, 10, 16);

    // DisplayListMenuID: LIST_MENU_BOX, four names with their levels, CANCEL last
    gb_box(dst, scene, l, 4, 2, 16, 11);
    for (int i = 0; i < 4; i++)
    {
        int index = st->scroll + i;
        char name[64];
        gb_list_entry_name(box, index, count, name, sizeof(name));
        gb_print(dst, scene, l, name, 6, 4 + i * 2);
        if (index < count)
            gb_level(dst, scene, l, gb_str(box->slots[index].summary, "level"), 14, 5 + i * 2);
    }
    if (st->scroll + 4 <= count)
        gb_print(dst, scene, l, "▼", 18, 11);
    gb_print(dst, scene, l, st->mode == GB_MENU ? "▷" : "▶", 5, 4 + (st->cursor - st->scroll) * 2);

    if (st->mode == GB_MENU)
        gb_draw_menu(dst, scene, l, st, &box->slots[st->cursor], 9, 8, 11);
    if (st->message)
        gb_message(dst, scene, l, st->message);
}

// ------------------------------------------------------------------ stats

#include "pkmgr-box-gb-stats.c"

// ------------------------------------------------------------------ the loop

static void gb_draw(SDL_Surface *dst, Scene *scene, const Layout *l, GbState *st)
{
    SDL_FillRect(dst, NULL, SDL_MapRGB(dst->format, 0, 0, 0));
    fill_logical(dst, l, 0, 0, GB_W, GB_H, gb_bg);
    if (st->mode == GB_STATS)
        gb_draw_stats(dst, scene, l, st);
    else if (gb_gen == 1)
        gb_draw_gen1(dst, scene, l, st);
    else
        gb_draw_gen2(dst, scene, l, st);
}

static void gb_fix_scroll(Scene *scene, GbState *st)
{
    int count = gb_count(&scene->boxes[scene->box]);
    if (st->cursor > count)
        st->cursor = count;
    if (st->cursor < 0)
        st->cursor = 0;
    int visible = gb_visible();
    if (st->cursor < st->scroll)
        st->scroll = st->cursor;
    if (st->cursor >= st->scroll + visible)
        st->scroll = st->cursor - visible + 1;
    if (st->scroll < 0)
        st->scroll = 0;
}

static void gb_change_box(Scene *scene, GbState *st, int delta)
{
    if (scene->box != 0)
        st->last_box = scene->box;
    scene->box = (scene->box + delta + scene->box_count) % scene->box_count;
    st->cursor = 0;
    st->scroll = 0;
}

// The next/previous Pokémon in the list for the stats screen (Gen 2's Up/Down there).
static void gb_stats_step(Scene *scene, GbState *st, int delta)
{
    int count = gb_count(&scene->boxes[scene->box]);
    if (count == 0)
        return;
    st->cursor = (st->cursor + delta + count) % count;
    gb_fix_scroll(scene, st);
}

static void gb_init(Scene *scene, GbState *st)
{
    gb_gen = get_int(scene->layout, "gen", 2);
    gb_bg = get_color(scene->layout, "colors.bg", gb_bg);
    gb_fg = get_color(scene->layout, "colors.fg", gb_fg);
    gb_grey = get_color(scene->layout, "colors.disabled", gb_grey);
    memset(st, 0, sizeof(*st));
    st->mode = GB_LIST;
    st->last_box = scene->box > 0 ? scene->box : 1;
    st->cursor = scene->slot;
    gb_fix_scroll(scene, st);
}

#ifdef DESKTOP
static void gb_screenshot(SDL_Surface *screen, Scene *scene, const char *state_name, const char *path)
{
    Layout l = gb_layout(screen);
    open_text_font(scene, &l);
    GbState st;
    gb_init(scene, &st);
    if (strcmp(state_name, "menu") == 0)
        st.mode = GB_MENU;
    else if (strcmp(state_name, "why") == 0 && st.cursor < gb_count(&scene->boxes[scene->box]))
    {
        Slot *slot = &scene->boxes[scene->box].slots[st.cursor];
        st.message = menu_why(slot, slot->can_transfer ? 2 : 0);
    }
    else if (strcmp(state_name, "party") == 0)
    {
        scene->box = 0;
        st.cursor = 0;
        gb_fix_scroll(scene, &st);
    }
    else if (strncmp(state_name, "summary", 7) == 0)
    {
        st.mode = GB_STATS;
        st.page = atoi(state_name + 7);
    }
    gb_draw(screen, scene, &l, &st);
    IMG_SavePNG(screen, path);
}
#endif

static int gb_run(SDL_Surface *screen, Scene *scene, const char *write_location)
{
    Layout l = gb_layout(screen);
    open_text_font(scene, &l);
    GbState st;
    gb_init(scene, &st);

    int exit_code = EXIT_BACK;
    const char *action = NULL;
    bool quitting = false;
    int dirty = 1;
    int show_setting = 0;
    int pages = gb_gen == 1 ? 2 : 3;
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

        Box *box = &scene->boxes[scene->box];
        int count = gb_count(box);
        if (PAD_justReleased(BTN_MENU))
        {
            exit_code = EXIT_MENU;
            quitting = true;
        }
        else if (st.message && (dx || dy || a || b || l1 || r1))
            st.message = NULL; // a button closes the explanation
        else if (st.mode == GB_LIST)
        {
            if (dx || l1 || r1)
                gb_change_box(scene, &st, dx ? dx : l1 ? -1 : 1);
            else if (dy)
            {
                st.cursor += dy;
                gb_fix_scroll(scene, &st);
            }
            else if (a && st.cursor < count)
            {
                st.mode = GB_MENU;
                st.menu_item = 0;
            }
            else if ((a && st.cursor == count) || b)
            {
                exit_code = EXIT_BACK;
                quitting = true;
            }
        }
        else if (st.mode == GB_MENU)
        {
            Slot *slot = &box->slots[st.cursor];
            if (dy)
                st.menu_item = (st.menu_item + dy + 4) % 4;
            else if (a && (st.menu_item == 0 || st.menu_item == 2) && menu_enabled(slot, st.menu_item))
            {
                action = st.menu_item == 0 ? "transfer" : "evolve";
                exit_code = EXIT_PICKED;
                quitting = true;
            }
            else if (a && (st.menu_item == 0 || st.menu_item == 2))
            {
                // greyed out: the menu closes and the text box says why
                st.message = menu_why(slot, st.menu_item);
                st.mode = GB_LIST;
            }
            else if (a && st.menu_item == 1)
            {
                st.mode = GB_STATS;
                st.page = 0;
            }
            else if ((a && st.menu_item == 3) || b)
                st.mode = GB_LIST;
        }
        else if (st.mode == GB_STATS)
        {
            if (gb_gen == 1 && a)
            {
                // Gen 1: A turns to the second page, then closes
                if (++st.page >= pages)
                    st.mode = GB_MENU;
            }
            else if (gb_gen == 2 && (dx || l1 || r1))
                st.page = (st.page + (dx ? dx : l1 ? -1 : 1) + pages) % pages;
            else if (gb_gen == 2 && dy)
                gb_stats_step(scene, &st, dy);
            else if (b)
                st.mode = GB_MENU;
        }

        if (dirty && !quitting)
        {
            gb_draw(screen, scene, &l, &st);
            GFX_flip(screen);
            dirty = 0;
        }
        else
        {
            GFX_sync();
        }
    }

    if (exit_code == EXIT_PICKED && action)
    {
        scene->slot = st.cursor;
        write_result(write_location, scene, NULL, action);
    }
    return exit_code;
}
