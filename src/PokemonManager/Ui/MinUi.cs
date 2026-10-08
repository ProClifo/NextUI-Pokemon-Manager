using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PokemonManager.Ui;

/// <summary>
/// Drives josegonzalez's minui-list / minui-presenter, which render with the NextUI theme.
/// Their stdout is not reliable, so results are always read back from files.
/// </summary>
public sealed class MinUi : IUi
{
    private const int ExitSelected = 0;

    private readonly string _tmp;
    private Process? _busy;

    public MinUi(string tempDir)
    {
        _tmp = tempDir;
        Directory.CreateDirectory(_tmp);
        // launch.sh shows a "Loading..." screen while the runtime starts; take it down.
        foreach (var p in Process.GetProcessesByName("minui-presenter"))
        {
            try { p.Kill(); }
            catch (Exception) { /* already gone */ }
        }
    }

    public static bool IsAvailable() => FindOnPath("minui-list") is not null && FindOnPath("minui-presenter") is not null;

    public int? Choose(string title, IReadOnlyList<string> items, int selected = 0)
    {
        StopBusy();
        if (items.Count == 0)
            return null;

        var input = Path.Combine(_tmp, "list.json");
        var output = Path.Combine(_tmp, "list-out.json");
        var array = new JsonArray();
        foreach (var item in items)
            array.Add((JsonNode)new JsonObject { ["name"] = string.IsNullOrWhiteSpace(item) ? "-" : item });
        var root = new JsonObject
        {
            ["items"] = array,
            ["selected"] = Math.Clamp(selected, 0, items.Count - 1),
            ["scroll_method"] = "pong",
        };
        File.WriteAllText(input, root.ToJsonString());
        File.Delete(output);

        int code = Run("minui-list",
            "--file", input,
            "--title", title,
            "--write-location", output,
            "--write-value", "state",
            "--confirm-text", "SELECT",
            "--cancel-text", "BACK");
        if (code != ExitSelected || !File.Exists(output))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(output));
            int index = doc.RootElement.GetProperty("selected").GetInt32();
            return index >= 0 && index < items.Count ? index : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Message(string text)
    {
        StopBusy();
        var pages = UiText.Paginate(text);
        if (pages.Count == 1)
        {
            Run("minui-presenter",
                "--message", Escape(pages[0]),
                "--confirm-show", "--confirm-text", "OK",
                "--timeout", "0");
            return;
        }

        var file = Path.Combine(_tmp, "pages.json");
        var items = new JsonArray();
        for (int i = 0; i < pages.Count; i++)
            items.Add((JsonNode)new JsonObject { ["text"] = $"{pages[i]}\n\n({i + 1}/{pages.Count}, LEFT/RIGHT to scroll)", ["alignment"] = "top" });
        File.WriteAllText(file, new JsonObject { ["items"] = items }.ToJsonString());
        Run("minui-presenter",
            "--file", file,
            "--confirm-show", "--confirm-text", "OK",
            "--timeout", "0");
    }

    public bool Confirm(string text, string yes = "YES", string no = "NO")
    {
        StopBusy();
        int code = Run("minui-presenter",
            "--message", Escape(text),
            "--confirm-show", "--confirm-text", yes,
            "--cancel-show", "--cancel-text", no,
            "--timeout", "0");
        return code == 0;
    }

    public void Busy(string text)
    {
        StopBusy();
        var psi = Start("minui-presenter", "--message", Escape(text), "--timeout", "-1");
        try
        {
            _busy = Process.Start(psi);
        }
        catch (Exception)
        {
            _busy = null;
        }
    }

    private void StopBusy()
    {
        if (_busy is null)
            return;
        try
        {
            if (!_busy.HasExited)
            {
                _busy.Kill();
                _busy.WaitForExit(2000);
            }
        }
        catch (Exception)
        {
            // already gone
        }
        _busy.Dispose();
        _busy = null;
    }

    /// <summary>minui-presenter interprets \n and \\ escapes in --message.</summary>
    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n");

    private static ProcessStartInfo Start(string tool, params string[] args)
    {
        var psi = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        return psi;
    }

    private static int Run(string tool, params string[] args)
    {
        using var p = Process.Start(Start(tool, args));
        if (p is null)
            return -1;
        // Drain output so the tool never blocks on a full pipe; contents are informational only.
        p.OutputDataReceived += static (_, _) => { };
        p.ErrorDataReceived += static (_, _) => { };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();
        return p.ExitCode;
    }

    private static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}

/// <summary>
/// Text-mode stand-in for testing on a PC: numbered lists on stdout, answers on stdin.
/// </summary>
public sealed class ConsoleUi : IUi
{
    public int? Choose(string title, IReadOnlyList<string> items, int selected = 0)
    {
        if (items.Count == 0)
            return null;
        Console.WriteLine();
        Console.WriteLine($"== {title} ==");
        for (int i = 0; i < items.Count; i++)
            Console.WriteLine($"{i + 1,3}. {items[i]}");
        Console.Write("Choose (blank = back): ");
        var line = Console.ReadLine();
        if (line is null || !int.TryParse(line.Trim(), out var n) || n < 1 || n > items.Count)
            return null;
        return n - 1;
    }

    public void Message(string text)
    {
        Console.WriteLine();
        Console.WriteLine(text);
        Console.Write("[Enter] ");
        Console.ReadLine();
    }

    public bool Confirm(string text, string yes = "YES", string no = "NO")
    {
        Console.WriteLine();
        Console.WriteLine(text);
        Console.Write($"{yes}/{no}? (y/N) ");
        var line = Console.ReadLine();
        return line is not null && line.Trim().StartsWith('y');
    }

    public void Busy(string text) => Console.WriteLine($"... {text}");
}
