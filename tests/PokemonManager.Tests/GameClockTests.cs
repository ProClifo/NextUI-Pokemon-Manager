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
    [InlineData(GameVersion.S, 1, 31)]
    [InlineData(GameVersion.S, 23, 59)]
    [InlineData(GameVersion.E, 0, 0)]
    public void Gen3ClockSurvivesWritingTheSave(GameVersion version, int hour, int minute)
    {
        var entry = _saves.Create(version, $"{version}.sav");
        var now = new DateTime(2026, 10, 11, 1, 5, 0);
        GameClock.Set(entry.Sav, now, 0, hour, minute);
        var reloaded = _saves.Roundtrip(entry);
        var after = GameClock.Read(reloaded.Sav, null, now);
        Assert.Equal((hour, minute), (after.Hour, after.Minute));
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

    [Theory]
    [InlineData(3, 45)] // the same weekday: no day passes, even at an earlier time
    [InlineData(4, 46)] // Thursday: the next day
    [InlineData(1, 50)] // Monday: forward to the next Monday, never back
    public void Gen2ClockKeepsTheGamesDayCount(int weekday, int day)
    {
        var sav = (SAV2)_saves.Create(GameVersion.C, "Crystal.sav").Sav;
        var now = new DateTime(2026, 10, 10, 13, 37, 12);
        // day 45 (a Wednesday): started on day 3, the cartridge clock 6 weeks in
        sav.Data[0x2044] = 3;
        var rtcPath = Path.Combine(_saves.Dir, "game.rtc");
        var start = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(start, (ulong)new DateTimeOffset(now.AddDays(-42)).ToUnixTimeSeconds());
        File.WriteAllBytes(rtcPath, start);
        Assert.Equal(45, GameClock.Day(sav, rtcPath, now));

        File.WriteAllBytes(rtcPath, GameClock.Set(sav, now, weekday, 6, 30, rtcPath)!);
        Assert.Equal(day, GameClock.Day(sav, rtcPath, now));
        Assert.Equal(new GameClock.Time(weekday, 6, 30), GameClock.Read(sav, rtcPath, now));
        Assert.Equal(weekday, sav.Data[0x2044]); // wStartDay stays a weekday
    }
}
