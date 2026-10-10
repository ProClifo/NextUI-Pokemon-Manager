// The Trainer Card (included by pkmgr-box.c): pkmgr-box --card card.json. card.json has the skin folder and the
// player's details: {"skin", "name", "id", "money", "dex", "has_dex", "hours", "minutes", "badges", "female"}.
//
// Gen 1 (Red/Blue/Yellow's trainer info screen, engine/menus/start_sub_menus.asm DrawTrainerInfo and
// draw_badges.asm): one page, A or B closes it. Gen 2 (Gold/Silver/Crystal's TRAINER CARD,
// engine/menus/trainer_card.asm): the status page and the badges page; Right/A turns to the badges, Left back,
// A on the badges or B closes it. Positions are the games' (tile columns and rows of the 160x144 screen).
// Gen 3 (Ruby/Sapphire, Emerald, FireRed/LeafGreen) adds "stars" and the back's records: see card3_front/back.

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


// ------------------------------------------------------------------ Gen 3

// Ruby/Sapphire, Emerald and FireRed/LeafGreen (src/trainer_card.c): the card's front and back, drawn on the GBA's
// 240x160 screen with the game's font. Positions are the games' (screen pixels, the top of each glyph cell).
enum { CARD3_RS, CARD3_E, CARD3_FRLG };
static int card3_game = -1; // -1: a Game Boy card

typedef struct
{
    SDL_Color fg, shadow;
} CardInk;

static CardInk card3_grey(void)
{
    if (card3_game == CARD3_RS)
        return (CardInk){{74, 74, 74, 255}, {213, 213, 205, 255}};
    return (CardInk){{98, 98, 98, 255}, {213, 213, 205, 255}};
}

static CardInk card3_red(void)
{
    if (card3_game == CARD3_RS)
        return (CardInk){{255, 0, 0, 255}, {255, 0, 255, 255}};
    return (CardInk){{230, 8, 8, 255}, {255, 189, 115, 255}};
}

static int card3_text(SDL_Surface *dst, const Layout *l, const char *text, int x, int y, int align, CardInk ink)
{
    return draw_game_text(dst, l, text, x, y, align, ink.fg, ink.shadow);
}

// Text in several colours, one after another, starting at x (or ending at x when right-aligned).
static void card3_runs(SDL_Surface *dst, const Layout *l, const char *const *texts, const CardInk *inks, int n, int x, int y, bool right)
{
    if (right)
        for (int i = 0; i < n; i++)
            x -= game_text_width(texts[i]);
    for (int i = 0; i < n; i++)
    {
        int w = game_text_width(texts[i]);
        card3_text(dst, l, texts[i], x, y, 0, inks[i]);
        x += w;
    }
}

static void card3_image(SDL_Surface *dst, Scene *scene, const Layout *l, const char *name, int x, int y)
{
    SDL_Surface *img = skin_image(scene, name);
    if (img)
        blit_scaled(img, NULL, dst, l, x, y);
}

static int card3_stars(JSON_Object *c)
{
    int stars = gb_int(c, "stars");
    return stars < 0 ? 0 : stars > 4 ? 4 : stars;
}

static void card3_front(SDL_Surface *dst, Scene *scene, const Layout *l, JSON_Object *c, bool colon)
{
    char buf[96], name[64];
    bool female = json_object_get_boolean(c, "female") == 1;
    int stars = card3_stars(c);
    snprintf(buf, sizeof(buf), "card_front_%d_%c.png", stars, female ? 'f' : 'm');
    card3_image(dst, scene, l, buf, 0, 0);
    snprintf(name, sizeof(name), "%s", gb_str(c, "name"));
    int id = gb_int(c, "id"), money = gb_int(c, "money"), dex = gb_int(c, "dex");
    int hours = gb_int(c, "hours"), minutes = gb_int(c, "minutes");
    hours = hours > 999 ? 999 : hours;
    minutes = minutes > 59 ? 59 : minutes < 0 ? 0 : minutes;
    bool has_dex = json_object_get_boolean(c, "has_dex") == 1;
    CardInk grey = card3_grey();
    char money_text[24], dex_text[16], hours_text[16], minutes_text[16];
    snprintf(money_text, sizeof(money_text), "¥%d", money);
    snprintf(dex_text, sizeof(dex_text), "%d", dex);
    snprintf(minutes_text, sizeof(minutes_text), "%02d", minutes);
    int pic_x = 153, pic_y = 40, badge_y = 120, star_y = 56;
    if (card3_game == CARD3_E)
    {
        snprintf(buf, sizeof(buf), "NAME: %s", name);
        card3_text(dst, l, buf, 24, 41, 0, grey);
        snprintf(buf, sizeof(buf), "IDNo.%05d", id);
        card3_text(dst, l, buf, 128 + (96 - game_text_width(buf)) / 2, 17, 0, grey);
        card3_text(dst, l, "MONEY", 24, 65, 0, grey);
        card3_text(dst, l, money_text, 136, 65, 2, grey);
        if (has_dex)
        {
            card3_text(dst, l, "POKéDEX", 24, 81, 0, grey);
            card3_text(dst, l, dex_text, 136, 81, 2, grey);
        }
        card3_text(dst, l, "TIME", 24, 97, 0, grey);
        // PrintTimeOnCard: hours in 3 figures, the colon, 2-figure minutes, ending at 136
        int cw = game_text_width(":"), x = 136 - (cw + 30);
        snprintf(hours_text, sizeof(hours_text), "%d", hours);
        card3_text(dst, l, hours_text, x + 18, 97, 2, grey);
        if (colon)
            card3_text(dst, l, ":", x + 18, 97, 0, grey);
        card3_text(dst, l, minutes_text, x + 18 + cw, 97, 0, grey);
    }
    else if (card3_game == CARD3_FRLG)
    {
        pic_x = 165, pic_y = 44, badge_y = 128;
        snprintf(buf, sizeof(buf), "NAME: %s", name);
        card3_text(dst, l, buf, 28, 37, 0, grey);
        snprintf(buf, sizeof(buf), "IDNo.%05d", id);
        card3_text(dst, l, buf, 150, 18, 0, grey);
        card3_text(dst, l, "MONEY", 28, 64, 0, grey);
        // the game places these assuming 6 pixels a character
        card3_text(dst, l, money_text, 142 - 6 * (int)(strlen(money_text) - 1), 64, 0, grey); // ¥ is 2 bytes
        if (has_dex)
        {
            card3_text(dst, l, "POKéDEX", 28, 80, 0, grey);
            card3_text(dst, l, dex_text, 144 - 6 * (int)strlen(dex_text), 80, 0, grey);
        }
        card3_text(dst, l, "TIME", 28, 96, 0, grey);
        snprintf(hours_text, sizeof(hours_text), "%3d", hours);
        card3_text(dst, l, hours_text, 109, 96, 0, grey);
        if (colon)
            card3_text(dst, l, ":", 127, 96, 0, grey);
        card3_text(dst, l, minutes_text, 132, 96, 0, grey);
    }
    else
    {
        // the labels are printed on the card; text is 4 pixels lower than its tile row
        pic_x = 152, pic_y = 44, badge_y = 124, star_y = 52;
        card3_text(dst, l, name, 56, 44, 0, grey);
        snprintf(buf, sizeof(buf), "%05d", id);
        card3_text(dst, l, buf, 160, 20, 0, grey);
        card3_text(dst, l, money_text, 128, 68, 2, grey);
        if (has_dex)
            card3_text(dst, l, dex_text, 128, 84, 2, grey);
        else
        {
            snprintf(buf, sizeof(buf), "card_nodex_%d.png", stars);
            card3_image(dst, scene, l, buf, 24, 84);
        }
        snprintf(buf, sizeof(buf), "%d %s %02d", hours, colon ? ":" : " ", minutes);
        card3_text(dst, l, buf, 128, 100, 2, grey);
    }
    snprintf(buf, sizeof(buf), "card_pic_%c.png", female ? 'f' : 'm');
    card3_image(dst, scene, l, buf, pic_x, pic_y);
    int badges = gb_int(c, "badges");
    for (int i = 0; i < 8; i++)
        if ((badges >> i) & 1)
            card_block(dst, scene, l, "card_badges.png", i, 16, 16, 32 + 24 * i, badge_y);
    for (int k = 0; k < stars; k++)
        card3_image(dst, scene, l, "card_star.png", 120 + 8 * k, star_y);
}

static int card3_value(JSON_Object *c, const char *key)
{
    return (int)json_object_get_number(c, key);
}

static void card3_back(SDL_Surface *dst, Scene *scene, const Layout *l, JSON_Object *c)
{
    char buf[96], a[16], b[16];
    bool female = json_object_get_boolean(c, "female") == 1;
    snprintf(buf, sizeof(buf), "card_back_%d_%c.png", card3_stars(c), female ? 'f' : 'm');
    card3_image(dst, scene, l, buf, 0, 0);
    CardInk grey = card3_grey(), red = card3_red();
    const char *name = gb_str(c, "name");
    JSON_Array *hof = json_object_get_array(c, "hof");
    int hh = hof ? (int)json_array_get_number(hof, 0) : 0, hm = hof ? (int)json_array_get_number(hof, 1) : 0;
    int hs = hof ? (int)json_array_get_number(hof, 2) : 0;
    int wins = card3_value(c, "link_wins"), losses = card3_value(c, "link_losses"), trades = card3_value(c, "trades");
    if (card3_game == CARD3_FRLG)
    {
        card3_text(dst, l, name, 146, 19, 0, grey);
        if (hof)
        {
            card3_text(dst, l, "HALL OF FAME DEBUT  ", 18, 43, 0, grey);
            snprintf(buf, sizeof(buf), "%3d:%02d:%02d", hh, hm, hs);
            card3_text(dst, l, buf, 172, 43, 0, red);
        }
        if (wins || losses)
        {
            card3_text(dst, l, "LINK BATTLES", 18, 59, 0, grey);
            card3_text(dst, l, "W:", 138, 59, 0, grey);
            card3_text(dst, l, "L:", 186, 59, 0, grey);
            snprintf(a, sizeof(a), "%4d", wins);
            snprintf(b, sizeof(b), "%4d", losses);
            card3_text(dst, l, a, 152, 59, 0, red);
            card3_text(dst, l, b, 200, 59, 0, red);
        }
        static const char *ROWS[][2] = {{"trades", "POKéMON TRADES"}, {"union", "UNION TRADES & BATTLES"}, {"berry_crush", "BERRY CRUSH"}};
        for (int r = 0; r < 3; r++)
        {
            int v = card3_value(c, ROWS[r][0]);
            if (v == 0)
                continue;
            card3_text(dst, l, ROWS[r][1], 18, 75 + 16 * r, 0, grey);
            snprintf(buf, sizeof(buf), "%5d", v);
            card3_text(dst, l, buf, 194, 75 + 16 * r, 0, red);
        }
        return;
    }
    snprintf(buf, sizeof(buf), "%s's TRAINER CARD", name);
    card3_text(dst, l, buf, 224, card3_game == CARD3_RS ? 20 : 17, 2, grey);
    int pokeblocks = card3_value(c, "pokeblocks"), contests = card3_value(c, "contests");
    if (card3_game == CARD3_E)
    {
        // PrintStatOnBackOfCard: row r's label at (24, r * 16 + 41), its value ending at 224
        if (hof)
        {
            card3_text(dst, l, "HALL OF FAME DEBUT  ", 24, 41, 0, grey);
            snprintf(buf, sizeof(buf), "%d:%02d:%02d", hh, hm, hs);
            card3_text(dst, l, buf, 224, 41, 2, red);
        }
        if (wins || losses)
        {
            card3_text(dst, l, "LINK BATTLES", 24, 57, 0, grey);
            snprintf(a, sizeof(a), "%d", wins);
            snprintf(b, sizeof(b), "%d", losses);
            const char *texts[] = {"W:", a, "  L:", b};
            const CardInk inks[] = {grey, red, grey, red};
            card3_runs(dst, l, texts, inks, 4, 224, 57, true);
        }
        if (trades)
        {
            card3_text(dst, l, "POKéMON TRADES", 24, 73, 0, grey);
            snprintf(buf, sizeof(buf), "%d", trades);
            card3_text(dst, l, buf, 224, 73, 2, red);
        }
        if (pokeblocks)
        {
            card3_text(dst, l, "{POKEBLOCK}S W/FRIENDS", 24, 89, 0, grey);
            snprintf(buf, sizeof(buf), "%d", pokeblocks);
            card3_text(dst, l, buf, 224, 89, 2, red);
        }
        if (contests)
        {
            card3_text(dst, l, "WON CONTESTS W/FRIENDS", 24, 105, 0, grey);
            snprintf(buf, sizeof(buf), "%d", contests);
            card3_text(dst, l, buf, 224, 105, 2, red);
        }
        int bp = card3_value(c, "bp");
        if (bp)
        {
            card3_text(dst, l, "BATTLE POINTS WON", 24, 121, 0, grey);
            snprintf(a, sizeof(a), "%d", bp);
            const char *texts[] = {a, "BP"};
            const CardInk inks[] = {red, grey};
            card3_runs(dst, l, texts, inks, 2, 224, 121, true);
        }
        return;
    }
    // Ruby/Sapphire
    if (hof)
    {
        card3_text(dst, l, "FIRST HALL OF FAME", 24, 44, 0, grey);
        snprintf(buf, sizeof(buf), "%d : %02d : %02d", hh, hm, hs);
        card3_text(dst, l, buf, 224, 44, 2, red);
    }
    if (wins || losses)
    {
        card3_text(dst, l, "LINK CABLE BATTLES  W/            L/", 24, 60, 0, grey);
        snprintf(a, sizeof(a), "%d", wins);
        snprintf(b, sizeof(b), "%d", losses);
        card3_text(dst, l, a, 176, 60, 2, red);
        card3_text(dst, l, b, 224, 60, 2, red);
    }
    if (trades)
    {
        card3_text(dst, l, "POKéMON TRADES", 24, 76, 0, grey);
        snprintf(buf, sizeof(buf), "%d", trades);
        card3_text(dst, l, buf, 224, 76, 2, red);
    }
    if (pokeblocks)
    {
        card3_text(dst, l, "{POKEBLOCK}S W/FRIENDS", 24, 92, 0, grey);
        snprintf(buf, sizeof(buf), "%d", pokeblocks);
        card3_text(dst, l, buf, 224, 92, 2, red);
    }
    if (contests)
    {
        card3_text(dst, l, "CONTESTS W/FRIENDS W/", 24, 108, 0, grey);
        snprintf(buf, sizeof(buf), "%d", contests);
        card3_text(dst, l, buf, 224, 108, 2, red);
    }
    int tower_wins = card3_value(c, "tower_wins"), streak = card3_value(c, "tower_streak");
    if (tower_wins || streak)
    {
        card3_text(dst, l, "BATTLE TOWER W/", 24, 124, 0, grey);
        card3_text(dst, l, "&", 138, 124, 0, grey);
        card3_text(dst, l, "STRAIGHT", 176, 124, 0, grey);
        snprintf(a, sizeof(a), "%d", tower_wins);
        snprintf(b, sizeof(b), "%d", streak);
        card3_text(dst, l, a, 136, 124, 2, red);
        card3_text(dst, l, b, 173, 124, 2, red);
    }
}

static void card_draw(SDL_Surface *dst, Scene *scene, const Layout *l, CardState *st, Uint32 ticks)
{
    SDL_FillRect(dst, NULL, SDL_MapRGB(dst->format, 0, 0, 0));
    if (card3_game >= 0)
    {
        // the time's colon blinks once a second
        if (st->page == 0)
            card3_front(dst, scene, l, st->card, (ticks / 1000) % 2 == 0);
        else
            card3_back(dst, scene, l, st->card);
        return;
    }
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
    const char *game = strrchr(scene.skin ? scene.skin : "", '/');
    game = game ? game + 1 : scene.skin ? scene.skin : "";
    card3_game = strcmp(game, "rs") == 0 ? CARD3_RS : strcmp(game, "e") == 0 ? CARD3_E : strcmp(game, "frlg") == 0 ? CARD3_FRLG : -1;
    if (card3_game >= 0)
    {
        load_game_fonts(scene.skin, scene.layout);
        use_font("normal");
    }
    else
    {
        gb_gen = get_int(scene.layout, "gen", 2);
        gb_bg = get_color(scene.layout, "colors.bg", gb_bg);
        gb_fg = get_color(scene.layout, "colors.fg", gb_fg);
        gb_load_font(&scene);
    }

    PWR_setCPUSpeed(CPU_SPEED_MENU);
    SDL_Surface *screen = GFX_init(MODE_MAIN);
    PAD_init();
    PWR_init();
    InitSettings();
    Layout l = card3_game >= 0 ? make_layout(screen) : gb_layout(screen);
    if (card3_game < 0)
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
        else if (card3_game >= 0)
        {
            // A turns the card over, then closes it; B closes the front and (but in Ruby/Sapphire) turns the
            // back over again
            if (a && st.page == 0)
                st.page = 1, dirty = 1;
            else if (a || (b && (st.page == 0 || card3_game == CARD3_RS)))
                quitting = true;
            else if (b)
                st.page = 0, dirty = 1;
        }
        else if (b || (gb_gen == 1 && a) || (st.page == 1 && a))
            quitting = true;
        else if (gb_gen == 2 && st.page == 0 && (right || a))
            st.page = 1, dirty = 1;
        else if (gb_gen == 2 && st.page == 1 && left)
            st.page = 0, dirty = 1;
        Uint32 now = SDL_GetTicks();
        if (((card3_game < 0 && gb_gen == 2) || card3_game >= 0) && st.page == 0 && now - last_blink >= (card3_game >= 0 ? 250u : 533u))
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
