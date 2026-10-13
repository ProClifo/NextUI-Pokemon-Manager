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

    /// <summary>What the player chose on the PC screen: Transfer or Evolve on a Pokémon (Summary is shown by
    /// the viewer itself), leaving it, or the viewer couldn't run.</summary>
    public enum Outcome { Transfer, Evolve, Back, Unavailable }

    public bool IsAvailable => scene.AssetsPresent && FindOnPath(Tool) is not null;

    public Outcome Pick(SaveFile sav, string title, ref SlotRef position, UiFont? font = null, UiFont? fallbackFont = null,
        Func<SlotRef, (string? TransferBlocked, string? EvolveBlocked)>? actions = null, string? language = null, string? evolved = null)
    {
        Directory.CreateDirectory(tempDir);
        var scenePath = Path.Combine(tempDir, "box-scene.json");
        var outPath = Path.Combine(tempDir, "box-result.json");
        try
        {
            var built = scene.Build(sav, title, position, font, fallbackFont, actions, language);
            // after an evolution, the viewer shows the evolved Pokémon with this text before the box
            if (evolved is not null)
                built["evolved"] = evolved;
            File.WriteAllText(scenePath, built.ToJsonString());
            File.Delete(outPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Couldn't prepare the box view: {ex}");
            return Outcome.Unavailable;
        }

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
                    return doc.RootElement.TryGetProperty("action", out var action) && action.GetString() == "evolve"
                        ? Outcome.Evolve : Outcome.Transfer;
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

    public sealed record ClockResult(int Weekday, int Hour, int Minute);

    /// <summary>
    /// The Clock editor ("Sunday 08:41 PM"; weekday -1 for games without days): the chosen time, null for Back,
    /// or Unavailable = true when the viewer couldn't run.
    /// </summary>
    public ClockResult? EditClock(int weekday, int hour, int minute, string? fontPath, string? background, out bool unavailable)
    {
        unavailable = false;
        if (FindOnPath(Tool) is null)
        {
            unavailable = true;
            return null;
        }
        Directory.CreateDirectory(tempDir);
        var outPath = Path.Combine(tempDir, "clock-result.json");
        try
        {
            File.Delete(outPath);
            var psi = new ProcessStartInfo(Tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("--clock");
            psi.ArgumentList.Add($"{weekday},{hour},{minute}");
            psi.ArgumentList.Add("--write-location");
            psi.ArgumentList.Add(outPath);
            if (fontPath is not null)
            {
                psi.ArgumentList.Add("--font");
                psi.ArgumentList.Add(fontPath);
            }
            if (background is not null)
            {
                psi.ArgumentList.Add("--background");
                psi.ArgumentList.Add(background);
            }
            using var p = Process.Start(psi);
            if (p is null)
            {
                unavailable = true;
                return null;
            }
            p.OutputDataReceived += static (_, _) => { };
            p.ErrorDataReceived += static (_, e) => { if (e.Data is { } line) Console.Error.WriteLine($"pkmgr-box: {line}"); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            if (p.ExitCode is 2 or 3)
                return null;
            if (p.ExitCode != 0 || !File.Exists(outPath))
            {
                unavailable = true;
                return null;
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
            var root = doc.RootElement;
            return new ClockResult(root.GetProperty("weekday").GetInt32(), root.GetProperty("hour").GetInt32(), root.GetProperty("minute").GetInt32());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Clock editor failed: {ex.Message}");
            unavailable = true;
            return null;
        }
    }

    /// <summary>Whether <see cref="ShowCard"/> can show this game's Trainer Card.</summary>
    /// <summary>A Pokémon's box icon as its game draws it (the first of the sheet's two 32x32 frames), if the pak has it.</summary>
    public BoxScene.ArtImage? Icon(SaveFile sav, PKM pk)
        => scene.Icon(pk, BoxScene.ArtSets(sav), sav is SAV3FRLG { Version: GameVersion.LG }) is { } icon && File.Exists(icon.Sheet)
            ? icon with { H = 32 } : null;

    /// <summary>
    /// <paramref name="background"/> (or black) with the Pokémon's front sprite above the middle, for a message
    /// screen's background ("Inject …?"); null when the viewer or the sprite isn't there.
    /// </summary>
    public string? SpriteBackground(SaveFile sav, PKM pk, string? background)
    {
        if (FindOnPath(Tool) is null || scene.Sprite(pk, BoxScene.ArtSets(sav), sav is SAV3FRLG { Version: GameVersion.LG }) is not { } sprite
            || !File.Exists(sprite.Sheet))
            return null;
        try
        {
            Directory.CreateDirectory(tempDir);
            var output = Path.Combine(tempDir, "sprite-background.bmp");
            File.Delete(output);
            var psi = new ProcessStartInfo(Tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "--compose", output, "--sprite", sprite.Sheet, "--sprite-rect", $"{sprite.X},{sprite.Y},{sprite.W},{sprite.H}" })
                psi.ArgumentList.Add(arg);
            if (background is not null && File.Exists(background))
            {
                psi.ArgumentList.Add("--background");
                psi.ArgumentList.Add(background);
            }
            using var p = Process.Start(psi);
            if (p is null)
                return null;
            p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode == 0 && File.Exists(output))
                return output;
            Console.Error.WriteLine($"pkmgr-box --compose failed ({p.ExitCode}): {error.Trim()}");
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"pkmgr-box --compose failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// A YES / NO question with the answers side by side in the middle (pkmgr-box --dialog), NO highlighted
    /// first. True for YES, false for NO, null when the viewer couldn't show it.
    /// </summary>
    public bool? YesNo(string title, string message, BoxScene.ArtImage? icon, string? background)
    {
        if (FindOnPath(Tool) is null)
            return null;
        try
        {
            Directory.CreateDirectory(tempDir);
            var path = Path.Combine(tempDir, "dialog.json");
            var json = new System.Text.Json.Nodes.JsonObject { ["title"] = title, ["message"] = message, ["selected"] = 1 };
            if (icon is not null)
            {
                json["icon"] = icon.Sheet;
                json["icon_rect"] = new System.Text.Json.Nodes.JsonArray(icon.X, icon.Y, icon.W, icon.H);
            }
            if (background is not null && File.Exists(background))
                json["background"] = background;
            File.WriteAllText(path, json.ToJsonString());
            var psi = new ProcessStartInfo(Tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("--dialog");
            psi.ArgumentList.Add(path);
            using var p = Process.Start(psi);
            if (p is null)
                return null;
            p.OutputDataReceived += static (_, _) => { };
            p.ErrorDataReceived += static (_, e) => { if (e.Data is { } line) Console.Error.WriteLine($"pkmgr-box: {line}"); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            return p.ExitCode switch { 0 => true, 2 => false, _ => null };
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"pkmgr-box --dialog failed: {ex.Message}");
            return null;
        }
    }

    public bool HasCard(SaveFile sav) => FindOnPath(Tool) is not null && TrainerCard.Available(sav, scene.Skin(sav));

    /// <summary>
    /// Shows the game's Trainer Card (Gen 1-3, with the skin's card art). False when there's no card art for the
    /// game or the viewer couldn't run.
    /// </summary>
    public bool ShowCard(SaveFile sav, string? fontPath)
    {
        var skin = scene.Skin(sav);
        if (!TrainerCard.Available(sav, skin) || FindOnPath(Tool) is null)
            return false;
        Directory.CreateDirectory(tempDir);
        var cardPath = Path.Combine(tempDir, "card.json");
        try
        {
            File.WriteAllText(cardPath, TrainerCard.For(sav, skin, fontPath).ToJsonString());
            var psi = new ProcessStartInfo(Tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("--card");
            psi.ArgumentList.Add(cardPath);
            using var p = Process.Start(psi);
            if (p is null)
                return false;
            p.OutputDataReceived += static (_, _) => { };
            p.ErrorDataReceived += static (_, e) => { if (e.Data is { } line) Console.Error.WriteLine($"pkmgr-box: {line}"); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            return p.ExitCode is 0 or 2 or 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Trainer card failed: {ex.Message}");
            return false;
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
