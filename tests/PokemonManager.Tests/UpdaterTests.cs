using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public class UpdaterTests
{
    [Theory]
    [InlineData("v0.2.53", "v0.2.52", true)]
    [InlineData("v0.2.52", "v0.2.52", false)]
    [InlineData("v0.2.51", "v0.2.52", false)]
    [InlineData("v0.2.53-beta.1", "v0.2.52", true)]
    [InlineData("v0.2.53", "v0.2.53-beta.1", true)]
    [InlineData("v0.2.52", "v0.2.53-beta.1", false)]
    [InlineData("v0.2.53-beta.2", "v0.2.53-beta.1", true)]
    [InlineData("v0.2.53-beta.10", "v0.2.53-beta.9", true)]
    [InlineData("v0.2.53-beta.1", "v0.2.53-alpha.3", true)]
    [InlineData("v0.2.10", "v0.2.9", true)]
    [InlineData("v0.2.52", "dev", true)]
    [InlineData("dev", "v0.2.52", false)]
    public void IsNewerComparesSemanticVersions(string tag, string installed, bool newer)
        => Assert.Equal(newer, Updater.IsNewer(tag, installed));

    private const string Releases = """
        [
          {"tag_name":"v0.2.53-beta.1","name":"","draft":false,"prerelease":true,"published_at":"2026-10-21T10:00:00Z",
           "assets":[{"name":"PokemonManager-h700-sdcard.zip","browser_download_url":"https://x/beta.zip","size":3}]},
          {"tag_name":"v0.2.54","name":"","draft":true,"prerelease":false,"published_at":null,
           "assets":[{"name":"PokemonManager-h700-sdcard.zip","browser_download_url":"https://x/draft.zip","size":1}]},
          {"tag_name":"v0.2.52","name":"v0.2.52","draft":false,"prerelease":false,"published_at":"2026-10-20T10:00:00Z",
           "assets":[{"name":"PokemonManager-h700-sdcard.zip","browser_download_url":"https://x/stable.zip","size":2}]}
        ]
        """;

    [Fact]
    public void PickOffersPreReleasesOnlyWithBetaUpdates()
    {
        Assert.Equal("v0.2.52", Updater.Pick(Releases, "h700", beta: false).Tag);
        var beta = Updater.Pick(Releases, "h700", beta: true);
        Assert.Equal("v0.2.53-beta.1", beta.Tag);
        Assert.Equal("https://x/beta.zip", beta.AssetUrl);
        Assert.Throws<InvalidDataException>(() => Updater.Pick(Releases, "tg5040", beta: true));
    }
}
