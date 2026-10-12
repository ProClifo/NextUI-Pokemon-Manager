using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class EvolutionTextTests
{
    [Theory]
    [InlineData("en", 3, "Congratulations! Your ABRA\nevolved into KADABRA!")]
    [InlineData("en", 2, "Congratulations!\nYour ABRA\fevolved into\nKADABRA!")]
    [InlineData("en", 1, "ABRA evolved\ninto KADABRA!")]
    public void TheMessageFollowsTheGameAndLanguage(string code, int generation, string expected)
        => Assert.Equal(expected, UiStrings.Evolved(code, generation, "ABRA", "KADABRA"));

    [Fact]
    public void TheQuestionIsEnglish() => Assert.Equal("Evolve ABRA?", UiStrings.EvolveQuestion("en", "ABRA"));
}
