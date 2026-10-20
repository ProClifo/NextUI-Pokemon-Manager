# Development

> Detailed reference. For a quick overview, see the [README](../README.md).

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download), `curl`, `zip`, `git`, Python 3 with Pillow, and Docker, which builds the patched minui-list and the PC box viewer.

```sh
scripts/build-pak.sh          # -> dist/PokemonManager.pak.zip and dist/PokemonManager-<platform>-sdcard.zip
dotnet test                   # run the test suite
scripts/build-native.sh h700  # just minui-list and the PC box viewer for one platform (Docker)
make -C native desktop        # desktop build of the viewer; --screenshot renders a frame to PNG
make -C native desktop-sdl12  # the same on SDL 1.2, as on MinUI's TrimUI Smart (RGB565 screen; screenshots are BMPs)
```

To refresh the ROM list from a newer No-Intro database, run `scripts/update-vanilla-roms.py <libretro-database>/metadat/no-intro`.

## Command line

The same binary has scriptable commands, which are handy for testing on a PC (`dotnet run --project src/PokemonManager -- ...`):

```
pkmgr [--sd <sdcard>] [--data <dir>] <command>
  ui                                   on-device menus (default)
  scan                                 list Pokémon saves under <sd>/Saves
  list <save>                          list Pokémon (slots: p:1 = party 1, 3:12 = box 3 slot 12)
  show <save> <slot>
  transfer <src> <slot> <dst> [--copy] [--unofficial]
  trade-evolve <save> <slot> [option] | trade-evolve <save> --all
  inject <save> <gift file> [--box]
  events <save> / redeem-event <save> <index>
  gen3-status <save>
```

`PKMGR_UI=console pkmgr ui` runs the full menu tree as a text UI in a terminal.

## How it works

- `src/PokemonManager` is a .NET 10 app using the [PKHeX.Core](https://www.nuget.org/packages/PKHeX.Core) library for save parsing, Pokémon conversion, legality checks, evolution data, Mystery Gift albums and the event database. It's published as one self-contained, partially trimmed, ReadyToRun `linux-arm64` executable (~65 MB). No .NET install is needed on the device, and it works with glibc 2.27 or newer.
- The UI uses josegonzalez's [`minui-list`](https://github.com/josegonzalez/minui-list) and [`minui-presenter`](https://github.com/josegonzalez/minui-presenter) (NextUI builds, so they follow your theme). They're driven from C#, so the runtime starts once per session. minui-list is built from source with a small patch (`native/minui-list.patch`): the released binaries crash when given a font, which picking NextUI's Next/OG font needs, and the patch adds an option to show the Legal/Illegal tag on the highlighted row only, and plays a row image's walking frames (`<image>.walk.png`) on the highlighted row.
- Gen 3 event injection isn't part of PKHeX itself. It follows the WC3 plugin's import procedure and goes through PKHeX's Gen 3 block accessors. Game-side details (flag IDs, CRC16, berry checksums, the Wonder Card save routine) were checked against the [pret](https://github.com/pret) decompilations.

## Where the art comes from

The art isn't stored in this repository. It's generated at build time from the pret decompilations: the wallpapers, cursor and background by `scripts/build-box-assets.py` from [pret/pokeemerald](https://github.com/pret/pokeemerald), each game's icons and sprites by `scripts/build-box-art.py` (Black/White and Black 2/White 2, which have no decompilation, from the ripped sheets in `assets/gen5`: front and shiny sprites from [The Spriters Resource](https://www.spriters-resource.com/), box icons ripped by Ploaj; the script reads each cell's dex number from its label), the Gen 3 PC and summary screens (art, window frames, wallpapers, text positions and colours, read from pokeruby, pokeemerald and pokefirered) by `scripts/pc_ui/build.py`, and the Game Boy PC and stats screens' tiles by `scripts/gb_ui/build.py`. Each game's art is packed into two sheets plus an index, so the SD card gets a few dozen files rather than thousands. The screen is drawn by `pkmgr-box`, a small C program in `native/` built against each device's NextUI platform layer, like `minui-list`. On MinUI's TrimUI Smart it's built against MinUI's, whose SDL is 1.2: `native/sdl12-compat.h` gives it the SDL2 surface calls the viewer uses (blend modes, scaled blits, colour mods), done in software with SDL2's blending, so it draws the same frames (`make desktop-sdl12` renders them on a PC to compare). If it's missing or fails, that save's Pokémon are shown as lists that time (the reason goes to the log) and the PC is tried again next time; **Settings → PC Box View** switches between the two.
