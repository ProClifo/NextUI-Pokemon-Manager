using PKHeX.Core;
using PokemonManager;
using PokemonManager.Core;
using PokemonManager.Gen3;
using PokemonManager.Ui;

// pkmgr: Pokémon Manager for NextUI.
//   pkmgr ui                      on-device menus (default; uses minui-list/minui-presenter when on PATH)
//   pkmgr <command> ...           scriptable commands, see Usage below

Legality.UseCartridgeEra();
var argList = args.ToList();
string? sd = TakeOption(argList, "--sd");
string? data = TakeOption(argList, "--data");
var command = argList.Count == 0 ? "ui" : argList[0];
var rest = argList.Skip(1).ToList();

try
{
    return command switch
    {
        "ui" => RunUi(),
        "version" or "--version" => Print($"pkmgr {typeof(App).Assembly.GetName().Version} (PKHeX.Core {typeof(SaveFile).Assembly.GetName().Version})"),
        "selftest" => SelfTest(),
        "scan" => Scan(),
        "list" => List(rest),
        "show" => Show(rest),
        "transfer" => Transfer(rest),
        "trade-evolve" => TradeEvolve(rest),
        "inject" => Inject(rest),
        "events" => Events(rest),
        "redeem-event" => RedeemEvent(rest),
        "gen3-status" => Gen3Status(rest),
        "box-scene" => BoxSceneCommand(rest),
        "gallery-build" => GalleryBuild(rest),
        "gallery" => GalleryList(rest),
        "help" or "--help" or "-h" => Usage(0),
        _ => Usage(2),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

int RunUi()
{
    var paths = AppPaths.FromEnvironment(sd, data);
    bool console = Environment.GetEnvironmentVariable("PKMGR_UI") == "console";
    if (!console && !MinUi.IsAvailable())
    {
        Console.Error.WriteLine("minui-list/minui-presenter not found on PATH. Set PKMGR_UI=console to use the text UI.");
        return 3;
    }
    IUi ui = console ? new ConsoleUi() : new MinUi(paths.TempDir, paths.IconsDir,
        GameFonts.Scale(Environment.GetEnvironmentVariable("PLATFORM"), Environment.GetEnvironmentVariable("DEVICE")));
    return new App(ui, paths).Run();
}

// Quick on-device sanity check used by launch.sh: proves the runtime starts and PKHeX loads its data.
int SelfTest()
{
    var sav = BlankSaveFile.Get(GameVersion.E);
    var pk = new PK3 { Species = (ushort)Species.Kadabra, CurrentLevel = 30 };
    var options = TradeEvolution.GetOptions(pk);
    return Print(options.Count == 1 && sav.Generation == 3 ? "ok" : "selftest failed");
}

int Scan()
{
    var paths = AppPaths.FromEnvironment(sd, data);
    var saves = SaveLibrary.Scan(paths.SaveRoots);
    var (shown, hidden) = OfficialRomFilter.Apply(saves, paths.RomRoots, paths.RomCheckCache, paths.ExtraSavesDir);
    foreach (var s in shown)
        Console.WriteLine($"{s.Path}\t{s.Label}\tofficial");
    foreach (var h in hidden)
        Console.WriteLine($"{h.Save.Path}\t{h.Save.Label}\thidden: {h.Check.Describe()}");
    return 0;
}

int List(List<string> a)
{
    var entry = LoadSave(a, 0);
    foreach (var slot in SlotRef.AllOccupied(entry.Sav))
        Console.WriteLine($"{Format(slot)}\t{Names.Summary(slot.Get(entry.Sav))}");
    return 0;
}

int Show(List<string> a)
{
    var entry = LoadSave(a, 0);
    var slot = ParseSlot(Arg(a, 1, "slot"));
    return Print(Names.Details(slot.Get(entry.Sav)));
}

int Transfer(List<string> a)
{
    bool copy = TakeFlag(a, "--copy");
    bool unofficial = TakeFlag(a, "--illegal") | TakeFlag(a, "--unofficial");
    var source = LoadSave(a, 0);
    var slot = ParseSlot(Arg(a, 1, "slot"));
    var dest = LoadSave(a, 2);
    var result = TransferService.Transfer(source, slot, dest, copy ? TransferMode.Copy : TransferMode.Move, unofficial, out _);
    if (!result.Ok)
        return Fail(result.Message);
    var lib = Library();
    lib.Write(dest);
    if (!copy)
        lib.Write(source);
    return Print(result.Message);
}

int TradeEvolve(List<string> a)
{
    bool all = TakeFlag(a, "--all");
    var entry = LoadSave(a, 0);
    if (all)
    {
        var (count, lines) = TradeEvolution.EvolveAllEligible(entry.Sav);
        if (count > 0)
            Library().Write(entry);
        lines.ForEach(Console.WriteLine);
        return Print($"Evolved {count} Pokémon.");
    }

    var slot = ParseSlot(Arg(a, 1, "slot"));
    var options = TradeEvolution.GetOptions(slot.Get(entry.Sav));
    if (options.Count == 0)
        return Fail("That Pokémon doesn't evolve by trading.");
    int choice = a.Count > 2 ? int.Parse(a[2]) : 0;
    if (options.Count > 1 && a.Count <= 2)
    {
        for (int i = 0; i < options.Count; i++)
            Console.WriteLine($"{i}\t{options[i].Label}");
        return Fail("Several evolutions are possible; pass the option number as the third argument.");
    }
    var result = TradeEvolution.Evolve(entry.Sav, slot, options[choice]);
    if (!result.Ok)
        return Fail(result.Message);
    Library().Write(entry);
    return Print(result.Message);
}

int Inject(List<string> a)
{
    bool toBox = TakeFlag(a, "--box");
    var entry = LoadSave(a, 0);
    var path = Arg(a, 1, "gift file");
    OpResult result;
    if (Gen3Events.Parse(path) is { } g3)
    {
        if (entry.Sav is not SAV3 sav3)
            return Fail("Gen 3 event files only go into Gen 3 saves.");
        result = Gen3Events.Inject(sav3, g3);
    }
    else
    {
        var gift = MysteryGift.GetMysteryGift(File.ReadAllBytes(path), Path.GetExtension(path))
                   ?? throw new InvalidDataException("Not a recognized gift file.");
        result = toBox || !GiftService.SupportsAlbum(entry.Sav)
            ? GiftService.Redeem(entry.Sav, gift)
            : GiftService.InjectCard(entry.Sav, gift);
    }
    if (!result.Ok)
        return Fail(result.Message);
    Library().Write(entry);
    return Print(result.Message);
}

int Events(List<string> a)
{
    var entry = LoadSave(a, 0);
    var events = GiftService.BuiltInEvents(entry.Sav);
    for (int i = 0; i < events.Count; i++)
        Console.WriteLine($"{i}\t{events[i].Name}");
    return 0;
}

int RedeemEvent(List<string> a)
{
    var entry = LoadSave(a, 0);
    int index = int.Parse(Arg(a, 1, "event index"));
    var events = GiftService.BuiltInEvents(entry.Sav);
    var result = GiftService.Redeem(entry.Sav, events[index].Encounter);
    if (!result.Ok)
        return Fail(result.Message);
    Library().Write(entry);
    return Print(result.Message);
}

int Gen3Status(List<string> a)
{
    var entry = LoadSave(a, 0);
    return entry.Sav is SAV3 sav3 ? Print(Gen3Events.Status(sav3)) : Fail("Not a Gen 3 save.");
}

// Writes the scene the PC box viewer would get, for testing pkmgr-box --screenshot on a PC.
int BoxSceneCommand(List<string> a)
{
    var entry = LoadSave(a, 0);
    var output = Arg(a, 1, "output file");
    var assets = a.Count > 2 ? a[2] : AppPaths.FromEnvironment(sd, data).BoxAssetsDir;
    var start = a.Count > 3 ? ParseSlot(a[3]) : new SlotRef(0, 0);
    var font = a.Count > 4 ? new GameFonts(a[4], 2).For(entry.Sav, GalleryLanguage.English) : null;
    File.WriteAllText(output, new BoxScene(assets).Build(entry.Sav, entry.Label, start, font).ToJsonString());
    return 0;
}

SaveLibrary Library() => new(AppPaths.FromEnvironment(sd, data).BackupDir);

static SaveEntry LoadSave(List<string> a, int index)
{
    var path = Arg(a, index, "save file");
    return SaveLibrary.Load(path) ?? throw new InvalidDataException($"{path} isn't a save file PKHeX recognizes.");
}

static string Arg(List<string> a, int index, string what)
    => index < a.Count ? a[index] : throw new ArgumentException($"missing argument: {what}");

static string Format(SlotRef s) => s.IsParty ? $"p:{s.Slot + 1}" : $"{s.Box + 1}:{s.Slot + 1}";

// "p:1" = party slot 1, "3:12" = box 3 slot 12 (both 1-based).
static SlotRef ParseSlot(string text)
{
    var parts = text.Split(':');
    if (parts.Length != 2)
        throw new ArgumentException("slots look like p:1 (party) or 3:12 (box 3, slot 12)");
    int slot = int.Parse(parts[1]) - 1;
    return parts[0] is "p" or "P" ? SlotRef.Party(slot) : new SlotRef(int.Parse(parts[0]) - 1, slot);
}

int GalleryBuild(List<string> a)
{
    if (a.Count < 2)
        return Usage(2);
    var (added, skipped) = GalleryBuilder.Build(a[0], a[1], Console.Error);
    return Print($"{added} gallery files bundled into {a[1]} ({skipped} unreadable or illegal files skipped)");
}

int GalleryList(List<string> a)
{
    bool allLanguages = TakeFlag(a, "--all-languages");
    bool unreleased = TakeFlag(a, "--unreleased");
    if (a.Count < 1)
        return Usage(2);
    var paths = AppPaths.FromEnvironment(sd, data);
    var sav = SaveLibrary.Load(a[0]) ?? throw new InvalidOperationException("Not a Pokémon save.");
    var roms = new VanillaRoms.Index(paths.RomRoots, null).FindRoms(a[0]);
    var profile = GameProfile.For(sav.Sav, roms);
    var gallery = new GalleryArchive(paths.GalleryFile);
    var what = a.Count > 1 ? a[1] : "all";
    var shown = what switch
    {
        "events" => Tickets.For(gallery, profile, sav.Sav)
            .Select(t => (t.File ?? new GalleryEntry("(Record Mixing)", 3, [], null, true, GalleryKind.Gen3, 0, 0, GalleryFlags.EventItem, ""))
                with { Title = $"{t.Ticket.Name} ({(t.Legal ? "legal" : "illegal")}): {t.File?.Title ?? "given as by Record Mixing"}" })
            .ToList(),
        "distributions" => GalleryLists.Distributions(gallery, profile),
        _ => gallery.Entries.Where(e => profile.Matches(e, allLanguages, unreleased)).ToList(),
    };
    Console.WriteLine($"{Names.Game(sav.Sav)}: Gen {profile.Generation}, games {string.Join('/', profile.Games)}, {GalleryLanguage.Name(profile.Language)}");
    foreach (var e in shown)
        Console.WriteLine($"  {e.Title}  [{e.Folder}]");
    return Print($"{shown.Count} files");
}

static string? TakeOption(List<string> a, string name)
{
    int i = a.IndexOf(name);
    if (i < 0 || i + 1 >= a.Count)
        return null;
    var value = a[i + 1];
    a.RemoveRange(i, 2);
    return value;
}

static bool TakeFlag(List<string> a, string name) => a.Remove(name);

static int Print(string text)
{
    Console.WriteLine(text);
    return 0;
}

static int Fail(string text)
{
    Console.Error.WriteLine(text);
    return 1;
}

static int Usage(int code)
{
    Console.WriteLine("""
        pkmgr - Pokémon Manager for NextUI (built on PKHeX)

        Usage: pkmgr [--sd <sdcard>] [--data <dir>] <command> [args]

          ui                                   on-device menus (default)
          scan                                 list Pokémon saves under <sd>/Saves and whether their ROM is official
          list <save>                          list Pokémon (slots: p:1 = party 1, 3:12 = box 3 slot 12)
          show <save> <slot>                   show one Pokémon
          transfer <src> <slot> <dst> [--copy] [--illegal]
          trade-evolve <save> <slot> [option] | trade-evolve <save> --all
          inject <save> <gift file> [--box]    .wc3/.wn3/.me3/.ect/.ecb or .pgt/.pcd/.pgf/.wc6/.wc7/.wc8/.wc9...
          events <save>                        list PKHeX's built-in events for this game
          redeem-event <save> <index>          send a built-in event Pokémon to the PC
          gen3-status <save>                   Gen 3 Mystery Gift / Event status
          box-scene <save> <out.json> [assets] [slot] [fonts]   scene file for the PC box viewer (testing)
          gallery-build <EventsGallery dir> <out.zip>   bundle the Gen 1-5 EventsGallery files (build time)
          gallery <save> [events|distributions|all] [--all-languages] [--unreleased]
                                               gallery files the game menus would list
          selftest | version
        """);
    return code;
}
