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

    [Fact]
    public void ASettingsFileWithoutBetaUpdatesIsIncompleteUntilSaved()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pkmgr-settings-{Guid.NewGuid():N}.txt");
        try
        {
            Assert.True(AppSettings.Load(path).Incomplete);
            File.WriteAllText(path, "seen_welcome=true\npc_box_view=false\n");
            var settings = AppSettings.Load(path);
            Assert.True(settings.Incomplete);
            settings.Save(path);
            Assert.Contains("beta_updates=false", File.ReadAllLines(path));
            var saved = AppSettings.Load(path);
            Assert.False(saved.Incomplete);
            Assert.True(saved.SeenWelcome);
            Assert.False(saved.PcBoxView);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
