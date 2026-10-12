using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class EvolutionTextTests
{
    [Theory]
    [InlineData("en", 3, "Congratulations! Your ABRA\nevolved into KADABRA!")]
    [InlineData("en", 2, "Congratulations!\nYour ABRA\fevolved into\nKADABRA!")]
    [InlineData("en", 1, "ABRA evolved\ninto KADABRA!")]
    [InlineData("de", 3, "Glückwunsch! Dein ABRA\nhat sich zu KADABRA entwickelt!")]
    [InlineData("ja", 4, "おめでとう！ ABRAは\nKADABRAに しんかした！")]
    public void TheMessageFollowsTheGameAndLanguage(string code, int generation, string expected)
        => Assert.Equal(expected, UiStrings.Evolved(code, generation, "ABRA", "KADABRA"));

    [Fact]
    public void KoreanPicksItsParticles()
    {
        Assert.Equal("캐이시를 진화시키겠습니까?", UiStrings.EvolveQuestion("ko", "캐이시"));
        Assert.Equal("축하합니다! 캐이시는\n윤겔라로 진화했다!", UiStrings.Evolved("ko", 4, "캐이시", "윤겔라"));
        Assert.Equal("Evolve ABRA?", UiStrings.EvolveQuestion("en", "ABRA"));
    }
}
