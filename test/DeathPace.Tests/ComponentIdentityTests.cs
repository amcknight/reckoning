using LiveSplit.UI.Components;
using Xunit;

namespace DeathPace.Tests;

public class ComponentIdentityTests
{
    [Fact]
    public void FactoryReportsTheSmwPrefixedMenuName()
    {
        Assert.Equal("SMW Death Pace", new DeathPaceComponentFactory().ComponentName);
    }
}
