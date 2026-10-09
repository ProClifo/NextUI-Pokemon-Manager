using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class SaveOrderTests : IDisposable
{
    private readonly TestSaves _saves = new();

    public void Dispose() => _saves.Dispose();

    [Fact]
    public void SavesFollowNextUisRecentlyPlayedThenFileTimes()
    {
        var sd = Path.Combine(_saves.Dir, "SD");
        var minui = Path.Combine(sd, ".userdata", "shared", ".minui");
        Directory.CreateDirectory(minui);
        File.WriteAllLines(Path.Combine(minui, "recent.txt"),
        [
            "/Roms/Game Boy Advance (GBA)/Pokemon Ruby.gba",
            "/Roms/Nintendo DS (NDS)/Pokemon Platinum.nds\tPlatinum",
        ]);
        var recents = SaveOrder.ReadRecents(Path.Combine(minui, "recent.txt"), sd);

        var emerald = _saves.Create(GameVersion.E, "Pokemon Emerald.sav");
        var ruby = _saves.Create(GameVersion.R, "Pokemon Ruby.sav");
        var platinum = _saves.Create(GameVersion.Pt, "Pokemon Platinum.sav");
        var crystal = _saves.Create(GameVersion.C, "Pokemon Crystal.sav");
        File.SetLastWriteTimeUtc(emerald.Path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(crystal.Path, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        IReadOnlyList<string> Roms(string save) => Path.GetFileNameWithoutExtension(save) switch
        {
            "Pokemon Ruby" => [Path.Combine(sd, "Roms", "Game Boy Advance (GBA)", "Pokemon Ruby.gba")],
            "Pokemon Platinum" => [Path.Combine(sd, "Roms", "Nintendo DS (NDS)", "Pokemon Platinum.nds")],
            var other => [Path.Combine(sd, "Roms", "X", other + ".gba")],
        };

        var ordered = SaveOrder.ByLastPlayed([emerald, crystal, platinum, ruby], Roms, recents);
        // Ruby and Platinum by NextUI's list; Crystal and Emerald (not in it) by their save files' times.
        Assert.Equal([ruby.Path, platinum.Path, crystal.Path, emerald.Path], ordered.Select(s => s.Path));
    }
}
