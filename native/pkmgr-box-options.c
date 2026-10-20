// --options options.json --write-location out.json: a settings list drawn like minarch's in-game options menu
// (ma_menu.c, MENU_VAR): the names on the left in font.small, the values on the right in font.tiny, the
// highlighted row on the theme's light pill with its name in the dark (white) pill, and the highlighted item's
// description centred at the bottom of the screen.
// options.json: {"items": [{"name", "desc", "options": [...], "selected"}...], "selected"}; rows without options
// are actions. Up/down pick, left/right change the value, A on an action row picks it (exit 0), B leaves (exit 2).
// Either way out.json gets {"selected": <row picked, -1 for B>, "values": [...]}.

#define OPTIONS_PADDING 8 // minarch's OPTION_PADDING
#define OPTIONS_MAX 32

#ifdef DESKTOP
#ifndef BUTTON_SIZE
#define BUTTON_SIZE 20
#endif
static void options_pill(SDL_Surface *dst, SDL_Rect r, SDL_Color c)
{
    Uint32 col = SDL_MapRGB(dst->format, c.r, c.g, c.b);
    int rad = r.h / 2;
    SDL_FillRect(dst, &(SDL_Rect){r.x + rad, r.y, r.w - 2 * rad, r.h}, col);
    for (int y = 0; y < r.h; y++)
    {
        int dy = y - rad, dx = 0;
        while ((dx + 1) * (dx + 1) + dy * dy <= rad * rad)
            dx++;
        SDL_FillRect(dst, &(SDL_Rect){r.x + rad - dx, r.y + y, dx, 1}, col);
        SDL_FillRect(dst, &(SDL_Rect){r.x + r.w - rad, r.y + y, dx, 1}, col);
    }
}
// the default theme's accent and white, as on the device
static void options_row_pill(SDL_Surface *dst, SDL_Rect r) { options_pill(dst, r, (SDL_Color){0x9b, 0x2c, 0x5c, 255}); }
static void options_name_pill(SDL_Surface *dst, SDL_Rect r) { options_pill(dst, r, (SDL_Color){255, 255, 255, 255}); }
static SDL_Color options_selected_text(void) { return (SDL_Color){0, 0, 0, 255}; }
static Uint32 options_backdrop(SDL_Surface *dst) { return SDL_MapRGB(dst->format, 0, 0, 0); }
#elif defined(PLATFORM_MINUI)
// minarch's own: a grey row with its name in white
static void options_row_pill(SDL_Surface *dst, SDL_Rect r) { GFX_blitPill(ASSET_OPTION, dst, &r); }
static void options_name_pill(SDL_Surface *dst, SDL_Rect r) { GFX_blitPill(ASSET_BUTTON, dst, &r); }
static SDL_Color options_selected_text(void) { return COLOR_BLACK; }
static Uint32 options_backdrop(SDL_Surface *dst) { return SDL_MapRGB(dst->format, 0, 0, 0); }
#else
static void options_row_pill(SDL_Surface *dst, SDL_Rect r) { GFX_blitPillLight(ASSET_BUTTON, dst, &r); }
static void options_name_pill(SDL_Surface *dst, SDL_Rect r) { GFX_blitPillDark(ASSET_BUTTON, dst, &r); }
static SDL_Color options_selected_text(void) { return uintToColour(THEME_COLOR5_255); }
static Uint32 options_backdrop(SDL_Surface *dst) { (void)dst; return THEME_COLOR7; }
#endif

typedef struct
{
    const char *name, *desc;
    JSON_Array *values;
    int value; // -1: an action row
} OptionsItem;

// A description's lines, centred, in font.tiny (minarch: GFX_blitText with a 12-pixel line height).
static void options_desc(SDL_Surface *screen, const char *desc)
{
    if (!desc || !font.tiny)
        return;
    char lines[6][256];
    int n = 0;
    for (const char *p = desc; *p && n < 6; n++)
    {
        const char *end = strchr(p, '\n');
        int len = end ? (int)(end - p) : (int)strlen(p);
        snprintf(lines[n], sizeof(lines[n]), "%.*s", len, p);
        p = end ? end + 1 : p + len;
    }
    int lh = SCALE1(12), y = screen->h - SCALE1(PADDING) - n * lh;
    for (int i = 0; i < n; i++)
    {
        SDL_Surface *t = lines[i][0] ? TTF_RenderUTF8_Blended(font.tiny, lines[i], (SDL_Color){255, 255, 255, 255}) : NULL;
        if (t)
        {
            SDL_BlitSurface(t, NULL, screen, &(SDL_Rect){(screen->w - t->w) / 2, y + i * lh, t->w, t->h});
            SDL_FreeSurface(t);
        }
    }
}

static int options_width(OptionsItem *items, int count, SDL_Surface *screen)
{
    int mw = 0;
    for (int i = 0; i < count; i++)
    {
        int lw = 0, w = 0;
        TTF_SizeUTF8(font.small, items[i].name, &lw, NULL);
        w = lw;
        for (size_t j = 0; items[i].values && j < json_array_get_count(items[i].values); j++)
        {
            int rw = 0;
            TTF_SizeUTF8(font.tiny, json_array_get_string(items[i].values, j), &rw, NULL);
            if (lw + rw > w)
                w = lw + rw;
        }
        w += SCALE1(OPTIONS_PADDING * 4);
        if (w > mw)
            mw = w;
    }
    return mw < screen->w - SCALE1(PADDING * 2) ? mw : screen->w - SCALE1(PADDING * 2);
}

static void options_draw(SDL_Surface *screen, OptionsItem *items, int count, int selected, int start, int visible, int show_setting)
{
    SDL_FillRect(screen, NULL, options_backdrop(screen));
#ifndef DESKTOP
    GFX_blitHardwareGroup(screen, show_setting);
#else
    (void)show_setting;
#endif
    int mw = options_width(items, count, screen);
    int ox = (screen->w - mw) / 2, oy = SCALE1(PADDING + PILL_SIZE);
    const char *desc = NULL;
    for (int i = start, j = 0; i < count && j < visible; i++, j++)
    {
        OptionsItem *item = &items[i];
        SDL_Color text_color = {255, 255, 255, 255};
        if (i == selected)
        {
            options_row_pill(screen, (SDL_Rect){ox, oy + SCALE1(j * BUTTON_SIZE), mw, SCALE1(BUTTON_SIZE)});
            int w = 0;
            TTF_SizeUTF8(font.small, item->name, &w, NULL);
            options_name_pill(screen, (SDL_Rect){ox, oy + SCALE1(j * BUTTON_SIZE), w + SCALE1(OPTIONS_PADDING * 2), SCALE1(BUTTON_SIZE)});
            text_color = options_selected_text();
            desc = item->desc;
        }
        SDL_Surface *t = TTF_RenderUTF8_Blended(font.small, item->name, text_color);
        if (t)
        {
            SDL_BlitSurface(t, NULL, screen, &(SDL_Rect){ox + SCALE1(OPTIONS_PADDING), oy + SCALE1(j * BUTTON_SIZE + 1), t->w, t->h});
            SDL_FreeSurface(t);
        }
        if (item->value >= 0 && item->values)
        {
            const char *v = json_array_get_string(item->values, (size_t)item->value);
            SDL_Surface *vt = v ? TTF_RenderUTF8_Blended(font.tiny, v, (SDL_Color){255, 255, 255, 255}) : NULL;
            if (vt)
            {
                SDL_BlitSurface(vt, NULL, screen, &(SDL_Rect){ox + mw - vt->w - SCALE1(OPTIONS_PADDING), oy + SCALE1(j * BUTTON_SIZE + 3), vt->w, vt->h});
                SDL_FreeSurface(vt);
            }
        }
    }
#ifndef DESKTOP
    // minarch's scroll arrows when there are more rows than fit
    if (count > visible)
    {
        int sx = (screen->w - SCALE1(24)) / 2, sy = SCALE1((PILL_SIZE - 4) / 2);
        if (start > 0)
            GFX_blitAsset(ASSET_SCROLL_UP, NULL, screen, &(SDL_Rect){sx, SCALE1(PADDING) + sy});
        if (start + visible < count)
            GFX_blitAsset(ASSET_SCROLL_DOWN, NULL, screen, &(SDL_Rect){sx, screen->h - SCALE1(PADDING + PILL_SIZE + BUTTON_SIZE) + sy});
    }
#endif
    options_desc(screen, desc);
}

static void options_write(const char *path, OptionsItem *items, int count, int picked)
{
    if (!path)
        return;
    JSON_Value *root = json_value_init_object();
    JSON_Object *o = json_value_get_object(root);
    json_object_set_number(o, "selected", picked);
    JSON_Value *values = json_value_init_array();
    for (int i = 0; i < count; i++)
        json_array_append_number(json_value_get_array(values), items[i].value < 0 ? 0 : items[i].value);
    json_object_set_value(o, "values", values);
    json_serialize_to_file(root, path);
    json_value_free(root);
}

static int options_run(const char *path, const char *write_location, const char *screenshot)
{
    JSON_Value *root = json_parse_file(path);
    JSON_Object *o = json_value_get_object(root);
    JSON_Array *list = o ? json_object_get_array(o, "items") : NULL;
    if (list == NULL)
    {
        fprintf(stderr, "could not read %s\n", path);
        return EXIT_ERROR;
    }
    OptionsItem items[OPTIONS_MAX];
    int count = (int)json_array_get_count(list);
    if (count > OPTIONS_MAX)
        count = OPTIONS_MAX;
    for (int i = 0; i < count; i++)
    {
        JSON_Object *it = json_array_get_object(list, (size_t)i);
        items[i].name = json_object_get_string(it, "name") ? json_object_get_string(it, "name") : "";
        items[i].desc = json_object_get_string(it, "desc");
        items[i].values = json_object_get_array(it, "options");
        items[i].value = items[i].values && json_array_get_count(items[i].values) > 0 ? (int)json_object_get_number(it, "selected") : -1;
    }
    int selected = json_object_has_value(o, "selected") ? (int)json_object_get_number(o, "selected") : 0;
    if (selected < 0 || selected >= count)
        selected = 0;

    PWR_setCPUSpeed(CPU_SPEED_MENU);
    SDL_Surface *screen = GFX_init(MODE_MAIN);
    PAD_init();
    PWR_init();
    InitSettings();
    // as many rows as fit between the top pill row and the description (minarch: 7 at 480 lines, 10 at 720)
    int visible = (screen->h - ((SCALE1(PADDING + PILL_SIZE) * 2) + SCALE1(BUTTON_SIZE))) / SCALE1(BUTTON_SIZE);
    if (visible < 1)
        visible = 1;
    int start = selected >= visible ? selected - visible + 1 : 0;
#ifdef DESKTOP
    if (screenshot)
    {
        options_draw(screen, items, count, selected, start, visible, 0);
        IMG_SavePNG(screen, screenshot);
        return EXIT_PICKED;
    }
#else
    (void)screenshot;
#endif
    int result = EXIT_BACK, picked = -1, dirty = 1, show_setting = 0;
    bool quitting = false;
    while (!quitting)
    {
        GFX_startFrame();
        PWR_update(&dirty, &show_setting, NULL, NULL);
        PAD_poll();
        if (PAD_justRepeated(BTN_UP) || PAD_justRepeated(BTN_DOWN))
        {
            selected = (selected + (PAD_justRepeated(BTN_UP) ? count - 1 : 1)) % count;
            if (selected < start)
                start = selected;
            else if (selected >= start + visible)
                start = selected - visible + 1;
            dirty = 1;
        }
        else if ((PAD_justRepeated(BTN_LEFT) || PAD_justRepeated(BTN_RIGHT)) && items[selected].value >= 0)
        {
            int n = (int)json_array_get_count(items[selected].values);
            items[selected].value = (items[selected].value + (PAD_justRepeated(BTN_LEFT) ? n - 1 : 1)) % n;
            dirty = 1;
        }
        else if (PAD_justReleased(BTN_A) && items[selected].value < 0)
        {
            result = EXIT_PICKED;
            picked = selected;
            quitting = true;
        }
        else if (PAD_justReleased(BTN_B))
        {
            result = EXIT_BACK;
            quitting = true;
        }
        if (dirty && !quitting)
        {
            options_draw(screen, items, count, selected, start, visible, show_setting);
            GFX_flip(screen);
            dirty = 0;
        }
        else
            GFX_sync();
    }
    options_write(write_location, items, count, picked);
    QuitSettings();
    PWR_quit();
    PAD_quit();
    GFX_quit();
    return result;
}
