using System.Buffers.Binary;
using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class GameClockTests : IDisposable
{
    private readonly TestSaves _saves = new();
    public void Dispose() => _saves.Dispose();

    [Theory]
    [InlineData(GameVersion.E)]
    [InlineData(GameVersion.S)]
    public void Gen3ClockShowsTheTimeSet(GameVersion version)
    {
        var sav = _saves.Create(version, $"{version}.sav").Sav;
        var now = new DateTime(2026, 10, 10, 13, 37, 12);
        Assert.Null(GameClock.Set(sav, now, 0, 21, 5));
        var after = GameClock.Read(sav, null, now);
        Assert.Equal((21, 5), (after.Hour, after.Minute));
        // An hour later on the device clock, the game is an hour later too.
        Assert.Equal(22, GameClock.Read(sav, null, now.AddHours(1)).Hour);
    }

    [Theory]
    [InlineData(GameVersion.C, LanguageID.English, 0x2044)]
    [InlineData(GameVersion.GD, LanguageID.Japanese, 0x202B)]
    public void Gen2ClockRestartsTheCartridgeClockAndSetsTheStartTime(GameVersion version, LanguageID language, int offset)
    {
        var sav = _saves.Create(version, $"{version}{language}.sav", language: language).Sav;
        var now = new DateTime(2026, 10, 10, 13, 37, 12);
        var rtc = GameClock.Set(sav, now, 3, 6, 30);
        Assert.NotNull(rtc);
        Assert.Equal(new byte[] { 3, 6, 30, 0 }, sav.Data.Slice(offset, 4).ToArray());
        Assert.Equal((ulong)new DateTimeOffset(now).ToUnixTimeSeconds(), BinaryPrimitives.ReadUInt64LittleEndian(rtc));

        var rtcPath = Path.Combine(_saves.Dir, "game.rtc");
        File.WriteAllBytes(rtcPath, rtc);
        Assert.Equal(new GameClock.Time(3, 6, 30), GameClock.Read(sav, rtcPath, now));
        // 20 hours later: Thursday 02:30.
        Assert.Equal(new GameClock.Time(4, 2, 30), GameClock.Read(sav, rtcPath, now.AddHours(20)));
    }
}
