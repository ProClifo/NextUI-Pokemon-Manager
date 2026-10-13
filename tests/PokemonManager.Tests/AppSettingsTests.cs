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
            GalleryAllLanguages = true, GalleryUnreleased = true, SaveStateDeletion = false, SaveStateWarning = false,
        };
        settings.ResetToDefaults();

        var defaults = new AppSettings();
        Assert.Equal(defaults.AllowIllegalTransfers, settings.AllowIllegalTransfers);
        Assert.Equal(defaults.OnlyOfficialRoms, settings.OnlyOfficialRoms);
        Assert.Equal(defaults.PcBoxView, settings.PcBoxView);
        Assert.Equal(defaults.GalleryAllLanguages, settings.GalleryAllLanguages);
        Assert.Equal(defaults.GalleryUnreleased, settings.GalleryUnreleased);
        Assert.Equal(defaults.SaveStateDeletion, settings.SaveStateDeletion);
        Assert.Equal(defaults.SaveStateWarning, settings.SaveStateWarning);
        Assert.True(settings.SeenWelcome);
    }

    [Fact]
    public void SaveStateDeletionIsOnByDefault() => Assert.True(new AppSettings().SaveStateDeletion);

    [Fact]
    public void SaveStateWarningIsOnByDefaultAndRemembered()
    {
        Assert.True(new AppSettings().SaveStateWarning);
        var path = Path.Combine(Path.GetTempPath(), $"pkmgr-settings-{Guid.NewGuid():N}.txt");
        try
        {
            new AppSettings { SaveStateWarning = false }.Save(path);
            Assert.False(AppSettings.Load(path).SaveStateWarning);
            File.WriteAllText(path, "save_state_deletion=true\n"); // from a version without the setting
            Assert.True(AppSettings.Load(path).SaveStateWarning);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
