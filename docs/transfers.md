# Transfer rules

> Detailed reference. For a quick overview, see the [README](../README.md).

By default Pokémon only move the way the real games allowed, and only once each game has progressed far enough. As in the games, Ruby/Sapphire get the National Pokédex by trading with a game that has it (pokeruby's trade code checks the partner's flag), so a transfer either way between Ruby/Sapphire and a Gen 3 game with the National Pokédex unlocks it. **Settings → Illegal Transfers** (off by default) turns all of this off: Pokémon can then go between any generations PKHeX can convert (e.g. Gen 4 → Gen 3), but they'll usually be flagged as illegal.

| Route | Allowed | Requirements (from the pret decompilations) |
| --- | --- | --- |
| Gen 1 ↔ Gen 1 | Cable Club | Each Gen 1 game has the Pokédex from Oak. |
| Gen 2 ↔ Gen 2 | Trade Center | Each game has given the Mystery Egg to Elm. |
| Gen 1 ↔ Gen 2 | Time Capsule | Gen 1: the Pokédex. Gen 2: Bill has switched the Time Capsule on (first visit to the Ecruteak City Pokémon Center). Gen 2-only species and moves can't go to Gen 1. |
| Gen 3 ↔ Gen 3 | Trade | FireRed/LeafGreen ↔ Ruby/Sapphire/Emerald: the Sapphire has been delivered to Celio, and Emerald is Champion. Emerald and FR/LG without the National Pokédex can't send or receive Eggs or Pokémon outside their Hoenn/Kanto Pokédex (Ruby/Sapphire don't check). Mew and Deoxys need the event flag. |
| Gen 3 → Gen 4 | Pal Park | The Gen 4 game has the National Pokédex; both games are the same language; no Eggs; no Gen 3 HM moves. |
| Gen 4 ↔ Gen 4, Gen 5 ↔ Gen 5 | Trade | None. |
| Gen 4 → Gen 5 | Poké Transfer | The Gen 5 game has the National Pokédex (documented, not decompiled); no Eggs; no Gen 4 HM moves. |
| Gen 1/2 → Gen 3+, anything backwards | — | Not possible in the games. |

Imported files follow the receiving side of these rules: Game Boy games need their link room open, and Emerald and FireRed/LeafGreen need the National Pokédex for Pokémon outside their regional Pokédex. Distributions (the Distributions and Events Gallery menus, and Mew) don't: the real distributions didn't need the National Pokédex. They only need the Pokédex itself, read from the games' own flags (Gen 1 `EVENT_GOT_POKEDEX`, Gen 2 the event Mr. Pokémon's house sets with `ENGINE_POKEDEX`, Gen 3 `FLAG_SYS_POKEDEX_GET`, Gen 4 the Pokédex block's `pokedexObtained`); Black/White have no decompilation, so there a Pokédex with anything caught counts. Pal Park's once-a-day and six-Pokémon limits aren't applied.

## Rules the app always enforces

- A transferred or imported Pokémon only moves into an empty PC slot (distributions too). Nothing is overwritten.
- The destination save is written first, then the source save. If the second write fails, the Pokémon ends up in both saves instead of being lost.
- You can't move your last party Pokémon.
- A party Pokémon can only be transferred or trade-evolved if its save was made in a Pokémon Center (any floor), as trades happen at the link rooms there. Ruby/Sapphire's Pokémon League center has no link room, so it doesn't count (Emerald's does). In Black/White and Black 2/White 2, party Pokémon trade anywhere once you have the C-Gear (the game's `FE_CGEAR_GET` flag), except ones that know an HM move (Cut, Fly, Surf, Strength, Waterfall, Dive), which still need a Pokémon Center. The map is read from the save, with map numbers from the pret decompilations (Black/White's from the Black 2/White 2 decompilation, whose first 427 zones are Black/White's). Places where the game turns the C-Gear off aren't known, so they aren't checked. Boxed Pokémon aren't limited.
- Japanese and international Gen 1/2 games can't trade with each other, as on real hardware.
