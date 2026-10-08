using System.Diagnostics;
using System.Text.Json;
using PKHeX.Core;
using PokemonManager.Core;

namespace PokemonManager.Ui;

/// <summary>
/// Runs pkmgr-box, the Gen 3 style PC box screen. If the viewer is missing or fails (for example on a
/// device it wasn't built for) the app falls back to its text lists.
/// </summary>
public sealed class BoxViewer(BoxScene scene, string tempDir)
{
    private const string Tool = "pkmgr-box";

    public enum Outcome { Picked, Back, Unavailable }

    public bool IsAvailable => scene.AssetsPresent && FindOnPath(Tool) is not null;

    public Outcome Pick(SaveFile sav, string title, ref SlotRef position)
    {
        Directory.CreateDirectory(tempDir);
        var scenePath = Path.Combine(tempDir, "box-scene.json");
        var outPath = Path.Combine(tempDir, "box-result.json");
        File.WriteAllText(scenePath, scene.Build(sav, title, position).ToJsonString());
        File.Delete(outPath);

        int code;
        try
        {
            var psi = new ProcessStartInfo(Tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("--scene");
            psi.ArgumentList.Add(scenePath);
            psi.ArgumentList.Add("--write-location");
            psi.ArgumentList.Add(outPath);
            using var p = Process.Start(psi);
            if (p is null)
                return Outcome.Unavailable;
            p.OutputDataReceived += static (_, _) => { };
            p.ErrorDataReceived += static (_, e) => { if (e.Data is { } line) Console.Error.WriteLine($"pkmgr-box: {line}"); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            code = p.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"pkmgr-box failed to start: {ex.Message}");
            return Outcome.Unavailable;
        }

        switch (code)
        {
            case 0 when File.Exists(outPath):
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
                    position = BoxScene.FromViewer(doc.RootElement.GetProperty("box").GetInt32(), doc.RootElement.GetProperty("slot").GetInt32());
                    return Outcome.Picked;
                }
                catch (Exception)
                {
                    return Outcome.Unavailable;
                }
            case 2 or 3:
                return Outcome.Back;
            default:
                Console.Error.WriteLine($"pkmgr-box exited with {code}; using the list view instead.");
                return Outcome.Unavailable;
        }
    }

    private static string? FindOnPath(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
