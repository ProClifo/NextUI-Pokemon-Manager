using PKHeX.Core;
using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class TicketReceivedTests : IDisposable
{
    private readonly TestSaves _saves = new();

    public void Dispose() => _saves.Dispose();

    private static TicketChoice Choice(string key) => new(Tickets.All.Single(t => t.Key == key), null, true);

    [Theory]
    [InlineData(GameVersion.E, Tickets.ItemAuroraTicket, "Aurora Ticket")]
    [InlineData(GameVersion.E, Tickets.ItemMysticTicket, "Mystic Ticket")]
    [InlineData(GameVersion.E, Tickets.ItemOldSeaMap, "Old Sea Map")]
    [InlineData(GameVersion.Pt, Tickets.ItemMemberCard, "Member Card")]
    [InlineData(GameVersion.Pt, Tickets.ItemOaksLetter, "Oak's Letter")]
    [InlineData(GameVersion.Pt, Tickets.ItemAzureFlute, "Azure Flute")]
    [InlineData(GameVersion.Pt, Tickets.ItemSecretKey, "Secret Key")]
    [InlineData(GameVersion.HG, Tickets.ItemEnigmaStone, "Enigma Stone")]
    [InlineData(GameVersion.B, Tickets.ItemLibertyPass, "Liberty Pass")]
    public void ItemIdsAreTheTickets(GameVersion version, int item, string name)
    {
        var sav = BlankSaveFile.Get(version, "ASH", LanguageID.English);
        var names = GameInfo.Strings.GetItemStrings(sav.Context, sav.Version);
        static string Plain(string s) => s.Replace('’', '\'').Replace(" (Pt)", "").Replace(" ", "");
        Assert.Equal(Plain(name), Plain(names[item]), ignoreCase: true);
    }

    private static void Give(SaveFile sav, int item)
    {
        var bag = sav.Inventory;
        Assert.True(bag.GetPouch(InventoryType.KeyItems).GiveItem(bag, (ushort)item, 1) > 0);
        bag.CopyTo(sav);
    }

    [Theory]
    [InlineData(GameVersion.E, "auroraticket", Tickets.ItemAuroraTicket, 0x8D5)]
    [InlineData(GameVersion.E, "mysticticket", Tickets.ItemMysticTicket, 0x8E0)]
    [InlineData(GameVersion.E, "oldseamap", Tickets.ItemOldSeaMap, 0x8D6)]
    [InlineData(GameVersion.FR, "auroraticket", Tickets.ItemAuroraTicket, 0x84B)]
    [InlineData(GameVersion.LG, "mysticticket", Tickets.ItemMysticTicket, 0x84A)]
    public void Gen3TicketsNeedTheItemAndTheFerryFlag(GameVersion version, string key, int item, int flag)
    {
        var sav = (SAV3)_saves.Create(version, $"{key}{version}.sav").Sav;
        var ticket = Choice(key);
        Assert.False(Tickets.Received(ticket, sav));
        sav.SetEventFlag(flag, true);
        Assert.False(Tickets.Received(ticket, sav)); // the flag alone
        sav.SetEventFlag(flag, false);
        Give(sav, item);
        Assert.False(Tickets.Received(ticket, sav)); // the item alone
        sav.SetEventFlag(flag, true);
        Assert.True(Tickets.Received(ticket, sav));
    }

    [Theory]
    [InlineData(GameVersion.Pt, "membercard", Tickets.ItemMemberCard, 0x43, 0x1209)]
    [InlineData(GameVersion.Pt, "oaksletter", Tickets.ItemOaksLetter, 0x44, 0x1112)]
    [InlineData(GameVersion.Pt, "azureflute", Tickets.ItemAzureFlute, 0x45, 0x1123)]
    [InlineData(GameVersion.Pt, "secretkey", Tickets.ItemSecretKey, 0x46, 0x1103)]
    [InlineData(GameVersion.D, "membercard", Tickets.ItemMemberCard, 0x43, 0x1209)]
    public void Gen4TicketsNeedTheItemAndTheDistributionVar(GameVersion version, string key, int item, int work, int magic)
    {
        var sav = (SAV4)_saves.Create(version, $"{key}{version}.sav").Sav;
        var ticket = Choice(key);
        Give(sav, item);
        Assert.False(Tickets.Received(ticket, sav)); // a card without the event isn't the real thing
        sav.SetWork(work, (ushort)magic);
        Assert.True(Tickets.Received(ticket, sav));
    }

    [Fact]
    public void EnigmaStoneStaysReceivedOnceStevenTakesIt()
    {
        var sav = (SAV4HGSS)_saves.Create(GameVersion.HG, "Enigma.sav").Sav;
        var ticket = Choice("enigmastone");
        sav.SetWork(0x43, 0x06F2);
        Assert.False(Tickets.Received(ticket, sav));
        Give(sav, Tickets.ItemEnigmaStone);
        Assert.True(Tickets.Received(ticket, sav));

        var taken = (SAV4HGSS)_saves.Create(GameVersion.HG, "Enigma2.sav").Sav;
        taken.SetWork(0x43, 0x06F2);
        taken.SetWork(0x83, 2); // the Pewter Museum event under way
        Assert.True(Tickets.Received(ticket, taken));
    }

    [Fact]
    public void LibertyPassIsReceivedWithTheItem()
    {
        var sav = _saves.Create(GameVersion.B, "Liberty.sav").Sav;
        Assert.False(Tickets.Received(Choice("libertypass"), sav));
        Give(sav, Tickets.ItemLibertyPass);
        Assert.True(Tickets.Received(Choice("libertypass"), sav));
    }

    [Theory]
    // Darkrai and Shaymin only ever came to Platinum; Arceus at the Hall of Origin never came at all.
    [InlineData(GameVersion.Pt, "membercard", true)]
    [InlineData(GameVersion.D, "membercard", false)]
    [InlineData(GameVersion.Pt, "oaksletter", true)]
    [InlineData(GameVersion.P, "oaksletter", false)]
    [InlineData(GameVersion.Pt, "azureflute", false)]
    [InlineData(GameVersion.Pt, "secretkey", true)] // only changes Rotom's form
    [InlineData(GameVersion.HG, "enigmastone", true)]
    [InlineData(GameVersion.B, "libertypass", true)]
    [InlineData(GameVersion.FR, "mysticticket", true)]
    // The Faraway Island Mew only in Japanese Emerald, Birth Island Deoxys in every other language's.
    [InlineData(GameVersion.E, "oldseamap", false)]
    [InlineData(GameVersion.E, "auroraticket", true)]
    public void TicketsAreIllegalOnlyWhenTheirPokemonIs(GameVersion version, string key, bool legal)
        => Assert.Equal(legal, Tickets.PokemonLegal(key, BlankSaveFile.Get(version, "ASH", LanguageID.German)));

    [Theory]
    [InlineData("oldseamap", true)]
    [InlineData("auroraticket", false)]
    public void JapaneseEmeraldIsTheOtherWayRound(string key, bool legal)
        => Assert.Equal(legal, Tickets.PokemonLegal(key, BlankSaveFile.Get(GameVersion.E, "サトシ", LanguageID.Japanese)));

    [Theory]
    // Japanese FireRed/LeafGreen did get the Aurora Ticket (only Emerald's Deoxys event skipped Japan).
    [InlineData(GameVersion.FR, "auroraticket")]
    [InlineData(GameVersion.LG, "mysticticket")]
    [InlineData(GameVersion.E, "mysticticket")]
    public void JapaneseGamesKeepTheirTickets(GameVersion version, string key)
        => Assert.True(Tickets.PokemonLegal(key, BlankSaveFile.Get(version, "サトシ", LanguageID.Japanese)));
}
