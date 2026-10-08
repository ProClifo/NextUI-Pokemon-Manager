# Pokémon Manager for NextUI

A NextUI tool pak, built on [PKHeX](https://github.com/kwsch/PKHeX), for editing Pokémon save files on your handheld:

- **Transfer Pokémon between games.** Move or copy a Pokémon from one save to another. PKHeX converts the data between generations (Gen 1↔2, Gen 1/2→7, 3→4→5→6→7→8...). Moving between games of the same era counts as a trade, so trade evolutions trigger.
- **Trade evolutions.** Evolve Kadabra, Machoke, Graveler, Haunter, Onix + Metal Coat, Scyther + Metal Coat, Seadra + Dragon Scale, Clamperl, Boldore and the rest without a second console. You can evolve one Pokémon or every Pokémon that's ready.
- **Mystery Gifts, Mystery Events and e-Reader cards.**
  - Gen 3: Wonder Cards (`.wc3`), Wonder News (`.wn3`), Mystery Events (`.me3`, e.g. the Eon Ticket e-Card), e-Card Trainers (`.ect`) and e-Reader Berries (`.ecb`). These are the formats used by suloku's *Gen III Mystery Gift Tool* and the PKHeX *WC3 plugin*.
  - Gen 4–7: Wonder Cards are placed in the in-game Mystery Gift album so you pick them up from the delivery person, as if you'd downloaded them (`.pgt .pcd .wc4 .pgf .wc6 .wc7 .wb7`...).
  - Any generation: a gift Pokémon can be sent straight to a PC box (`.wc8 .wb8 .wa8 .wc9 .wa9` included).
  - **Built-in event library**: every official distribution PKHeX knows about (e.g. WISHMKR Jirachi, 10 ANIV Celebi, GB-era Mew) can be generated with correct event data and sent to your PC.
- **Import/export** PKHeX Pokémon files (`.pk1`–`.pk9`...).
- **Automatic backups** before every change, and a *Restore a backup* option.

> **Status:** early release. The code is covered by automated tests on generated saves, and the ARM64 build has been run under emulation. It has **not yet been tested on real hardware**. Keep your own backup of any save you care about until it has been.

## Installation

### From a release

1. Download `PokemonManager-tg5040-sdcard.zip` from the [Releases](../../releases) page.
2. Unzip it onto the root of your SD card. You get `Tools/tg5040/Pokemon Manager.pak` and a `PokemonManager` folder.
3. On a TrimUI Smart Pro S, Miyoo Flip or Anbernic H700 device, rename `Tools/tg5040` to your platform folder (`tg5050`, `my355`, `h700`). The pak contains the helper binaries for all four.

`PokemonManager.pak.zip` holds the same pak with its contents at the zip root, for Pak Store–style installers.

### Building it yourself

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download), `curl` and `zip`.

```sh
scripts/build-pak.sh          # -> dist/PokemonManager.pak.zip and dist/PokemonManager-tg5040-sdcard.zip
dotnet test                   # run the test suite
```

## Using it

Open **Tools → Pokemon Manager**. The app scans `Saves/` for anything PKHeX recognizes and lists it as *Game - Trainer (file)*. Pick a save, then:

| Menu | What it does |
| --- | --- |
| Pokémon (view / transfer / evolve) | Browse the party and boxes. For each Pokémon: view summary, move or copy to another game, trade evolve, export to file. |
| Trade evolutions | Every Pokémon in the save that evolves by trade, plus *Evolve all that are ready* (no item needed, or already holding the right one). |
| Mystery Gifts, Events & e-Reader | Gift files from your SD card, PKHeX's event library and (Gen 3) the current Mystery Gift/Event status. |
| Import Pokémon from file | Puts a `.pk*` file from `PokemonManager/Import` into the first free PC slot. |
| Restore a backup | Rolls the save back to one of the automatic backups. |

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
    └── Saves/                 ← optional: extra saves to manage (e.g. copied from another device)
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
| `.me3` Mystery Event (Eon Ticket e-Card, ...) | Ruby, Sapphire, Emerald | As the original event (Eon Ticket: your dad at the Petalburg Gym) |
| `.ect` e-Card Trainer | Ruby, Sapphire, Emerald, FireRed, LeafGreen | The game's e-Reader trainer battle |
| `.ecb` e-Reader Berry | Ruby, Sapphire | Replaces the Enigma Berry data |

Notes:

- Wonder Cards and Mystery Events share one script slot, so injecting one replaces the other. That's also how the games behave.
- Japanese and international Wonder Cards/News have different sizes and only work in a save of the matching region.
- Injecting a Wonder Card or Wonder News also unlocks the Mystery Gift menu. Injecting a Mystery Event into Ruby/Sapphire or Japanese Emerald unlocks Mystery Events.
- **Non-Japanese Emerald** has no Mystery Event menu, and setting its Mystery Event flag corrupts the save. Pokémon Manager never sets that flag. It writes the event script and warns that the event may not trigger.
- Files are validated before anything is written. A file with a bad checksum, or for the wrong game, is refused.

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
- Gen 3 event injection isn't part of PKHeX. Its offsets and procedure follow suloku's Gen III Mystery Gift Tool, checked against PKHeX's Gen 3 save layout and the [pret](https://github.com/pret) decompilations (flag IDs, CRC16, Wonder Card save routine). The tests check every write against PKHeX's own Gen 3 structures.

## Credits

- [PKHeX](https://github.com/kwsch/PKHeX) by Kaphotics and contributors (GPLv3)
- [minui-list / minui-presenter](https://github.com/josegonzalez) by Jose Diaz-Gonzalez (MIT)
- suloku's Gen III Mystery Gift Tool, for the Gen 3 event file formats
- [NextUI](https://github.com/LoveRetro/NextUI)

Pokémon is © Nintendo / Creatures Inc. / GAME FREAK inc. This project is not affiliated with or endorsed by them.

## License

GPL-3.0-or-later, the same as PKHeX. See [LICENSE](LICENSE).
