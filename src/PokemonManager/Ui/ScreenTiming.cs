using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PokemonManager.Ui;

/// <summary>
/// Logs where the time goes between the pak's screens, each a program of its own: the pak's work after one screen
/// closes until it starts the next, then that program's start-up until its first frame is on screen. The programs
/// built against the patched NextUI (scripts/patch-nextui-keep-frame.py) write the time of their first frame to
/// PKMGR_FRAME_LOG; others (MinUI, the keyboard) leave the start-up out.
/// </summary>
public static class ScreenTiming
{
    private static readonly string FrameLog = Path.Combine(Path.GetTempPath(), $"pkmgr-frames-{Environment.ProcessId}.txt");
    private static readonly object Lock = new();
    private static long _lastClosed = Now();

    static ScreenTiming()
    {
        Environment.SetEnvironmentVariable("PKMGR_FRAME_LOG", FrameLog);
        try { File.Delete(FrameLog); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Starts a screen program (as <see cref="Process.Start(ProcessStartInfo)"/>) and logs its timing when it exits.</summary>
    public static Process? Start(ProcessStartInfo psi)
    {
        long started = Now();
        long work;
        lock (Lock)
            work = started - _lastClosed;
        var p = Process.Start(psi);
        if (p is null)
            return null;
        // the tool and its kind of screen ("minui-presenter --message", "pkmgr-box --scene")
        string name = Path.GetFileName(psi.FileName);
        if (psi.ArgumentList.FirstOrDefault(a => a.StartsWith("--", StringComparison.Ordinal)) is { } mode)
            name += " " + mode;
        int pid = p.Id;
        p.EnableRaisingEvents = true;
        p.Exited += (_, _) =>
        {
            long closed = Now();
            lock (Lock)
                _lastClosed = closed;
            string startup = FirstFrame(pid) is { } frame ? $"{Ms(frame - started)} ms to its first frame" : "first frame not reported";
            Console.Error.WriteLine($"[timing] {name}: {Ms(work)} ms of pak work before it, {startup}, open {Ms(closed - started)} ms");
        };
        return p;
    }

    private static long Ms(long ns) => ns / 1_000_000;

    /// <summary>
    /// Linux's monotonic clock in nanoseconds, the one the programs write (.NET's Stopwatch on the device counts from
    /// elsewhere, so its times can't be compared with theirs).
    /// </summary>
    private static long Now()
    {
        if (OperatingSystem.IsLinux() && Environment.Is64BitProcess)
        {
            try
            {
                if (clock_gettime(1 /* CLOCK_MONOTONIC */, out var ts) == 0)
                    return ts.Seconds * 1_000_000_000 + ts.Nanoseconds;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        return Stopwatch.GetTimestamp() * (1_000_000_000 / Stopwatch.Frequency);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public long Seconds;
        public long Nanoseconds;
    }

    [DllImport("libc")]
    private static extern int clock_gettime(int clock, out Timespec ts);

    /// <summary>When the program's first frame was shown, in nanoseconds of the monotonic clock.</summary>
    private static long? FirstFrame(int pid)
    {
        try
        {
            foreach (var line in File.ReadLines(FrameLog))
            {
                var parts = line.Split(' ');
                if (parts.Length == 2 && parts[0] == pid.ToString() && long.TryParse(parts[1], out var ns))
                    return ns;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }
}
