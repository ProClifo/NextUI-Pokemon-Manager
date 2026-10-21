using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PokemonManager.Ui;

/// <summary>
/// Runs the pak's screen programs (minui-list, minui-presenter, pkmgr-box), in pkmgr-host when the pak has it, else
/// each as a program of its own.
/// </summary>
public static class Screens
{
    /// <summary>
    /// Runs <paramref name="tool"/> with these arguments until the player leaves it: its exit code, or null when it
    /// couldn't run, and what it printed. Its errors go to the log.
    /// </summary>
    public static int? Run(string tool, IReadOnlyList<string> args, out string stdout)
    {
        if (ScreenHost.Run(tool, args, showOnly: false, out stdout) is { } code)
            return code;
        return RunProgram(tool, args, out stdout);
    }

    private static int? RunProgram(string tool, IReadOnlyList<string> args, out string stdout)
    {
        stdout = "";
        try
        {
            var psi = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            using var p = ScreenTiming.Start(psi);
            if (p is null)
                return null;
            var output = new System.Text.StringBuilder();
            p.OutputDataReceived += (_, e) => { if (e.Data is { } line) lock (output) output.AppendLine(line); };
            p.ErrorDataReceived += (_, e) => { if (e.Data is { } line) Console.Error.WriteLine($"{tool}: {line}"); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            lock (output)
                stdout = output.ToString();
            return p.ExitCode;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"{tool} failed to start: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// pkmgr-host (native/pkmgr-host.c): one process that shows every screen of the pak, so moving between them doesn't
/// wait half a second for a new program to open the display. It's started with the pak and sent each screen as the
/// program and arguments that would otherwise be started. When it can't run one (or stops), that screen and the rest
/// run as programs again; after a second failure it isn't used until the pak restarts.
/// </summary>
public static class ScreenHost
{
    private const string Tool = "pkmgr-host";
    private static readonly string[] Hosted = ["minui-list", "minui-presenter", "pkmgr-box"];
    private static readonly object Lock = new();
    private static Process? _host;
    private static int _failures;
    private static long _lastAnswer = Stopwatch.GetTimestamp();

    /// <summary>Starts the host now, so it opens the display while the pak gets its first screen ready.</summary>
    public static void Start()
    {
        lock (Lock)
        {
            if (Directory() is not null)
                Started();
        }
    }

    /// <summary>
    /// Closes the host and the display, for a program it doesn't run (the keyboard) or the pak's end. The next screen
    /// starts it again.
    /// </summary>
    public static void Stop()
    {
        lock (Lock)
        {
            if (_host is null)
                return;
            try
            {
                _host.StandardInput.Close();
                if (!_host.WaitForExit(3000))
                    _host.Kill();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            _host.Dispose();
            _host = null;
        }
    }

    /// <summary>
    /// Runs a screen in the host: its exit code, or null when the host can't (then it's for a program of its own).
    /// <paramref name="showOnly"/> answers once the screen is shown and leaves it up ("please wait").
    /// </summary>
    public static int? Run(string tool, IReadOnlyList<string> args, bool showOnly, out string stdout)
    {
        stdout = "";
        lock (Lock)
        {
            if (!Hosted.Contains(tool) || Directory() is not { } dir || !File.Exists(Path.Combine(dir, tool + ".so")))
                return null;
            var host = Started();
            if (host is null)
                return null;
            long asked = Stopwatch.GetTimestamp();
            long work = asked - _lastAnswer;
            try
            {
                var request = new JsonObject
                {
                    ["program"] = tool,
                    ["args"] = new JsonArray(args.Select(a => (JsonNode?)a).ToArray()),
                    ["show_only"] = showOnly,
                };
                host.StandardInput.WriteLine(request.ToJsonString());
                host.StandardInput.Flush();
                var line = host.StandardOutput.ReadLine();
                if (line is null)
                {
                    Lost($"it stopped (exit {(host.WaitForExit(2000) ? host.ExitCode : -1)})");
                    return null;
                }
                using var answer = JsonDocument.Parse(line);
                var root = answer.RootElement;
                int code = root.GetProperty("code").GetInt32();
                stdout = root.GetProperty("out").GetString() ?? "";
                _lastAnswer = Stopwatch.GetTimestamp();
                string mode = args.FirstOrDefault(a => a.StartsWith("--", StringComparison.Ordinal)) is { } m ? " " + m : "";
                Console.Error.WriteLine($"[timing] hosted {tool}{mode}: {Ms(work)} ms of pak work before it, open {Ms(_lastAnswer - asked)} ms");
                if (code == -1)
                {
                    // the library didn't load: as programs from now on
                    _failures = int.MaxValue;
                    Lost($"it couldn't load {tool}");
                    return null;
                }
                if (root.TryGetProperty("restart", out var restart) && restart.GetBoolean())
                {
                    // grown too big: it quits, and the next screen starts a new one
                    host.WaitForExit(3000);
                    host.Dispose();
                    _host = null;
                }
                return code;
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                Lost(ex.Message);
                return null;
            }
        }
    }

    private static long Ms(long ticks) => ticks * 1000 / Stopwatch.Frequency;

    /// <summary>The folder with pkmgr-host and the screens it runs, if the pak has them and it's still to be used.</summary>
    private static string? Directory()
    {
        if (_failures >= 2 || Environment.GetEnvironmentVariable("PKMGR_HOST") == "0")
            return null;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(dir, Tool)))
                return dir;
        }
        return null;
    }

    private static Process? Started()
    {
        if (_host is { HasExited: false })
            return _host;
        _host?.Dispose();
        _host = null;
        try
        {
            // its errors (and NextUI's logging) go straight to the pak's log
            var psi = new ProcessStartInfo(Tool) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true };
            _host = Process.Start(psi);
            _lastAnswer = Stopwatch.GetTimestamp();
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            Lost(ex.Message);
        }
        return _host;
    }

    private static void Lost(string why)
    {
        _failures++;
        Console.Error.WriteLine($"pkmgr-host: {why}; this screen runs as a program{(_failures >= 2 ? ", and the rest too" : "")}.");
        try
        {
            if (_host is { HasExited: false })
                _host.Kill();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        _host?.Dispose();
        _host = null;
    }
}
