using System.Diagnostics;

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
    private static long _lastClosed = Stopwatch.GetTimestamp();

    static ScreenTiming()
    {
        Environment.SetEnvironmentVariable("PKMGR_FRAME_LOG", FrameLog);
        try { File.Delete(FrameLog); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Starts a screen program (as <see cref="Process.Start(ProcessStartInfo)"/>) and logs its timing when it exits.</summary>
    public static Process? Start(ProcessStartInfo psi)
    {
        long started = Stopwatch.GetTimestamp();
        long work;
        lock (Lock)
            work = started - _lastClosed;
        var p = Process.Start(psi);
        if (p is null)
            return null;
        string name = Path.GetFileName(psi.FileName);
        int pid = p.Id;
        p.EnableRaisingEvents = true;
        p.Exited += (_, _) =>
        {
            long closed = Stopwatch.GetTimestamp();
            lock (Lock)
                _lastClosed = closed;
            string startup = FirstFrame(pid) is { } frame ? $"{Ms(frame - started)} ms to its first frame" : "first frame not reported";
            Console.Error.WriteLine($"[timing] {name}: {Ms(work)} ms of pak work before it, {startup}, open {Ms(closed - started)} ms");
        };
        return p;
    }

    private static long Ms(long ticks) => ticks * 1000 / Stopwatch.Frequency;

    /// <summary>The program's first frame, in <see cref="Stopwatch"/> ticks (both are Linux's monotonic clock).</summary>
    private static long? FirstFrame(int pid)
    {
        try
        {
            foreach (var line in File.ReadLines(FrameLog))
            {
                var parts = line.Split(' ');
                if (parts.Length == 2 && parts[0] == pid.ToString() && long.TryParse(parts[1], out var ns))
                    return ns * Stopwatch.Frequency / 1_000_000_000;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }
}
