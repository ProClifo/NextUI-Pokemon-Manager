// The Game Boy stats screens (included by pkmgr-box-gb.c), at the games' tile positions:
// Gen 1's two status pages (pokered engine/pokemon/status_screen.asm) and Gen 2's pink, green and blue pages
// (pokecrystal engine/pokemon/stats_screen.asm; Gold/Silver place the item and the OT name a little differently).

static void gb_tile_from(SDL_Surface *dst, Scene *scene, const Layout *l, const char *image, int index, int col, int row)
{
    SDL_Surface *strip = skin_image(scene, image);
    if (strip == NULL)
        return;
    SDL_Rect src = {index * 8, 0, 8, 8};
    blit_scaled(strip, &src, dst, l, col * 8, row * 8);
}

static int gb_int(JSON_Object *s, const char *key)
{
    if (s == NULL)
        return 0;
    if (json_object_get_value(s, key) && json_value_get_type(json_object_get_value(s, key)) == JSONNumber)
        return (int)json_object_get_number(s, key);
    const char *text = json_object_get_string(s, key);
    return text ? atoi(text) : 0;
}

// A number right-aligned so its last digit is at right_col (PrintNumber leaves the leading cells alone).
static void gb_number(SDL_Surface *dst, Scene *scene, const Layout *l, int value, int right_col, int row, int zero_pad)
{
    char text[16];
    if (zero_pad)
        snprintf(text, sizeof(text), "%0*d", zero_pad, value);
    else
        snprintf(text, sizeof(text), "%d", value);
    gb_print_right(dst, scene, l, text, right_col, row);
}

// The HP bar's six tiles (48 px) from (col, row): at least a pixel while the Pokémon has HP, coloured by how much
// is left (green from green_px, yellow from 10 px, else red).
static void gb_hp_bar(SDL_Surface *dst, Scene *scene, const Layout *l, int hp, int max, int col, int row, int green_px)
{
    int px = max > 0 ? hp * 48 / max : 0;
    if (hp > 0 && px == 0)
        px = 1;
    const char *image = px >= green_px ? "hp_green.png" : px >= 10 ? "hp_yellow.png" : "hp_red.png";
    for (int i = 0; i < 6; i++)
    {
        int fill = px - i * 8;
        gb_tile_from(dst, scene, l, image, fill >= 8 ? 8 : fill > 0 ? fill : 0, col + i, row);
    }
}

static int gb_level_value(JSON_Object *s)
{
    return gb_int(s, "level");
}

static void gb_next_level(SDL_Surface *dst, Scene *scene, const Layout *l, JSON_Object *s, int col, int row)
{
    int next = gb_level_value(s) + 1;
    if (next > 100)
        next = 100;
    char level[16];
    snprintf(level, sizeof(level), "%d", next);
    gb_level(dst, scene, l, level, col, row);
}

static const char *gb_type(JSON_Object *s, int index)
{
    JSON_Array *types = json_object_get_array(s, "type_names");
    if (types == NULL || (int)json_array_get_count(types) <= index)
        return NULL;
    return json_array_get_string(types, index);
}

static JSON_Object *gb_move(JSON_Object *s, int i)
{
    JSON_Array *moves = json_object_get_array(s, "moves");
    return moves && (int)json_array_get_count(moves) > i ? json_array_get_object(moves, i) : NULL;
}

static bool gb_move_known(JSON_Object *mv)
{
    return mv && json_object_has_value(mv, "max_pp");
}

// ------------------------------------------------------------------ Gen 1

static void gb_stats_gen1(SDL_Surface *dst, Scene *scene, const Layout *l, GbState *st, Slot *slot)
{
    JSON_Object *s = slot->summary;
    gb_pic(dst, l, slot, 8, 0, true); // LoadFlippedFrontSpriteByMonIndex at (1,0)

    // "№.001" under the picture, on line box 1's bottom line
    for (int y = 1; y <= 6; y++)
        gb_image(dst, scene, l, "line_v.png", 19 * 8, y * 8);
    gb_image(dst, scene, l, "line_corner.png", 19 * 8, 7 * 8);
    for (int x = 9; x <= 18; x++)
        gb_image(dst, scene, l, "line_h.png", x * 8, 7 * 8);
    gb_image(dst, scene, l, "line_end.png", 8 * 8, 7 * 8);
    gb_print(dst, scene, l, "<No>", 1, 7);
    gb_image(dst, scene, l, "dot.png", 2 * 8, 7 * 8);
    gb_number(dst, scene, l, gb_int(s, "dex_no"), 5, 7, 3);

    if (st->page == 0)
    {
        gb_print(dst, scene, l, gb_str(s, "nickname"), 9, 1);
        gb_level(dst, scene, l, gb_str(s, "level"), 14, 2);
        gb_image(dst, scene, l, "hp_label.png", 11 * 8, 3 * 8);
        gb_hp_bar(dst, scene, l, gb_int(s, "hp_cur"), gb_int(s, "hp_max"), 13, 3, 27);
        gb_image(dst, scene, l, "hp_end.png", 19 * 8, 3 * 8);
        gb_number(dst, scene, l, gb_int(s, "hp_cur"), 14, 4, 0);
        gb_print(dst, scene, l, "/", 15, 4);
        gb_number(dst, scene, l, gb_int(s, "hp_max"), 18, 4, 0);
        gb_print(dst, scene, l, "STATUS/", 9, 6);
        gb_print(dst, scene, l, gb_str(s, "status")[0] ? gb_str(s, "status") : "OK", 16, 6);

        // line box 2 with the types, ID and OT
        for (int y = 9; y <= 16; y++)
            gb_image(dst, scene, l, "line_v.png", 19 * 8, y * 8);
        gb_image(dst, scene, l, "line_corner.png", 19 * 8, 17 * 8);
        for (int x = 13; x <= 18; x++)
            gb_image(dst, scene, l, "line_h.png", x * 8, 17 * 8);
        gb_image(dst, scene, l, "line_end.png", 12 * 8, 17 * 8);
        gb_print(dst, scene, l, "TYPE1/", 10, 9);
        if (gb_type(s, 0))
            gb_print(dst, scene, l, gb_type(s, 0), 11, 10);
        if (gb_type(s, 1))
        {
            gb_print(dst, scene, l, "TYPE2/", 10, 11);
            gb_print(dst, scene, l, gb_type(s, 1), 11, 12);
        }
        gb_print(dst, scene, l, "<ID><No>/", 10, 13);
        gb_number(dst, scene, l, gb_int(s, "id"), 16, 14, 5);
        gb_print(dst, scene, l, "OT/", 10, 15);
        gb_print(dst, scene, l, gb_str(s, "ot"), 12, 16);

        // the stats box
        gb_box(dst, scene, l, 0, 8, 10, 10);
        static const char *LABELS[] = {"ATTACK", "DEFENSE", "SPEED", "SPECIAL"};
        static const char *KEYS[] = {"attack", "defense", "speed", "sp_atk"};
        for (int i = 0; i < 4; i++)
        {
            gb_print(dst, scene, l, LABELS[i], 1, 9 + i * 2);
            gb_number(dst, scene, l, gb_int(s, KEYS[i]), 8, 10 + i * 2, 0);
        }
        return;
    }

    // page 2: the species, experience and moves
    gb_print(dst, scene, l, gb_str(s, "species"), 9, 1);
    gb_print(dst, scene, l, "EXP POINTS", 9, 3);
    gb_number(dst, scene, l, gb_int(s, "exp_points"), 18, 4, 0);
    gb_print(dst, scene, l, "LEVEL UP", 9, 5);
    gb_number(dst, scene, l, gb_int(s, "next_lv"), 13, 6, 0);
    gb_image(dst, scene, l, "to.png", 14 * 8, 6 * 8);
    gb_next_level(dst, scene, l, s, 16, 6);
    gb_box(dst, scene, l, 0, 8, 20, 10);
    for (int i = 0; i < 4; i++)
    {
        JSON_Object *mv = gb_move(s, i);
        int row = 9 + i * 2;
        gb_print(dst, scene, l, gb_move_known(mv) ? gb_str(mv, "name") : "-", 2, row);
        if (!gb_move_known(mv))
        {
            gb_print(dst, scene, l, "--", 11, row + 1);
            continue;
        }
        gb_image(dst, scene, l, "bold_p.png", 11 * 8, (row + 1) * 8);
        gb_image(dst, scene, l, "bold_p.png", 12 * 8, (row + 1) * 8);
        gb_number(dst, scene, l, gb_int(mv, "pp"), 15, row + 1, 0);
        gb_print(dst, scene, l, "/", 16, row + 1);
        gb_number(dst, scene, l, gb_int(mv, "max_pp"), 18, row + 1, 0);
    }
}

// ------------------------------------------------------------------ Gen 2

static const char *GB_PAGES[] = {"pink", "green", "blue"};

static void gb_stats_gen2(SDL_Surface *dst, Scene *scene, const Layout *l, GbState *st, Slot *slot)
{
    JSON_Object *s = slot->summary;
    bool crystal = json_object_get_boolean(scene->layout, "crystal") == 1;
    bool egg = json_object_get_boolean(s, "egg") == 1;

    // the page's colour fills the lower half
    char key[48];
    snprintf(key, sizeof(key), "colors.pages.%s", GB_PAGES[st->page]);
    fill_logical(dst, l, 0, 64, GB_W, 80, get_color(scene->layout, key, gb_bg));

    // the upper half: picture (mirrored, except Unown), number, level, name, species, page squares
    gb_pic(dst, l, slot, 0, 0, gb_int(s, "species_id") != 201);
    if (egg)
    {
        // EggStatsScreen
        gb_print(dst, scene, l, "EGG", 8, 1);
        gb_print(dst, scene, l, "<ID><No>.", 8, 3);
        gb_print(dst, scene, l, "?????", 11, 3);
        gb_print(dst, scene, l, "OT/", 8, 5);
        gb_print(dst, scene, l, "?????", 11, 5);
        for (int x = 0; x < 20; x++)
            gb_image(dst, scene, l, "divider_h.png", x * 8, 7 * 8);
        gb_print(dst, scene, l, "It looks like this", 1, 9);
        gb_print(dst, scene, l, "EGG will take a", 1, 11);
        gb_print(dst, scene, l, "long time to hatch.", 1, 13);
        return;
    }
    gb_print(dst, scene, l, "<No>.", 8, 0);
    gb_number(dst, scene, l, gb_int(s, "dex_no"), 12, 0, 3);
    gb_level(dst, scene, l, gb_str(s, "level"), 14, 0);
    const char *gender = gb_str(s, "gender");
    if (strcmp(gender, "male") == 0)
        gb_print(dst, scene, l, "♂", 18, 0);
    else if (strcmp(gender, "female") == 0)
        gb_print(dst, scene, l, "♀", 18, 0);
    if (json_object_get_boolean(s, "shiny") == 1)
        gb_image(dst, scene, l, "shiny.png", 19 * 8, 0);
    gb_print(dst, scene, l, gb_str(s, "nickname"), 8, 2);
    gb_print(dst, scene, l, "/", 9, 4);
    gb_print(dst, scene, l, gb_str(s, "species"), 10, 4);
    gb_image(dst, scene, l, "arrow_page_left.png", 12 * 8, 6 * 8);
    gb_print(dst, scene, l, "▶", 19, 6);
    for (int i = 0; i < 3; i++)
    {
        char name[48];
        snprintf(name, sizeof(name), "page_%s_%s.png", i == st->page ? "large" : "small", GB_PAGES[i]);
        gb_image(dst, scene, l, name, (13 + i * 2) * 8, 5 * 8);
    }
    for (int x = 0; x < 20; x++)
        gb_image(dst, scene, l, "divider_h.png", x * 8, 7 * 8);

    if (st->page == 0)
    {
        // pink: HP, status, types, experience
        gb_image(dst, scene, l, "hp_label.png", 0, 9 * 8);
        gb_hp_bar(dst, scene, l, gb_int(s, "hp_cur"), gb_int(s, "hp_max"), 2, 9, 24);
        gb_image(dst, scene, l, "hp_cap.png", 8 * 8, 9 * 8);
        gb_number(dst, scene, l, gb_int(s, "hp_cur"), 3, 10, 0);
        gb_print(dst, scene, l, "/", 4, 10);
        gb_number(dst, scene, l, gb_int(s, "hp_max"), 7, 10, 0);
        gb_print(dst, scene, l, "STATUS/", 0, 12);
        gb_print(dst, scene, l, gb_str(s, "status")[0] ? gb_str(s, "status") : "OK", 6, 13);
        gb_print(dst, scene, l, "TYPE/", 0, 14);
        if (gb_type(s, 0))
            gb_print(dst, scene, l, gb_type(s, 0), 1, 15);
        if (gb_type(s, 1))
            gb_print(dst, scene, l, gb_type(s, 1), 1, 16);
        for (int y = 8; y <= 17; y++)
            gb_image(dst, scene, l, "divider_v.png", 9 * 8, y * 8);
        gb_print(dst, scene, l, "EXP POINTS", 10, 9);
        gb_number(dst, scene, l, gb_int(s, "exp_points"), 19, 10, 0);
        gb_print(dst, scene, l, "LEVEL UP", 10, 12);
        gb_number(dst, scene, l, gb_int(s, "next_lv"), 19, 13, 0);
        gb_print(dst, scene, l, "TO", 14, 14);
        gb_next_level(dst, scene, l, s, 17, 14);
        // the EXP bar fills from the right (FillInExpBar)
        gb_image(dst, scene, l, "exp_cap_left.png", 10 * 8, 16 * 8);
        double fill = json_object_get_number(s, "exp_fill");
        int px = (int)(fill * 64);
        for (int i = 0; i < 8; i++)
        {
            int part = px - i * 8;
            gb_tile_from(dst, scene, l, "exp.png", part >= 8 ? 8 : part > 0 ? part : 0, 18 - i, 16);
        }
        gb_image(dst, scene, l, "exp_cap_right.png", 19 * 8, 16 * 8);
    }
    else if (st->page == 1)
    {
        // green: item and moves
        gb_print(dst, scene, l, "ITEM", 0, 8);
        const char *item = gb_str(s, "item");
        gb_print(dst, scene, l, item[0] && strcmp(item, "NONE") != 0 ? item : "---", crystal ? 8 : 6, 8);
        gb_print(dst, scene, l, "MOVE", 0, 10);
        for (int i = 0; i < 4; i++)
        {
            JSON_Object *mv = gb_move(s, i);
            int row = 10 + i * 2;
            gb_print(dst, scene, l, gb_move_known(mv) ? gb_str(mv, "name") : "-", 8, row);
            if (!gb_move_known(mv))
            {
                gb_print(dst, scene, l, "--", 12, row + 1);
                continue;
            }
            gb_image(dst, scene, l, "bold_p.png", 12 * 8, (row + 1) * 8);
            gb_image(dst, scene, l, "bold_p.png", 13 * 8, (row + 1) * 8);
            gb_number(dst, scene, l, gb_int(mv, "pp"), 16, row + 1, 0);
            gb_print(dst, scene, l, "/", 17, row + 1);
            gb_number(dst, scene, l, gb_int(mv, "max_pp"), 19, row + 1, 0);
        }
    }
    else
    {
        // blue: ID, OT and stats
        gb_print(dst, scene, l, "<ID><No>.", 0, 9);
        gb_number(dst, scene, l, gb_int(s, "id"), 6, 10, 5);
        gb_print(dst, scene, l, "OT/", 0, 12);
        const char *ot = gb_str(s, "ot");
        int len = (int)gb_measure(ot, NULL);
        gb_print(dst, scene, l, ot, crystal || len <= 8 ? 2 : 10 - len, 13);
        if (crystal && json_object_get_boolean(s, "ot_gender_known") == 1)
            gb_print(dst, scene, l, json_object_get_boolean(s, "ot_female") == 1 ? "♀" : "♂", 9, 13);
        for (int y = 8; y <= 17; y++)
            gb_image(dst, scene, l, "divider_v.png", 10 * 8, y * 8);
        static const char *LABELS[] = {"ATTACK", "DEFENSE", "SPCL.ATK", "SPCL.DEF", "SPEED"};
        static const char *KEYS[] = {"attack", "defense", "sp_atk", "sp_def", "speed"};
        for (int i = 0; i < 5; i++)
        {
            gb_print(dst, scene, l, LABELS[i], 11, 8 + i * 2);
            gb_number(dst, scene, l, gb_int(s, KEYS[i]), 19, 9 + i * 2, 0);
        }
    }
}

static void gb_draw_stats(SDL_Surface *dst, Scene *scene, const Layout *l, GbState *st)
{
    Box *box = &scene->boxes[scene->box];
    if (st->cursor >= gb_count(box))
        return;
    Slot *slot = &box->slots[st->cursor];
    if (gb_gen == 1)
        gb_stats_gen1(dst, scene, l, st, slot);
    else
        gb_stats_gen2(dst, scene, l, st, slot);
}
