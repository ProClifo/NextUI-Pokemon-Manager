# Gen 3 events: what goes where

> Detailed reference. For a quick overview, see the [README](../README.md).

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
- Event files need the game's own menu unlocked first, as on the cartridge (and the Mystery Gift menu's tickets stay hidden until then): Mystery Event in Ruby/Sapphire (after beating Norman, give "MYSTERY EVENT IS EXCITING" as your profile to the man in the Petalburg City Pokémon Center; the game checks `FLAG_BADGE05_GET`, so the app does too), Mystery Gift in FireRed/LeafGreen/Emerald (fill in a Poké Mart questionnaire with "LINK TOGETHER WITH ALL"; Wonder Cards, Wonder News and e-Cards), and Mystery Event in Japanese Emerald for `.me3` files ("MYSTERY EVENT IS EXCITING" in the questionnaire). Pokémon Manager never unlocks these menus itself.
- **Non-Japanese Emerald** has no Mystery Event menu (and setting its flag corrupts the save), so `.me3` files can't go into it.
- Like the WC3 plugin, stale checksums in a file (e.g. after hand-editing) are recalculated, and the result message says so. Files for the wrong game or region, and Wonder Cards without an event flag, are refused.
- Injecting a Mystery Event into Emerald clears any Wonder Card, because the card would point at the replaced script.

**Compared with WC3 plugin 2.6.0:** a test harness runs the plugin's decompiled import code next to Pokémon Manager on the same files. The saves come out byte-identical except for two deliberate differences. Pokémon Manager also unlocks the Mystery Gift/Event menu. For normal Wonder Cards it resets the card's stats block and copies in the icon, as the game's own `SaveWonderCard()` does. It also writes the full 32-bit checksum for Ruby/Sapphire scripts and for berries, where the plugin writes only the low 16 bits.
