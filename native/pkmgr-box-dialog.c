// --dialog dialog.json: a YES / NO question drawn like NextUI's menus, the two answers side by side in the middle
// of the screen. dialog.json: {"title", "icon", "icon_rect": [x, y, w, h], "message" ("\n" breaks lines),
// "background", "yes", "no", "selected": 0 YES / 1 NO}. Left/right pick, A answers, B is NO.
// Exit code 0 for YES, 2 for NO (EXIT_BACK), 1 when it couldn't run.
// With "answers": ["...", ...] (up to 4) they're listed one above the other instead, up/down to pick, "selected"
// the one highlighted first; A prints "answer=<index>" (on a line of its own: NextUI's libraries print to stdout
// too) and exits 0, B exits 2.

// The selection pill and the text colours: NextUI's theme on the device, MinUI's white pill and black text (its menus
// have no theme), white and black on a desktop.
#ifdef DESKTOP
static void dialog_pill(SDL_Surface *dst, SDL_Rect r, bool selected)
{
    if (!selected)
        return;
    Uint32 c = SDL_MapRGB(dst->format, 255, 255, 255);
    int rad = r.h / 2;
    SDL_FillRect(dst, &(SDL_Rect){r.x + rad, r.y, r.w - 2 * rad, r.h}, c);
    for (int y = 0; y < r.h; y++)
    {
        int dy = y - rad, dx = 0;
        while ((dx + 1) * (dx + 1) + dy * dy <= rad * rad)
            dx++;
        SDL_FillRect(dst, &(SDL_Rect){r.x + rad - dx, r.y + y, dx, 1}, c);
        SDL_FillRect(dst, &(SDL_Rect){r.x + r.w - rad, r.y + y, dx, 1}, c);
    }
}
static SDL_Color dialog_text(bool selected) { return selected ? (SDL_Color){0, 0, 0, 255} : (SDL_Color){255, 255, 255, 255}; }
static Uint32 dialog_backdrop(SDL_Surface *dst) { return SDL_MapRGB(dst->format, 0, 0, 0); }
#elif defined(PLATFORM_MINUI)
static void dialog_pill(SDL_Surface *dst, SDL_Rect r, bool selected)
{
    if (selected)
        GFX_blitPill(ASSET_WHITE_PILL, dst, &r);
}
static SDL_Color dialog_text(bool selected) { return selected ? COLOR_BLACK : COLOR_WHITE; }
static Uint32 dialog_backdrop(SDL_Surface *dst) { return SDL_MapRGB(dst->format, 0, 0, 0); }
#else
static void dialog_pill(SDL_Surface *dst, SDL_Rect r, bool selected)
{
    if (selected)
        GFX_blitPillColor(ASSET_WHITE_PILL, dst, &r, THEME_COLOR1, RGB_WHITE);
}
static SDL_Color dialog_text(bool selected) { return uintToColour(selected ? THEME_COLOR5_255 : THEME_COLOR4_255); }
static Uint32 dialog_backdrop(SDL_Surface *dst) { (void)dst; return THEME_COLOR7; }
#endif

static SDL_Surface *dialog_icon(const char *path, JSON_Array *rect, int max_h)
{
    SDL_Surface *loaded = path ? IMG_Load(path) : NULL;
    if (loaded == NULL || rect == NULL)
        return loaded;
    SDL_Surface *rgba = SDL_ConvertSurfaceFormat(loaded, SDL_PIXELFORMAT_RGBA32, 0);
    SDL_FreeSurface(loaded);
    if (rgba == NULL)
        return NULL;
    int rx = (int)json_array_get_number(rect, 0), ry = (int)json_array_get_number(rect, 1);
    int rw = (int)json_array_get_number(rect, 2), rh = (int)json_array_get_number(rect, 3);
    // cropped to its visible pixels, then a whole number of times bigger, up to the title's height
    int x0 = rx + rw, y0 = ry + rh, x1 = rx - 1, y1 = ry - 1;
    for (int y = ry > 0 ? ry : 0; y < ry + rh && y < rgba->h; y++)
        for (int x = rx > 0 ? rx : 0; x < rx + rw && x < rgba->w; x++)
            if (((Uint8 *)rgba->pixels)[y * rgba->pitch + x * 4 + 3])
            {
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                if (y > y1) y1 = y;
            }
    if (x1 < x0)
    {
        x0 = rx, y0 = ry, x1 = rx + rw - 1, y1 = ry + rh - 1;
    }
    int w = x1 - x0 + 1, h = y1 - y0 + 1, k = max_h / h > 0 ? max_h / h : 1;
    SDL_Surface *out = SDL_CreateRGBSurfaceWithFormat(0, w * k, h * k, 32, SDL_PIXELFORMAT_RGBA32);
    if (out)
    {
        SDL_SetSurfaceBlendMode(rgba, SDL_BLENDMODE_NONE);
        SDL_BlitScaled(rgba, &(SDL_Rect){x0, y0, w, h}, out, NULL);
        SDL_SetSurfaceBlendMode(out, SDL_BLENDMODE_BLEND);
    }
    SDL_FreeSurface(rgba);
    return out;
}

#define DIALOG_MAX_ANSWERS 4

// The answers: "answers" when given (stacked), else YES and NO (side by side). Returns how many.
static int dialog_answers(JSON_Object *o, const char *labels[DIALOG_MAX_ANSWERS], bool *stacked)
{
    JSON_Array *a = json_object_get_array(o, "answers");
    int n = a ? (int)json_array_get_count(a) : 0;
    if (n > DIALOG_MAX_ANSWERS)
        n = DIALOG_MAX_ANSWERS;
    *stacked = n > 0;
    for (int i = 0; i < n; i++)
        labels[i] = json_array_get_string(a, (size_t)i) ? json_array_get_string(a, (size_t)i) : "";
    if (n > 0)
        return n;
    labels[0] = json_object_get_string(o, "yes") ? json_object_get_string(o, "yes") : "YES";
    labels[1] = json_object_get_string(o, "no") ? json_object_get_string(o, "no") : "NO";
    return 2;
}

static void dialog_answer(SDL_Surface *screen, const char *label, SDL_Rect r, bool selected)
{
    dialog_pill(screen, r, selected);
    SDL_Surface *text = font.large ? TTF_RenderUTF8_Blended(font.large, label, dialog_text(selected)) : NULL;
    if (text)
    {
        SDL_BlitSurface(text, NULL, screen, &(SDL_Rect){r.x + (r.w - text->w) / 2, r.y + (r.h - text->h) / 2, text->w, text->h});
        SDL_FreeSurface(text);
    }
}

static void dialog_draw(SDL_Surface *screen, JSON_Object *o, SDL_Surface *bg, SDL_Surface *icon, int selected)
{
    SDL_FillRect(screen, NULL, dialog_backdrop(screen));
    if (bg)
    {
        // filling the screen, keeping its shape
        double s = (double)screen->w / bg->w > (double)screen->h / bg->h ? (double)screen->w / bg->w : (double)screen->h / bg->h;
        int w = (int)(bg->w * s), h = (int)(bg->h * s);
        SDL_BlitScaled(bg, NULL, screen, &(SDL_Rect){(screen->w - w) / 2, (screen->h - h) / 2, w, h});
    }
    // the title, in a black pill like the lists' over a background
    const char *title = json_object_get_string(o, "title");
    if (title && font.medium)
    {
        // white on a black pill over a background, else in the list text colour, as minui-list draws titles
        SDL_Surface *text = TTF_RenderUTF8_Blended(font.medium, title, bg ? (SDL_Color){255, 255, 255, 255} : dialog_text(false));
        int iw = icon ? icon->w + SCALE1(BUTTON_PADDING) / 2 : 0;
        int pw = SCALE1(BUTTON_PADDING * 2) + iw + (text ? text->w : 0);
        SDL_Rect pill = {SCALE1(PADDING), SCALE1(PADDING), pw, SCALE1(PILL_SIZE)};
        if (bg)
        {
#ifdef DESKTOP
            SDL_FillRect(screen, &pill, SDL_MapRGB(screen->format, 0, 0, 0));
#else
            GFX_blitPill(ASSET_BLACK_PILL, screen, &pill);
#endif
        }
        int x = pill.x + SCALE1(BUTTON_PADDING);
        if (icon)
        {
            SDL_BlitSurface(icon, NULL, screen, &(SDL_Rect){x, pill.y + (pill.h - icon->h) / 2, icon->w, icon->h});
            x += iw;
        }
        if (text)
        {
            SDL_BlitSurface(text, NULL, screen, &(SDL_Rect){x, pill.y + (pill.h - text->h) / 2, text->w, text->h});
            SDL_FreeSurface(text);
        }
    }
    // the message's lines, centred above the answers
    const char *message = json_object_get_string(o, "message");
    int line_h = font.large ? TTF_FontHeight(font.large) : SCALE1(16);
    int lines = 1;
    for (const char *p = message ? message : ""; *p; p++)
        lines += *p == '\n';
    const char *labels[DIALOG_MAX_ANSWERS];
    bool stacked;
    int count = dialog_answers(o, labels, &stacked);
    // stacked answers sit pill against pill, like a list's rows, and closer under the message
    int answers_h = SCALE1(PILL_SIZE), step = answers_h, gap_above = SCALE1(stacked ? PADDING : PADDING * 2);
    int block = lines * line_h + gap_above + (stacked ? count * step : answers_h);
    // centred between the title and the button hints
    int top = SCALE1(PADDING + PILL_SIZE), bottom = screen->h - SCALE1(PADDING + PILL_SIZE);
    int y = top + (bottom - top - block) / 2;
    if (message && font.large)
    {
        char line[256];
        const char *p = message;
        for (int i = 0; i < lines; i++)
        {
            const char *end = strchr(p, '\n');
            int len = end ? (int)(end - p) : (int)strlen(p);
            snprintf(line, sizeof(line), "%.*s", len, p);
            SDL_Surface *text = line[0] ? TTF_RenderUTF8_Blended(font.large, line, dialog_text(false)) : NULL;
            if (text)
            {
                SDL_BlitSurface(text, NULL, screen, &(SDL_Rect){(screen->w - text->w) / 2, y + i * line_h, text->w, text->h});
                SDL_FreeSurface(text);
            }
            p = end ? end + 1 : p + len;
        }
    }
    y += lines * line_h + gap_above;
    int widths[DIALOG_MAX_ANSWERS] = {0}, gap = SCALE1(PADDING * 2);
    for (int i = 0; i < count; i++)
    {
        int tw = 0;
        if (font.large)
            TTF_SizeUTF8(font.large, labels[i], &tw, NULL);
        widths[i] = tw + SCALE1(BUTTON_PADDING * 4);
    }
    if (stacked)
    {
        // one above the other, each centred
        for (int i = 0; i < count; i++)
            dialog_answer(screen, labels[i], (SDL_Rect){(screen->w - widths[i]) / 2, y + i * step, widths[i], answers_h}, i == selected);
    }
    else
    {
        // YES and NO side by side, the chosen one in the selection pill
        int x = (screen->w - widths[0] - gap - widths[1]) / 2;
        for (int i = 0; i < 2; i++)
        {
            dialog_answer(screen, labels[i], (SDL_Rect){x, y, widths[i], answers_h}, i == selected);
            x += widths[i] + gap;
        }
    }
    GFX_blitButtonGroup((char *[]){"B", "BACK", "A", "SELECT", NULL}, 1, screen, 1);
}

static int dialog_run(const char *path, const char *screenshot, const char *state_name)
{
    JSON_Value *root = json_parse_file(path);
    JSON_Object *o = json_value_get_object(root);
    if (o == NULL)
    {
        fprintf(stderr, "could not read %s\n", path);
        return EXIT_ERROR;
    }
    PWR_setCPUSpeed(CPU_SPEED_MENU);
    SDL_Surface *screen = GFX_init(MODE_MAIN);
    PAD_init();
    PWR_init();
    InitSettings();
    SDL_Surface *bg = json_object_get_string(o, "background") ? IMG_Load(json_object_get_string(o, "background")) : NULL;
    SDL_Surface *icon = dialog_icon(json_object_get_string(o, "icon"), json_object_get_array(o, "icon_rect"), SCALE1(PILL_SIZE - 4));
    const char *labels[DIALOG_MAX_ANSWERS];
    bool stacked;
    int count = dialog_answers(o, labels, &stacked);
    int selected = json_object_has_value(o, "selected") ? (int)json_object_get_number(o, "selected") : count - 1;
    if (selected < 0 || selected >= count)
        selected = count - 1;
#ifdef DESKTOP
    if (screenshot)
    {
        if (state_name && strcmp(state_name, "yes") == 0)
            selected = 0;
        else if (state_name && state_name[0] >= '0' && state_name[0] < '0' + count)
            selected = state_name[0] - '0';
        dialog_draw(screen, o, bg, icon, selected);
        IMG_SavePNG(screen, screenshot);
        return EXIT_PICKED;
    }
#else
    (void)screenshot;
    (void)state_name;
#endif
    int result = EXIT_BACK, dirty = 1, show_setting = 0;
    bool quitting = false;
    while (!quitting)
    {
        GFX_startFrame();
        PWR_update(&dirty, &show_setting, NULL, NULL);
        PAD_poll();
        if (!stacked && (PAD_justRepeated(BTN_LEFT) || PAD_justRepeated(BTN_RIGHT)))
        {
            selected = 1 - selected;
            dirty = 1;
        }
        else if (stacked && (PAD_justRepeated(BTN_UP) || PAD_justRepeated(BTN_DOWN)))
        {
            selected = (selected + (PAD_justRepeated(BTN_UP) ? count - 1 : 1)) % count;
            dirty = 1;
        }
        else if (PAD_justReleased(BTN_A))
        {
            if (stacked)
            {
                printf("\nanswer=%d\n", selected);
                fflush(stdout);
                result = EXIT_PICKED;
            }
            else
                result = selected == 0 ? EXIT_PICKED : EXIT_BACK;
            quitting = true;
        }
        else if (PAD_justReleased(BTN_B))
        {
            result = EXIT_BACK;
            quitting = true;
        }
        if (dirty && !quitting)
        {
            dialog_draw(screen, o, bg, icon, selected);
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
    return result;
}
