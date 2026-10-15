using System.Text.Json.Nodes;
using PKHeX.Core;
using PokemonManager.Core;
using PokemonManager.Gen3;

namespace PokemonManager.Ui;

/// <summary>
/// The on-device menu tree.
/// </summary>
public sealed class App
{
    private const string Title = "Pokémon Manager";

    private const string SaveStateWarning =
        "Close the game before editing, and after editing start it fresh: don't resume from a save state, " +
        "or the old state will overwrite these changes the next time you save in-game.";

    private readonly IUi _ui;
    private readonly AppPaths _paths;
    private readonly SaveLibrary _library;
    private readonly AppSettings _settings;
    private readonly BoxViewer _boxViewer;
    private readonly Dictionary<string, SlotRef> _boxPositions = new();
    private List<SaveEntry>? _saves;
    /// <summary>Saves left off the main menu, and why.</summary>
    private List<(SaveEntry Save, string Reason)> _hidden = [];
    private SaveNames? _saveNames;
    private SaveNames SavedNames => _saveNames ??= new SaveNames(_paths.SaveNamesFile, _paths.SdRoot);
    private SavePlacement? _placement;
    private int _mainSelected;
    private SavePlacement Placement => _placement ??= new SavePlacement(_paths.SaveListsFile, _paths.OfficialSavesFile, _paths.SdRoot);
    private VanillaRoms.Index? _romIndex;
    private readonly GameBackgrounds _backgrounds;
    private readonly (int Width, int Height)? _screen =
        GameBackgrounds.ScreenSize(Environment.GetEnvironmentVariable("PLATFORM"), Environment.GetEnvironmentVariable("DEVICE"));
    private readonly Dictionary<string, string?> _backgroundCache = new();
    private readonly Dictionary<string, GameProfile> _profiles = new();
    private readonly Dictionary<string, List<TicketChoice>> _tickets = new();
    private readonly GalleryArchive _gallery;
    private readonly UiFont? _nextFont;
    /// <summary>After a Karrablast/Shelmet trade: the partner's evolved Pokémon, shown beside ours on the evolution screen.</summary>
    private JsonObject? _evolvedWith;
    private readonly UiFont? _ogFont;
    private readonly int _uiScale;

    public App(IUi ui, AppPaths paths)
    {
        _ui = ui;
        _paths = paths;
        _paths.EnsureCreated();
        _library = new SaveLibrary(paths.BackupDir);
        _boxViewer = new BoxViewer(new BoxScene(paths.BoxAssetsDir), paths.TempDir);
        BoxSummary.LoadAbilityDescriptions(Path.Combine(paths.BoxAssetsDir, "abilities.json"));
        _backgrounds = new GameBackgrounds(paths.BackgroundsDir);
        _gallery = new GalleryArchive(paths.GalleryFile);
        _uiScale = UiFont.Scale(Environment.GetEnvironmentVariable("PLATFORM"), Environment.GetEnvironmentVariable("DEVICE"));
        // NextUI's Next font on the main menu, its OG font inside a save.
        _nextFont = UiFont.Next(paths.SdRoot);
        _ogFont = UiFont.OG(paths.SdRoot);
        _ui.Font = _nextFont;
        _settings = AppSettings.Load(paths.SettingsFile);
    }

    public int Run()
    {
        if (!_settings.SeenWelcome)
        {
            if (!_boxViewer.Text(Title, HelpText))
                _ui.Message(HelpText);
            _settings.SeenWelcome = true;
            _settings.Save(_paths.SettingsFile);
        }
        else if (StartupUpdate())
            return 0;

        while (true)
        {
            var saves = GetSaves();
            // The game in its own language ("Emerald", "Smaragd"), with "NAME <player sprite> ID" on the right.
            var items = saves.Select(SaveName).ToList();
            var sprites = saves.Select(s => TrainerSprite(s.Sav)).ToList();
            var tags = saves.Select((s, i) => (string?)TrainerTag(s.Sav, sprites[i] is not null)).ToList();
            int hidden = _hidden.Count == 0 ? -1 : items.Count;
            if (hidden >= 0)
                items.Add($"{_hidden.Count} others");
            int settings = items.Count; items.Add("Settings");
            int update = items.Count; items.Add("Update");
            int help = items.Count; items.Add("Help");

            var icons = saves.Select(CartridgeIcon).ToList();
            // Y renames the highlighted save, SELECT moves it to Others (not on the menu's own rows)
            var noActions = items.Select((_, i) => i >= saves.Count).ToList();
            var choice = _ui.Choose(saves.Count == 0 ? $"{Title} - no saves found" : $"{Title} - choose a save", items, _mainSelected, tags: tags, images: sprites, icons: icons,
                actionText: "RENAME", secondaryText: "MOVE", noActions: noActions);
            if (choice is null)
                return 0;
            _mainSelected = choice.Value;
            if (choice < saves.Count && _ui.ActionPressed) { RenameSave(saves[choice.Value]); continue; }
            if (choice < saves.Count && _ui.SecondaryPressed) { MoveSave(saves[choice.Value], toMain: false); continue; }
            if (choice == hidden) { ShowHidden(); continue; }
            if (choice == settings) { SettingsMenu(); continue; }
            if (choice == update) { if (UpdateFlow()) return 0; continue; }
            if (choice == help) { HelpMenu(); continue; }
            SaveMenu(saves[choice.Value]);
        }
    }

    /// <summary>
    /// Update: the newest GitHub release replaces this pak's files when it's not the one installed. Returns true
    /// when it did (the app then closes, so the new version starts next time).
    /// </summary>
    private bool UpdateFlow()
    {
        var platform = Environment.GetEnvironmentVariable("PLATFORM");
        if (string.IsNullOrEmpty(platform))
        {
            _ui.Message("Updates are only available on the device.");
            return false;
        }
        var updater = NewUpdater();
        Updater.Release release;
        try
        {
            _ui.Busy("Checking for updates...");
            release = updater.Latest(platform);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"update check failed: {ex}");
            _ui.Message($"Couldn't check for updates.\n{ex.Message}\n\nUpdates need Wi-Fi: turn it on in NextUI's settings.");
            return false;
        }
        var installed = updater.InstalledTag;
        if (release.Tag == installed)
        {
            _ui.Message($"Pokémon Manager is up to date ({installed}).");
            return false;
        }
        if (release.AssetUrl is null)
        {
            _ui.Message($"{release.Name} has no download for this device ({platform}).");
            return false;
        }
        var size = release.AssetSize > 0 ? $" ({release.AssetSize / (1024 * 1024)} MB)" : "";
        if (!_ui.Confirm($"Update Pokémon Manager?\n\nInstalled: {installed}\nNew: {release.Name}{size}", "UPDATE", "CANCEL"))
            return false;
        return Install(updater, release, platform);
    }

    /// <summary>
    /// On startup (on the device, with Wi-Fi connected): offers a newer release when there is one. Says nothing
    /// when it's up to date or GitHub can't be reached quickly. Returns true when it updated (the app then closes).
    /// </summary>
    private bool StartupUpdate()
    {
        var platform = Environment.GetEnvironmentVariable("PLATFORM");
        if (string.IsNullOrEmpty(platform) || !Updater.MaybeOnline())
            return false;
        var updater = NewUpdater();
        var installed = updater.InstalledTag;
        if (installed == "dev")
            return false;
        Updater.Release release;
        try
        {
            release = updater.Latest(platform, TimeSpan.FromSeconds(4));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"startup update check: {ex.Message}");
            return false;
        }
        if (release.Tag == installed || release.AssetUrl is null)
            return false;
        var size = release.AssetSize > 0 ? $" ({release.AssetSize / (1024 * 1024)} MB)" : "";
        if (!_ui.Confirm($"An update is available.\n\nInstalled: {installed}\nNew: {release.Name}{size}", "UPDATE", "LATER"))
            return false;
        return Install(updater, release, platform);
    }

    private Updater NewUpdater() =>
        new(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..")), Path.Combine(_paths.DataDir, "update"));

    private bool Install(Updater updater, Updater.Release release, string platform)
    {
        try
        {
            _ui.Busy($"Downloading {release.Name}...\nThis can take a few minutes.");
            int files = updater.Install(release, platform);
            Console.Error.WriteLine($"update: installed {files} files from {release.Tag}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"update failed: {ex}");
            _ui.Message($"The update didn't finish.\n{ex.Message}\n\nDetails are in the log.");
            return false;
        }
        _ui.Message($"Updated to {release.Name}.\nPokémon Manager will close now: open it again to use the new version.");
        return true;
    }

    private List<SaveEntry> GetSaves()
    {
        if (_saves is not null)
            return _saves;
        _ui.Busy("Looking for Pokémon saves...");
        var all = SaveLibrary.Scan(_paths.SaveRoots);
        _hidden = [];
        _romIndex = null;
        _backgroundCache.Clear();
        _profiles.Clear();
        _tickets.Clear();
        _cardLegal.Clear();
        // Only saves with their game's ROM (same file name), in games that have got as far as receiving the
        // Pokédex, are shown at all; "N others" lists those whose ROM isn't an official release.
        all.RemoveAll(s => RomIndex.FindRoms(s.Path).Count == 0 || !TradeRules.HasPokedex(s.Sav));
        if (all.Count != 0)
        {
            _ui.Busy("Checking ROMs are official...\n(The first check of each ROM can take a while.)");
            List<OfficialRomFilter.Hidden> unofficial;
            (all, unofficial) = OfficialRomFilter.Apply(all, RomIndex);
            _hidden.AddRange(unofficial.Select(h => (h.Save, h.Check.Describe())));
        }
        // ...unless the player has moved them between the lists with SELECT
        var toMain = _hidden.Where(h => Placement.OnMainList(h.Save.Path) == true).ToList();
        _hidden.RemoveAll(h => Placement.OnMainList(h.Save.Path) == true);
        all.AddRange(toMain.Select(h => h.Save));
        foreach (var s in all.Where(s => Placement.OnMainList(s.Path) == false).ToList())
        {
            all.Remove(s);
            _hidden.Add((s, "moved to Others"));
        }
        _saves = SaveOrder.ByLastPlayed(all, RomIndex.FindRoms, SaveOrder.ReadRecents(_paths.RecentFile, _paths.SdRoot));
        return _saves;
    }

    private VanillaRoms.Index RomIndex => _romIndex ??= new VanillaRoms.Index(_paths.RomRoots, _paths.RomCheckCache);

    /// <summary>The save's game art, worked out from its ROM header the first time the save is opened.</summary>
    private string? BackgroundFor(SaveEntry entry)
    {
        if (!_backgroundCache.TryGetValue(entry.Path, out var background))
        {
            try
            {
                background = _backgrounds.Find(entry.Sav, entry.Path, RomIndex.FindRoms(entry.Path), _screen);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Couldn't pick a background: {ex.Message}");
                background = null;
            }
            _backgroundCache[entry.Path] = background;
        }
        return background;
    }

    /// <summary>
    /// The saves that aren't listed (no ROM, no Pokédex yet, an unofficial ROM), shown like the main menu's;
    /// picking one opens its menu, as on the main menu.
    /// </summary>
    private void ShowHidden()
    {
        int selected = 0;
        while (_hidden.Count > 0)
        {
            selected = Math.Min(selected, _hidden.Count - 1);
            var saves = _hidden.Select(h => h.Save).ToList();
            var sprites = saves.Select(s => TrainerSprite(s.Sav)).ToList();
            var tags = saves.Select((s, i) => (string?)TrainerTag(s.Sav, sprites[i] is not null)).ToList();
            var choice = _ui.Choose("Other saves", saves.Select(SaveName).ToList(), selected, tags: tags, images: sprites,
                icons: saves.Select(CartridgeIcon).ToList(), actionText: "RENAME", secondaryText: "MOVE");
            if (choice is not { } c)
                return;
            selected = c;
            if (_ui.ActionPressed)
            {
                RenameSave(saves[c]);
                continue;
            }
            if (_ui.SecondaryPressed)
            {
                MoveSave(saves[c], toMain: true);
                continue;
            }
            Console.Error.WriteLine($"opening {_hidden[c].Save.FileName}, not listed: {_hidden[c].Reason}");
            SaveMenu(_hidden[c].Save);
        }
    }

    // ---------------------------------------------------------------- save menu

    private void SaveMenu(SaveEntry entry)
    {
        // A save's menus are drawn in NextUI's OG font.
        var previousFont = _ui.Font;
        _ui.Font = _ogFont;
        // ...over the game's title art, which stays behind its sub-menus and messages (not Pokémon storage).
        var previousBackground = _ui.Background;
        _ui.Background = BackgroundFor(entry);
        // ...and with the game's BUTTON MODE: L=A, L works as A in them
        _ui.LIsA = BoxScene.LIsA(entry.Sav);
        try
        {
            SaveMenuLoop(entry);
        }
        finally
        {
            _ui.Font = previousFont;
            _ui.Background = previousBackground;
            _ui.LIsA = false;
        }
    }

    private void SaveMenuLoop(SaveEntry entry)
    {
        int selected = 0;
        while (true)
        {
            var sav = entry.Sav;
            Legality.For(sav); // legality reports in this save's menus are for this game and trainer
            var actions = new List<(string Label, string? Tag, Action Run)> { ("Pokémon", null, () => BrowseMenu(entry)) };
            // A game with one event gets that event on its menu; a game with several gets a Mystery Gift menu.
            // Each is hidden until the game's own menu that delivered it (Mystery Gift, Mystery Event) is unlocked.
            // Each ticket goes away once it's been received properly, and the Mystery Gift menu with the last one.
            var tickets = TicketsFor(entry)
                .Where(t => Tickets.Locked(t, sav) is null && !Tickets.Received(t, sav))
                .ToList();
            // Emerald's Record Mixing Eon Ticket isn't a Mystery Gift: it has its own entry.
            if (tickets.Find(t => t.File is null) is { } eon)
            {
                tickets.Remove(eon);
                actions.Add((eon.Ticket.Name, LegalTag(eon.Legal), () => GiveTicket(entry, eon)));
            }
            // Crystal's GS Ball, until its event is enabled
            if (sav is SAV2 { Version: GameVersion.C } crystal)
            {
                if (!crystal.IsEnabledGSBallMobileEvent)
                    actions.Add(("GS Ball", LegalTag(crystal.Japanese), () => GsBallFlow(entry)));
            }
            else if (tickets.Count == 1)
                actions.Add((tickets[0].Ticket.Name, LegalTag(tickets[0].Legal), () => GiveTicket(entry, tickets[0])));
            else if (tickets.Count > 1)
                actions.Add(("Mystery Gift", null, () => EventsMenu(entry, tickets)));
            // Gold/Silver/Crystal's daily Mystery Gift, once unlocked in Goldenrod
            if (sav is SAV2 gsc && MysteryGift2.Unlocked(gsc))
                actions.Add(("Mystery Gift", null, () => DailyMysteryGift(entry)));
            // Gen 1/2's event-only Pokémon get their own entries (Gen 1/2's other distributions are in the Gallery):
            // Mew in Gen 1 (and Korean Gen 2, which can't link with Gen 1), Celebi in Gen 2 outside Japan, where the
            // GS Ball never ran on cartridges.
            if (sav is SAV1 || sav is SAV2 { Korean: true })
                actions.Add(("Mew", LegalTag(true), () => GiveGameBoyEvent(entry, "Mew", GiftService.Mew)));
            if (sav is SAV2 { Japanese: false })
                actions.Add(("Celebi", LegalTag(true), () => GiveGameBoyEvent(entry, "Celebi", GiftService.Celebi)));
            if (sav.Generation >= 3)
                actions.Add(("Distributions", null, () => DistributionsMenu(entry)));
            actions.Add(("Events Gallery", null, () => GalleryMenu(entry)));
            // Ruby/Sapphire/Emerald, once Record Mixing (Mauville City) is open
            if (Decorations.Supported(sav) && Decorations.RecordMixingUnlocked((SAV3)sav))
                actions.Add(("Decorations", null, () => DecorationsMenu(entry)));
            actions.Add(("Info", null, () => InfoMenu(entry)));

            // Headed like the save on the main menu: "[ENG] Emerald   NAME <player sprite> ID".
            var choice = _ui.Choose($"{SaveName(entry)}\t{sav.OT}\t{sav.DisplayTID:D5}", actions.Select(a => a.Label).ToList(), selected,
                BackgroundFor(entry), actions.Select(a => a.Tag).ToList(), titleImage: TrainerSprite(sav));
            if (choice is null)
                return;
            selected = choice.Value;
            RunSafely(actions[choice.Value].Run, entry);
        }
    }

    /// <summary>Y on a saves list: a name of the player's own (an empty one brings back the game's name).</summary>
    private void RenameSave(SaveEntry save)
    {
        if (_ui.Keyboard("Rename save", SaveName(save)) is not { } name)
            return;
        try
        {
            SavedNames.Set(save.Path, name == GameName(save) ? null : name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"couldn't save the name: {ex}");
            _ui.Message($"Couldn't save the name.\n{ex.Message}");
        }
    }

    /// <summary>SELECT on a saves list: moves the save between the main list and Others, remembered from now on.</summary>
    private void MoveSave(SaveEntry save, bool toMain)
    {
        try
        {
            Placement.Set(save.Path, toMain);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"couldn't save the move: {ex}");
            _ui.Message($"Couldn't move the save.\n{ex.Message}");
            return;
        }
        var saves = GetSaves();
        if (toMain)
        {
            _hidden.RemoveAll(h => h.Save == save);
            _saves = SaveOrder.ByLastPlayed([.. saves, save], RomIndex.FindRoms, SaveOrder.ReadRecents(_paths.RecentFile, _paths.SdRoot));
        }
        else
        {
            _saves = saves.Where(s => s != save).ToList();
            _hidden.Add((save, "moved to Others"));
        }
    }

    /// <summary>
    /// Runs a menu action; an unexpected error is shown and logged instead of closing the app.
    /// The save is reloaded from disk so nothing half-done stays in memory.
    /// </summary>
    private void RunSafely(Action action, SaveEntry entry)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            entry.Reload();
            _ui.Message($"Something went wrong: {ex.Message}\nNothing was saved. Details are in the log.");
        }
    }

    /// <summary>Info, headed with the save's file name: its details, the Clock and the Trainer Card for the games with them.</summary>
    private void InfoMenu(SaveEntry entry)
    {
        int selected = 0;
        while (true)
        {
            var actions = new List<(string Label, Action Run)> { ("Details", () => _ui.Message(SaveInfo(entry))) };
            var tags = new List<string?> { null };
            if (GameClock.Supported(entry.Sav))
            {
                actions.Add(("Clock", () => ClockMenu(entry)));
                tags.Add(ClockText(entry));
            }
            if (_boxViewer.HasCard(entry.Sav))
                actions.Add(("Trainer Card", () =>
                {
                    if (!_boxViewer.ShowCard(entry.Sav, _ogFont?.Path))
                        _ui.Message("The Trainer Card couldn't be shown.");
                }));
            while (tags.Count < actions.Count)
                tags.Add(null);
            // the game's time beside Clock on every row, not just the highlighted one (images: none, so the tags
            // show on all rows)
            var choice = _ui.Choose(Path.GetFileName(entry.Path), actions.Select(a => a.Label).ToList(), selected,
                tags: tags, images: actions.Select(_ => (string?)null).ToList());
            if (choice is null)
                return;
            selected = choice.Value;
            actions[choice.Value].Run();
        }
    }

    /// <summary>
    /// Ruby/Sapphire/Emerald's decorations, by category as the PC lists them; one can be sent to another of
    /// these games (not while it's placed in the player's room or Secret Base).
    /// </summary>
    private void DecorationsMenu(SaveEntry entry)
    {
        var sav = (SAV3)entry.Sav;
        if (!Decorations.RecordMixingUnlocked(sav))
        {
            _ui.Message("Decorations can be sent once Record Mixing is open: visit Mauville City first.");
            return;
        }
        int category = 0;
        while (true)
        {
            var labels = Decorations.Categories.Select((c, i) => $"{c} ({Decorations.Count(sav, i)}/{Decorations.Capacity(i)})").ToList();
            var pick = _ui.Choose("Decorations", labels, category);
            if (pick is null)
                return;
            category = pick.Value;
            DecorationList(entry, category);
        }
    }

    private void DecorationList(SaveEntry entry, int category)
    {
        int selected = 0;
        while (true)
        {
            var sav = (SAV3)entry.Sav;
            var owned = Decorations.List(sav, category);
            if (owned.Count == 0)
            {
                _ui.Message($"There are no {Decorations.Categories[category].ToLowerInvariant()}s in the PC.");
                return;
            }
            // Placed decorations are greyed out: they'd have to be put away in-game first.
            var labels = owned.Select(d => d.InUse ? $"{d.Name} (in use)" : d.Name).ToList();
            var pick = _ui.Choose($"{Decorations.Categories[category]}: send which?", labels, Math.Min(selected, owned.Count - 1),
                disabled: owned.Select(d => d.InUse).ToList());
            if (pick is null)
                return;
            selected = pick.Value;
            if (!owned[pick.Value].InUse)
                SendDecoration(entry, owned[pick.Value]);
        }
    }

    private void SendDecoration(SaveEntry entry, Decorations.Owned decoration)
    {
        var targets = GetSaves().Where(s => s.Path != entry.Path && s.Sav is SAV3 to && Decorations.Supported(to)
                                            && Decorations.RecordMixingUnlocked(to) && Decorations.HasRoom(to, decoration.Category)).ToList();
        if (targets.Count == 0)
        {
            _ui.Message("No other Ruby, Sapphire or Emerald save with Record Mixing (Mauville City visited) has room for it.");
            return;
        }
        var sprites = targets.Select(s => TrainerSprite(s.Sav)).ToList();
        var tags = targets.Select((s, i) => (string?)TrainerTag(s.Sav, sprites[i] is not null)).ToList();
        var choice = _ui.Choose($"Send the {decoration.Name} to which game?", targets.Select(SaveName).ToList(), 0, tags: tags, images: sprites,
            icons: targets.Select(CartridgeIcon).ToList());
        if (choice is not { } c)
            return;
        var dest = targets[c];
        if (!_ui.Confirm($"Send the {decoration.Name} from {Names.Game(entry.Sav)} ({entry.Sav.OT})\nto {Names.Game(dest.Sav)} ({dest.Sav.OT})?", "SEND", "CANCEL"))
            return;
        var result = Decorations.Send((SAV3)entry.Sav, decoration, (SAV3)dest.Sav);
        if (!result.Ok)
        {
            entry.Reload();
            dest.Reload();
            _ui.Message(result.Message);
            return;
        }
        if (!ConfirmStateLoss(dest, entry))
            return;
        // The receiving game first: if the sender then fails, the decoration is duplicated, not lost.
        if (!TryWrite(dest, out _))
        {
            entry.Reload();
            return;
        }
        if (!TryWrite(entry, out _))
        {
            _ui.Message("The decoration arrived, but this save couldn't be updated, so both games have it.");
            return;
        }
        _ui.Message($"{result.Message}\n\n{SaveStateWarning}");
    }

    private static string SaveInfo(SaveEntry entry)
    {
        var sav = entry.Sav;
        var lines = new List<string>
        {
            $"Game: {Names.Game(sav)} (Gen {sav.Generation})",
            $"Trainer: {sav.OT}  ID: {sav.DisplayTID:D5}",
            $"Play time: {sav.PlayedHours}:{sav.PlayedMinutes:00}",
            $"Party: {sav.PartyCount}  Boxes: {sav.BoxCount}",
            $"File: {entry.Path}",
        };
        if (sav is SAV3 sav3)
        {
            lines.Add("");
            lines.Add(Gen3Events.Status(sav3));
        }
        return string.Join('\n', lines);
    }

    // ---------------------------------------------------------------- browsing

    private SlotRef? PickSlot(SaveEntry entry, string title, Func<PKM, bool>? filter = null)
    {
        var sav = entry.Sav;
        int boxSelected = 0;
        while (true)
        {
            var boxes = new List<int> { SlotRef.PartyBox };
            for (int b = 0; b < sav.BoxCount; b++)
                boxes.Add(b);
            var labels = boxes.Select(b => b == SlotRef.PartyBox
                ? $"Party ({sav.PartyCount}/6)"
                : $"{SlotRef.BoxName(sav, b)} ({CountBox(sav, b)}/{sav.BoxSlotCount})").ToList();
            var boxChoice = _ui.Choose($"{title}: choose a box", labels, boxSelected);
            if (boxChoice is null)
                return null;
            boxSelected = boxChoice.Value;
            int box = boxes[boxChoice.Value];

            var slots = new List<SlotRef>();
            int max = box == SlotRef.PartyBox ? sav.PartyCount : sav.BoxSlotCount;
            for (int i = 0; i < max; i++)
            {
                var slot = box == SlotRef.PartyBox ? SlotRef.Party(i) : new SlotRef(box, i);
                var pk = slot.Get(sav);
                if (pk.Species == 0 || (filter is not null && !filter(pk)))
                    continue;
                slots.Add(slot);
            }
            if (slots.Count == 0)
            {
                _ui.Message("No Pokémon here.");
                continue;
            }
            var slotLabels = slots.Select(s => $"{s.Slot + 1}. {Names.Summary(s.Get(sav))}").ToList();
            var slotChoice = _ui.Choose(labels[boxChoice.Value], slotLabels);
            if (slotChoice is null)
                continue;
            return slots[slotChoice.Value];
        }
    }

    private static int CountBox(SaveFile sav, int box)
    {
        int n = 0;
        for (int i = 0; i < sav.BoxSlotCount; i++)
        {
            if (sav.GetBoxSlotAtIndex(box, i).Species != 0)
                n++;
        }
        return n;
    }

    /// <summary>Pokémon storage: the PC box screen or the lists, without the game's background.</summary>
    private void BrowseMenu(SaveEntry entry)
    {
        var background = _ui.Background;
        _ui.Background = null;
        try
        {
            BrowseStorage(entry);
        }
        finally
        {
            _ui.Background = background;
        }
    }

    private void BrowseStorage(SaveEntry entry)
    {
        if (_settings.PcBoxView && _boxViewer.IsAvailable)
        {
            var position = _boxPositions.GetValueOrDefault(entry.Path, new SlotRef(0, 0));
            string? evolved = null;
            while (true)
            {
                var with = _evolvedWith;
                var outcome = _boxViewer.Pick(entry.Sav, entry.Label, ref position, _ogFont, _nextFont, ActionsFor(entry),
                    ProfileFor(entry).ShownLanguage ?? ProfileFor(entry).Language, evolved,
                    with is null ? null : scene => scene["evolved_with"] = with.DeepClone());
                evolved = null;
                _evolvedWith = null;
                _boxPositions[entry.Path] = position;
                if (outcome == BoxViewer.Outcome.Back)
                    return;
                if (outcome == BoxViewer.Outcome.Unavailable)
                {
                    // Lists for this save this time; the PC is tried again next time (the reason is in the log).
                    break;
                }
                // The viewer has its own Transfer/Summary/Evolve/Cancel menu, like the game's PC.
                // the viewer asked "Evolve <nickname>?" itself, and shows the result when it reopens
                if (outcome == BoxViewer.Outcome.Evolve)
                    EvolveFlow(entry, position, confirmed: true, out evolved);
                else
                    TransferFlow(entry, position, TransferMode.Move);
            }
        }

        while (true)
        {
            var slot = PickSlot(entry, "Pokémon");
            if (slot is not { } s)
                return;
            PokemonMenu(entry, s);
        }
    }

    private void PokemonMenu(SaveEntry entry, SlotRef slot)
    {
        while (true)
        {
            var pk = slot.Get(entry.Sav);
            if (pk.Species == 0)
                return; // moved away or slot compacted
            // Transfer moves the Pokémon (no copies: that would be a clone); Evolve is a trade evolution.
            // Transfer/Evolve are greyed out when they can't be done; picking one then does nothing.
            var actions = new List<(string Label, bool Enabled, Func<bool> Run)>
            {
                ("Transfer", CanTransfer(entry, slot), () => TransferFlow(entry, slot, TransferMode.Move)),
                ("Summary", true, () => { _ui.Message(Names.Details(pk)); return false; }),
                ("Evolve", CanEvolve(entry, slot), () => EvolveFlow(entry, slot, confirmed: false, out _)),
                ("Cancel", true, () => true),
            };

            var choice = _ui.Choose($"{slot}: {Names.Summary(pk)}", actions.Select(a => a.Label).ToList(),
                disabled: actions.Select(a => !a.Enabled).ToList());
            if (choice is null)
                return;
            if (!actions[choice.Value].Enabled)
                continue;
            bool slotChanged = actions[choice.Value].Run();
            if (slotChanged)
                return;
        }
    }

    // ---------------------------------------------------------------- transfers

    /// <summary>
    /// The saves the Pokémon can go to, shown like the main menu ("[ENG] Emerald" with NAME, sprite and ID).
    /// Transfer is greyed out when there are none, so the list is never empty in practice.
    /// </summary>
    private SaveEntry? PickOtherSave(SaveEntry source, string title, SlotRef from)
    {
        var targets = GetSaves().Where(s => s.Path != source.Path && CanTransferTo(source, from, s)).ToList();
        if (targets.Count == 0)
        {
            _ui.Message("No other game can take this Pokémon.");
            return null;
        }
        var sprites = targets.Select(s => TrainerSprite(s.Sav)).ToList();
        var tags = targets.Select((s, i) => (string?)TrainerTag(s.Sav, sprites[i] is not null)).ToList();
        // the Pokémon's box icon in front of the question
        var icon = _boxViewer.Icon(source.Sav, from.Get(source.Sav));
        var choice = _ui.Choose(icon is null ? title : "\t\t" + title, targets.Select(SaveName).ToList(), 0, tags: tags, images: sprites,
            icons: targets.Select(CartridgeIcon).ToList(), titleImage: icon?.Sheet, titleImageRect: icon is null ? null : (icon.X, icon.Y, icon.W, icon.H),
            confirmText: "SEND");
        return choice is { } c ? targets[c] : null;
    }

    /// <summary>
    /// Transfer/Evolve availability for every slot of a save, for the PC viewer's menu. Whether another save
    /// can take a Pokémon is worked out once per kind of Pokémon, so a full PC stays quick.
    /// </summary>
    private Func<SlotRef, (string? TransferBlocked, string? EvolveBlocked)> ActionsFor(SaveEntry entry)
    {
        var others = GetSaves().Where(s => s.Path != entry.Path).ToList();
        var accepted = new Dictionary<(ushort, byte, bool, bool, int, GameVersion, (ushort, ushort, ushort, ushort)), bool>();
        return slot =>
        {
            var pk = slot.Get(entry.Sav);
            // moves too: HMs block Pal Park and Poké Transfer, Gen 2 moves the Time Capsule
            var key = (pk.Species, pk.Form, pk.IsEgg, pk.FatefulEncounter, pk.HeldItem, pk.Version, (pk.Move1, pk.Move2, pk.Move3, pk.Move4));
            if (!accepted.TryGetValue(key, out bool anywhere))
                accepted[key] = anywhere = others.Any(s => CanTransferTo(entry, slot, s));
            var move = TransferService.CanMove(entry.Sav, slot);
            string? transfer = (slot.IsParty ? ActionReasons.PartyTrade(entry.Sav, pk, evolve: false) : null)
                ?? (move.Ok ? null : slot.IsParty && entry.Sav.PartyCount <= 1 ? ActionReasons.LastPartyPokemon
                    : move.Message.Contains("last party", StringComparison.Ordinal) ? ActionReasons.LastPartyPokemon : ActionReasons.CantBeMoved)
                ?? (others.Count == 0 ? ActionReasons.NoOtherGames : anywhere ? null : ActionReasons.NoGamesToSendTo);
            // Karrablast/Shelmet: always offered; the trade partner is picked from the other games after YES
            string? evolve = ActionReasons.Evolve(pk, partnerAvailable: true)
                ?? (slot.IsParty ? ActionReasons.PartyTrade(entry.Sav, pk, evolve: true) : null);
            return (transfer, evolve);
        };
    }

    /// <summary>Whether the Pokémon can be moved to <paramref name="dest"/>, following the games' trade rules.</summary>
    private bool CanTransferTo(SaveEntry source, SlotRef from, SaveEntry dest)
    {
        var pk = from.Get(source.Sav);
        if (!_settings.AllowIllegalTransfers
            && TradeRules.CheckTransfer(pk, source.Sav, dest.Sav, ProfileFor(source).Language, ProfileFor(dest).Language) is { Ok: false })
            return false;
        return TransferService.Prepare(pk, dest.Sav, _settings.AllowIllegalTransfers, out _).Ok
               && SlotRef.FirstEmptyBoxSlot(dest.Sav) is not null;
    }

    /// <summary>Whether Transfer can be used: the Pokémon may leave its slot and some other save can take it.</summary>
    private bool CanTransfer(SaveEntry entry, SlotRef slot)
    {
        if (!TransferService.CanMove(entry.Sav, slot).Ok)
            return false;
        if (slot.IsParty && TradeLocation.PartyTradeBlocked(entry.Sav, slot.Get(entry.Sav)) is not null)
            return false;
        return GetSaves().Any(s => s.Path != entry.Path && CanTransferTo(entry, slot, s));
    }

    /// <summary>Whether Evolve can be used: a trade evolution whose conditions (held item...) are met, no Everstone.</summary>
    private static bool CanEvolveNow(PKM pk)
        => !TradeEvolution.HoldsEverstone(pk) && TradeEvolution.GetOptions(pk).Any(o => o.ConditionsMet);

    private bool CanEvolve(SaveEntry entry, SlotRef slot)
    {
        var pk = slot.Get(entry.Sav);
        return ActionReasons.Evolve(pk, partnerAvailable: true) is null && !(slot.IsParty && TradeLocation.PartyTradeBlocked(entry.Sav, pk) is not null);
    }

    /// <summary>A question in lines the highlight skips, then YES / NO with NO highlighted. True for YES.</summary>
    private bool AskYesNo(string question, string title, string? titleImage = null, (int X, int Y, int W, int H)? titleImageRect = null)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in question.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > 26)
            {
                lines.Add(line);
                line = word;
            }
            else
                line = line.Length == 0 ? word : $"{line} {word}";
        }
        if (line.Length > 0)
            lines.Add(line);
        var items = lines.Concat(["YES", "NO"]).ToList();
        var choice = _ui.Choose(title, items, items.Count - 1, titleImage: titleImage, titleImageRect: titleImageRect,
            unselectable: items.Select((_, i) => i < lines.Count).ToList());
        return choice == items.Count - 2;
    }

    /// <returns>True if the source slot no longer holds this Pokémon.</returns>
    private bool TransferFlow(SaveEntry source, SlotRef from, TransferMode mode)
    {
        if (PartyTradeBlocked(source, from))
            return false;
        var dest = PickOtherSave(source, mode == TransferMode.Move ? "Move to which game?" : "Copy to which game?", from);
        if (dest is null)
            return false;

        var pk = from.Get(source.Sav);
        if (!_settings.AllowIllegalTransfers
            && TradeRules.CheckTransfer(pk, source.Sav, dest.Sav, ProfileFor(source).Language, ProfileFor(dest).Language) is { Ok: false } refused)
        {
            _ui.Message(refused.Message);
            return false;
        }
        var check = TransferService.Prepare(pk, dest.Sav, _settings.AllowIllegalTransfers, out _);
        if (!check.Ok)
        {
            _ui.Message(check.Message);
            return false;
        }

        // A newer generation's game can't send it back (Pal Park and Poké Transfer only go forward)
        if (source.Sav.Generation >= 3 && dest.Sav.Generation > source.Sav.Generation)
        {
            // headed "<icon> Ruby → Platinum", like the game list
            var icon = _boxViewer.Icon(source.Sav, pk);
            var title = $"{SaveName(source)} → {SaveName(dest)}";
            // YES / NO side by side in the middle (the viewer), else as a list
            var answer = _boxViewer.YesNo(title, "WARNING: This action is irreversible.\nContinue?", icon, null)
                ?? AskYesNo("WARNING: This action is irreversible. Continue?", icon is null ? title : "\t\t" + title,
                    icon?.Sheet, icon is null ? null : (icon.X, icon.Y, icon.W, icon.H));
            if (!answer)
                return false;
        }

        // Picking the game is the confirmation: it's done at once, then "<NICKNAME> was sent to <PLAYER>."
        var result = TransferService.Transfer(source, from, dest, mode, _settings.AllowIllegalTransfers, out var placedAt);
        if (!result.Ok)
        {
            source.Reload();
            dest.Reload();
            _ui.Message(result.Message);
            return false;
        }

        var message = result.Message;
        if (check.Message.Length != 0)
            message += " " + check.Message;
        // Ruby/Sapphire get the National Pokédex from a trade partner that has it.
        if (TradeRules.UnlockNationalDexByTrade(source.Sav, dest.Sav) is { } unlocked)
            message += $" {Names.Game(unlocked)} received the National Pokédex.";

        if (!ConfirmStateLoss(mode == TransferMode.Move ? new[] { dest, source } : new[] { dest }))
        {
            source.Reload();
            return false;
        }
        // Write the destination first: if the source write then fails, nothing is lost (only duplicated).
        if (!TryWrite(dest, out var destBackup))
        {
            source.Reload();
            return false;
        }
        if (mode == TransferMode.Move && !TryWrite(source, out _))
        {
            _ui.Message("The Pokémon was added to the destination, but the source save couldn't be updated, so it now exists in both.");
            return true;
        }

        Console.Error.WriteLine($"transfer: {message}");
        // an egg by the game's word for it (Gen 3 stores every egg as "タマゴ")
        var sent = pk.IsEgg ? (pk.Format <= 3 ? UiStrings.EggName(pk.Format).ToUpperInvariant() : UiStrings.EggName(pk.Format)) : pk.Nickname;
        _ui.Message($"{sent} was sent to {dest.Sav.OT}.");
        return mode == TransferMode.Move;
    }

    /// <summary>Party Pokémon trade only where the game allows it (a Pokémon Center); says why not.</summary>
    private bool PartyTradeBlocked(SaveEntry entry, SlotRef slot)
    {
        if (!slot.IsParty || TradeLocation.PartyTradeBlocked(entry.Sav, slot.Get(entry.Sav)) is not { } reason)
            return false;
        _ui.Message(reason);
        return true;
    }

    // ---------------------------------------------------------------- clock

    /// <summary>The game's clock: the time of day, and in Gen 2 the day of the week (left/right to change).</summary>
    /// <summary>The cartridge clock file NextUI's gambatte core keeps next to the save (Gold/Silver/Crystal).</summary>
    private string? RtcPath(SaveEntry entry)
    {
        // minarch names it like the save, after the ROM file ("Crystal.gbc.rtc"; for a zip, the file inside it)
        var name = Path.GetFileNameWithoutExtension(entry.Path);
        if (Path.GetExtension(name).ToLowerInvariant() is ".gb" or ".gbc")
            return Path.ChangeExtension(entry.Path, ".rtc");
        return RomIndex.FindRoms(entry.Path).FirstOrDefault() is { } rom
            ? Path.Combine(Path.GetDirectoryName(entry.Path)!, Path.GetFileName(rom) + ".rtc")
            : null;
    }

    /// <summary>The game's time as the Clock editor shows it: "08:41 PM", or "Sunday 08:41 PM" with weekdays.</summary>
    private string? ClockText(SaveEntry entry)
    {
        try
        {
            var t = GameClock.Read(entry.Sav, RtcPath(entry), ClockNow(entry.Sav));
            int h12 = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
            var time = $"{h12:00}:{t.Minute:00} {(t.Hour >= 12 ? "PM" : "AM")}";
            return GameClock.HasWeekdays(entry.Sav) ? $"{GameClock.Weekdays[t.Weekday % 7]} {time}" : time;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// "Now" for the game's clock: Gen 3's cartridge clock is the device's local time as libc gives it (the
    /// emulator's view, which .NET's can differ from); Gen 2's counts Unix seconds, which DateTime.Now gives right.
    /// </summary>
    private static DateTime ClockNow(SaveFile sav) => sav is SAV3 ? DeviceClock.LocalNow() : DateTime.Now;

    private void ClockMenu(SaveEntry entry)
    {
        var sav = entry.Sav;
        var rtcPath = RtcPath(entry);
        var now = GameClock.Read(sav, rtcPath, ClockNow(sav));
        bool weekdays = GameClock.HasWeekdays(sav);
        // "Sunday 08:41 PM": Left/Right pick a part, Up/Down change it (the viewer's Clock editor)
        var edited = _boxViewer.EditClock(weekdays ? now.Weekday : -1, now.Hour, now.Minute, _ogFont?.Path, BackgroundFor(entry), out bool unavailable);
        if (!unavailable)
        {
            if (edited is not null)
                SetClock(entry, rtcPath, edited.Weekday < 0 ? 0 : edited.Weekday, edited.Hour, edited.Minute);
            return;
        }
        var names = new List<string>();
        var options = new List<string[]>();
        var values = new List<int>();
        if (weekdays)
        {
            names.Add("Day");
            options.Add(GameClock.Weekdays);
            values.Add(now.Weekday);
        }
        names.Add("Hour");
        options.Add(Enumerable.Range(0, 24).Select(h => $"{h:00}").ToArray());
        values.Add(now.Hour);
        names.Add("Minute");
        options.Add(Enumerable.Range(0, 60).Select(m => $"{m:00}").ToArray());
        values.Add(now.Minute);

        if (_ui.Options("Clock", names, options, values) is not { } result)
            return;
        int i = weekdays ? 1 : 0;
        SetClock(entry, rtcPath, weekdays ? result.Values[0] : 0, result.Values[i], result.Values[i + 1]);
    }

    /// <summary>
    /// Resuming a save state brings back the game's memory as it was, with the old clock in it; the new time
    /// shows only when the game starts from its save. Offers to delete the state (unless Save State Deletion
    /// already did on writing).
    /// </summary>
    private void OfferResumeStateDeletion(SaveEntry entry)
    {
        if (_settings.SaveStateDeletion || _paths.SdRoot is not { } sd)
            return;
        var (states, draStic) = FindSaveStates(sd, entry);
        if (states.Count == 0 && !draStic)
            return;
        if (_ui.Confirm($"{Names.Game(entry.Sav)} has a save state it would resume from, which keeps the old clock.\n" +
                "Delete it so the game starts from its save with the new time?", "DELETE", "KEEP"))
        {
            foreach (var (rom, root) in states)
                SaveStates.DeleteSelected(sd, root, rom);
            if (draStic)
                SaveStates.DeleteDraSticAuto(sd, entry.Path, Environment.GetEnvironmentVariable("PLATFORM"));
        }
    }

    /// <summary>The save states resuming this save's game would load: NextUI's selected slot, DraStic's auto state.</summary>
    private (List<(string Rom, string Root)> States, bool DraStic) FindSaveStates(string sd, SaveEntry entry)
    {
        var states = RomIndex.FindRoms(entry.Path)
            .SelectMany(rom => _paths.RomRoots.Select(root => (rom, root)))
            .Where(r => SaveStates.HasSelected(sd, r.root, r.rom))
            .ToList();
        bool draStic = entry.Sav.Generation >= 4 && SaveStates.HasDraSticAuto(sd, entry.Path, Environment.GetEnvironmentVariable("PLATFORM"));
        return (states, draStic);
    }

    /// <summary>
    /// Before a change is written: with Save State Deletion and Save State Warning on, and a save state that writing
    /// would delete, asks first (No by default). "Yes, don't warn me again" turns Save State Warning off. On No the
    /// saves are put back as they are on disk, and false is returned.
    /// </summary>
    private bool ConfirmStateLoss(params SaveEntry[] entries)
    {
        if (!_settings.SaveStateDeletion || !_settings.SaveStateWarning || _paths.SdRoot is not { } sd)
            return true;
        if (!entries.Any(e => FindSaveStates(sd, e) is var (states, draStic) && (states.Count > 0 || draStic)))
        {
            // for reports of a missing warning: where the states were looked for
            foreach (var e in entries)
                foreach (var rom in RomIndex.FindRoms(e.Path))
                    foreach (var root in _paths.RomRoots)
                        Console.Error.WriteLine($"save state: none for {rom} (emulator {SaveStates.EmuName(root, rom) ?? "?"}, roms {root})");
            return true;
        }
        const string message = "This will delete your current save state.\nContinue?";
        string[] answers = ["Yes, don't warn me again", "Yes", "No"];
        var title = string.Join(" & ", entries.Select(e => Names.Game(e.Sav)).Distinct());
        int? pick = _boxViewer.Choose(title, message, answers, 2);
        if (pick is null)
            pick = _ui.Choose(message.Replace("\n", " "), answers.ToList(), 2);
        if (pick == 0)
        {
            _settings.SaveStateWarning = false;
            _settings.Save(_paths.SettingsFile);
        }
        if (pick is 0 or 1)
            return true;
        foreach (var e in entries)
            e.Reload();
        return false;
    }

    private void SetClock(SaveEntry entry, string? rtcPath, int weekday, int hour, int minute)
    {
        var now = ClockNow(entry.Sav);
        Console.Error.WriteLine($"clock: set {hour:00}:{minute:00} at device {now:yyyy-MM-dd HH:mm:ss} (.NET local {DateTime.Now:HH:mm:ss}, " +
            $"UTC {DateTime.UtcNow:HH:mm:ss}, TZ={Environment.GetEnvironmentVariable("TZ") ?? "-"})");
        var rtc = GameClock.Set(entry.Sav, now, weekday, hour, minute, rtcPath);
        if (!ConfirmStateLoss(entry) || !TryWrite(entry, out _))
            return;
        OfferResumeStateDeletion(entry);
        if (rtc is not null && rtcPath is not null)
        {
            try
            {
                File.WriteAllBytes(rtcPath, rtc);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ui.Message($"Couldn't set the cartridge clock ({ex.Message}).");
            }
        }
    }

    // ---------------------------------------------------------------- trade evolution

    /// <param name="confirmed">The PC viewer already asked "Evolve &lt;nickname&gt;?".</param>
    /// <param name="evolvedText">"Congratulations! Your &lt;NICKNAME&gt; evolved into &lt;POKEMON&gt;!" for the viewer to
    /// show; without the viewer (<paramref name="confirmed"/> false) it's shown as a message.</param>
    private bool EvolveFlow(SaveEntry entry, SlotRef slot, bool confirmed, out string? evolvedText)
    {
        evolvedText = null;
        if (PartyTradeBlocked(entry, slot))
            return false;
        var pk = slot.Get(entry.Sav);
        if (PartnerTrade.Applies(pk))
            return PartnerTradeFlow(entry, slot, pk, confirmed, out evolvedText);
        // Only evolutions a real trade would trigger: Onix must hold the Metal Coat to become Steelix.
        var options = TradeEvolution.GetOptions(pk).Where(o => o.ConditionsMet).ToList();
        if (options.Count == 0 || TradeEvolution.HoldsEverstone(pk))
            return false;

        var option = options[0];
        if (options.Count > 1)
        {
            var pick = _ui.Choose($"Evolve {Names.Species(pk)} into...", options.Select(o => o.Label).ToList());
            if (pick is null)
                return false;
            option = options[pick.Value];
        }

        if (!confirmed && !_ui.Confirm(UiStrings.EvolveQuestion("en", pk.Nickname), "YES", "NO"))
            return false;

        var nickname = pk.Nickname;
        // names as the game itself writes them, in its own language
        int gameLanguage = UiStrings.LanguageId(UiStrings.Code(ProfileFor(entry).ShownLanguage ?? ProfileFor(entry).Language, entry.Sav));
        var result = TradeEvolution.Evolve(entry.Sav, slot, option, gameLanguage);
        if (!result.Ok)
        {
            entry.Reload();
            _ui.Message(result.Message);
            return false;
        }
        if (!ConfirmStateLoss(entry) || !TryWrite(entry, out _))
            return false;
        Console.Error.WriteLine($"evolve: {result.Message}");
        // the species as the game writes it (KADABRA in Gen 1-4, Kadabra in Gen 5)
        var evolvedPk = slot.Get(entry.Sav);
        var species = SpeciesName.GetSpeciesNameGeneration(evolvedPk.Species, gameLanguage, entry.Sav.Generation);
        // each game's own words and line breaks ("\f" a new page); the species in the game's language
        evolvedText = UiStrings.Evolved("en", entry.Sav.Generation, nickname, species);
        if (!confirmed)
            _ui.Message(evolvedText.Replace("\f", "\n"));
        return false;
    }

    /// <summary>
    /// Karrablast/Shelmet: pick a game with the other one ("trade for Shelmet"), then the Pokémon in its storage
    /// (only the partner species can be traded), and both are traded and evolve. Returns true when the slot now holds
    /// a different Pokémon; <paramref name="evolvedText"/> is "Congratulations! Your Pokémon evolved into ...!" for
    /// the viewer, which shows both evolved Pokémon (<see cref="_evolvedWith"/>).
    /// </summary>
    private bool PartnerTradeFlow(SaveEntry entry, SlotRef slot, PKM pk, bool confirmed, out string? evolvedText)
    {
        evolvedText = null;
        if (ActionReasons.Evolve(pk, partnerAvailable: true) is { } blocked)
        {
            _ui.Message(blocked);
            return false;
        }
        if (!confirmed && !_ui.Confirm(UiStrings.EvolveQuestion("en", pk.Nickname), "YES", "NO"))
            return false;
        var wanted = Names.Species((ushort)PartnerTrade.PartnerSpecies(pk));
        var partners = PartnerTrade.Find(pk, GetSaves().Where(s => s.Path != entry.Path));
        if (partners.Count == 0)
        {
            _ui.Message(ActionReasons.Evolve(pk) ?? $"No {wanted} to trade with!");
            return false;
        }
        var games = partners.Select(p => p.Save).DistinctBy(s => s.Path).ToList();
        var header = $"trade for {wanted}";
        var icon = _boxViewer.Icon(entry.Sav, pk);
        var sprites = games.Select(s => TrainerSprite(s.Sav)).ToList();
        var tags = games.Select((s, i) => (string?)TrainerTag(s.Sav, sprites[i] is not null)).ToList();
        int selected = 0;
        while (true)
        {
            var choice = _ui.Choose(icon is null ? header : "\t\t" + header, games.Select(SaveName).ToList(), selected, tags: tags, images: sprites,
                icons: games.Select(CartridgeIcon).ToList(), titleImage: icon?.Sheet, titleImageRect: icon is null ? null : (icon.X, icon.Y, icon.W, icon.H));
            if (choice is null)
                return false;
            selected = choice.Value;
            var game = games[selected];
            var inGame = partners.Where(p => p.Save.Path == game.Path).ToList();
            var partner = PickPartner(game, inGame, pk, entry, header, icon);
            if (partner is null)
                continue;
            return CompleteTrade(entry, slot, pk, partner, confirmed, out evolvedText);
        }
    }

    /// <summary>
    /// The partner in <paramref name="game"/>'s storage: the PC screen with "trade for ..." in its message box and only
    /// TRADE / CANCEL in its menu (TRADE only on the partner species), or a list of them. Null for back.
    /// </summary>
    private PartnerTrade.Partner? PickPartner(SaveEntry game, List<PartnerTrade.Partner> partners, PKM pk, SaveEntry entry, string header,
        BoxScene.ArtImage? icon)
    {
        if (_settings.PcBoxView && _boxViewer.IsAvailable)
        {
            var position = partners[0].Slot;
            var iconJson = _boxViewer.IconJson(entry.Sav, pk);
            var tradable = partners.Select(p => BoxScene.ToViewer(p.Slot)).ToHashSet();
            var notThis = $"Only {Names.Species((ushort)PartnerTrade.PartnerSpecies(pk))} can be traded!";
            var outcome = _boxViewer.Pick(game.Sav, game.Label, ref position, _ogFont, _nextFont, null,
                ProfileFor(game).ShownLanguage ?? ProfileFor(game).Language, decorate: scene =>
                {
                    var trade = new JsonObject { ["header"] = header };
                    if (iconJson is not null)
                    {
                        trade["icon"] = iconJson["icon"]!.DeepClone();
                        trade["icon_rect"] = iconJson["icon_rect"]!.DeepClone();
                    }
                    scene["trade"] = trade;
                    var boxes = scene["boxes"]!.AsArray();
                    for (int b = 0; b < boxes.Count; b++)
                    {
                        var slots = boxes[b]!["slots"]!.AsArray();
                        for (int i = 0; i < slots.Count; i++)
                        {
                            if (slots[i] is not JsonObject so || !so.ContainsKey("name"))
                                continue;
                            bool can = tradable.Contains((b, i));
                            so["can_trade"] = can;
                            if (!can)
                                so["trade_why"] = notThis;
                        }
                    }
                });
            if (outcome == BoxViewer.Outcome.Trade)
                return partners.FirstOrDefault(p => p.Slot == position);
            if (outcome != BoxViewer.Outcome.Unavailable)
                return null;
        }
        if (partners.Count == 1)
            return partners[0];
        var labels = partners.Select(p => $"{Names.Summary(p.Pokemon)} ({p.Slot.Describe(p.Save.Sav)})").ToList();
        var pick = _ui.Choose(icon is null ? header : "\t\t" + header, labels, titleImage: icon?.Sheet,
            titleImageRect: icon is null ? null : (icon.X, icon.Y, icon.W, icon.H), confirmText: "TRADE");
        return pick is { } c ? partners[c] : null;
    }

    /// <summary>Trades and evolves both, writes both saves (undoing the first if the second fails).</summary>
    private bool CompleteTrade(SaveEntry entry, SlotRef slot, PKM pk, PartnerTrade.Partner partner, bool viewer, out string? evolvedText)
    {
        evolvedText = null;
        var result = PartnerTrade.Trade(entry, slot, partner, _settings.AllowIllegalTransfers);
        if (!result.Ok)
        {
            entry.Reload();
            partner.Save.Reload();
            _ui.Message(result.Message);
            return false;
        }
        if (!ConfirmStateLoss(partner.Save, entry))
            return false;
        // A swap: if the second write fails, the partner's save is put back from its backup, or its Pokémon is lost.
        if (!TryWrite(partner.Save, out var partnerBackup))
        {
            entry.Reload();
            return false;
        }
        if (!TryWrite(entry, out _))
        {
            try
            {
                File.Copy(partnerBackup, partner.Save.Path, overwrite: true);
                partner.Save.Reload();
                _ui.Message($"The trade was undone: {SaveName(partner.Save)} was put back as it was.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"couldn't restore {partner.Save.Path}: {ex}");
                _ui.Message($"The trade was saved in {SaveName(partner.Save)} but not in this game.\nRestore {partner.Save.FileName} from PokemonManager/Backups/{Path.GetFileName(partnerBackup)}.");
            }
            return false;
        }
        Console.Error.WriteLine($"partner trade: {result.Message}");
        // both evolved Pokémon, named as the game writes them: ours first
        int gameLanguage = UiStrings.LanguageId(UiStrings.Code(ProfileFor(entry).ShownLanguage ?? ProfileFor(entry).Language, entry.Sav));
        var mine = slot.Get(entry.Sav);
        var theirs = partner.Slot.Get(partner.Save.Sav);
        string Name(PKM p) => SpeciesName.GetSpeciesNameGeneration(p.Species, gameLanguage, entry.Sav.Generation);
        evolvedText = $"Congratulations! Your Pokémon evolved into {Name(mine)} and {Name(theirs)}!";
        if (viewer)
            _evolvedWith = _boxViewer.SpriteJson(partner.Save.Sav, theirs);
        else
            _ui.Message(evolvedText);
        return true;
    }

    // ---------------------------------------------------------------- gifts & events

    private void InjectGen3(SaveEntry entry, Gen3EventFile file, string name)
    {
        if (entry.Sav is not SAV3 sav3)
            return;
        if (!_ui.Confirm($"Inject {name} into {Names.Game(sav3)}?", "INJECT", "CANCEL"))
            return;

        var result = Gen3Events.Inject(sav3, file);
        if (!result.Ok)
        {
            entry.Reload();
            _ui.Message(result.Message);
            return;
        }
        if (ConfirmStateLoss(entry) && TryWrite(entry, out _))
            _ui.Message(file.Kind switch
            {
                Gen3EventKind.WonderCard or Gen3EventKind.WonderNews => "Mystery Gift received!",
                Gen3EventKind.MysteryEvent => "Mystery Event received!",
                _ => "e-Card received!",
            });
    }

    /// <summary>
    /// Puts a Mystery Gift card into the save's album, to be picked up from the delivery man in-game as if it had
    /// come over Wi-Fi. Its Pokémon only ever arrive that way, never straight into the PC.
    /// </summary>
    private void GiftActions(SaveEntry entry, MysteryGift gift, string name)
    {
        var sav = entry.Sav;
        if (gift is not DataMysteryGift data || !GiftService.SupportsAlbum(sav))
        {
            _ui.Message($"{Names.Game(sav)} has no Mystery Gift album, so this card can't be added.");
            return;
        }
        if (!_ui.Confirm($"Inject {name} into {Names.Game(sav)}?", "INJECT", "CANCEL"))
            return;
        var result = GiftService.InjectCard(sav, data);
        if (!result.Ok)
        {
            entry.Reload();
            _ui.Message(result.Message);
            return;
        }
        if (ConfirmStateLoss(entry) && TryWrite(entry, out _))
            _ui.Message("Mystery Gift received!");
    }

    // ---------------------------------------------------------------- events, distributions, gallery

    private GameProfile ProfileFor(SaveEntry entry)
    {
        if (!_profiles.TryGetValue(entry.Path, out var profile))
            _profiles[entry.Path] = profile = GameProfile.For(entry.Sav, RomIndex.FindRoms(entry.Path));
        return profile;
    }

    private bool GalleryAvailable()
    {
        if (_gallery.Entries.Count != 0)
            return true;
        _ui.Message("The event gallery is missing from this copy of the pak (res/gallery.zip). Reinstall Pokémon Manager.");
        return false;
    }

    private static string LegalTag(bool legal) => legal ? "Legal" : "Illegal";

    /// <summary>
    /// The player's overworld sprite in this save's game, boy or girl (scripts/build-trainers.py), or an empty
    /// path where there's none: Black/White have no decompilation to take it from.
    /// </summary>
    /// <summary>A save as the menus name it: the name given to it on the ROM hacks list, else its game in the game's own language ("Emerald", "Smaragd").</summary>
    private string SaveName(SaveEntry save) => SavedNames.Get(save.Path) ?? GameName(save);

    private string GameName(SaveEntry save)
    {
        var profile = ProfileFor(save);
        return Names.NativeGame(profile.Version(save.Sav), profile.ShownLanguage ?? profile.Language);
    }

    /// <summary>The cartridge drawn in front of a save's name, in its game's colours (scripts/build-carts.py).</summary>
    private string? CartridgeIcon(SaveEntry save)
    {
        var name = ProfileFor(save).Version(save.Sav) switch
        {
            GameVersion.RD => "red", GameVersion.GN => "green", GameVersion.BU => "blue", GameVersion.YW => "yellow",
            GameVersion.GD => "gold", GameVersion.SI => "silver", GameVersion.C => "crystal",
            GameVersion.R => "ruby", GameVersion.S => "sapphire", GameVersion.E => "emerald",
            GameVersion.FR => "firered", GameVersion.LG => "leafgreen",
            GameVersion.D => "diamond", GameVersion.P => "pearl", GameVersion.Pt => "platinum",
            GameVersion.HG => "heartgold", GameVersion.SS => "soulsilver",
            GameVersion.B => "black", GameVersion.W => "white", GameVersion.B2 => "black2", GameVersion.W2 => "white2",
            // a save that doesn't say which of the pair it is gets the first's
            GameVersion.RB or GameVersion.RBY => "red", GameVersion.GS or GameVersion.GSC => "gold",
            GameVersion.RS or GameVersion.RSE => "ruby", GameVersion.FRLG => "firered", GameVersion.DP => "diamond",
            GameVersion.HGSS => "heartgold", GameVersion.BW => "black", GameVersion.B2W2 => "black2",
            _ => null,
        };
        if (name is null)
            return null;
        // the Japanese Red and Blue cartridges are grey
        var profile = ProfileFor(save);
        if (name is "red" or "blue" && (profile.ShownLanguage ?? profile.Language) == GalleryLanguage.Japanese)
            name += "-jp";
        var path = Path.Combine(_paths.IconsDir, $"cart-{name}.png");
        return File.Exists(path) ? path : null;
    }

    /// <summary>"NAME\tID", the tab marking where the player sprite goes (just spaced without one).</summary>
    private static string TrainerTag(SaveFile sav, bool withSprite) => $"{sav.OT}{(withSprite ? "\t" : "  ")}{sav.DisplayTID:D5}";

    private string? TrainerSprite(SaveFile sav)
    {
        var set = sav switch
        {
            SAV1 { Version: GameVersion.YW } => "y",
            SAV1 => "rb",
            SAV2 { Version: GameVersion.C } => "c",
            SAV2 => "gs",
            SAV3E => "e",
            SAV3FRLG => "frlg",
            SAV3 => "rs",
            SAV4DP => "dp",
            SAV4Pt => "pt",
            SAV4HGSS => "hgss",
            _ => null,
        };
        if (set is null)
            return null;
        var gender = sav.Gender == 1 ? "f" : "m";
        foreach (var name in new[] { $"{set}-{gender}", $"{set}-m" }) // Red/Blue/Yellow and Gold/Silver have only a boy
        {
            var path = Path.Combine(_paths.TrainersDir, $"{name}-{_uiScale}x.png");
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    private List<TicketChoice> TicketsFor(SaveEntry entry)
    {
        if (!_tickets.TryGetValue(entry.Path, out var tickets))
        {
            try
            {
                tickets = _gallery.Entries.Count == 0 ? [] : Tickets.For(_gallery, ProfileFor(entry), entry.Sav);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Couldn't list event tickets: {ex}");
                tickets = [];
            }
            _tickets[entry.Path] = tickets;
        }
        return tickets;
    }

    /// <summary>Every ticket the game has, legitimate or not.</summary>
    private void EventsMenu(SaveEntry entry, List<TicketChoice> tickets)
    {
        int selected = 0;
        while (true)
        {
            var choice = _ui.Choose("Mystery Gift", tickets.Select(t => t.Ticket.Name).ToList(), selected, tags: tickets.Select(t => (string?)LegalTag(t.Legal)).ToList());
            if (choice is null)
                return;
            selected = choice.Value;
            GiveTicket(entry, tickets[choice.Value]);
        }
    }

    private void GiveTicket(SaveEntry entry, TicketChoice ticket)
    {
        // (an illegal one says so in the menu)
        if (ticket.File is { } file)
        {
            GiveGalleryFile(entry, file, ticket.Ticket.Name);
            return;
        }
        if (entry.Sav is not SAV3E emerald)
            return;
        if (!_ui.Confirm($"Inject {ticket.Ticket.Name} into {Names.Game(emerald)}?", "INJECT", "CANCEL"))
            return;
        var result = Gen3Events.GiveEonTicketByRecordMixing(emerald);
        if (!result.Ok)
        {
            entry.Reload();
            _ui.Message(result.Message);
            return;
        }
        if (ConfirmStateLoss(entry) && TryWrite(entry, out _))
            _ui.Message("Eon Ticket is now in your bag!");
    }

    private void DistributionsMenu(SaveEntry entry)
    {
        if (!GalleryAvailable())
            return;
        var profile = ProfileFor(entry);
        // Only released distributions that can be given legally to this save are listed.
        _ui.Busy("Checking distributions...");
        var list = GalleryLists.Distributions(_gallery, profile)
            .Where(e => EventPokemon.CanGive(_gallery, e, entry.Sav))
            .Select(e => (Title: GalleryLists.MenuName(e), Tag: (string?)"Legal", Run: (Action)(() => GiveGalleryFile(entry, e, GalleryLists.MenuName(e))))).ToList();
        // Ruby/Sapphire/Emerald also get the Regi dolls, which no normal play hands out (decorations can't be illegal),
        // as many as wanted, once Record Mixing is open (Mauville City visited)
        if (entry.Sav is SAV3RS or SAV3E && Decorations.RecordMixingUnlocked((SAV3)entry.Sav))
            list.AddRange(Gen3Events.RegiDolls.Select(d => (d.Name, Tag: (string?)null, Run: (Action)(() => GiveDoll(entry, d.Id)))));
        if (list.Count == 0)
        {
            _ui.Message($"Every Pokémon the gallery has for {Names.Game(entry.Sav)} in {GalleryLanguage.Name(profile.Language)} can be obtained without an event. See the Events Gallery for all distributions.");
            return;
        }
        int selected = 0;
        while (true)
        {
            var choice = _ui.Choose($"Distributions ({list.Count})", list.Select(e => e.Title).ToList(), selected,
                tags: list.Select(e => e.Tag).ToList());
            if (choice is null)
                return;
            selected = choice.Value;
            list[choice.Value].Run();
        }
    }

    private void GiveDoll(SaveEntry entry, byte doll)
    {
        var sav = (SAV3)entry.Sav;
        if (TradeRules.CheckDistribution(sav) is { Ok: false } noDex)
        {
            _ui.Message(noDex.Message);
            return;
        }
        var name = Gen3Events.RegiDolls.First(d => d.Id == doll).Name;
        if (!_ui.Confirm($"Inject the {name} into {Names.Game(sav)}?", "INJECT", "CANCEL"))
            return;
        var result = Gen3Events.GiveDoll(sav, doll);
        if (!result.Ok)
        {
            entry.Reload();
            _ui.Message(result.Message);
            return;
        }
        if (ConfirmStateLoss(entry) && TryWrite(entry, out _))
            _ui.Message($"The {name.ToUpperInvariant()} has arrived in your PC!");
    }

    private void GalleryMenu(SaveEntry entry)
    {
        if (!GalleryAvailable())
            return;
        var profile = ProfileFor(entry);
        var files = _gallery.Entries
            .Where(e => profile.Matches(e, _settings.GalleryAllLanguages, _settings.GalleryUnreleased))
            .ToList();
        if (files.Count == 0)
        {
            _ui.Message($"The Events Gallery has nothing for {Names.Game(entry.Sav)} in {GalleryLanguage.Name(profile.Language)}.");
            return;
        }
        GalleryFolder(entry, files, "", profile);
    }

    /// <summary>Browses one gallery folder. Folders that only lead to one other folder are skipped through.</summary>
    private void GalleryFolder(SaveEntry entry, List<GalleryEntry> files, string folder, GameProfile profile)
    {
        var view = GalleryTree.Open(files, folder);
        int selected = 0;
        while (true)
        {
            var labels = view.Folders.Select(f => $"{GalleryTree.Name(f)}/").ToList();
            labels.AddRange(view.Items.Select(GalleryLabel));
            // Language folders say nothing once the list is filtered to one language.
            var path = string.Join('/', view.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => _settings.GalleryAllLanguages || !GalleryLanguage.Tags.Contains(p)));
            var title = path.Length == 0 ? "Events Gallery" : $"Events Gallery: {path}";
            var tags = view.Folders.Select(_ => (string?)null)
                .Concat(view.Items.Select(i => (string?)LegalTag(i.Copies.Any(f => IsLegalFor(f, entry))))).ToList();
            var choice = _ui.Choose(title, labels, selected, tags: tags);
            if (choice is null)
                return;
            selected = choice.Value;
            if (choice < view.Folders.Count)
                GalleryFolder(entry, files, view.Folders[choice.Value], profile);
            else
                GiveGalleryFile(entry, PickCopy(view.Items[choice.Value - view.Folders.Count], entry));
        }
    }

    /// <summary>
    /// Illegal only when the Pokémon it gives would be: Pokémon files by PKHeX's verdict on the file (they're
    /// regenerated for the save and checked again before being added), Pokémon cards by whether PKHeX can make a
    /// legal one for this save, tickets by whether PKHeX accepts the Pokémon they lead to in this game. Items that
    /// give no Pokémon (berries, decorations, news...) are legal.
    /// </summary>
    private bool IsLegalFor(GalleryEntry e, SaveEntry entry)
    {
        if (e.Kind == GalleryKind.Pokemon)
            return e.IsFileLegal;
        if (Tickets.ForEntry(e) is { } ticket)
            return Tickets.PokemonLegal(ticket.Key, entry.Sav);
        if (e.Kind != GalleryKind.Card)
            return true;
        var key = (entry.Path, e.Path);
        if (!_cardLegal.TryGetValue(key, out var legal))
        {
            try
            {
                legal = EventPokemon.CanGive(_gallery, e, entry.Sav);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"couldn't check {e.Path}: {ex.Message}");
                legal = false;
            }
            _cardLegal[key] = legal;
        }
        return legal;
    }

    private readonly Dictionary<(string Save, string File), bool> _cardLegal = [];

    /// <summary>One of an item's copies at random, a legal one when there is one (the copies differ only in
    /// IDs and the like, and Pokémon are regenerated for the save anyway).</summary>
    private GalleryEntry PickCopy(GalleryTree.Item item, SaveEntry entry)
    {
        var legal = item.Copies.Where(f => IsLegalFor(f, entry)).ToList();
        var pool = legal.Count > 0 ? legal : item.Copies;
        return pool[Random.Shared.Next(pool.Count)];
    }

    private string GalleryLabel(GalleryTree.Item item)
        => _settings.GalleryAllLanguages && item.Language is { } language ? $"{item.Title} - {language}" : item.Title;

    /// <param name="name">What it's called in the question (the menu's name for it), else the gallery's.</param>
    private void GiveGalleryFile(SaveEntry entry, GalleryEntry file, string? name = null)
    {
        name ??= GalleryNames.DisplayTitle(file.Title);
        // a ticket the game already has (the item and its event's flag) isn't given again
        if (Tickets.ForEntry(file) is { } ticket && Tickets.Received(new TicketChoice(ticket, file, true), entry.Sav))
        {
            _ui.Message(GiftService.AlreadyInjected);
            return;
        }
        var gift = _gallery.Load(file);
        if (gift is null)
        {
            _ui.Message("That gallery file couldn't be read.");
            return;
        }
        if (gift.Pokemon is not null)
            GiveEventPokemon(entry, file, name);
        else if (gift.Card is { } card)
            GiftActions(entry, card, name);
        else if (gift.Gen3 is { } g3)
        {
            if (entry.Sav is not SAV3 sav3 || !Gen3Events.IsApplicable(sav3, g3))
            {
                _ui.Message($"{name} can't be added to {Names.Game(entry.Sav)}: it's for a different game or language version.");
                return;
            }
            InjectGen3(entry, g3, name);
        }
    }

    /// <summary>
    /// Gives a gallery Pokémon the way the distribution did: a new Pokémon (PID, nature, IVs...) generated
    /// for this save, which must pass PKHeX's legality check.
    /// </summary>
    private void GiveEventPokemon(SaveEntry entry, GalleryEntry file, string name)
    {
        if (TradeRules.CheckDistribution(entry.Sav) is { Ok: false } noDex)
        {
            _ui.Message(noDex.Message);
            return;
        }
        _ui.Busy($"Generating {name}...");
        var result = EventPokemon.FromGallery(_gallery, file, entry.Sav);
        if (result.Pokemon is not { } pk)
        {
            _ui.Message(result.Message);
            return;
        }
        // an unreleased file that's illegal is marked so in the menu, and added like any other
        if (!result.Legal && file.Released)
        {
            _ui.Message($"PKHeX flags {name} as illegal in {Names.Game(entry.Sav)}, so it wasn't added.\n\n{result.Message}");
            return;
        }
        // No trade rules: the real distributions didn't need the National Pokédex (only the Pokédex, checked above).
        PlaceConverted(entry, pk);
    }

    /// <summary>Asks, then puts a distributed Pokémon in the first free PC slot (callers check the Pokédex).</summary>
    private void PlaceConverted(SaveEntry entry, PKM pk)
    {
        var sav = entry.Sav;
        var target = SlotRef.ForDistribution(sav);
        if (target.Value is not { } slot)
        {
            _ui.Message(target.Message);
            return;
        }
        // the Pokémon's sprite above the question, over the game's background
        var background = _ui.Background;
        _ui.Background = _boxViewer.SpriteBackground(sav, pk, background) ?? background;
        bool inject;
        try
        {
            inject = _ui.Confirm(Names.InjectQuestion(pk, sav));
        }
        finally
        {
            _ui.Background = background;
        }
        if (!inject)
            return;
        slot.Set(sav, pk);
        if (ConfirmStateLoss(entry) && TryWrite(entry, out _))
            _ui.Message(Names.Arrived(pk));
    }

    /// <summary>A Game Boy era event Pokémon generated for this save (see <see cref="GiftService.Mew"/> and <see cref="GiftService.Celebi"/>).</summary>
    private void GiveGameBoyEvent(SaveEntry entry, string name, Func<SaveFile, Random?, PKM?> generate)
    {
        if (TradeRules.CheckDistribution(entry.Sav) is { Ok: false } noDex)
        {
            _ui.Message(noDex.Message);
            return;
        }
        if (generate(entry.Sav, null) is not { } pk)
        {
            _ui.Message($"PKHeX couldn't generate a legal {name} for {Names.Game(entry.Sav)}.");
            return;
        }
        PlaceConverted(entry, pk);
    }

    private void GsBallFlow(SaveEntry entry)
    {
        if (entry.Sav is not SAV2 sav)
            return;
        if (sav.IsEnabledGSBallMobileEvent)
        {
            _ui.Message("The GS Ball event is already enabled.");
            return;
        }
        if (!_ui.Confirm($"Enable the GS Ball event in {Names.Game(sav)}?", "ENABLE", "CANCEL"))
            return;
        sav.EnableGSBallMobileEvent();
        if (ConfirmStateLoss(entry) && TryWrite(entry, out _))
            _ui.Message("The GS BALL event has been enabled!");
    }

    /// <summary>The game day Gen 2's Mystery Gift counts its five a day by (its own clock, not the device's).</summary>
    private int GiftDay(SaveEntry entry) => GameClock.Day((SAV2)entry.Sav, RtcPath(entry), ClockNow(entry.Sav));

    /// <summary>One gift, as the game's Mystery Gift gives them: an item waits at the Pokémon Center, a decoration goes home.</summary>
    private void DailyMysteryGift(SaveEntry entry)
    {
        if (entry.Sav is not SAV2 sav)
            return;
        // no question when the game would turn the gift down, just its reason, checked in its order (DoMysteryGift)
        if (MysteryGift2.Left(sav, GiftDay(entry)) == 0)
        {
            _ui.Message("Sorry--only five\ngifts a day.");
            return;
        }
        if (MysteryGift2.ItemWaiting(sav))
        {
            _ui.Message("Must receive Mystery Gift\nat Pokémon Center.");
            return;
        }
        if (!_ui.Confirm("Inject random Mystery Gift?"))
            return;
        if (MysteryGift2.Receive(sav, GiftDay(entry), Random.Shared) is not { } gift)
            return;
        if (!ConfirmStateLoss(entry) || !TryWrite(entry, out _))
            return;
        _ui.Message(gift.Decoration ? $"{gift.Name} sent home!" : $"{gift.Name} sent to Pokémon Center!");
    }

    // ---------------------------------------------------------------- files

    // ---------------------------------------------------------------- settings & help

    private void SettingsMenu()
    {
        // Like NextUI's own settings: the name on the left, Enabled/Disabled on the right (left/right to change).
        string[] onOff = ["Disabled", "Enabled"];
        var names = new List<string>
        {
            "Illegal Transfers", "PC Box View", "Save State Deletion", "Save State Warning",
            "Gallery: All Languages", "Gallery: Unreleased Files", "Reset to Default",
        };
        // shown under the list for the highlighted setting, as in NextUI's in-game menu
        string[] descriptions =
        [
            "Move Pokémon between any games PKHeX can convert,\nskipping the games' own trade requirements.\nThey'll usually be flagged as illegal.",
            "Browse Pokémon on each game's own PC screen.\nOff lists them instead.",
            "Delete the game's save state after every change,\nso resuming it can't undo the change.",
            "Ask before a change deletes a save state.",
            "List Events Gallery files in every language,\nnot just the game's.",
            "Add debug and test files that were never\ndistributed to the Events Gallery.",
            "Put every setting back to how it was\nwhen the pak was installed.",
        ];
        int reset = names.Count - 1;
        int selected = 0;
        while (true)
        {
            bool[] current =
            [
                _settings.AllowIllegalTransfers, _settings.PcBoxView, _settings.SaveStateDeletion,
                _settings.SaveStateWarning, _settings.GalleryAllLanguages, _settings.GalleryUnreleased,
            ];
            var options = names.Select((_, i) => i < current.Length ? onOff : []).ToList();
            var values = names.Select((_, i) => i < current.Length && current[i] ? 1 : 0).ToList();
            // the viewer's menu keeps changes on B, like NextUI's; the list version only on A (SAVE)
            bool leaving = false;
            var shown = _boxViewer.Settings(names, descriptions, options, values, selected);
            if (shown is { Selected: < 0 })
            {
                leaving = true;
                shown = (shown.Value.Values, 0);
            }
            if ((shown ?? _ui.Options("Settings", names, options, values, selected)) is not { } result)
                return;
            selected = Math.Max(result.Selected, 0);
            bool On(int i) => result.Values[i] == 1;

            if (result.Selected == reset)
            {
                if (_ui.Confirm("Put every setting back to how it was when the pak was installed?", "RESET", "CANCEL"))
                {
                    _settings.ResetToDefaults();
                    _settings.Save(_paths.SettingsFile);
                }
                continue;
            }

            _settings.AllowIllegalTransfers = On(0);
            _settings.PcBoxView = On(1);
            _settings.SaveStateDeletion = On(2);
            _settings.SaveStateWarning = On(3);
            _settings.GalleryAllLanguages = On(4);
            _settings.GalleryUnreleased = On(5);
            _settings.Save(_paths.SettingsFile);
            if (leaving || shown is null)
                return; // saved
        }
    }

    private bool TryWrite(SaveEntry entry, out string backup)
    {
        backup = "";
        try
        {
            backup = _library.Write(entry);
        }
        catch (Exception ex)
        {
            entry.Reload();
            _ui.Message($"Couldn't write {entry.FileName}: {ex.Message}\nNothing was changed.");
            return false;
        }
        // The save is written now: a failure past here mustn't report it as unchanged.
        if (_settings.SaveStateDeletion && _paths.SdRoot is { } sd)
        {
            try
            {
                // Resuming the game would load a state from before this change and undo it.
                foreach (var rom in RomIndex.FindRoms(entry.Path))
                    foreach (var root in _paths.RomRoots)
                        SaveStates.DeleteSelected(sd, root, rom);
                // the DS pak's DraStic keeps its own auto save state
                if (entry.Sav.Generation >= 4)
                    SaveStates.DeleteDraSticAuto(sd, entry.Path, Environment.GetEnvironmentVariable("PLATFORM"));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"couldn't delete the save state for {entry.FileName}: {ex}");
                _ui.Message($"{entry.FileName} was saved, but its save state couldn't be deleted: don't resume it, or the change is undone.");
            }
        }
        return true;
    }

    /// <summary>Help: questions to pick, each answered on its own screen.</summary>
    private void HelpMenu()
    {
        int selected = 0;
        while (true)
        {
            var choice = _ui.Choose("Help", HelpTopics.Select(t => t.Question).ToList(), selected);
            if (choice is null)
                return;
            selected = choice.Value;
            var (question, answer) = HelpTopics[choice.Value];
            // scrolled in the viewer; the message box can't scroll a long answer
            if (!_boxViewer.Text(question, answer))
                _ui.Message(answer);
        }
    }

    private static readonly (string Question, string Answer)[] HelpTopics =
    [
        ("What is Pokémon Manager?", HelpText),
        ("Why isn't my save on the list?",
            "A save is listed when its game's ROM is on the SD card with the same name (Pokemon Emerald.gba and " +
            "Pokemon Emerald.sav), and the game has got as far as receiving the Pokédex.\n\n" +
            "Saves from ROM hacks and other unofficial ROMs are under \"others\" at the bottom of the list. " +
            "SELECT moves a save between the two lists."),
        ("Why can't I transfer a Pokémon?",
            "Pokémon move only the way the games allowed: Gen 1 and 2 trade with each other, Gen 3 games trade " +
            "with each other, Gen 3 goes to Gen 4 by Pal Park and Gen 4 to Gen 5 by Poké Transfer. Nothing goes " +
            "back.\n\nThe games also have to be far enough along, e.g. Pal Park and Poké Transfer need the " +
            "National Pokédex, and Eggs can't be moved to a newer generation. When Transfer is greyed out, picking " +
            "it says why.\n\nSettings > Illegal Transfers lifts these rules."),
        ("Why can't I evolve a Pokémon?",
            "Evolve is for Pokémon that evolve by trading. Some need to hold an item while they're traded, like " +
            "Onix with the Metal Coat, and an Everstone stops it. Karrablast and Shelmet need each other, from " +
            "another Black/White save.\n\nWhen Evolve is greyed out, picking it says why."),
        ("Why isn't Mystery Gift showing?",
            "It appears once the game's own Mystery Gift is unlocked:\n" +
            "FireRed, LeafGreen, Emerald: answer a Poké Mart questionnaire with LINK TOGETHER WITH ALL.\n" +
            "Diamond, Pearl, Platinum: answer the Jubilife TV questionnaire with EVERYONE HAPPY WI-FI CONNECTION.\n" +
            "HeartGold, SoulSilver: get the Pokédex.\n" +
            "Ruby, Sapphire: after beating Norman, give MYSTERY EVENT IS EXCITING as your profile in the " +
            "Petalburg City Pokémon Center.\n\n" +
            "Items you've received are hidden, and so is the menu once you have them all. A game with one item " +
            "shows it by name instead."),
        ("How do I get the gift in my game?",
            "Start the game from its save (not a save state), then:\n" +
            "FireRed, LeafGreen, Emerald: talk to the delivery man upstairs in any Pokémon Center.\n" +
            "Ruby, Sapphire: talk to your dad at the Petalburg City Gym.\n" +
            "Diamond, Pearl, Platinum, HeartGold, SoulSilver: talk to the delivery man in green in any Poké Mart.\n" +
            "Black, White: talk to the delivery person in any Pokémon Center."),
        ("What do Legal and Illegal mean?",
            "Illegal means PKHeX, the tool fans use to check Pokémon, would flag the Pokémon you'd get from it, " +
            "usually because that event never ran in that game or language. Everything else is Legal."),
        ("Why did my change disappear?",
            "Resuming a save state brings back the game as it was before the change, and the next in-game save " +
            "writes the old data back.\n\nClose the game before using Pokémon Manager, and start it fresh from " +
            "its save afterwards. Settings > Save State Deletion deletes the state for you."),
        ("Where are my backups?",
            "Before every change, the save is copied to PokemonManager/Backups on the SD card, in a folder for each " +
            "game, trainer and ID. The last 999 are kept.\n\nTo go back, copy a backup over the save in the Saves " +
            "folder, renamed to the save's own name."),
        ("How do I update Pokémon Manager?",
            "With Wi-Fi on, the pak checks for a new version when it starts and offers to install it. You can also " +
            "pick Update on the main menu. Your saves, backups and settings stay as they are."),
    ];

    private const string HelpText =
        "Pokémon Manager (built on PKHeX)\n" +
        "\n" +
        "Saves are read from the SD card's Saves folder.\n" +
        "A backup is written to PokemonManager/Backups before every change.\n" +
        "\n" +
        "Each game's menu has Mystery Gift (every ticket the game has, like the Aurora Ticket, once the game's Mystery Gift is unlocked), Distributions (Pokémon " +
        "you can only get from an event; Red/Blue/Yellow have Mew instead) and the Events Gallery (every event file for that game and language, from Project " +
        "Pokémon's EventsGallery). Items are marked Legal or Illegal. Info shows the save's details.\n" +
        "\n" +
        SaveStateWarning;
}
