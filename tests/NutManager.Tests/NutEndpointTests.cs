using NutManager.Core.Models;
using Xunit;

namespace NutManager.Tests;

public sealed class NutEndpointTests
{
    [Fact]
    public void UsesTheDefaultNutPort()
    {
        var endpoint = new NutEndpoint("ups.example.test");

        Assert.Equal(3493, endpoint.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsEmptyHosts(string host)
    {
        Assert.Throws<ArgumentException>(() => new NutEndpoint(host));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void RejectsInvalidPorts(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NutEndpoint("ups.example.test", port));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidTimeouts(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NutEndpoint("ups.example.test", timeout: TimeSpan.FromMilliseconds(milliseconds)));
    }
}
