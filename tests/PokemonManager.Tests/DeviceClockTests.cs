using PokemonManager.Core;
using Xunit;

namespace PokemonManager.Tests;

public sealed class DeviceClockTests
{
    [Fact]
    public void LibcLocalTimeMatchesDotNetWhereBothKnowTheZone()
    {
        // here .NET has time zone data, so both clocks agree; on a device without it only libc's is right
        var difference = (DeviceClock.LocalNow() - DateTime.Now).Duration();
        Assert.True(difference < TimeSpan.FromMinutes(1), $"off by {difference}");
    }
}
