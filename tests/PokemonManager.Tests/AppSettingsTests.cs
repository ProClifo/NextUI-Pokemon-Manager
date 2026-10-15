using Xunit;

namespace PokemonManager.Tests;

public class AppSettingsTests
{
    [Fact]
    public void ResetToDefaultsRestoresEverySettingButKeepsTheWelcomeSeen()
    {
        var settings = new AppSettings
        {
            AllowIllegalTransfers = true, PcBoxView = false, SeenWelcome = true, SeenStateMoved = true,
            GalleryAllLanguages = true, GalleryUnreleased = true,
        };
        settings.ResetToDefaults();

        var defaults = new AppSettings();
        Assert.Equal(defaults.AllowIllegalTransfers, settings.AllowIllegalTransfers);
        Assert.Equal(defaults.PcBoxView, settings.PcBoxView);
        Assert.Equal(defaults.GalleryAllLanguages, settings.GalleryAllLanguages);
        Assert.Equal(defaults.GalleryUnreleased, settings.GalleryUnreleased);
        Assert.True(settings.SeenWelcome);
        Assert.False(settings.SeenStateMoved);
    }
}
