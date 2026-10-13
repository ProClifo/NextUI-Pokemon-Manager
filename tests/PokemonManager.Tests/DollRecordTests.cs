using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class DollRecordTests
{
    [Fact]
    public void RemembersEachGamesDollsAcrossRuns()
    {
        var file = Path.Combine(Path.GetTempPath(), $"pkmgr-dolls-{Guid.NewGuid():N}.txt");
        try
        {
            new DollRecord(file).Add("Emerald - MAY - 12345", 118);
            var again = new DollRecord(file);
            Assert.True(again.Has("Emerald - MAY - 12345", 118));
            Assert.False(again.Has("Emerald - MAY - 12345", 119));
            Assert.False(again.Has("Ruby - BRENDAN - 54321", 118));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
