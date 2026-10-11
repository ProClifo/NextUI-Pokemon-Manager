using System.Runtime.InteropServices;

namespace PokemonManager.Core;

/// <summary>
/// The device's local time as the emulators see it: libc's localtime() (mGBA's GBA cartridge clock uses it).
/// .NET's own DateTime.Now can differ: without time zone data it falls back to UTC, and Ruby/Sapphire/Emerald's
/// clock would then be set an hour or more off from what the game shows.
/// </summary>
public static class DeviceClock
{
    public static DateTime LocalNow()
    {
        if (!OperatingSystem.IsLinux())
            return DateTime.Now;
        try
        {
            long t = time(IntPtr.Zero);
            // struct tm: tm_sec, tm_min, tm_hour, tm_mday, tm_mon, tm_year, ... (ints first), then tm_gmtoff/tm_zone
            var tm = new int[16];
            if (localtime_r(ref t, tm) == IntPtr.Zero)
                return DateTime.Now;
            return new DateTime(tm[5] + 1900, tm[4] + 1, tm[3], tm[2], tm[1], Math.Min(tm[0], 59));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ArgumentOutOfRangeException)
        {
            return DateTime.Now;
        }
    }

    [DllImport("libc.so.6")]
    private static extern long time(IntPtr t);

    [DllImport("libc.so.6")]
    private static extern IntPtr localtime_r(ref long t, [Out] int[] tm);
}
