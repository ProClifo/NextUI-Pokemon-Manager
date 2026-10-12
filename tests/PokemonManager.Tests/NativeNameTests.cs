using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class NativeNameTests
{
    [Theory]
    [InlineData(GameVersion.E, "ENG", "Emerald")]
    [InlineData(GameVersion.E, "GER", "Smaragd")]
    [InlineData(GameVersion.E, "FRE", "Émeraude")]
    [InlineData(GameVersion.FR, "SPA", "Rojo Fuego")]
    [InlineData(GameVersion.R, "JPN", "ルビー")]
    [InlineData(GameVersion.GD, "KOR", "금")]
    [InlineData(GameVersion.C, "INT", "Crystal")]
    [InlineData(GameVersion.RS, "ITA", "Rubino/Zaffiro")]
    [InlineData(GameVersion.E, "KOR", "Emerald")] // no Korean Emerald: English
    public void GamesAreNamedInTheirOwnLanguage(GameVersion version, string language, string expected)
        => Assert.Equal(expected, Names.NativeGame(version, language));
}
