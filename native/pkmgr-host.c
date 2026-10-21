// pkmgr-host: one process that shows all of the pak's screens.
//
//   pkmgr-host        reads requests from stdin, one JSON object per line:
//                       {"program": "minui-list", "args": ["--file", "x.json", ...], "show_only": false}
//                     and answers each on stdout, one line: {"code": 0, "out": "...", "restart": false}
//
// Every screen used to be a program of its own (minui-list, minui-presenter, pkmgr-box), and each spent half a second
// starting up: loading its libraries, opening the display and the GPU, NextUI's fonts and theme. Here the same programs
// are built as libraries (<program>.so next to this one, their main renamed pkmgr_hosted_main) and run one after the
// other in this process, which opens the display, input and power management once: their GFX_init, PAD_init, PWR_init
// and InitSettings find them open, and their quits leave them so. Each library is loaded for its screen and unloaded
// after, so it starts with its variables as fresh as a new program's.
//
// "out" is what the program wrote to stdout and "code" what main returned. "show_only" (a "please wait" message) answers
// as soon as the screen is shown, leaving it up until the next. "restart" means this process got too big (the programs
// count on exiting to free their memory) and quits after answering; the pak starts a new one.
//
// The NextUI sources linked in here (and this file) are compiled with their GFX_init, GFX_quit, GFX_flip,
// PLAT_initInput, PLAT_quitInput, PWR_init and PWR_quit renamed REAL_* (HOST_RENAMES in native/Makefile), so the
// functions of those names below are the ones the programs call.

#define _GNU_SOURCE // RTLD_NEXT
#include <dlfcn.h>
#include <fcntl.h>
#include <getopt.h>
#include <limits.h>
#include <setjmp.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include <msettings.h>
#include "parson/parson.h"
#include "defines.h"
#include "api.h"

// compiled with the same renames as the NextUI sources, so api.h declared REAL_*; the names are this file's again
#undef GFX_init
#undef GFX_quit
#undef GFX_flip
#undef PLAT_initInput
#undef PLAT_quitInput
#undef PWR_init
#undef PWR_quit

#define MAX_GROWTH_MB 64 // what the programs may leave behind before this process starts over

static int hosting = 1;  // 0 once stdin closes: the quits are real again
static SDL_Surface *screen; // opened once
static int show_only;    // the screen being run answers at its first frame
static jmp_buf shown;    // ...back to run() from there

SDL_Surface *GFX_init(int mode)
{
    if (!screen)
        screen = REAL_GFX_init(mode);
    return screen;
}

void GFX_quit(void)
{
    if (!hosting)
        REAL_GFX_quit();
}

void GFX_flip(SDL_Surface *surface)
{
    REAL_GFX_flip(surface);
    if (show_only)
    {
        show_only = 0;
        longjmp(shown, 1);
    }
}

void PLAT_initInput(void)
{
    static int done;
    if (!done)
    {
        done = 1;
        REAL_PLAT_initInput();
    }
}

void PLAT_quitInput(void)
{
    if (!hosting)
        REAL_PLAT_quitInput();
}

void PWR_init(void)
{
    static int done;
    if (!done)
    {
        done = 1;
        REAL_PWR_init();
    }
}

void PWR_quit(void)
{
    if (!hosting)
        REAL_PWR_quit();
}

// libmsettings' own: the settings stay mapped for the power thread, whatever the programs do
static void call_next(const char *name)
{
    void (*next)(void) = (void (*)(void))dlsym(RTLD_NEXT, name);
    if (next)
        next();
}

void InitSettings(void)
{
    static int done;
    if (!done)
    {
        done = 1;
        call_next("InitSettings");
    }
}

void QuitSettings(void)
{
    if (!hosting)
        call_next("QuitSettings");
}

static char program_dir[PATH_MAX];
static int reply_fd;

static long rss_mb(void)
{
    long pages = 0, resident = 0;
    FILE *f = fopen("/proc/self/statm", "r");
    if (!f)
        return 0;
    if (fscanf(f, "%ld %ld", &pages, &resident) != 2)
        resident = 0;
    fclose(f);
    return resident * (sysconf(_SC_PAGESIZE) / 1024) / 1024;
}

// The text in a file, or "" (the program's stdout).
static char *read_all(int fd)
{
    off_t size = lseek(fd, 0, SEEK_END);
    char *text = malloc(size > 0 ? size + 1 : 1);
    if (!text)
        return NULL;
    lseek(fd, 0, SEEK_SET);
    ssize_t got = size > 0 ? read(fd, text, size) : 0;
    text[got > 0 ? got : 0] = '\0';
    return text;
}

// Runs one screen: <program>.so's main with these arguments. Returns its exit code (-1 when it can't be loaded) and
// what it wrote to stdout in *out.
static int run(const char *program, int argc, char **argv, int only_show, char **out)
{
    *out = NULL;
    char path[PATH_MAX];
    snprintf(path, sizeof(path), "%s/%s.so", program_dir, program);
    void *library = dlopen(path, RTLD_NOW | RTLD_LOCAL);
    if (!library)
    {
        fprintf(stderr, "pkmgr-host: %s\n", dlerror());
        return -1;
    }
    int (*hosted_main)(int, char **) = (int (*)(int, char **))dlsym(library, "pkmgr_hosted_main");
    if (!hosted_main)
    {
        fprintf(stderr, "pkmgr-host: %s has no pkmgr_hosted_main\n", path);
        dlclose(library);
        return -1;
    }

    // its stdout goes to a file, sent back with the answer
    char out_path[64];
    snprintf(out_path, sizeof(out_path), "/tmp/pkmgr-host-out-%d", (int)getpid());
    int out_fd = open(out_path, O_RDWR | O_CREAT | O_TRUNC, 0600);
    fflush(stdout);
    if (out_fd >= 0)
        dup2(out_fd, STDOUT_FILENO);

    // as a new program would start: options parsed from the top, no buttons held, the menu's CPU speed, autosleep on
    optind = 0;
    PAD_reset();
    PWR_setCPUSpeed(CPU_SPEED_MENU);
    PWR_enableAutosleep();

    int code = 0;
    show_only = only_show;
    if (setjmp(shown) == 0)
        code = hosted_main(argc, argv);
    show_only = 0;

    fflush(stdout);
    if (out_fd >= 0)
    {
        dup2(STDERR_FILENO, STDOUT_FILENO);
        *out = read_all(out_fd);
        close(out_fd);
        unlink(out_path);
    }
    // its signal handlers are about to be unloaded
    signal(SIGINT, SIG_DFL);
    signal(SIGTERM, SIG_DFL);
    dlclose(library);
    return code;
}

// One line from stdin, without its newline, or NULL at the end. Read with read(), not stdio, so a program reading its
// stdin never takes requests.
static char *read_request(void)
{
    static char *buffer;
    static size_t size, used;
    for (;;)
    {
        char *newline = used ? memchr(buffer, '\n', used) : NULL;
        if (newline)
        {
            size_t length = newline - buffer;
            char *line = malloc(length + 1);
            memcpy(line, buffer, length);
            line[length] = '\0';
            memmove(buffer, newline + 1, used - length - 1);
            used -= length + 1;
            return line;
        }
        if (used + 4096 > size)
        {
            size = size ? size * 2 : 65536;
            buffer = realloc(buffer, size);
        }
        ssize_t got = read(STDIN_FILENO, buffer + used, size - used);
        if (got <= 0)
            return NULL;
        used += got;
    }
}

static void reply(int code, const char *out, int restart)
{
    JSON_Value *value = json_value_init_object();
    JSON_Object *answer = json_value_get_object(value);
    json_object_set_number(answer, "code", code);
    json_object_set_string(answer, "out", out ? out : "");
    json_object_set_boolean(answer, "restart", restart);
    char *text = json_serialize_to_string(value);
    size_t length = strlen(text);
    text[length] = '\n'; // the terminator becomes the line's end...
    for (size_t sent = 0; sent <= length;)
    {
        ssize_t n = write(reply_fd, text + sent, length + 1 - sent);
        if (n <= 0)
            break;
        sent += n;
    }
    text[length] = '\0'; // ...and back, for json_free_serialized_string
    json_free_serialized_string(text);
    json_value_free(value);
}

int main(int argc, char *argv[])
{
    (void)argc;
    ssize_t n = readlink("/proc/self/exe", program_dir, sizeof(program_dir) - 1);
    program_dir[n > 0 ? n : 0] = '\0';
    char *slash = strrchr(program_dir, '/');
    if (slash)
        *slash = '\0';
    else
        snprintf(program_dir, sizeof(program_dir), "%s", ".");
    (void)argv;

    // answers go to the stdout the pak reads, and nothing else: NextUI's logging (stdout) goes to stderr, the log, and
    // a program's stdout to a file around each run
    reply_fd = dup(STDOUT_FILENO);
    dup2(STDERR_FILENO, STDOUT_FILENO);

    // opened now, while the pak works out its first screen
    PWR_setCPUSpeed(CPU_SPEED_MENU);
    GFX_init(MODE_MAIN);
    PLAT_initInput();
    PWR_init();
    InitSettings();

    long start_mb = rss_mb();
    char *line;
    while ((line = read_request()))
    {
        JSON_Value *value = json_parse_string(line);
        free(line);
        JSON_Object *request = json_value_get_object(value);
        const char *program = request ? json_object_get_string(request, "program") : NULL;
        JSON_Array *args = request ? json_object_get_array(request, "args") : NULL;
        if (!program || strchr(program, '/'))
        {
            reply(-1, "", 0);
            json_value_free(value);
            continue;
        }
        int count = args ? (int)json_array_get_count(args) : 0;
        char **program_argv = calloc(count + 2, sizeof(char *));
        program_argv[0] = (char *)program;
        for (int i = 0; i < count; i++)
        {
            const char *arg = json_array_get_string(args, i);
            program_argv[i + 1] = strdup(arg ? arg : "");
        }
        char *out = NULL;
        int code = run(program, count + 1, program_argv, json_object_get_boolean(request, "show_only") == 1, &out);
        for (int i = 1; i <= count; i++)
            free(program_argv[i]);
        free(program_argv);
        json_value_free(value);

        int restart = rss_mb() - start_mb > MAX_GROWTH_MB;
        reply(code, out, restart);
        free(out);
        if (restart)
            break;
    }

    // the pak is done (or this process too big): close everything for real
    hosting = 0;
    QuitSettings();
    PWR_quit();
    PLAT_quitInput();
    GFX_quit();
    return 0;
}
