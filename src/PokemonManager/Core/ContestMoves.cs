namespace PokemonManager.Core;

/// <summary>The Hoenn contests' categories of moves 1-354 (pokeemerald's src/data/contest_moves.h: gContestMoves[].contestCategory).</summary>
public static class ContestMoves
{
    // One letter per move number (50 a line): C(ool), B(eauty), c(U)te, S(mart), T(ough); "-" for none.
    private const string Categories =
        "-TTTTTSBBCTTCCBCSCSSTTCTCCCCUTCCCTTTTTTUSCCCTUCUSC" +
        "SSBBBUBBBBBBBCCCCTTTTSSSBCCSSSBSCBCCCCTTTSSSSSBCCC" +
        "CSUSCSTUSSUUCBBSCTUSBTTTTTBTTCTCTUSBCTSSTSBCSUCBBS" +
        "UTTBTTUTCUBBTCSCSCTSSSBUTUBCBCTBUCTSUUTUTSCSSBBCTS" +
        "CTSTUTCUUCCCBUUBUUUBSBTCCCUUSCUCCCBSBSCCTBTBSCTSST" +
        "BSUUTTTBBTSBTUTSUBSSSSUUUSTBSTCUSTBSSUTSSBTSBSSBUB" +
        "UBSUCSCBBCSSSSCBSSBSSUCBBSCCSBTCCBTUCCCBUTSUCBUSCC" +
        "TCBCS";

    /// <summary>The type icon's name for a move's contest category (contest_cool...), or null.</summary>
    public static string? Category(ushort move) => move < Categories.Length
        ? Categories[move] switch
        {
            'C' => "contest_cool",
            'B' => "contest_beauty",
            'U' => "contest_cute",
            'S' => "contest_smart",
            'T' => "contest_tough",
            _ => null,
        }
        : null;
}
