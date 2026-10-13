using Xunit;

namespace PokemonManager.Tests;

public class AppSettingsTests
{
    [Fact]
    public void ResetToDefaultsRestoresEverySettingButKeepsTheWelcomeSeen()
    {
        var settings = new AppSettings
        {
            AllowIllegalTransfers = true, OnlyOfficialRoms = false, PcBoxView = false, SeenWelcome = true,
            GalleryAllLanguages = true, GalleryUnreleased = true, SaveStateDeletion = false,
        };
        settings.ResetToDefaults();

        var defaults = new AppSettings();
        Assert.Equal(defaults.AllowIllegalTransfers, settings.AllowIllegalTransfers);
        Assert.Equal(defaults.OnlyOfficialRoms, settings.OnlyOfficialRoms);
        Assert.Equal(defaults.PcBoxView, settings.PcBoxView);
        Assert.Equal(defaults.GalleryAllLanguages, settings.GalleryAllLanguages);
        Assert.Equal(defaults.GalleryUnreleased, settings.GalleryUnreleased);
        Assert.Equal(defaults.SaveStateDeletion, settings.SaveStateDeletion);
        Assert.True(settings.SeenWelcome);
    }

    [Fact]
    public void SaveStateDeletionIsOnByDefault() => Assert.True(new AppSettings().SaveStateDeletion);
}
