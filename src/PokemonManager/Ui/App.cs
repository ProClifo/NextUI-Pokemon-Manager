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
    private List<OfficialRomFilter.Hidden> _hidden = [];
    private VanillaRoms.Index? _romIndex;
    private readonly GameBackgrounds _backgrounds;
    private readonly (int Width, int Height)? _screen =
        GameBackgrounds.ScreenSize(Environment.GetEnvironmentVariable("PLATFORM"), Environment.GetEnvironmentVariable("DEVICE"));
    private readonly Dictionary<string, string?> _backgroundCache = new();
    private readonly Dictionary<string, GameProfile> _profiles = new();
    private readonly Dictionary<string, List<TicketChoice>> _tickets = new();
    private readonly GalleryArchive _gallery;
    private readonly GameFonts _fonts;

    public App(IUi ui, AppPaths paths)
    {
        _ui = ui;
        _paths = paths;
        _paths.EnsureCreated();
        _library = new SaveLibrary(paths.BackupDir);
        _boxViewer = new BoxViewer(new BoxScene(paths.BoxAssetsDir), paths.TempDir);
        _backgrounds = new GameBackgrounds(paths.BackgroundsDir);
        _gallery = new GalleryArchive(paths.GalleryFile);
        _fonts = new GameFonts(paths.FontsDir,
            GameFonts.Scale(Environment.GetEnvironmentVariable("PLATFORM"), Environment.GetEnvironmentVariable("DEVICE")));
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
            var items = saves.Select(s => s.Label).ToList();
            int hidden = _hidden.Count == 0 ? -1 : items.Count;
            if (hidden >= 0)
                items.Add($"[{_hidden.Count} hidden: not official ROMs]");
            int rescan = items.Count; items.Add("[Rescan SD card]");
            int settings = items.Count; items.Add("[Settings]");
            int help = items.Count; items.Add("[Help]");

            var choice = _ui.Choose(saves.Count == 0 ? $"{Title} - no saves found" : $"{Title} - choose a save", items);
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
        if (_settings.OnlyOfficialRoms && all.Count != 0)
        {
            _ui.Busy("Checking ROMs are official...\n(The first check of each ROM can take a while.)");
            (all, _hidden) = OfficialRomFilter.Apply(all, RomIndex, _paths.ExtraSavesDir);
        }
        _saves = all;
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
        var lines = _hidden.Select(h => $"- {h.Save.FileName}: {h.Check.Describe()}");
        _ui.Message(
            "Only saves made with unmodified, official Pokémon ROMs are shown. Hidden:\n" +
            string.Join('\n', lines) +
            "\n\nTo manage these anyway, turn off Settings > Official ROMs only, " +
            "or copy the save into PokemonManager/Saves.");
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
        try
        {
            SaveMenuLoop(entry);
        }
        finally
        {
            _ui.Font = previousFont;
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
            actions.Add(("Distributions", null, () => DistributionsMenu(entry)));
            actions.Add(("Gallery", null, () => GalleryMenu(entry)));
            actions.Add(("More", null, () => MoreMenu(entry)));

            var choice = _ui.Choose(entry.Label, actions.Select(a => a.Label).ToList(), selected, BackgroundFor(entry),
                actions.Select(a => a.Tag).ToList());
            if (choice is null)
                return;
            selected = choice.Value;
            RunSafely(actions[choice.Value].Run, entry);
        }
    }

    private void MoreMenu(SaveEntry entry)
    {
        int selected = 0;
        while (true)
        {
            var actions = new List<(string Label, Action Run)>
            {
                ("Trade evolutions", () => TradeEvolutionMenu(entry)),
                ("Import Pokémon from file", () => ImportMenu(entry)),
                ("Gift files on SD card", () => GiftFilesMenu(entry, GiftService.ListFiles(_paths.GiftsDir, entry.Sav))),
                ("Restore a backup", () => RestoreMenu(entry)),
                ("Save info", () => _ui.Message(SaveInfo(entry))),
            };
            if (entry.Sav is SAV3 sav3)
                actions.Insert(3, ("Mystery Gift / Event status", () => _ui.Message(Gen3Events.Status(sav3))));
            var choice = _ui.Choose($"{entry.Label}: more", actions.Select(a => a.Label).ToList(), selected);
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

    private void BrowseMenu(SaveEntry entry)
    {
        if (_settings.PcBoxView && !_boxViewFailed && _boxViewer.IsAvailable)
        {
            var position = _boxPositions.GetValueOrDefault(entry.Path, new SlotRef(0, 0));
            while (true)
            {
                var outcome = _boxViewer.Pick(entry.Sav, entry.Label, ref position);
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
            var options = TradeEvolution.GetOptions(pk);
            var actions = new List<(string Label, Func<bool> Run)>
            {
                ("View summary", () => { _ui.Message(Names.Details(pk)); return false; }),
                ("Move to another game", () => TransferFlow(entry, slot, TransferMode.Move)),
                ("Copy to another game", () => TransferFlow(entry, slot, TransferMode.Copy)),
            };
            if (options.Count != 0)
                actions.Add(("Trade evolve", () => EvolveFlow(entry, slot)));
            actions.Add(("Export to file", () => { ExportFlow(entry, slot); return false; }));

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

    private void TradeEvolutionMenu(SaveEntry entry)
    {
        while (true)
        {
            var sav = entry.Sav;
            var candidates = SlotRef.AllOccupied(sav)
                .Select(s => (Slot: s, Pk: s.Get(sav)))
                .Where(x => TradeEvolution.GetOptions(x.Pk).Count != 0)
                .ToList();
            if (candidates.Count == 0)
            {
                _ui.Message("None of your Pokémon in this save evolve by trading.");
                return;
            }

            int ready = candidates.Count(c => !TradeEvolution.HoldsEverstone(c.Pk) && TradeEvolution.GetOptions(c.Pk).Any(o => o.ConditionsMet));
            var labels = new List<string>();
            if (ready > 0)
                labels.Add($"Evolve all that are ready ({ready})");
            foreach (var (slot, pk) in candidates)
            {
                var targets = string.Join(" / ", TradeEvolution.GetOptions(pk).Select(o => Names.Species(o.Species)).Distinct());
                labels.Add($"{Names.Summary(pk)} -> {targets} ({slot})");
            }

            var choice = _ui.Choose("Trade evolutions", labels);
            if (choice is null)
                return;

            if (ready > 0 && choice == 0)
            {
                if (!_ui.Confirm($"Evolve {ready} Pokémon that would evolve in a real trade right now (no item needed, or holding the right item)?", "EVOLVE", "CANCEL"))
                    continue;
                var (count, lines) = TradeEvolution.EvolveAllEligible(sav);
                if (count > 0 && TryWrite(entry, out _))
                    _ui.Message($"Evolved {count} Pokémon:\n{string.Join('\n', lines)}\n\n{SaveStateWarning}");
                continue;
            }

            int index = choice.Value - (ready > 0 ? 1 : 0);
            EvolveFlow(entry, candidates[index].Slot);
        }
    }

    // ---------------------------------------------------------------- gifts & events

    private void GiftFilesMenu(SaveEntry entry, List<GiftFile> files)
    {
        if (files.Count == 0)
        {
            var kinds = entry.Sav.Generation == 3
                ? ".wc3 (Wonder Card), .wn3 (Wonder News), .me3 (Mystery Event), .ect (e-Card Trainer) or .ecb (e-Reader Berry)"
                : "PKHeX Mystery Gift files (.pgt .pcd .wc4 .pgf .wc6 .wc7 .wb7 .wc8 .wb8 .wa8 .wc9 .wa9)";
            _ui.Message($"No gift files for this game were found.\n\nCopy {kinds} files into:\nPokemonManager/Gifts\non your SD card.");
            return;
        }

        while (true)
        {
            var choice = _ui.Choose("Gift files", files.Select(f => f.DisplayName).ToList());
            if (choice is null)
                return;
            var file = files[choice.Value];
            if (file.Gen3 is { } g3)
                InjectGen3(entry, g3);
            else if (file.Gift is { } gift)
                GiftActions(entry, gift);
        }
    }

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
            actions.Add(("Send the Pokémon straight to a PC box", () => GiftService.RedeemToBox(sav, gift, !_settings.AllowIllegalTransfers)));
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
            labels.AddRange(view.Files.Select(GalleryLabel));
            // Language folders say nothing once the list is filtered to one language.
            var path = string.Join('/', view.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => _settings.GalleryAllLanguages || !GalleryLanguage.Tags.Contains(p)));
            var title = path.Length == 0 ? $"Gallery ({files.Count})" : $"Gallery: {path}";
            var tags = view.Folders.Select(_ => (string?)null).Concat(view.Files.Select(f => (string?)LegalTag(IsLegalFor(f, profile)))).ToList();
            var choice = _ui.Choose(title, labels, selected, tags: tags);
            if (choice is null)
                return;
            selected = choice.Value;
            if (choice < view.Folders.Count)
                GalleryFolder(entry, files, view.Folders[choice.Value], profile);
            else
                GiveGalleryFile(entry, view.Files[choice.Value - view.Folders.Count]);
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

    private string GalleryLabel(GalleryEntry e)
    {
        var label = e.Title;
        if (_settings.GalleryAllLanguages && e.Language is { } language)
            label += $" ({language})";
        return label;
    }

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
        if (!_settings.AllowIllegalTransfers && TradeRules.CheckReceive(pk, entry.Sav) is { Ok: false } refused)
        {
            _ui.Message(refused.Message);
            return;
        }
        PlaceConverted(entry, pk, $"Put {file.Title}", origin);
    }

    /// <summary>Converts a Pokémon for this save if needed and puts it in the first free PC slot.</summary>
    private void PlacePokemon(SaveEntry entry, PKM pk, string verb)
    {
        if (!_settings.AllowIllegalTransfers && TradeRules.CheckFile(pk, entry.Sav) is { Ok: false } refused)
        {
            _ui.Message($"{refused.Message}\n\n(Settings > Illegal transfers turns these rules off.)");
            return;
        }
        var check = TransferService.Prepare(pk, entry.Sav, _settings.AllowIllegalTransfers, out var prepared);
        if (!check.Ok || prepared is null)
        {
            _ui.Message(check.Message);
            return;
        }
        PlaceConverted(entry, prepared.Converted, verb);
    }

    private void PlaceConverted(SaveEntry entry, PKM pk, string verb, string details = "")
    {
        var sav = entry.Sav;
        var target = SlotRef.FirstEmptyBoxSlot(sav);
        if (target is not { } slot)
        {
            _ui.Message("Every PC box is full.");
            return;
        }
        if (!_ui.Confirm($"{verb} ({Names.Summary(pk)}) into {SlotRef.BoxName(sav, slot.Box)}, slot {slot.Slot + 1}?"))
            return;
        slot.Set(sav, pk);
        if (TryWrite(entry, out _))
        {
            var extra = details.Length == 0 ? "" : $"\n{details}";
            _ui.Message($"Added {Names.Summary(slot.Get(sav))}.{extra}\nLegality: {Names.Legality(slot.Get(sav))}\n\n{SaveStateWarning}");
        }
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

    private void ExportFlow(SaveEntry entry, SlotRef slot)
    {
        var pk = slot.Get(entry.Sav);
        var dir = Path.Combine(_paths.ExportDir, Path.GetFileNameWithoutExtension(entry.Path));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Sanitize(pk.FileName));
        var bytes = new byte[pk.SIZE_PARTY];
        pk.WriteDecryptedDataParty(bytes);
        File.WriteAllBytes(path, bytes);
        _ui.Message($"Saved {Names.Summary(pk)} to\n{Path.GetRelativePath(_paths.DataDir, path)}\n(inside PokemonManager on the SD card).");
    }

    private void ImportMenu(SaveEntry entry)
    {
        var sav = entry.Sav;
        var files = Directory.Exists(_paths.ImportDir)
            ? Directory.EnumerateFiles(_paths.ImportDir, "*", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith('.') && PokemonExtensions.Contains(Path.GetExtension(f).TrimStart('.').ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];
        if (files.Count == 0)
        {
            _ui.Message("No Pokémon files found.\n\nCopy .pk1-.pk9 (or other PKHeX Pokémon files) into PokemonManager/Import on your SD card.");
            return;
        }

        var choice = _ui.Choose("Import which Pokémon?", files.Select(f => Path.GetFileName(f)).ToList());
        if (choice is null)
            return;
        var path = files[choice.Value];
        var data = File.ReadAllBytes(path);
        if (!FileUtil.TryGetPKM(data, out var pk, Path.GetExtension(path), sav))
        {
            _ui.Message("That file isn't a Pokémon PKHeX can read.");
            return;
        }

        PlacePokemon(entry, pk, $"Import {Path.GetFileName(path)}");
    }

    private void RestoreMenu(SaveEntry entry)
    {
        var name = Path.GetFileNameWithoutExtension(entry.Path);
        var ext = Path.GetExtension(entry.Path);
        var backups = Directory.Exists(_paths.BackupDir)
            ? Directory.GetFiles(_paths.BackupDir, $"{name}.*{ext}").OrderByDescending(File.GetLastWriteTimeUtc).ToList()
            : [];
        if (backups.Count == 0)
        {
            _ui.Message("There are no backups of this save yet. One is made automatically before every change.");
            return;
        }
        var choice = _ui.Choose("Restore which backup?", backups.Select(b => $"{File.GetLastWriteTime(b):yyyy-MM-dd HH:mm:ss}  {Path.GetFileName(b)}").ToList());
        if (choice is null)
            return;
        if (!_ui.Confirm("Replace the current save with this backup?\n(The current save is backed up first.)", "RESTORE", "CANCEL"))
            return;
        try
        {
            _library.Backup(entry.Path);
            File.Copy(backups[choice.Value], entry.Path, overwrite: true);
            entry.Reload();
            _ui.Message($"Backup restored.\n\n{SaveStateWarning}");
        }
        catch (Exception ex)
        {
            _ui.Message($"Couldn't restore the backup: {ex.Message}");
        }
    }

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

    private static readonly HashSet<string> PokemonExtensions = [.. EntityFileExtension.GetExtensionsAll()];

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private const string HelpText =
        "Pokémon Manager (built on PKHeX)\n" +
        "\n" +
        "Saves are read from the Saves folder, plus PokemonManager/Saves.\n" +
        "A backup is written to PokemonManager/Backups before every change.\n" +
        "\n" +
        "Each game's menu has Events (every ticket the game has, like the Aurora Ticket), Distributions (Pokémon " +
        "you can only get from an event) and the Gallery (every event file for that game and language, from Project " +
        "Pokémon's EventsGallery). Items are marked Legal or Illegal.\n" +
        "\n" +
        "Your own gift files go in PokemonManager/Gifts (More > Gift files):\n" +
        "Gen 3: .wc3 .wn3 .me3 .ect .ecb\n" +
        "Gen 4+: .pgt .pcd .wc4 .pgf .wc6 .wc7 .wc8 .wc9...\n" +
        "Put Pokémon files (.pk3 etc.) in PokemonManager/Import.\n" +
        "\n" +
        SaveStateWarning;
}
