// The Trainer Card (included by pkmgr-box.c): pkmgr-box --card card.json. card.json has the skin folder and the
// player's details: {"skin", "name", "id", "money", "dex", "has_dex", "hours", "minutes", "badges", "female"}.
//
// Gen 1 (Red/Blue/Yellow's trainer info screen, engine/menus/start_sub_menus.asm DrawTrainerInfo and
// draw_badges.asm): one page, A or B closes it. Gen 2 (Gold/Silver/Crystal's TRAINER CARD,
// engine/menus/trainer_card.asm): the status page and the badges page; Right/A turns to the badges, Left back,
// A on the badges or B closes it. Positions are the games' (tile columns and rows of the 160x144 screen).

typedef struct
{
    JSON_Object *card;
    int page;
} CardState;

static void card_tile(SDL_Surface *dst, Scene *scene, const Layout *l, const char *image, int index, int col, int row)
{
    gb_tile_from(dst, scene, l, image, index, col, row);
}

static void card_block(SDL_Surface *dst, Scene *scene, const Layout *l, const char *image, int index, int size_w, int size_h, int x, int y)
{
    SDL_Surface *strip = skin_image(scene, image);
    if (strip == NULL)
        return;
    SDL_Rect src = {index * size_w, 0, size_w, size_h};
    blit_scaled(strip, &src, dst, l, x, y);
}

// "¥3000": the money, ¥ just before the first digit.
static void card_money(char *out, size_t size, int money)
{
    snprintf(out, size, "¥%d", money);
}

// Gen 1: a box of trainer_info.png's tiles from (c0, r0) to (c1, r1).
static void card_box_gen1(SDL_Surface *dst, Scene *scene, const Layout *l, int c0, int r0, int c1, int r1)
{
    for (int r = r0; r <= r1; r++)
    {
        for (int c = c0; c <= c1; c++)
        {
            int t = -1;
            if (r == r0)
                t = c == c0 ? 2 : c == c1 ? 4 : 3; // $79 $7A $7B
            else if (r == r1)
                t = c == c0 ? 6 : c == c1 ? 7 : 0; // $7D $77 $7E
            else if (c == c0)
                t = 5; // $7C
            else if (c == c1)
                t = 1; // $78
            if (t >= 0)
                card_tile(dst, scene, l, "card_tiles.png", t, c, r);
        }
    }
}

static void card_draw_gen1(SDL_Surface *dst, Scene *scene, const Layout *l, CardState *st)
{
    JSON_Object *c = st->card;
    card_box_gen1(dst, scene, l, 0, 0, 19, 7);
    gb_image(dst, scene, l, "card_pic.png", 15 * 8, 1 * 8);
    card_box_gen1(dst, scene, l, 0, 0, 19, 7); // the box's right edge and bottom cover the picture's edge
    card_box_gen1(dst, scene, l, 1, 10, 18, 17);
    for (int r = 10; r <= 17; r++)
    {
        card_tile(dst, scene, l, "card_tiles.png", 8, 0, r); // the background pattern $D7
        card_tile(dst, scene, l, "card_tiles.png", 8, 19, r);
    }
    gb_print(dst, scene, l, "NAME/", 2, 2);
    gb_print(dst, scene, l, "MONEY/", 2, 4);
    gb_print(dst, scene, l, "TIME/", 2, 6);
    gb_print(dst, scene, l, gb_str(c, "name"), 7, 2);
    char text[32];
    card_money(text, sizeof(text), gb_int(c, "money"));
    gb_print(dst, scene, l, text, 8, 4);
    snprintf(text, sizeof(text), "%d", gb_int(c, "hours"));
    int len = gb_print(dst, scene, l, text, 9, 6);
    gb_image(dst, scene, l, "card_colon.png", (9 + len) * 8, 6 * 8);
    snprintf(text, sizeof(text), "%02d", gb_int(c, "minutes"));
    gb_print(dst, scene, l, text, 10 + len, 6);
    // ◯BADGES◯ and the eight leaders: a badge where it was earned, the leader's face where it wasn't
    gb_image(dst, scene, l, "card_circle.png", 6 * 8, 9 * 8);
    gb_print(dst, scene, l, "BADGES", 7, 9);
    gb_image(dst, scene, l, "card_circle.png", 13 * 8, 9 * 8);
    int badges = gb_int(c, "badges");
    for (int i = 0; i < 8; i++)
    {
        int x = 2 + 4 * (i % 4), r = i < 4 ? 11 : 14;
        card_tile(dst, scene, l, "card_numbers.png", i, x, r);
        card_block(dst, scene, l, (badges >> i) & 1 ? "card_badges.png" : "card_faces.png", i, 16, 16, (x + 1) * 8, (r + 1) * 8);
    }
}

// Gen 2: TrainerCard_InitBorder from row r0 with d inner rows.
static void card_border_gen2(SDL_Surface *dst, Scene *scene, const Layout *l, const char *tiles, int r0, int d)
{
    int last = r0 + d + 3;
    for (int r = r0; r <= last; r++)
    {
        for (int c = 0; c < 20; c++)
        {
            bool fill = r == r0 || r == last || c == 0 || c == 19;
            if (fill)
                card_tile(dst, scene, l, tiles, 0, c, r);
        }
    }
    card_tile(dst, scene, l, tiles, 1, 1, last - 1); // the inner bottom-left corner $24
}

static void card_draw_gen2(SDL_Surface *dst, Scene *scene, const Layout *l, CardState *st, Uint32 ticks)
{
    JSON_Object *c = st->card;
    bool crystal = json_object_get_boolean(scene->layout, "crystal") == 1;
    bool female = crystal && json_object_get_boolean(c, "female") == 1;
    const char *tiles = female ? "card_tiles_f.png" : "card_tiles_m.png";
    const char *pic = female ? "card_pic_f.png" : "card_pic_m.png";
    card_border_gen2(dst, scene, l, tiles, 0, 5);
    card_border_gen2(dst, scene, l, tiles, 8, 6);
    // the top-right corners: Crystal's own tile, Gold/Silver's the picture's top-right tile
    for (int r = 1; r <= 9; r += 8)
    {
        if (crystal)
            gb_image(dst, scene, l, female ? "card_corner_f.png" : "card_corner_m.png", 18 * 8, r * 8);
        else
            card_block(dst, scene, l, pic, 4, 8, 8, 18 * 8, r * 8);
    }

    // the top half: name, ID, money and the picture
    gb_print(dst, scene, l, "NAME/", 2, 2);
    gb_print(dst, scene, l, "MONEY", 2, 6);
    card_tile(dst, scene, l, tiles, 4, 2, 4); // ID
    card_tile(dst, scene, l, tiles, 5, 3, 4); // No
    gb_print(dst, scene, l, gb_str(c, "name"), 7, 2);
    char text[32];
    snprintf(text, sizeof(text), "%05d", gb_int(c, "id"));
    gb_print(dst, scene, l, text, 5, 4);
    card_money(text, sizeof(text), gb_int(c, "money"));
    gb_print_right(dst, scene, l, text, 13, 6);
    for (int col = 1; col <= 12; col++)
        card_tile(dst, scene, l, tiles, 2, col, 3);
    card_tile(dst, scene, l, tiles, 3, 13, 3);
    SDL_Surface *picture = skin_image(scene, pic);
    if (picture)
    {
        // 5x7 tiles at (14, 1); its top-right tile is the corner
        SDL_Rect src = {0, 0, 40, 56};
        blit_scaled(picture, &src, dst, l, 14 * 8, 8);
        if (crystal)
            gb_image(dst, scene, l, female ? "card_corner_f.png" : "card_corner_m.png", 18 * 8, 8);
    }

    if (st->page == 0)
    {
        for (int i = 0; i < 5; i++)
            card_tile(dst, scene, l, female ? "card_status_f.png" : "card_status_m.png", i, 2 + i, 8);
        if (json_object_get_boolean(c, "has_dex") == 1)
        {
            gb_print(dst, scene, l, "POKéDEX", 2, 10);
            snprintf(text, sizeof(text), "%d", gb_int(c, "dex"));
            gb_print_right(dst, scene, l, text, 17, 10);
        }
        gb_print(dst, scene, l, "PLAY TIME", 2, 12);
        snprintf(text, sizeof(text), "%d", gb_int(c, "hours"));
        gb_print_right(dst, scene, l, text, 14, 12);
        // the colon blinks every 32 frames
        if ((ticks / 533) % 2)
            card_tile(dst, scene, l, female ? "card_status_f.png" : "card_status_m.png", 5, 15, 12);
        snprintf(text, sizeof(text), "%02d", gb_int(c, "minutes"));
        gb_print(dst, scene, l, text, 16, 12);
        gb_print(dst, scene, l, "BADGES▶", 12, 15);
        return;
    }

    // the badges page: the leaders, and the badges earned
    for (int i = 0; i < 5; i++)
        card_tile(dst, scene, l, "card_badges_tab.png", i, 2 + i, 8);
    for (int k = 0; k < 8; k++)
    {
        int x = 2 + 4 * (k % 4), r = k < 4 ? 10 : 13;
        card_tile(dst, scene, l, "card_numbers.png", k, x, r);
        card_block(dst, scene, l, "card_faces.png", k, 24, 16, (x + 1) * 8, (r + 1) * 8);
    }
    // TrainerCard_JohtoBadgesOAM, by badge bit: Zephyr, Hive, Plain, Fog, Mineral, Storm, Glacier, Rising
    static const int BADGE_XY[8][2] = {{16, 88}, {48, 88}, {80, 88}, {112, 88}, {48, 112}, {16, 112}, {80, 112}, {112, 112}};
    int badges = gb_int(c, "badges");
    for (int b = 0; b < 8; b++)
        if ((badges >> b) & 1)
            card_block(dst, scene, l, "card_badges.png", b, 16, 16, BADGE_XY[b][0], BADGE_XY[b][1]);
}

static void card_draw(SDL_Surface *dst, Scene *scene, const Layout *l, CardState *st, Uint32 ticks)
{
    SDL_FillRect(dst, NULL, SDL_MapRGB(dst->format, 0, 0, 0));
    fill_logical(dst, l, 0, 0, GB_W, GB_H, gb_bg);
    if (gb_gen == 1)
        card_draw_gen1(dst, scene, l, st);
    else
        card_draw_gen2(dst, scene, l, st, ticks);
}

static int card_run(const char *card_path, const char *screenshot, const char *state_name)
{
    JSON_Value *root = json_parse_file(card_path);
    JSON_Object *card = json_value_get_object(root);
    if (card == NULL)
    {
        fprintf(stderr, "could not read %s\n", card_path);
        return EXIT_ERROR;
    }
    Scene scene;
    memset(&scene, 0, sizeof(scene));
    scene.skin = dup_string(card, "skin");
    scene.font = dup_string(card, "font");
    char layout_path[1024];
    snprintf(layout_path, sizeof(layout_path), "%s/layout.json", scene.skin ? scene.skin : ".");
    scene.layout_root = json_parse_file(layout_path);
    scene.layout = json_value_get_object(scene.layout_root);
    if (scene.layout == NULL)
    {
        fprintf(stderr, "could not read %s\n", layout_path);
        return EXIT_ERROR;
    }
    gb_gen = get_int(scene.layout, "gen", 2);
    gb_bg = get_color(scene.layout, "colors.bg", gb_bg);
    gb_fg = get_color(scene.layout, "colors.fg", gb_fg);
    gb_load_font(&scene);

    PWR_setCPUSpeed(CPU_SPEED_MENU);
    SDL_Surface *screen = GFX_init(MODE_MAIN);
    PAD_init();
    PWR_init();
    InitSettings();
    Layout l = gb_layout(screen);
    open_text_font(&scene, &l);
    CardState st = {card, 0};

#ifdef DESKTOP
    if (screenshot)
    {
        st.page = state_name && strcmp(state_name, "page2") == 0 ? 1 : 0;
        card_draw(screen, &scene, &l, &st, 600);
        IMG_SavePNG(screen, screenshot);
        return EXIT_PICKED;
    }
#else
    (void)screenshot;
    (void)state_name;
#endif

    int exit_code = EXIT_BACK;
    bool quitting = false;
    int dirty = 1, show_setting = 0;
    Uint32 last_blink = 0;
    while (!quitting)
    {
        GFX_startFrame();
        PWR_update(&dirty, &show_setting, NULL, NULL);
        PAD_poll();
        bool a = PAD_justReleased(BTN_A), b = PAD_justReleased(BTN_B);
        bool left = PAD_justRepeated(BTN_LEFT), right = PAD_justRepeated(BTN_RIGHT);
        if (PAD_justReleased(BTN_MENU))
        {
            exit_code = EXIT_MENU;
            quitting = true;
        }
        else if (b || (gb_gen == 1 && a) || (st.page == 1 && a))
            quitting = true;
        else if (gb_gen == 2 && st.page == 0 && (right || a))
            st.page = 1, dirty = 1;
        else if (gb_gen == 2 && st.page == 1 && left)
            st.page = 0, dirty = 1;
        Uint32 now = SDL_GetTicks();
        if (gb_gen == 2 && st.page == 0 && now - last_blink >= 533)
        {
            last_blink = now;
            dirty = 1;
        }
        if (dirty && !quitting)
        {
            card_draw(screen, &scene, &l, &st, now);
            GFX_flip(screen);
            dirty = 0;
        }
        else
            GFX_sync();
    }
    QuitSettings();
    PWR_quit();
    PAD_quit();
    GFX_quit();
    return exit_code;
}
