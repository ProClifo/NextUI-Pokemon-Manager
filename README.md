# Pokémon Manager for NextUI

A NextUI tool pak, built on [PKHeX](https://github.com/kwsch/PKHeX), for editing Pokémon save files on your handheld:

- **Transfer Pokémon between games.** Move or copy a Pokémon from one save to another. PKHeX converts the data between generations (Gen 1↔2, Gen 1/2→7, 3→4→5→6→7→8...). Moving between games of the same era counts as a trade, so trade evolutions trigger.
- **Trade evolutions.** Evolve Kadabra, Machoke, Graveler, Haunter, Onix + Metal Coat, Scyther + Metal Coat, Seadra + Dragon Scale, Clamperl, Boldore and the rest without a second console. You can evolve one Pokémon or every Pokémon that's ready.
- **Mystery Gifts, Mystery Events and e-Reader cards.**
  - Gen 3: Wonder Cards (`.wc3`), Wonder News (`.wn3`), Mystery Events (`.me3`, e.g. the Eon Ticket e-Card), e-Card Trainers (`.ect`) and e-Reader Berries (`.ecb`). These are the formats of the PKHeX *WC3 plugin* and suloku's *Gen III Mystery Gift Tool*, and injection produces the same save data as WC3 plugin 2.6.0 (see below).
  - Gen 4–7: Wonder Cards are placed in the in-game Mystery Gift album so you pick them up from the delivery person, as if you'd downloaded them (`.pgt .pcd .wc4 .pgf .wc6 .wc7 .wb7`...).
  - Any generation: a gift Pokémon can be sent straight to a PC box (`.wc8 .wb8 .wa8 .wc9 .wa9` included).
- **Event gallery built in.** The Gen 1–5 files of Project Pokémon's [EventsGallery](https://github.com/projectpokemon/EventsGallery) ship with the pak, filtered to the game and language of each save:
  - **Events**: key items that unlock in-game events, e.g. Aurora Ticket, Mystic Ticket, Eon Ticket, Old Sea Map, Member Card, Oak's Letter, Secret Key, Enigma Stone, Liberty Pass.
  - **Distributions**: notable Pokémon giveaways (mythical and legendary Pokémon, e.g. WISHMKR Jirachi, Aura Mew, Movie Darkrai), each listed once.
  - **Gallery**: every file for the game, in the gallery's own folders.
  - Crystal gets **Enable GS Ball Event** instead of Events.
- **Import/export** PKHeX Pokémon files (`.pk1`–`.pk9`...).
- **Automatic backups** before every change, and a *Restore a backup* option.

> **Status:** early release. The code is covered by automated tests on generated saves, and the ARM64 build has been run under emulation. It has **not yet been tested on real hardware**. Keep your own backup of any save you care about until it has been.

## Installation

### From a release

1. Download the SD-card zip for your device from the [Releases](../../releases) page:

   | Device | Zip |
   | --- | --- |
   | TrimUI Brick, TrimUI Smart Pro | `PokemonManager-tg5040-sdcard.zip` |
   | TrimUI Smart Pro S | `PokemonManager-tg5050-sdcard.zip` |
   | Miyoo Flip | `PokemonManager-my355-sdcard.zip` |
   | Anbernic H700 devices (RG35XX Plus/H/SP/2024, RG40XX, RG CubeXX, RG34XX...) | `PokemonManager-h700-sdcard.zip` |

2. Unzip it onto the root of your SD card. You get `Tools/<platform>/Pokemon Manager.pak` and a `PokemonManager` folder.

`PokemonManager.pak.zip` holds the same pak with its contents at the zip root, for Pak Store–style installers.

### Building it yourself

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download), `curl`, `zip`, `git`, Python 3 with Pillow, and Docker for the PC box viewer. Without Docker the pak still builds and shows Pokémon as lists.

```sh
scripts/build-pak.sh          # -> dist/PokemonManager.pak.zip and dist/PokemonManager-<platform>-sdcard.zip
dotnet test                   # run the test suite
scripts/build-native.sh h700  # just the PC box viewer for one platform (Docker)
make -C native desktop        # desktop build of the viewer; --screenshot renders a frame to PNG
```

## Using it

Open **Tools → Pokemon Manager**. The app scans `Saves/` for anything PKHeX recognizes whose ROM is official (see below) and lists it as *Game - Trainer (file)*. Pick a save, then:

### Official ROMs only (default)

By default the app only lists saves made with **unmodified, official Pokémon ROMs**. ROM hacks, translations and other modified ROMs are hidden. Hack saves often look like normal FireRed/Emerald saves to PKHeX, and editing them as if they were can corrupt them.

- A save is matched to its ROM by name, the way NextUI names saves: `Roms/.../Pokemon Emerald.gba` ↔ `Saves/GBA/Pokemon Emerald.gba.sav`, `Pokemon Emerald.sav` or `Pokemon Emerald.srm`. Zipped ROMs work too.
- The ROM's CRC32 is compared with the 175 retail dumps of the main-series games (Red/Green/Blue/Yellow through Black 2/White 2, all regions and revisions) from the No-Intro database. Zipped ROMs are checked without unzipping, and trimmed DS ROMs are recognised.
- Each ROM is hashed once and the result is cached in `PokemonManager/rom-check-cache.txt`, so only the first scan is slow.
- Hidden saves appear as **[N hidden: not official ROMs]** in the main menu. Select it to see each file and the reason.
- To manage other saves anyway, turn off **Settings → Official ROMs only**, or copy the save into `PokemonManager/Saves`. Saves there have no ROM on the card and are always shown.

To refresh the ROM list from a newer No-Intro database, run `scripts/update-vanilla-roms.py <libretro-database>/metadat/no-intro`.

### PC box view

**Pokémon (view / transfer / evolve)** opens a PC box screen styled after Pokémon Emerald:

- Each box shows its own wallpaper from the save (Gen 3 saves). Other games cycle through the 16 Emerald wallpapers.
- Pokémon appear as their Gen 3 box icons, including Unown letters and eggs. Pokémon newer than Gen 3 show a "?" icon.
- The panel on the left shows the Pokémon under the cursor: front sprite (shiny palette for shinies), name, level, gender, held item and OT.
- **D-pad** moves the hand cursor, **L/R** switch boxes (the first "box" is your party), **A** opens the Pokémon's actions, **B** goes back.

The art isn't stored in this repository. `scripts/build-box-assets.py` generates it at build time from the [pret/pokeemerald](https://github.com/pret/pokeemerald) decompilation. The screen is drawn by `pkmgr-box`, a small C program in `native/` built against each device's NextUI platform layer, like `minui-list`. If it's missing or fails, the app falls back to lists; **Settings → PC box view** switches between the two.

### Game backgrounds

A save's menu shows art for its game: Ruby, Sapphire, Emerald, FireRed or LeafGreen. The game is read from the ROM's header (the GBA game code, e.g. `BPRE` = FireRed USA, `BPGP` = LeafGreen Europe), so renamed or zipped ROMs still match; if no ROM is found it falls back to what the save says and then to the file name. The art lives in `assets/backgrounds/<game>.png` (240×160) and `scripts/build-backgrounds.py` pre-scales it with whole-number scaling for every NextUI screen size, so it fills the screen pixel-sharp.

| Menu | What it does |
| --- | --- |
| Pokémon | Browse the party and boxes. For each Pokémon: view summary, move or copy to another game, trade evolve, export to file. |
| Events (Gen 3–5) | Key items for in-game events, for this game and language. Gen 3 also shows the current Mystery Gift/Event status. |
| Enable GS Ball Event (Crystal) | Turns on the GS Ball event, as the 3DS Virtual Console release did. |
| Distributions | Notable Pokémon giveaways for this game and language. Copies of one distribution that differ only in PID/IVs (hundreds of MYSTRY Mew, for example) are listed once. |
| Gallery | Every gallery file for this game and language, in the gallery's folders. |
| More | Trade evolutions (incl. *Evolve all that are ready*), import a `.pk*` file from `PokemonManager/Import`, gift files from `PokemonManager/Gifts`, restore a backup, save info. |

Picking a gift: Pokémon files go to the first free PC slot. Gen 4/5 Wonder Cards can go to the in-game Mystery Gift album (pick them up from the delivery person) or, for Pokémon, straight to the PC. Gen 3 cards are injected as described under *Gen 3 events* below.

### Event gallery

The gallery is [projectpokemon/EventsGallery](https://github.com/projectpokemon/EventsGallery) at a pinned commit, Gen 1–5 only (the generations NextUI emulates). The build bundles the files PKHeX can read into `res/gallery.zip` with an index, so the SD card holds one 3 MB file instead of 7,500 small ones.

- **Game**: from the ROM header, e.g. `BPEE` = Emerald, `CPUE` = Platinum, `IRBO` = Black 2; otherwise from the save. Cards only show for the games named in their file name (a HeartGold/SoulSilver card doesn't show for Platinum). Pokémon files show for every game of their generation, since they can be traded between them.
- **Language**: from the ROM header's region letter (`E`/`P` English, `D` German, `F` French, `I` Italian, `S` Spanish, `J` Japanese, `K` Korean); otherwise from the save (Gen 4/5) or the player's own Pokémon (Gen 3). Gen 1–2 only distinguish Japanese, Korean and international.
- **Settings → Show all languages in gallery** (off by default) lists every language in the Gallery. Events and Distributions always stick to the game's language.
- **Settings → Show unreleased files in gallery** (off by default) adds the gallery's *Unreleased* folder: debug and test data that was never distributed.

### ⚠️ Save states

Close the game before editing. Afterwards, **start the game fresh from the menu, not from a save state or NextUI's auto-resume**. A save state holds the old save data in memory, and the next in-game save writes it back over your changes.

### SD card folders

```
SDCARD/
├── Saves/                     ← NextUI's saves; scanned automatically
└── PokemonManager/
    ├── Gifts/                 ← put .wc3 .wn3 .me3 .ect .ecb .pgt .pcd .pgf .wc6 .wc7 .wc8 .wc9 ... here
    ├── Import/                ← .pk1 – .pk9 files to import
    ├── Export/                ← exported Pokémon land here (one folder per save)
    ├── Backups/               ← automatic backups (last 20 per save)
    ├── Saves/                 ← optional: extra saves to manage (always shown, even with "Official ROMs only")
    └── rom-check-cache.txt    ← cached ROM checks
```

Gift and event files are widely shared by the community, for example in Project Pokémon's event gallery. Pokémon Manager doesn't ship any.

### Transfers

By default only routes the real games supported are allowed. If PKHeX reports "no transfer route", the menu says so. **Settings → Unofficial transfers** lets PKHeX force a conversion (e.g. Gen 4 → Gen 3), but those Pokémon will usually be flagged as illegal.

Rules the app enforces:

- A Pokémon only moves into an empty PC slot. Nothing is overwritten.
- The destination save is written first, then the source save. If the second write fails, the Pokémon ends up in both saves instead of being lost.
- You can't move your last party Pokémon.
- Japanese and international Gen 1/2 games can't trade with each other, as on real hardware.

### Gen 3 events: what goes where

| File | Games | Where to collect it |
| --- | --- | --- |
| `.wc3` Wonder Card (Aurora Ticket, Mystic Ticket, ...) | FireRed, LeafGreen, Emerald | Delivery man in green, 2nd floor of any Pokémon Center |
| `.wn3` Wonder News | FireRed, LeafGreen, Emerald | Mystery Gift → Wonder News on the title menu |
| `.me3` Mystery Event (Eon Ticket e-Card, ...), 1004 or 1012 bytes | Ruby, Sapphire, Emerald | As the original event (Eon Ticket: your dad at the Petalburg Gym). 1012-byte files also set the Record Mixing item. |
| `.ect` e-Card Trainer | Ruby, Sapphire, Emerald, FireRed, LeafGreen | The game's e-Reader trainer battle |
| `.ecb` e-Reader Berry: 1328 bytes for R/S, 52 bytes for FR/LG/E | All five | Replaces the Enigma Berry data |

Notes:

- Wonder Cards and Mystery Events share one script slot, so injecting one replaces the other. That's also how the games behave.
- Japanese and international Wonder Cards/News have different sizes and only work in a save of the matching region.
- Injecting a Wonder Card or Wonder News also unlocks the Mystery Gift menu. Injecting a Mystery Event into Ruby/Sapphire or Japanese Emerald unlocks Mystery Events.
- **Non-Japanese Emerald** has no Mystery Event menu, and setting its Mystery Event flag corrupts the save. Pokémon Manager never sets that flag. It writes the event script and warns that the event may not trigger.
- Like the WC3 plugin, stale checksums in a file (e.g. after hand-editing) are recalculated, and the result message says so. Files for the wrong game or region, and Wonder Cards without an event flag, are refused.
- Injecting a Mystery Event into Emerald clears any Wonder Card, because the card would point at the replaced script.

**Compared with WC3 plugin 2.6.0:** a test harness runs the plugin's decompiled import code next to Pokémon Manager on the same files. The saves come out byte-identical except for two deliberate differences. Pokémon Manager also unlocks the Mystery Gift/Event menu. For normal Wonder Cards it resets the card's stats block and copies in the icon, as the game's own `SaveWonderCard()` does. It also writes the full 32-bit checksum for Ruby/Sapphire scripts and for berries, where the plugin writes only the low 16 bits.

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
- The UI uses josegonzalez's [`minui-list`](https://github.com/josegonzalez/minui-list) and [`minui-presenter`](https://github.com/josegonzalez/minui-presenter) (NextUI builds, so they follow your theme). They're driven from C#, so the runtime starts once per session.
- Gen 3 event injection isn't part of PKHeX itself. It follows the WC3 plugin's import procedure and goes through PKHeX's Gen 3 block accessors. Game-side details (flag IDs, CRC16, berry checksums, the Wonder Card save routine) were checked against the [pret](https://github.com/pret) decompilations.

## Credits

- [PKHeX](https://github.com/kwsch/PKHeX) by Kaphotics and contributors (GPLv3)
- [minui-list / minui-presenter](https://github.com/josegonzalez) by Jose Diaz-Gonzalez (MIT)
- The PKHeX WC3 plugin and suloku's Gen III Mystery Gift Tool, for the Gen 3 event file formats and import procedure
- [NextUI](https://github.com/LoveRetro/NextUI)
- PC box art from the [pret/pokeemerald](https://github.com/pret/pokeemerald) decompilation, generated at build time
- ROM checksums from the [No-Intro](https://no-intro.org) DATs, via [libretro-database](https://github.com/libretro/libretro-database)
- Event files from [Project Pokémon's EventsGallery](https://github.com/projectpokemon/EventsGallery), bundled at build time

Pokémon is © Nintendo / Creatures Inc. / GAME FREAK inc. This project is not affiliated with or endorsed by them.

## License

GPL-3.0-or-later, the same as PKHeX. See [LICENSE](LICENSE).
