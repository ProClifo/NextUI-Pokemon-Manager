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
    private bool _boxViewFailed;
    private readonly Dictionary<string, SlotRef> _boxPositions = new();
    private List<SaveEntry>? _saves;
    /// <summary>Saves left off the main menu, and why.</summary>
    private List<(SaveEntry Save, string Reason)> _hidden = [];
    private VanillaRoms.Index? _romIndex;
    private readonly GameBackgrounds _backgrounds;
    private readonly (int Width, int Height)? _screen =
        GameBackgrounds.ScreenSize(Environment.GetEnvironmentVariable("PLATFORM"), Environment.GetEnvironmentVariable("DEVICE"));
    private readonly Dictionary<string, string?> _backgroundCache = new();
    private readonly Dictionary<string, GameProfile> _profiles = new();
    private readonly Dictionary<string, List<TicketChoice>> _tickets = new();
    private readonly GalleryArchive _gallery;
    private readonly GameFonts _fonts;
    private readonly int _uiScale;

    public App(IUi ui, AppPaths paths)
    {
        _ui = ui;
        _paths = paths;
        _paths.EnsureCreated();
        _library = new SaveLibrary(paths.BackupDir);
        _boxViewer = new BoxViewer(new BoxScene(paths.BoxAssetsDir), paths.TempDir);
        _backgrounds = new GameBackgrounds(paths.BackgroundsDir);
        _gallery = new GalleryArchive(paths.GalleryFile);
        _uiScale = GameFonts.Scale(Environment.GetEnvironmentVariable("PLATFORM"), Environment.GetEnvironmentVariable("DEVICE"));
        _fonts = new GameFonts(paths.FontsDir, _uiScale);
        _settings = AppSettings.Load(paths.SettingsFile);
    }

    public int Run()
    {
        if (!_settings.SeenWelcome)
        {
            _ui.Message(HelpText);
            _settings.SeenWelcome = true;
            _settings.Save(_paths.SettingsFile);
        }

        while (true)
        {
            var saves = GetSaves();
            // "[ENG] Emerald", with "NAME <player sprite> ID" on the right of every save.
            var items = saves.Select(s => $"[{ProfileFor(s).ShownLanguage ?? ProfileFor(s).Language}] {Names.Game(s.Sav)}").ToList();
            var sprites = saves.Select(s => TrainerSprite(s.Sav)).ToList();
            var tags = saves.Select((s, i) => (string?)$"{s.Sav.OT}{(sprites[i] is null ? "  " : "\t")}{s.Sav.DisplayTID:D5}").ToList();
            int hidden = _hidden.Count == 0 ? -1 : items.Count;
            if (hidden >= 0)
                items.Add($"[{_hidden.Count} hidden saves]");
            int rescan = items.Count; items.Add("[Rescan SD card]");
            int settings = items.Count; items.Add("[Settings]");
            int help = items.Count; items.Add("[Help]");

            var choice = _ui.Choose(saves.Count == 0 ? $"{Title} - no saves found" : $"{Title} - choose a save", items, tags: tags, images: sprites);
            if (choice is null)
                return 0;
            if (choice == hidden) { ShowHidden(); continue; }
            if (choice == rescan) { _saves = null; continue; }
            if (choice == settings) { SettingsMenu(); continue; }
            if (choice == help) { _ui.Message(HelpText); continue; }
            SaveMenu(saves[choice.Value]);
        }
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
        // Only saves with their game's ROM (same file name) are listed.
        foreach (var save in all.Where(s => RomIndex.FindRoms(s.Path).Count == 0).ToList())
        {
            _hidden.Add((save, new RomCheck(RomStatus.RomNotFound, null, null).Describe()));
            all.Remove(save);
        }
        // ...and only games that have got as far as receiving the Pokédex.
        foreach (var save in all.Where(s => !TradeRules.HasPokedex(s.Sav)).ToList())
        {
            _hidden.Add((save, "hasn't received the Pokédex yet"));
            all.Remove(save);
        }
        if (_settings.OnlyOfficialRoms && all.Count != 0)
        {
            _ui.Busy("Checking ROMs are official...\n(The first check of each ROM can take a while.)");
            List<OfficialRomFilter.Hidden> unofficial;
            (all, unofficial) = OfficialRomFilter.Apply(all, RomIndex, _paths.ExtraSavesDir);
            _hidden.AddRange(unofficial.Select(h => (h.Save, h.Check.Describe())));
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

    private void ShowHidden()
    {
        var lines = _hidden.Select(h => $"- {h.Save.FileName}: {h.Reason}");
        _ui.Message(
            "Only saves that have received the Pokédex and whose game's ROM is on the card (same file name), " +
            "unmodified and official, are shown. Hidden:\n" +
            string.Join('\n', lines) +
            "\n\nTo manage saves from unofficial ROMs anyway, turn off Settings > Official ROMs only. " +
            "A save without its ROM or the Pokédex is never shown.");
    }

    // ---------------------------------------------------------------- save menu

    private void SaveMenu(SaveEntry entry)
    {
        // The game's menus are drawn in the game's own font.
        var previousFont = _ui.Font;
        try
        {
            _ui.Font = _fonts.For(entry.Sav, ProfileFor(entry).Language);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Couldn't pick the game font: {ex.Message}");
        }
        // ...over the game's title art, which stays behind its sub-menus and messages (not Pokémon storage).
        var previousBackground = _ui.Background;
        _ui.Background = BackgroundFor(entry);
        try
        {
            SaveMenuLoop(entry);
        }
        finally
        {
            _ui.Font = previousFont;
            _ui.Background = previousBackground;
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
            // A game with one event gets that event on its menu; a game with several gets an Events menu.
            var tickets = TicketsFor(entry);
            if (sav is SAV2 { Version: GameVersion.C } crystal)
                actions.Add(("GS Ball", LegalTag(crystal.Japanese), () => GsBallFlow(entry)));
            else if (tickets.Count == 1)
                actions.Add((tickets[0].Ticket.Name, LegalTag(tickets[0].Legal), () => GiveTicket(entry, tickets[0])));
            else if (tickets.Count > 1)
                actions.Add(("Events", null, () => EventsMenu(entry, tickets)));
            // Gen 1/2's event-only Pokémon get their own entries (Gen 1/2's other distributions are in the Gallery):
            // Mew in Gen 1 (and Korean Gen 2, which can't link with Gen 1), Celebi in Gen 2 outside Japan, where the
            // GS Ball never ran on cartridges.
            if (sav is SAV1 || sav is SAV2 { Korean: true })
                actions.Add(("Mew", LegalTag(true), () => GiveGameBoyEvent(entry, "Mew", GiftService.Mew)));
            if (sav is SAV2 { Japanese: false })
                actions.Add(("Celebi", LegalTag(true), () => GiveGameBoyEvent(entry, "Celebi", GiftService.Celebi)));
            if (sav.Generation >= 3)
                actions.Add(("Distributions", null, () => DistributionsMenu(entry)));
            actions.Add(("Gallery", null, () => GalleryMenu(entry)));
            actions.Add(("Info", null, () => _ui.Message(SaveInfo(entry))));

            var choice = _ui.Choose(entry.Label, actions.Select(a => a.Label).ToList(), selected, BackgroundFor(entry),
                actions.Select(a => a.Tag).ToList());
            if (choice is null)
                return;
            selected = choice.Value;
            RunSafely(actions[choice.Value].Run, entry);
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
        if (_settings.PcBoxView && !_boxViewFailed && _boxViewer.IsAvailable)
        {
            var position = _boxPositions.GetValueOrDefault(entry.Path, new SlotRef(0, 0));
            while (true)
            {
                var outcome = _boxViewer.Pick(entry.Sav, entry.Label, ref position, _ui.Font);
                _boxPositions[entry.Path] = position;
                if (outcome == BoxViewer.Outcome.Back)
                    return;
                if (outcome == BoxViewer.Outcome.Unavailable)
                {
                    _boxViewFailed = true; // fall back to the lists for the rest of this session
                    break;
                }
                PokemonMenu(entry, position);
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
            var actions = new List<(string Label, Func<bool> Run)>
            {
                ("Transfer", () => TransferFlow(entry, slot, TransferMode.Move)),
                ("Summary", () => { _ui.Message(Names.Details(pk)); return false; }),
                ("Evolve", () => EvolveFlow(entry, slot)),
                ("Cancel", () => true),
            };

            var choice = _ui.Choose($"{slot}: {Names.Summary(pk)}", actions.Select(a => a.Label).ToList());
            if (choice is null)
                return;
            bool slotChanged = actions[choice.Value].Run();
            if (slotChanged)
                return;
        }
    }

    // ---------------------------------------------------------------- transfers

    private SaveEntry? PickOtherSave(SaveEntry exclude, string title)
    {
        var others = GetSaves().Where(s => s.Path != exclude.Path).ToList();
        if (others.Count == 0)
        {
            _ui.Message("No other Pokémon saves were found. Put the other game's save in NextUI's Saves folder or in PokemonManager/Saves.");
            return null;
        }
        var choice = _ui.Choose(title, others.Select(s => s.Label).ToList());
        return choice is null ? null : others[choice.Value];
    }

    /// <returns>True if the source slot no longer holds this Pokémon.</returns>
    private bool TransferFlow(SaveEntry source, SlotRef from, TransferMode mode)
    {
        var dest = PickOtherSave(source, mode == TransferMode.Move ? "Move to which game?" : "Copy to which game?");
        if (dest is null)
            return false;

        var pk = from.Get(source.Sav);
        if (!_settings.AllowIllegalTransfers
            && TradeRules.CheckTransfer(pk, source.Sav, dest.Sav, ProfileFor(source).Language, ProfileFor(dest).Language) is { Ok: false } refused)
        {
            _ui.Message($"{refused.Message}\n\n(Settings > Illegal transfers turns these rules off.)");
            return false;
        }
        var check = TransferService.Prepare(pk, dest.Sav, _settings.AllowIllegalTransfers, out _);
        if (!check.Ok)
        {
            _ui.Message(check.Message);
            return false;
        }

        var verb = mode == TransferMode.Move ? "Move" : "Copy";
        var question = $"{verb} {Names.Summary(pk)}\nfrom {Names.Game(source.Sav)} ({source.Sav.OT})\nto {Names.Game(dest.Sav)} ({dest.Sav.OT})?";
        if (check.Message.Length != 0)
            question += "\n\n" + check.Message;
        if (!_ui.Confirm(question))
            return false;

        var result = TransferService.Transfer(source, from, dest, mode, _settings.AllowIllegalTransfers, out var placedAt);
        if (!result.Ok)
        {
            source.Reload();
            dest.Reload();
            _ui.Message(result.Message);
            return false;
        }

        var message = result.Message;

        // A trade between games of the same era triggers trade evolutions, just like a link cable.
        var placed = placedAt.Get(dest.Sav);
        bool sameEra = pk.Format == dest.Sav.Generation || (pk.Format <= 2 && dest.Sav.Generation <= 2);
        var evo = sameEra && !TradeEvolution.HoldsEverstone(placed)
            ? TradeEvolution.GetOptions(placed).FirstOrDefault(o => o.ConditionsMet)
            : null;
        if (evo is not null && _ui.Confirm($"What? {Names.Species(placed)} is evolving!\nLet it evolve into {Names.Species(evo.Species)}?", "EVOLVE", "STOP"))
        {
            var evolved = TradeEvolution.Evolve(dest.Sav, placedAt, evo);
            if (evolved.Ok)
                message += "\n" + evolved.Message;
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

        _ui.Message($"{message}\n\nBackups were saved to PokemonManager/Backups.\n{SaveStateWarning}");
        return mode == TransferMode.Move;
    }

    // ---------------------------------------------------------------- trade evolution

    private bool EvolveFlow(SaveEntry entry, SlotRef slot)
    {
        var pk = slot.Get(entry.Sav);
        var options = TradeEvolution.GetOptions(pk);
        if (options.Count == 0)
        {
            _ui.Message($"{Names.Species(pk)} doesn't evolve by trading.");
            return false;
        }
        if (TradeEvolution.HoldsEverstone(pk))
        {
            _ui.Message($"{Names.Summary(pk)} is holding an Everstone. Take it away in-game first.");
            return false;
        }

        var option = options[0];
        if (options.Count > 1)
        {
            var pick = _ui.Choose($"Evolve {Names.Species(pk)} into...", options.Select(o => o.Label).ToList());
            if (pick is null)
                return false;
            option = options[pick.Value];
        }

        var question = $"Evolve {Names.Summary(pk)} into {Names.Species(option.Species)}?";
        if (option.Method == EvolutionType.TradeHeldItem && !option.HoldsRequiredItem)
            question += $"\n\nIt isn't holding {Names.Item(option.RequiredItem, pk.Context)}, which a real trade needs. Evolve anyway?";
        if (!_ui.Confirm(question, "EVOLVE", "CANCEL"))
            return false;

        var result = TradeEvolution.Evolve(entry.Sav, slot, option);
        if (!result.Ok)
        {
            entry.Reload();
            _ui.Message(result.Message);
            return false;
        }
        if (TryWrite(entry, out _))
            _ui.Message($"{result.Message}\n\n{SaveStateWarning}");
        return false;
    }

    // ---------------------------------------------------------------- gifts & events

    private void InjectGen3(SaveEntry entry, Gen3EventFile file)
    {
        if (entry.Sav is not SAV3 sav3)
            return;
        var question = $"Inject {file.DisplayName} into {Names.Game(sav3)}?";
        if (file.Kind is Gen3EventKind.WonderCard or Gen3EventKind.MysteryEvent)
            question += "\n\nThe game holds one event script at a time; this replaces any current one.";
        if (!_ui.Confirm(question, "INJECT", "CANCEL"))
            return;

        var result = Gen3Events.Inject(sav3, file);
        if (!result.Ok)
        {
            entry.Reload();
            _ui.Message(result.Message);
            return;
        }
        if (TryWrite(entry, out _))
            _ui.Message($"{result.Message}\n\n{SaveStateWarning}");
    }

    private void GiftActions(SaveEntry entry, MysteryGift gift)
    {
        var sav = entry.Sav;
        var actions = new List<(string Label, Func<OpResult> Run)>();
        if (gift is DataMysteryGift data && GiftService.SupportsAlbum(sav))
            actions.Add(("Add to Mystery Gift album (pick up in-game)", () => GiftService.InjectCard(sav, data)));
        if (gift.IsEntity)
            actions.Add(("Send the Pokémon to your party", () => GiftService.Redeem(sav, gift)));
        if (actions.Count == 0)
        {
            _ui.Message($"{Names.Game(sav)} has no Mystery Gift album, and this gift isn't a Pokémon, so it can't be added.");
            return;
        }

        var choice = _ui.Choose(GiftService.Describe(gift), actions.Select(a => a.Label).ToList());
        if (choice is null)
            return;
        var result = actions[choice.Value].Run();
        if (!result.Ok)
        {
            entry.Reload();
            _ui.Message(result.Message);
            return;
        }
        if (TryWrite(entry, out _))
            _ui.Message($"{result.Message}\n\n{SaveStateWarning}");
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
            var choice = _ui.Choose("Events", tickets.Select(t => t.Ticket.Name).ToList(), selected, tags: tickets.Select(t => (string?)LegalTag(t.Legal)).ToList());
            if (choice is null)
                return;
            selected = choice.Value;
            GiveTicket(entry, tickets[choice.Value]);
        }
    }

    private void GiveTicket(SaveEntry entry, TicketChoice ticket)
    {
        if (!ticket.Legal)
        {
            var profile = ProfileFor(entry);
            var source = ticket.File is { } f ? $"Use \"{StripItemPrefix(f.Title)}\" anyway?" : "Add it anyway?";
            if (!_ui.Confirm(
                    $"{ticket.Ticket.Name} was never officially distributed for {Names.Game(entry.Sav)} in {GalleryLanguage.Name(profile.Language)}. " +
                    $"PKHeX will flag Pokémon met through it as illegal.\n\n{source}", "USE IT", "CANCEL"))
                return;
        }
        if (ticket.File is { } file)
        {
            GiveGalleryFile(entry, file);
            return;
        }
        if (entry.Sav is not SAV3E emerald)
            return;
        if (!_ui.Confirm($"Give {Names.Game(emerald)} the Eon Ticket, as if received by Record Mixing with Ruby/Sapphire?", "GIVE", "CANCEL"))
            return;
        var result = Gen3Events.GiveEonTicketByRecordMixing(emerald);
        if (!result.Ok)
        {
            entry.Reload();
            _ui.Message(result.Message);
            return;
        }
        if (TryWrite(entry, out _))
            _ui.Message($"{result.Message}\n\n{SaveStateWarning}");
    }

    private void DistributionsMenu(SaveEntry entry)
    {
        if (!GalleryAvailable())
            return;
        var profile = ProfileFor(entry);
        var list = GalleryLists.Distributions(_gallery, profile);
        if (list.Count == 0)
        {
            _ui.Message($"Every Pokémon the gallery has for {Names.Game(entry.Sav)} in {GalleryLanguage.Name(profile.Language)} can be obtained without an event. See the Gallery for all distributions.");
            return;
        }
        int selected = 0;
        while (true)
        {
            // Only released distributions are listed, and every Pokémon handed out passes the legality check.
            var choice = _ui.Choose($"Distributions ({list.Count})", list.Select(e => e.Title).ToList(), selected,
                tags: list.Select(_ => (string?)"Legal").ToList());
            if (choice is null)
                return;
            selected = choice.Value;
            GiveGalleryFile(entry, list[choice.Value]);
        }
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
            _ui.Message($"The gallery has nothing for {Names.Game(entry.Sav)} in {GalleryLanguage.Name(profile.Language)}.");
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
            var title = path.Length == 0 ? "Gallery" : $"Gallery: {path}";
            var tags = view.Folders.Select(_ => (string?)null)
                .Concat(view.Items.Select(i => (string?)LegalTag(i.Copies.Any(f => IsLegalFor(f, profile))))).ToList();
            var choice = _ui.Choose(title, labels, selected, tags: tags);
            if (choice is null)
                return;
            selected = choice.Value;
            if (choice < view.Folders.Count)
                GalleryFolder(entry, files, view.Folders[choice.Value], profile);
            else
                GiveGalleryFile(entry, PickCopy(view.Items[choice.Value - view.Folders.Count], profile));
        }
    }

    /// <summary>
    /// Released Pokémon are legal (they're regenerated for the save and checked before being added); cards are
    /// legal when officially distributed for this game and language; unreleased files go by PKHeX's verdict
    /// on the file, and unreleased cards were never distributed at all.
    /// </summary>
    private static bool IsLegalFor(GalleryEntry e, GameProfile profile) => e.Kind == GalleryKind.Pokemon
        ? e.IsFileLegal
        : e.Released && (e.Language is null || e.Language == profile.Language);

    /// <summary>One of an item's copies at random, a legal one when there is one (the copies differ only in
    /// IDs and the like, and Pokémon are regenerated for the save anyway).</summary>
    private static GalleryEntry PickCopy(GalleryTree.Item item, GameProfile profile)
    {
        var legal = item.Copies.Where(f => IsLegalFor(f, profile)).ToList();
        var pool = legal.Count > 0 ? legal : item.Copies;
        return pool[Random.Shared.Next(pool.Count)];
    }

    private string GalleryLabel(GalleryTree.Item item)
        => _settings.GalleryAllLanguages && item.Language is { } language ? $"{item.Title} - {language}" : item.Title;

    /// <summary>"Item AuroraTicket (UK)" -> "Aurora Ticket (UK)".</summary>
    private static string StripItemPrefix(string title)
    {
        if (title.StartsWith("Item ", StringComparison.Ordinal))
            title = title[5..];
        return title.Replace("AuroraTicket", "Aurora Ticket").Replace("MysticTicket", "Mystic Ticket");
    }

    private void GiveGalleryFile(SaveEntry entry, GalleryEntry file)
    {
        var gift = _gallery.Load(file);
        if (gift is null)
        {
            _ui.Message("That gallery file couldn't be read.");
            return;
        }
        if (gift.Pokemon is not null)
            GiveEventPokemon(entry, file);
        else if (gift.Card is { } card)
            GiftActions(entry, card);
        else if (gift.Gen3 is { } g3)
        {
            if (entry.Sav is not SAV3 sav3 || !Gen3Events.IsApplicable(sav3, g3))
            {
                _ui.Message($"{StripItemPrefix(file.Title)} can't be added to {Names.Game(entry.Sav)}: it's for a different game or language version.");
                return;
            }
            InjectGen3(entry, g3);
        }
    }

    /// <summary>
    /// Gives a gallery Pokémon the way the distribution did: a new Pokémon (PID, nature, IVs...) generated
    /// for this save, which must pass PKHeX's legality check.
    /// </summary>
    private void GiveEventPokemon(SaveEntry entry, GalleryEntry file)
    {
        if (TradeRules.CheckDistribution(entry.Sav) is { Ok: false } noDex)
        {
            _ui.Message(noDex.Message);
            return;
        }
        _ui.Busy($"Generating {file.Title}...");
        var result = EventPokemon.FromGallery(_gallery, file, entry.Sav);
        if (result.Pokemon is not { } pk)
        {
            _ui.Message(result.Message);
            return;
        }
        if (!result.Legal)
        {
            if (file.Released)
            {
                _ui.Message($"PKHeX flags {file.Title} as illegal in {Names.Game(entry.Sav)}, so it wasn't added.\n\n{result.Message}");
                return;
            }
            if (!_ui.Confirm($"This unreleased file is flagged as illegal by PKHeX:\n{result.Message}\n\nAdd it anyway?", "ADD", "CANCEL"))
                return;
        }
        var origin = result.Source switch
        {
            EventPokemon.Source.Generated => $"Generated like the original distribution: {Names.Rolled(pk)}.",
            EventPokemon.Source.GalleryCopy => $"PKHeX can't regenerate this event, so this is one of the original copies at random: {Names.Rolled(pk)}.",
            _ => "",
        };
        if (entry.Sav.Generation >= 3 && pk.Language != entry.Sav.Language && GalleryLanguage.FromLanguageId(pk.Language) is { } from)
            origin += $"\nThis distribution wasn't given out in your game's language, so it's {LanguageArticle(from)} Pokémon, as if traded from {LanguageArticle(from)} game.";
        // No trade rules: the real distributions didn't need the National Pokédex (only the Pokédex, checked above).
        PlaceConverted(entry, pk, $"Put {file.Title}", origin, distribution: true);
    }

    private static string LanguageArticle(string code)
    {
        var name = GalleryLanguage.Name(code);
        return (name[0] is 'E' or 'I' ? "an " : "a ") + name;
    }

    /// <summary>Puts a Pokémon in the first free PC slot, or for a distribution where the games put it (the party).</summary>
    private void PlaceConverted(SaveEntry entry, PKM pk, string verb, string details = "", bool distribution = false)
    {
        var sav = entry.Sav;
        SlotRef slot;
        if (distribution)
        {
            var target = SlotRef.ForDistribution(sav);
            if (target.Value is not { } found)
            {
                _ui.Message(target.Message);
                return;
            }
            slot = found;
        }
        else if (SlotRef.FirstEmptyBoxSlot(sav) is { } free)
        {
            slot = free;
        }
        else
        {
            _ui.Message("Every PC box is full.");
            return;
        }
        if (!_ui.Confirm($"{verb} ({Names.Summary(pk)}) into {slot.Describe(sav)}?"))
            return;
        slot.Set(sav, pk);
        if (TryWrite(entry, out _))
        {
            var extra = details.Length == 0 ? "" : $"\n{details}";
            _ui.Message($"Added {Names.Summary(slot.Get(sav))}.{extra}\nLegality: {Names.Legality(slot.Get(sav))}\n\n{SaveStateWarning}");
        }
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
        PlaceConverted(entry, pk, $"Put {name}", $"Generated like the original distribution: {Names.Rolled(pk)}.", distribution: true);
    }

    private void GsBallFlow(SaveEntry entry)
    {
        if (entry.Sav is not SAV2 sav)
            return;
        const string HowTo =
            "After entering the Hall of Fame, walk into the Goldenrod City Pokémon Center: a woman will give you the GS Ball. " +
            "Take it to Kurt in Azalea Town, then put it in the Ilex Forest shrine to meet Celebi.";
        if (sav.IsEnabledGSBallMobileEvent)
        {
            _ui.Message($"The GS Ball event is already enabled.\n\n{HowTo}");
            return;
        }
        var question = sav.Japanese
            ? "Enable the GS Ball event? Japanese Crystal handed it out through the Mobile System."
            : "Enable the GS Ball event? Outside Japan it only ran in the 3DS Virtual Console release, so PKHeX flags a Celebi from it in a cartridge copy as illegal.";
        if (!_ui.Confirm(question, "ENABLE", "CANCEL"))
            return;
        sav.EnableGSBallMobileEvent();
        if (TryWrite(entry, out _))
            _ui.Message($"GS Ball event enabled.\n\n{HowTo}\n\n{SaveStateWarning}");
    }

    // ---------------------------------------------------------------- files

    // ---------------------------------------------------------------- settings & help

    private void SettingsMenu()
    {
        while (true)
        {
            var items = new List<string>
            {
                $"Illegal transfers: {(_settings.AllowIllegalTransfers ? "ON" : "OFF")}",
                $"Official ROMs only: {(_settings.OnlyOfficialRoms ? "ON" : "OFF")}",
                $"PC box view: {(_settings.PcBoxView ? "ON" : "OFF (lists)")}",
                $"Show all languages in gallery: {(_settings.GalleryAllLanguages ? "ON" : "OFF")}",
                $"Show unreleased files in gallery: {(_settings.GalleryUnreleased ? "ON" : "OFF")}",
                "Show welcome screen again",
            };
            var choice = _ui.Choose("Settings", items);
            if (choice is null)
                return;
            if (choice == 0)
            {
                if (!_settings.AllowIllegalTransfers && !_ui.Confirm(
                        "Illegal transfers let you move Pokémon in ways the real games never allowed: between any " +
                        "generations (Gen 4 back to Gen 3, Gen 2 up to Gen 3...) and without the games' requirements, such as " +
                        "having the National Pokédex. Those Pokémon are usually flagged as illegal. Turn on?", "TURN ON", "CANCEL"))
                    continue;
                _settings.AllowIllegalTransfers = !_settings.AllowIllegalTransfers;
            }
            else if (choice == 1)
            {
                if (_settings.OnlyOfficialRoms && !_ui.Confirm(
                        "Show saves from ROM hacks and other unofficial ROMs too? PKHeX may misread a hack's save, " +
                        "and editing it can corrupt it. Backups are still made before every change.", "SHOW ALL", "CANCEL"))
                    continue;
                _settings.OnlyOfficialRoms = !_settings.OnlyOfficialRoms;
                _saves = null; // rescan with the new rule
            }
            else if (choice == 2)
            {
                _settings.PcBoxView = !_settings.PcBoxView;
                _boxViewFailed = false;
            }
            else if (choice == 3)
            {
                _settings.GalleryAllLanguages = !_settings.GalleryAllLanguages;
            }
            else if (choice == 4)
            {
                if (!_settings.GalleryUnreleased && !_ui.Confirm(
                        "Unreleased files are debug and test data that were never distributed. PKHeX flags their " +
                        "Pokémon as illegal, and some may not work in-game. Show them in the gallery?", "SHOW", "CANCEL"))
                    continue;
                _settings.GalleryUnreleased = !_settings.GalleryUnreleased;
            }
            else
            {
                _settings.SeenWelcome = false;
            }
            _settings.Save(_paths.SettingsFile);
        }
    }

    private bool TryWrite(SaveEntry entry, out string backup)
    {
        backup = "";
        try
        {
            backup = _library.Write(entry);
            return true;
        }
        catch (Exception ex)
        {
            entry.Reload();
            _ui.Message($"Couldn't write {entry.FileName}: {ex.Message}\nNothing was changed.");
            return false;
        }
    }

    private const string HelpText =
        "Pokémon Manager (built on PKHeX)\n" +
        "\n" +
        "Saves are read from the Saves folder, plus PokemonManager/Saves.\n" +
        "A backup is written to PokemonManager/Backups before every change.\n" +
        "\n" +
        "Each game's menu has Events (every ticket the game has, like the Aurora Ticket), Distributions (Pokémon " +
        "you can only get from an event; Red/Blue/Yellow have Mew instead) and the Gallery (every event file for that game and language, from Project " +
        "Pokémon's EventsGallery). Items are marked Legal or Illegal. Info shows the save's details.\n" +
        "\n" +
        SaveStateWarning;
}
