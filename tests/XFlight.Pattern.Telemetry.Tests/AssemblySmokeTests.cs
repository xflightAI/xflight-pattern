using System.Reflection;

namespace XFlight.Pattern.Telemetry.Tests;

public sealed class AssemblySmokeTests
{
    [Fact]
    public void Telemetry_assembly_can_be_loaded()
    {
        Assembly assembly = Assembly.Load("XFlight.Pattern.Telemetry");

        Assert.Equal("XFlight.Pattern.Telemetry", assembly.GetName().Name);
    }
}

