using System.Reflection;

namespace XFlight.Pattern.Core.Tests;

public sealed class AssemblySmokeTests
{
    [Fact]
    public void Core_assembly_can_be_loaded()
    {
        Assembly assembly = Assembly.Load("XFlight.Pattern.Core");

        Assert.Equal("XFlight.Pattern.Core", assembly.GetName().Name);
    }
}

