using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class DecorationsTests : IDisposable
{
    private readonly TestSaves _saves = new();

    public void Dispose() => _saves.Dispose();

    private const int Desk = 0, Doll = 6;
    private const byte SmallDesk = 1, PikaDoll = 82;

    [Fact]
    public void TheTableMatchesTheGames()
    {
        Assert.Equal(121, Decorations.All.Length);
        Assert.Equal(("SMALL DESK", Desk), Decorations.All[SmallDesk]);
        Assert.Equal(("REGISTEEL DOLL", Doll), Decorations.All[120]);
    }

    [Fact]
    public void ADecorationMovesToAnotherGamesPc()
    {
        var emerald = (SAV3)_saves.Create(GameVersion.E, "Emerald.sav").Sav;
        var ruby = (SAV3)_saves.Create(GameVersion.R, "Ruby.sav").Sav;
        // Emerald's desks at 0x2734, Ruby/Sapphire's at 0x26A0.
        emerald.Large[0x2734] = SmallDesk;
        emerald.Large[0x2735] = 3;

        var desk = Decorations.List(emerald, Desk)[0];
        var result = Decorations.Send(emerald, desk, ruby);
        Assert.True(result.Ok, result.Message);
        Assert.Equal([(byte)3], Decorations.List(emerald, Desk).Select(d => d.Id)); // packed to the front
        Assert.Equal([SmallDesk], Decorations.List(ruby, Desk).Select(d => d.Id));
    }

    [Fact]
    public void APlacedDecorationStaysPut()
    {
        var emerald = (SAV3)_saves.Create(GameVersion.E, "Emerald.sav").Sav;
        var ruby = (SAV3)_saves.Create(GameVersion.R, "Ruby.sav").Sav;
        // Two dolls, one of them in the player's room (playerRoomDecorations at 0x271C).
        emerald.Large[0x2798] = PikaDoll;
        emerald.Large[0x2799] = PikaDoll;
        emerald.Large[0x271C] = PikaDoll;
        var dolls = Decorations.List(emerald, Doll);
        Assert.Equal([true, false], dolls.Select(d => d.InUse));
        Assert.False(Decorations.Send(emerald, dolls[0], ruby).Ok);
        Assert.True(Decorations.Send(emerald, dolls[1], ruby).Ok);
    }
}
