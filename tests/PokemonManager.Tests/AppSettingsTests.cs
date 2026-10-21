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

    [Fact]
    public void BetaUpdatesIsOnlyTurnedOnInSettingsTxtAndSurvivesResetToDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pkmgr-settings-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "pc_box_view=true\nbeta_updates=true\n");
            var settings = AppSettings.Load(path);
            Assert.True(settings.BetaUpdates);
            settings.ResetToDefaults();
            Assert.True(settings.BetaUpdates);
            settings.Save(path);
            Assert.True(AppSettings.Load(path).BetaUpdates);
            Assert.False(new AppSettings().BetaUpdates);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
