using PKHeX.Core;

namespace PokemonManager.Core;

/// <summary>
/// Works out which Pokémon can only be had from an event, per generation and language: those PKHeX finds no
/// legal non-event way to obtain in a handheld game of that language (catching, gifts, in-game trades,
/// breeding, transfers from earlier handheld generations). Language matters: a Mew caught on Faraway Island
/// is only legitimate in Japanese Emerald, where the Old Sea Map was distributed, so Mew is event-only for
/// every other language.
/// </summary>
public static class EventOnly
{
    /// <summary>Gallery language codes that have their own save language per generation.</summary>
    public static string[] Languages(int generation) => generation switch
    {
        1 => [GalleryLanguage.Japanese, GalleryLanguage.International],
        2 => [GalleryLanguage.Japanese, GalleryLanguage.International, GalleryLanguage.Korean],
        3 => [GalleryLanguage.Japanese, GalleryLanguage.English, GalleryLanguage.French, GalleryLanguage.Italian, GalleryLanguage.German, GalleryLanguage.Spanish],
        _ => [.. GalleryLanguage.Tags],
    };

    /// <summary>Handheld games a Pokémon in this generation's games can come from (GameCube games don't count).</summary>
    public static GameVersion[] Sources(int generation) => generation switch
    {
        <= 2 => [GameVersion.RD, GameVersion.GN, GameVersion.BU, GameVersion.YW, GameVersion.GD, GameVersion.SI, GameVersion.C],
        3 => [GameVersion.R, GameVersion.S, GameVersion.E, GameVersion.FR, GameVersion.LG],
        4 => [.. Sources(3), GameVersion.D, GameVersion.P, GameVersion.Pt, GameVersion.HG, GameVersion.SS],
        _ => [.. Sources(4), GameVersion.B, GameVersion.W, GameVersion.B2, GameVersion.W2],
    };

    /// <summary>A blank save of the generation, with a trainer of that language, to judge legality against.</summary>
    public static SaveFile TrainerSave(int generation, string language)
    {
        var version = generation switch
        {
            1 => GameVersion.RD,
            2 => language == GalleryLanguage.Korean ? GameVersion.GD : GameVersion.C, // Korea only had Gold/Silver
            3 => GameVersion.E,
            4 => GameVersion.Pt,
            _ => GameVersion.B2,
        };
        // PKHeX generates Gen 1/2 encounters for Japanese trainers with blank names, and the Japanese and
        // international games have the same encounters anyway, so judge Japanese Gen 1/2 with an international trainer.
        if (generation <= 2 && language == GalleryLanguage.Japanese)
            language = GalleryLanguage.International;
        var (id, name) = language switch
        {
            GalleryLanguage.Japanese => (LanguageID.Japanese, "テスト"),
            GalleryLanguage.Korean => (LanguageID.Korean, "테스트"),
            GalleryLanguage.French => (LanguageID.French, "TEST"),
            GalleryLanguage.Italian => (LanguageID.Italian, "TEST"),
            GalleryLanguage.German => (LanguageID.German, "TEST"),
            GalleryLanguage.Spanish => (LanguageID.Spanish, "TEST"),
            _ => (LanguageID.English, "TEST"),
        };
        return BlankSaveFile.Get(version, name, id);
    }

    /// <summary>Whether a Pokémon of this species and form can be obtained legally without an event, in this save's language.</summary>
    public static bool IsObtainable(SaveFile sav, ushort species, byte form)
    {
        Legality.For(sav);
        var template = sav.BlankPKM;
        template.Species = species;
        template.Form = form;
        template.Language = sav.Language;
        IEnumerable<IEncounterable> encounters;
        try
        {
            encounters = EncounterMovesetGenerator.GenerateEncounters(template, sav, ReadOnlyMemory<ushort>.Empty, Sources(sav.Generation));
        }
        catch (Exception)
        {
            return true; // when in doubt, don't call a Pokémon event-only
        }

        foreach (var encounter in encounters)
        {
            if (EventPokemon.IsPerRecipient(encounter) || encounter is not IEncounterConvertible convertible)
                continue;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var pk = convertible.ConvertToPKM(sav);
                    if (pk.Species != species || pk.Form != form)
                        break;
                    if (sav.Generation <= 2)
                    {
                        // Gen 1/2 Pokémon can't change between the Japanese and international formats, but both
                        // regions' games have the same encounters, so judge the Pokémon as generated.
                        if (Legality.IsLegal(pk, sav))
                            return true;
                        continue;
                    }
                    // Obtained in a copy of this language, not traded in from another region's game.
                    pk.Language = sav.Language;
                    var check = TransferService.Prepare(pk, sav, allowUnofficial: false, out var prepared);
                    if (check.Ok && prepared is not null && Legality.IsLegal(prepared.Converted, sav))
                        return true;
                }
                catch (Exception)
                {
                    break;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// The form that decides whether a Pokémon is obtainable: forms the player can change in-game (Deoxys from
    /// Gen 4, Rotom, Giratina, Shaymin...) count as the base form.
    /// </summary>
    public static byte KeyForm(ushort species, byte form, int generation)
    {
        var context = (EntityContext)generation;
        return form != 0 && FormInfo.IsFormChangeable(species, form, 0, context, context) ? (byte)0 : form;
    }

    /// <summary>The event-only Pokémon among <paramref name="candidates"/>, for one generation and language.</summary>
    public static List<(ushort Species, byte Form)> Compute(int generation, string language, IEnumerable<(ushort Species, byte Form)> candidates)
    {
        var sav = TrainerSave(generation, language);
        return candidates.Distinct().Where(c => !IsObtainable(sav, c.Species, c.Form)).Order().ToList();
    }
}
