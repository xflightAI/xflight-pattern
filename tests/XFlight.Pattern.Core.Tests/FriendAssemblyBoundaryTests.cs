using System.Runtime.CompilerServices;
using XFlight.Pattern.Core.PhaseDetection;

namespace XFlight.Pattern.Core.Tests;

public sealed class FriendAssemblyBoundaryTests
{
    [Fact]
    public void CoreAssemblyHasNoFriendAssemblyGrant()
    {
        Assert.Empty(typeof(PhaseReplayContext).Assembly
            .GetCustomAttributes(typeof(InternalsVisibleToAttribute), inherit: false));
    }
}
