using System.Buffers.Binary;
using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// The in-game clock of the games with a real-time clock in the cartridge:
/// <list type="bullet">
/// <item>Ruby/Sapphire/Emerald show the cartridge clock minus SaveBlock2's localTimeOffset (pokeemerald rtc.c,
/// RtcCalcLocalTime). The emulator's cartridge clock is the device clock, so a time is set by choosing the
/// offset, as the game's own clock setting does (RtcCalcLocalTimeOffset).</item>
/// <item>Gold/Silver/Crystal show the cartridge clock plus wStartDay/Hour/Minute/Second from the save
/// (pokecrystal home/time.asm, FixTime); the weekday is wCurDay mod 7. NextUI's Game Boy core (gambatte) keeps
/// the cartridge clock in Saves/GBC/&lt;rom&gt;.rtc as the Unix time it started counting from (8 bytes, little
/// endian), so a time is set by restarting that clock and putting the time in wStart*, keeping the game's day count.</item>
/// </list>
/// </summary>
public static class GameClock
{
    public static readonly string[] Weekdays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    public static bool Supported(SaveFile sav) => sav is SAV3RS or SAV3E or SAV2;

    /// <summary>Whether the game has days of the week (Gen 2 does; Gen 3 only counts days).</summary>
    public static bool HasWeekdays(SaveFile sav) => sav is SAV2;

    public sealed record Time(int Weekday, int Hour, int Minute);

    /// <summary>The time the game would show now.</summary>
    public static Time Read(SaveFile sav, string? rtcPath, DateTime now)
    {
        switch (sav)
        {
            case SAV2 gs:
            {
                long seconds = Seconds(gs, rtcPath, now);
                long minutes = seconds / 60, hours = minutes / 60, days = hours / 24;
                return new Time((int)(days % 7), (int)(hours % 24), (int)(minutes % 60));
            }
            case SAV3 s3 when s3.SmallBlock is ISaveBlock3SmallHoenn hoenn:
            {
                var offset = hoenn.ClockInitial;
                long rtc = RtcSeconds(now);
                long local = rtc - OffsetSeconds(offset);
                long minutes = local / 60, hours = minutes / 60;
                return new Time((int)((local / 86400) % 7 + 7) % 7, (int)((hours % 24 + 24) % 24), (int)((minutes % 60 + 60) % 60));
            }
            default:
                throw new NotSupportedException($"{Names.Game(sav)} has no clock.");
        }
    }

    /// <summary>
    /// Sets the game's clock to <paramref name="hour"/>:<paramref name="minute"/> (Gen 2: on
    /// <paramref name="weekday"/>, 0 = Sunday). Gen 2 also needs its .rtc file written (<paramref name="rtcPath"/>
    /// is the current one); returns its new bytes for the caller to write after the save.
    /// </summary>
    public static byte[]? Set(SaveFile sav, DateTime now, int weekday, int hour, int minute, string? rtcPath = null)
    {
        switch (sav)
        {
            case SAV2 gs:
            {
                // The game's day count (wCurDay = wStartDay + the cartridge clock's days, wrapping at 140) dates its
                // daily events, which count the days since they last ran. It only goes forward: to the same weekday
                // it stays, to another it moves on to that day's next one, as if waited for. Going back would read
                // as most of 140 days passing.
                int today = Day(gs, rtcPath, now) % DaysWrap;
                int day = (today + (weekday - today % 7 + 7) % 7) % DaysWrap;
                // wStartDay stays a weekday as the game sets it; the cartridge clock restarts at the remaining whole
                // weeks, 00:00:00, so the start values are the time itself.
                int o = StartOffset(gs);
                gs.Data[o] = (byte)weekday;
                gs.Data[o + 1] = (byte)hour;
                gs.Data[o + 2] = (byte)minute;
                gs.Data[o + 3] = 0;
                var rtc = new byte[8];
                BinaryPrimitives.WriteUInt64LittleEndian(rtc, (ulong)(ToUnix(now) - (day - weekday) * 86400L));
                return rtc;
            }
            case SAV3 s3 when s3.SmallBlock is ISaveBlock3SmallHoenn hoenn:
            {
                // Keep the game's day count; offset = cartridge clock - wanted time (RtcCalcTimeDifference).
                long rtc = RtcSeconds(now);
                long day = (long)Math.Floor((rtc - OffsetSeconds(hoenn.ClockInitial)) / 86400.0);
                long wanted = day * 86400 + hour * 3600L + minute * 60L;
                long diff = rtc - wanted;
                long days = (long)Math.Floor(diff / 86400.0), rest = diff - days * 86400;
                var offset = hoenn.ClockInitial;
                offset.Day = (ushort)(short)days;
                offset.Hour = (int)(rest / 3600);
                offset.Minute = (int)(rest % 3600 / 60);
                offset.Second = (int)(rest % 60);
                hoenn.ClockInitial = offset;
                return null;
            }
            default:
                throw new NotSupportedException($"{Names.Game(sav)} has no clock.");
        }
    }

    /// <summary>Gen 2's day counter (wCurDay, before it wraps at 140), as day-based events like Mystery Gift count days.</summary>
    public static int Day(SAV2 sav, string? rtcPath, DateTime now) => (int)(Seconds(sav, rtcPath, now) / 86400);

    /// <summary>Gen 2's time as seconds since day 0: wStart* plus how long the cartridge clock has run.</summary>
    private static long Seconds(SAV2 sav, string? rtcPath, DateTime now)
    {
        int o = StartOffset(sav);
        long elapsed = Math.Max(0, ToUnix(now) - (ReadBaseTime(rtcPath) ?? ToUnix(now)));
        return sav.Data[o + 3] + 60L * (sav.Data[o + 2] + 60L * (sav.Data[o + 1] + 24L * sav.Data[o])) + elapsed;
    }

    /// <summary>Where wStartDay is: after the player's ID and five names (6 bytes each in Japanese games).</summary>
    private const int DaysWrap = 140; // FixDays keeps the cartridge clock's days under 140; 140 days are 20 weeks

    private static int StartOffset(SAV2 sav) => sav.Japanese ? 0x202B : 0x2044;

    /// <summary>The GBA cartridge clock (the device clock) as pokeemerald counts it: seconds since 2000-01-01.</summary>
    private static long RtcSeconds(DateTime now)
        => (long)(now.Date - new DateTime(2000, 1, 1)).TotalDays * 86400 + now.Hour * 3600 + now.Minute * 60 + now.Second;

    private static long OffsetSeconds(RTC3 offset) => (short)offset.Day * 86400L + offset.Hour * 3600 + offset.Minute * 60 + offset.Second;

    private static long ToUnix(DateTime now) => new DateTimeOffset(now).ToUnixTimeSeconds();

    private static long? ReadBaseTime(string? rtcPath)
    {
        try
        {
            if (rtcPath is null || !File.Exists(rtcPath))
                return null;
            var bytes = File.ReadAllBytes(rtcPath);
            return bytes.Length >= 8 ? (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes) : bytes.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
