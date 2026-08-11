using System.Net.Sockets;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.NutProtocol;
using Xunit;

namespace NutManager.Tests;

public sealed class ManagedNutConnectionTesterTests
{
    [Fact]
    public async Task SuccessUsesReadOnlyListUpsAndFindsPreferredUps()
    {
        var client = new FakeNutClient([new UpsIdentity("ups-a"), new UpsIdentity("ups-b")]);
        var tester = new ManagedNutConnectionTester(client);

        var result = await tester.TestAsync(new NutEndpoint("host", 3493), "ups-b", CancellationToken.None);

        Assert.Equal(ManagedNutConnectionTestStatus.Success, result.Status);
        Assert.Equal(1, client.ListCalls);
        Assert.Equal(0, client.SnapshotCalls);
        Assert.Equal(["ups-a", "ups-b"], result.DiscoveredUpsNames);
    }

    [Fact]
    public async Task EmptyListAndMissingPreferredUpsHaveDistinctResults()
    {
        var empty = await new ManagedNutConnectionTester(new FakeNutClient([]))
            .TestAsync(new NutEndpoint("host"), null, CancellationToken.None);
        var missing = await new ManagedNutConnectionTester(new FakeNutClient([new UpsIdentity("ups-a")]))
            .TestAsync(new NutEndpoint("host"), "ups-b", CancellationToken.None);

        Assert.Equal(ManagedNutConnectionTestStatus.NoUpsDiscovered, empty.Status);
        Assert.Equal(ManagedNutConnectionTestStatus.PreferredUpsMissing, missing.Status);
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task OperationalFailuresMapToTypedStatus(Exception exception, ManagedNutConnectionTestStatus expected)
    {
        var tester = new ManagedNutConnectionTester(new FakeNutClient(exception));

        var result = await tester.TestAsync(new NutEndpoint("host"), null, CancellationToken.None);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task CallerCancellationIsControlled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var tester = new ManagedNutConnectionTester(new FakeNutClient(new OperationCanceledException(cancellation.Token)));

        var result = await tester.TestAsync(new NutEndpoint("host"), null, cancellation.Token);

        Assert.Equal(ManagedNutConnectionTestStatus.Cancelled, result.Status);
    }

    public static TheoryData<Exception, ManagedNutConnectionTestStatus> FailureCases => new()
    {
        { new SocketException(), ManagedNutConnectionTestStatus.EndpointUnreachable },
        { new IOException(), ManagedNutConnectionTestStatus.EndpointUnreachable },
        { new TimeoutException(), ManagedNutConnectionTestStatus.Timeout },
        { new NutProtocolException("bad protocol"), ManagedNutConnectionTestStatus.ProtocolError },
        { new NutServerErrorException("ERR UNKNOWN-COMMAND"), ManagedNutConnectionTestStatus.ProtocolError },
        { new InvalidOperationException(), ManagedNutConnectionTestStatus.Failed }
    };

    private sealed class FakeNutClient : INutClient
    {
        private readonly IReadOnlyList<UpsIdentity>? _devices;
        private readonly Exception? _exception;

        public FakeNutClient(IReadOnlyList<UpsIdentity> devices) => _devices = devices;

        public FakeNutClient(Exception exception) => _exception = exception;

        public int ListCalls { get; private set; }

        public int SnapshotCalls { get; private set; }

        public Task<IReadOnlyList<UpsIdentity>> ListUpsAsync(NutEndpoint endpoint, CancellationToken cancellationToken)
        {
            ListCalls++;
            return _exception is null
                ? Task.FromResult(_devices!)
                : Task.FromException<IReadOnlyList<UpsIdentity>>(_exception);
        }

        public Task<UpsSnapshot> GetSnapshotAsync(NutEndpoint endpoint, string upsName, CancellationToken cancellationToken)
        {
            SnapshotCalls++;
            throw new NotSupportedException();
        }
    }
}
