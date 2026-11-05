using OpenSynapse.Core.Displays;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class InternalDisplayAvailabilityTests
{
    // An external-display-only laptop is a normal setup. The controller signals it with
    // this exception so callers can hide the panel controls without showing an error.
    [Fact]
    public void NoActiveInternalDisplayIsDistinguishableFromAGenericFailure()
    {
        Assert.IsAssignableFrom<InvalidOperationException>(
            new NoActiveInternalDisplayException());
    }
}
