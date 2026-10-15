// The Clock editor (included by pkmgr-box.c): pkmgr-box --clock WEEKDAY,HOUR,MINUTE [--font OG.ttf]
// [--background art.png] --write-location out.json. WEEKDAY is 0-6 (Sunday first) for Gold/Silver/Crystal or -1
// for Ruby/Sapphire/Emerald, which have no days of the week. Shows "Sunday 08:41 PM" (or "08:41 PM"):
// Left/Right pick the weekday, hour, minute or AM/PM, Up/Down change it, Y sets the device's own time (localtime, as
// NextUI's clock shows it), A saves, B goes back.
// Writes {"weekday": n, "hour": 0-23, "minute": n}; exit codes as the PC's (0 saved, 2 back, 3 MENU).

static const char *CLOCK_DAYS[] = {"Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"};

typedef struct
{
    int weekday; // -1: none
    int hour;    // 0-23
    int minute;
    int field;   // 0 weekday (when there is one), then hour, minute, AM/PM
} Clock;

static int clock_fields(const Clock *c)
{
    return c->weekday >= 0 ? 4 : 3;
}

// The field under the cursor, counting the weekday only when there is one: 0 weekday, 1 hour, 2 minute, 3 AM/PM.
static int clock_kind(const Clock *c)
{
    return c->weekday >= 0 ? c->field : c->field + 1;
}

static void clock_change(Clock *c, int delta)
{
    switch (clock_kind(c))
    {
    case 0:
        c->weekday = (c->weekday + delta + 7) % 7;
        break;
    case 1:
    {
        // the hour within its half of the day: 12, 1, ... 11
        int half = c->hour >= 12 ? 12 : 0;
        c->hour = half + (c->hour % 12 + delta + 12) % 12;
        break;
    }
    case 2:
        c->minute = (c->minute + delta + 60) % 60;
        break;
    default:
        c->hour = (c->hour + 12) % 24;
        break;
    }
}

static void clock_fill_round(SDL_Surface *dst, SDL_Rect r, Uint32 color)
{
    int radius = r.h / 2;
    SDL_FillRect(dst, &(SDL_Rect){r.x + radius, r.y, r.w - 2 * radius, r.h}, color);
    for (int y = 0; y < r.h; y++)
    {
        double dy = y + 0.5 - radius;
        int half = (int)sqrt(radius * radius - dy * dy > 0 ? radius * radius - dy * dy : 0);
        SDL_FillRect(dst, &(SDL_Rect){r.x + radius - half, r.y + y, half, 1}, color);
        SDL_FillRect(dst, &(SDL_Rect){r.x + r.w - radius, r.y + y, half, 1}, color);
    }
}

static void clock_triangle(SDL_Surface *dst, int cx, int y, int size, bool up, Uint32 color)
{
    for (int i = 0; i < size; i++)
    {
        int row = up ? y + i : y + size - 1 - i;
        SDL_FillRect(dst, &(SDL_Rect){cx - i, row, 2 * i + 1, 1}, color);
    }
}

static void clock_draw(SDL_Surface *screen, TTF_Font *big, TTF_Font *title, SDL_Surface *background, const Clock *c)
{
    SDL_FillRect(screen, NULL, SDL_MapRGB(screen->format, 0, 0, 0));
    if (background)
    {
        SDL_BlitScaled(background, NULL, screen, NULL);
        // dimmed like a menu over the game's art
        SDL_Surface *shade = SDL_CreateRGBSurfaceWithFormat(0, screen->w, screen->h, 32, SDL_PIXELFORMAT_RGBA8888);
        if (shade)
        {
            SDL_FillRect(shade, NULL, SDL_MapRGBA(shade->format, 0, 0, 0, 140));
            SDL_SetSurfaceBlendMode(shade, SDL_BLENDMODE_BLEND);
            SDL_BlitSurface(shade, NULL, screen, NULL);
            SDL_FreeSurface(shade);
        }
    }
    SDL_Color white = {255, 255, 255, 255}, dark = {0, 0, 0, 255};
    if (title)
    {
        SDL_Surface *t = TTF_RenderUTF8_Blended(title, "Clock", white);
        if (t)
        {
            SDL_BlitSurface(t, NULL, screen, &(SDL_Rect){SCALE1(PADDING) + SCALE1(10), SCALE1(PADDING) + (SCALE1(PILL_SIZE) - t->h) / 2, 0, 0});
            SDL_FreeSurface(t);
        }
    }
    if (big == NULL)
        return;

    // the fields: [Sunday] [08] : [41] [PM]
    char parts[5][16];
    int kinds[5], n = 0;
    if (c->weekday >= 0)
    {
        snprintf(parts[n], 16, "%s", CLOCK_DAYS[c->weekday]);
        kinds[n++] = 0;
    }
    int h12 = c->hour % 12 == 0 ? 12 : c->hour % 12;
    snprintf(parts[n], 16, "%02d", h12);
    kinds[n++] = 1;
    snprintf(parts[n], 16, ":");
    kinds[n++] = -1;
    snprintf(parts[n], 16, "%02d", c->minute);
    kinds[n++] = 2;
    snprintf(parts[n], 16, "%s", c->hour >= 12 ? "PM" : "AM");
    kinds[n++] = 3;

    int pad = SCALE1(8), gap = SCALE1(4), widths[5], total = 0, text_h = 0;
    for (int i = 0; i < n; i++)
    {
        int w, h;
        TTF_SizeUTF8(big, parts[i], &w, &h);
        widths[i] = w + (kinds[i] >= 0 ? 2 * pad : 0);
        total += widths[i] + (i && kinds[i] >= 0 && kinds[i - 1] >= 0 ? gap : 0);
        text_h = h > text_h ? h : text_h;
    }
    int x = (screen->w - total) / 2, pill_h = text_h + SCALE1(6), y = (screen->h - pill_h) / 2;
    int selected = clock_kind(c);
    for (int i = 0; i < n; i++)
    {
        bool on = kinds[i] == selected;
        if (on)
        {
            clock_fill_round(screen, (SDL_Rect){x, y, widths[i], pill_h}, SDL_MapRGB(screen->format, 255, 255, 255));
            int cx = x + widths[i] / 2, size = SCALE1(5);
            clock_triangle(screen, cx, y - SCALE1(4) - size, size, true, SDL_MapRGB(screen->format, 255, 255, 255));
            clock_triangle(screen, cx, y + pill_h + SCALE1(4), size, false, SDL_MapRGB(screen->format, 255, 255, 255));
        }
        SDL_Surface *t = TTF_RenderUTF8_Blended(big, parts[i], on ? dark : white);
        if (t)
        {
            SDL_BlitSurface(t, NULL, screen, &(SDL_Rect){x + (widths[i] - t->w) / 2, y + (pill_h - t->h) / 2, 0, 0});
            SDL_FreeSurface(t);
        }
        x += widths[i] + (i + 1 < n && kinds[i] >= 0 && kinds[i + 1] >= 0 ? gap : 0);
    }
    GFX_blitButtonGroup((char *[]){"Y", "SYSTEM TIME", NULL}, 0, screen, 0);
    GFX_blitButtonGroup((char *[]){"B", "BACK", "A", "SAVE", NULL}, 1, screen, 1);
}

// The device's time, as NextUI's clock shows it (the weekday only where the game has one).
static void clock_set_system(Clock *c)
{
    time_t now = time(NULL);
    struct tm tm;
    if (localtime_r(&now, &tm) == NULL)
        return;
    if (c->weekday >= 0)
        c->weekday = tm.tm_wday;
    c->hour = tm.tm_hour;
    c->minute = tm.tm_min;
}

static int clock_run(const char *spec, const char *font_path, const char *background_path, const char *write_location, const char *screenshot)
{
    Clock c = {-1, 12, 0, 0};
    sscanf(spec, "%d,%d,%d", &c.weekday, &c.hour, &c.minute);
    if (c.weekday > 6)
        c.weekday = -1;
    c.hour = ((c.hour % 24) + 24) % 24;
    c.minute = ((c.minute % 60) + 60) % 60;

    PWR_setCPUSpeed(CPU_SPEED_MENU);
    SDL_Surface *screen = GFX_init(MODE_MAIN);
    PAD_init();
    PWR_init();
    InitSettings();
    TTF_Font *big = font_path ? TTF_OpenFont(font_path, SCALE1(FONT_LARGE) * 2) : NULL;
    TTF_Font *title = font_path ? TTF_OpenFont(font_path, SCALE1(FONT_LARGE)) : NULL;
    if (big == NULL)
        big = font.large;
    if (title == NULL)
        title = font.large;
    SDL_Surface *background = background_path ? load_image(background_path) : NULL;

#ifdef DESKTOP
    if (screenshot)
    {
        clock_draw(screen, big, title, background, &c);
        IMG_SavePNG(screen, screenshot);
        return EXIT_PICKED;
    }
#else
    (void)screenshot;
#endif

    int exit_code = EXIT_BACK;
    bool quitting = false;
    int dirty = 1, show_setting = 0;
    while (!quitting)
    {
        GFX_startFrame();
        PWR_update(&dirty, &show_setting, NULL, NULL);
        PAD_poll();
        int fields = clock_fields(&c);
        if (PAD_justRepeated(BTN_LEFT))
            c.field = (c.field + fields - 1) % fields, dirty = 1;
        else if (PAD_justRepeated(BTN_RIGHT))
            c.field = (c.field + 1) % fields, dirty = 1;
        else if (PAD_justRepeated(BTN_UP))
            clock_change(&c, 1), dirty = 1;
        else if (PAD_justRepeated(BTN_DOWN))
            clock_change(&c, -1), dirty = 1;
        else if (PAD_justReleased(BTN_Y))
            clock_set_system(&c), dirty = 1;
        else if (PAD_justReleased(BTN_A))
        {
            exit_code = EXIT_PICKED;
            quitting = true;
        }
        else if (PAD_justReleased(BTN_B))
            quitting = true;
        if (dirty && !quitting)
        {
            clock_draw(screen, big, title, background, &c);
            GFX_flip(screen);
            dirty = 0;
        }
        else
            GFX_sync();
    }
    if (exit_code == EXIT_PICKED && write_location)
    {
        FILE *f = fopen(write_location, "w");
        if (f)
        {
            fprintf(f, "{\"weekday\": %d, \"hour\": %d, \"minute\": %d}\n", c.weekday, c.hour, c.minute);
            fclose(f);
        }
    }
    QuitSettings();
    PWR_quit();
    PAD_quit();
    GFX_quit();
    return exit_code;
}
