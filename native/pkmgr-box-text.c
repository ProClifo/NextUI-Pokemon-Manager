// --text text.json: a page of text to read, wrapped to the screen and scrolled with up/down (left/right a page
// at a time), A or B to close. text.json: {"title", "text"} ("\n" starts a new line, a blank line between
// paragraphs). The title sits at the top like the lists' titles, the button hint at the bottom right.

#define TEXT_MAX_LINES 512

typedef struct
{
    char *lines[TEXT_MAX_LINES];
    int count;
} TextLines;

// Splits the text into lines no wider than max_w in font.medium, breaking at spaces.
static void text_wrap(const char *text, int max_w, TextLines *out)
{
    out->count = 0;
    const char *p = text ? text : "";
    while (*p && out->count < TEXT_MAX_LINES)
    {
        const char *end = strchr(p, '\n');
        int len = end ? (int)(end - p) : (int)strlen(p);
        char para[2048];
        snprintf(para, sizeof(para), "%.*s", len, p);
        p = end ? end + 1 : p + len;
        if (para[0] == '\0')
        {
            out->lines[out->count++] = strdup("");
            continue;
        }
        char *s = para;
        while (*s && out->count < TEXT_MAX_LINES)
        {
            // the longest run of words that fits
            int best = 0, i = 0;
            while (1)
            {
                while (s[i] && s[i] != ' ')
                    i++;
                char saved = s[i];
                s[i] = '\0';
                int w = 0;
                TTF_SizeUTF8(font.medium, s, &w, NULL);
                s[i] = saved;
                if (w > max_w && best > 0)
                    break;
                best = i;
                if (!saved || w > max_w)
                    break;
                i++;
            }
            out->lines[out->count++] = strndup(s, (size_t)best);
            s += best;
            while (*s == ' ')
                s++;
        }
    }
}

static void text_draw(SDL_Surface *screen, const char *title, TextLines *lines, int top, int visible, int line_h, int show_setting)
{
    SDL_FillRect(screen, NULL, dialog_backdrop(screen));
#ifndef DESKTOP
    GFX_blitHardwareGroup(screen, show_setting);
#else
    (void)show_setting;
#endif
    if (title && font.medium)
    {
        SDL_Surface *t = TTF_RenderUTF8_Blended(font.medium, title, (SDL_Color){255, 255, 255, 255});
        if (t)
        {
            SDL_BlitSurface(t, NULL, screen, &(SDL_Rect){SCALE1(PADDING + BUTTON_PADDING), SCALE1(PADDING) + (SCALE1(PILL_SIZE) - t->h) / 2, t->w, t->h});
            SDL_FreeSurface(t);
        }
    }
    int y = SCALE1(PADDING + PILL_SIZE);
    for (int i = top; i < lines->count && i < top + visible; i++, y += line_h)
    {
        if (!lines->lines[i][0])
            continue;
        SDL_Surface *t = TTF_RenderUTF8_Blended(font.medium, lines->lines[i], (SDL_Color){255, 255, 255, 255});
        if (t)
        {
            SDL_BlitSurface(t, NULL, screen, &(SDL_Rect){SCALE1(PADDING + BUTTON_PADDING), y, t->w, t->h});
            SDL_FreeSurface(t);
        }
    }
#ifndef DESKTOP
    // NextUI's scroll arrows when there's more above or below
    int sx = (screen->w - SCALE1(24)) / 2;
    if (top > 0)
        GFX_blitAsset(ASSET_SCROLL_UP, NULL, screen, &(SDL_Rect){sx, SCALE1(PADDING) + SCALE1((PILL_SIZE - 4) / 2)});
    if (top + visible < lines->count)
        GFX_blitAsset(ASSET_SCROLL_DOWN, NULL, screen, &(SDL_Rect){sx, screen->h - SCALE1(PADDING + PILL_SIZE) + SCALE1((PILL_SIZE - 4) / 2)});
#endif
    GFX_blitButtonGroup((char *[]){"B", "BACK", NULL}, 1, screen, 1);
}

static int text_run(const char *path, const char *screenshot, const char *state_name)
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
    TextLines lines;
    text_wrap(json_object_get_string(o, "text"), screen->w - SCALE1((PADDING + BUTTON_PADDING) * 2), &lines);
    const char *title = json_object_get_string(o, "title");
    int line_h = font.medium ? TTF_FontLineSkip(font.medium) : SCALE1(16);
    // between the title row and the button hints
    int visible = (screen->h - SCALE1(PADDING + PILL_SIZE) * 2) / line_h;
    if (visible < 1)
        visible = 1;
    int max_top = lines.count > visible ? lines.count - visible : 0, top = 0;
#ifdef DESKTOP
    if (screenshot)
    {
        if (state_name && strcmp(state_name, "end") == 0)
            top = max_top;
        text_draw(screen, title, &lines, top, visible, line_h, 0);
        IMG_SavePNG(screen, screenshot);
        return EXIT_PICKED;
    }
#else
    (void)screenshot;
    (void)state_name;
#endif
    int dirty = 1, show_setting = 0;
    bool quitting = false;
    while (!quitting)
    {
        GFX_startFrame();
        PWR_update(&dirty, &show_setting, NULL, NULL);
        PAD_poll();
        int before = top;
        if (PAD_justRepeated(BTN_UP))
            top--;
        else if (PAD_justRepeated(BTN_DOWN))
            top++;
        else if (PAD_justRepeated(BTN_LEFT))
            top -= visible;
        else if (PAD_justRepeated(BTN_RIGHT))
            top += visible;
        else if (PAD_justReleased(BTN_A) || PAD_justReleased(BTN_B))
            quitting = true;
        if (top > max_top)
            top = max_top;
        if (top < 0)
            top = 0;
        if (top != before)
            dirty = 1;
        if (dirty && !quitting)
        {
            text_draw(screen, title, &lines, top, visible, line_h, show_setting);
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
    return EXIT_PICKED;
}
