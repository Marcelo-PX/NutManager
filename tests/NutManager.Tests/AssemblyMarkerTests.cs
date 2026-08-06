using NutManager.Core;
using NutManager.Infrastructure;

namespace NutManager.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void ProjectReferencesAreAvailable()
    {
        Assert.NotNull(new NutManager.Core.AssemblyMarker());
        Assert.NotNull(new NutManager.Infrastructure.AssemblyMarker());
    }
}
