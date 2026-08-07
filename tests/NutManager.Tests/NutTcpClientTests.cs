using System.Net;
using System.Net.Sockets;
using System.Text;
using NutManager.Core.Models;
using NutManager.Infrastructure.NutProtocol;
using Xunit;

namespace NutManager.Tests;

public sealed class NutTcpClientTests
{
    [Fact]
    public async Task ListsOneUpsAndPreservesAQuotedDescription()
    {
        await using var server = await FakeNutServer.StartAsync(stream =>
            FakeNutServer.WriteResponseAsync(stream, "BEGIN LIST UPS\nUPS primary \"Primary UPS\"\nEND LIST UPS\n"));

        var devices = await CreateClient().ListUpsAsync(server.Endpoint, CancellationToken.None);

        Assert.Equal("LIST UPS", await server.Command);
        var device = Assert.Single(devices);
        Assert.Equal("primary", device.Name);
        Assert.Equal("Primary UPS", device.Description);
    }

    [Fact]
    public async Task ListsMultipleUpsAndDecodesEscapedQuotesAndBackslashes()
    {
        await using var server = await FakeNutServer.StartAsync(stream =>
            FakeNutServer.WriteResponseAsync(
                stream,
                "BEGIN LIST UPS\nUPS alpha \"Rack \\\"A\\\" \\\\ West\"\nUPS beta \"Secondary UPS\"\nEND LIST UPS\n"));

        var devices = await CreateClient().ListUpsAsync(server.Endpoint, CancellationToken.None);

        Assert.Collection(
            devices,
            device =>
            {
                Assert.Equal("alpha", device.Name);
                Assert.Equal("Rack \"A\" \\ West", device.Description);
            },
            device => Assert.Equal("beta", device.Name));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task AcceptsEmptyListsWithLfOrCrlf(string newline)
    {
        await using var server = await FakeNutServer.StartAsync(stream =>
            FakeNutServer.WriteResponseAsync(stream, $"BEGIN LIST UPS{newline}END LIST UPS{newline}"));

        var devices = await CreateClient().ListUpsAsync(server.Endpoint, CancellationToken.None);

        Assert.Empty(devices);
    }

    [Fact]
    public async Task ReadsFragmentedResponses()
    {
        await using var server = await FakeNutServer.StartAsync(stream =>
            FakeNutServer.WriteResponseAsync(
                stream,
                "BEGIN LIST UPS\nUPS primary \"Primary UPS\"\nEND LIST UPS\n",
                writeOneByteAtATime: true));

        var devices = await CreateClient().ListUpsAsync(server.Endpoint, CancellationToken.None);

        Assert.Equal("primary", Assert.Single(devices).Name);
    }

    [Fact]
    public async Task RetrievesVariablesMapsIdentityAndPreservesUnknownVariables()
    {
        var timestamp = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
        await using var server = await FakeNutServer.StartAsync(stream =>
            FakeNutServer.WriteResponseAsync(stream, CreateValidVariableResponse("mockups")));

        var snapshot = await CreateClient(timestamp).GetSnapshotAsync(server.Endpoint, "mockups", CancellationToken.None);

        Assert.Equal("LIST VAR \"mockups\"", await server.Command);
        Assert.Equal("Primary UPS", snapshot.Identity.Description);
        Assert.Equal("Example Power", snapshot.Identity.Manufacturer);
        Assert.Equal("Tower 1500", snapshot.Identity.Model);
        Assert.Equal("SERIAL-42", snapshot.Identity.SerialNumber);
        Assert.Equal("unmapped", snapshot.Variables["vendor.variable"].Value);
        Assert.Equal(DataSource.Live, snapshot.Source);
        Assert.Equal(timestamp, snapshot.LastSuccessfulUpdate);
    }

    [Fact]
    public async Task FallsBackToUpsIdentityVariables()
    {
        const string response = "BEGIN LIST VAR mockups\n" +
                                "VAR mockups ups.mfr \"Fallback Power\"\n" +
                                "VAR mockups ups.model \"Fallback Model\"\n" +
                                "VAR mockups ups.serial \"Fallback Serial\"\n" +
                                "END LIST VAR mockups\n";
        await using var server = await FakeNutServer.StartAsync(stream => FakeNutServer.WriteResponseAsync(stream, response));

        var snapshot = await CreateClient().GetSnapshotAsync(server.Endpoint, "mockups", CancellationToken.None);

        Assert.Equal("Fallback Power", snapshot.Identity.Manufacturer);
        Assert.Equal("Fallback Model", snapshot.Identity.Model);
        Assert.Equal("Fallback Serial", snapshot.Identity.SerialNumber);
        Assert.Empty(snapshot.StatusTokens);
    }

    [Fact]
    public async Task MapsStatusAndAllNormalizedMetrics()
    {
        await using var server = await FakeNutServer.StartAsync(stream =>
            FakeNutServer.WriteResponseAsync(stream, CreateValidVariableResponse("mockups")));

        var snapshot = await CreateClient().GetSnapshotAsync(server.Endpoint, "mockups", CancellationToken.None);

        Assert.Equal(new[] { "OL", "VENDOR_TOKEN" }, snapshot.StatusTokens.Select(token => token.OriginalToken));
        Assert.False(snapshot.StatusTokens[1].IsKnown);
        Assert.Equal(230.4m, snapshot.InputVoltage);
        Assert.Equal(231m, snapshot.OutputVoltage);
        Assert.Equal(120.5m, snapshot.LoadPercentage);
        Assert.Equal(50m, snapshot.Frequency);
        Assert.Equal(29.5m, snapshot.Temperature);
        Assert.Equal(27.2m, snapshot.BatteryVoltage);
        Assert.Equal(98m, snapshot.BatteryChargePercentage);
        Assert.Equal(TimeSpan.FromSeconds(1800), snapshot.Runtime);
    }

    [Fact]
    public async Task UsesOutputFrequencyWhenInputFrequencyIsAbsent()
    {
        const string response = "BEGIN LIST VAR mockups\n" +
                                "VAR mockups output.frequency \"60\"\n" +
                                "END LIST VAR mockups\n";
        await using var server = await FakeNutServer.StartAsync(stream => FakeNutServer.WriteResponseAsync(stream, response));

        var snapshot = await CreateClient().GetSnapshotAsync(server.Endpoint, "mockups", CancellationToken.None);

        Assert.Equal(60m, snapshot.Frequency);
    }

    [Fact]
    public async Task KeepsInvalidNumericValuesRawAndLeavesNormalizedValuesMissing()
    {
        const string response = "BEGIN LIST VAR mockups\n" +
                                "VAR mockups input.voltage \"not-a-number\"\n" +
                                "VAR mockups battery.runtime \"-1\"\n" +
                                "END LIST VAR mockups\n";
        await using var server = await FakeNutServer.StartAsync(stream => FakeNutServer.WriteResponseAsync(stream, response));

        var snapshot = await CreateClient().GetSnapshotAsync(server.Endpoint, "mockups", CancellationToken.None);

        Assert.Equal("not-a-number", snapshot.Variables["input.voltage"].Value);
        Assert.Null(snapshot.InputVoltage);
        Assert.Null(snapshot.Runtime);
    }

    [Fact]
    public async Task PreservesServerErrorResponses()
    {
        await using var server = await FakeNutServer.StartAsync(stream =>
            FakeNutServer.WriteResponseAsync(stream, "ERR UNKNOWN-UPS\n"));

        var exception = await Assert.ThrowsAsync<NutServerErrorException>(
            () => CreateClient().ListUpsAsync(server.Endpoint, CancellationToken.None));

        Assert.Equal("ERR UNKNOWN-UPS", exception.RawResponse);
        Assert.Equal("The NUT server returned an error.", exception.Message);
    }

    [Theory]
    [InlineData("BEGIN LIST VAR mockups\n")]
    [InlineData("BEGIN LIST UPS\nEND LIST VAR\n")]
    [InlineData("UPS primary \"Missing begin\"\n")]
    public async Task RejectsInvalidListUpsFraming(string response)
    {
        await using var server = await FakeNutServer.StartAsync(stream => FakeNutServer.WriteResponseAsync(stream, response));

        await Assert.ThrowsAsync<NutProtocolException>(
            () => CreateClient().ListUpsAsync(server.Endpoint, CancellationToken.None));
    }

    [Theory]
    [InlineData("BEGIN LIST VAR mockups\nVAR mockups value\nEND LIST VAR mockups\n")]
    [InlineData("BEGIN LIST VAR otherups\nEND LIST VAR otherups\n")]
    [InlineData("BEGIN LIST VAR mockups\nVAR otherups ups.status \"OL\"\nEND LIST VAR mockups\n")]
    [InlineData("BEGIN LIST VAR mockups\nVAR mockups ups.status \"unterminated\n")]
    public async Task RejectsMalformedVariableResponses(string response)
    {
        await using var server = await FakeNutServer.StartAsync(stream => FakeNutServer.WriteResponseAsync(stream, response));

        await Assert.ThrowsAsync<NutProtocolException>(
            () => CreateClient().GetSnapshotAsync(server.Endpoint, "mockups", CancellationToken.None));
    }

    [Fact]
    public async Task RejectsDuplicateVariables()
    {
        const string response = "BEGIN LIST VAR mockups\n" +
                                "VAR mockups ups.status \"OL\"\n" +
                                "VAR mockups ups.status \"OB\"\n" +
                                "END LIST VAR mockups\n";
        await using var server = await FakeNutServer.StartAsync(stream => FakeNutServer.WriteResponseAsync(stream, response));

        var exception = await Assert.ThrowsAsync<NutProtocolException>(
            () => CreateClient().GetSnapshotAsync(server.Endpoint, "mockups", CancellationToken.None));

        Assert.Contains("Duplicate variable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsLineBreaksInUpsNames()
    {
        await using var server = await FakeNutServer.StartAsync(stream => Task.CompletedTask);

        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateClient().GetSnapshotAsync(server.Endpoint, "mockups\nLIST UPS", CancellationToken.None));
    }

    [Fact]
    public async Task RespectsCancellationBeforeConnecting()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient().ListUpsAsync(new NutEndpoint("127.0.0.1", 1), cancellationTokenSource.Token));
    }

    [Fact]
    public async Task RespectsCancellationWhileWaitingForAResponse()
    {
        var responseBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await FakeNutServer.StartAsync(async stream =>
        {
            responseBlocked.SetResult();
            var buffer = new byte[1];
            await stream.ReadAtLeastAsync(buffer, 1, throwOnEndOfStream: false);
        });
        using var cancellationTokenSource = new CancellationTokenSource();
        var operation = CreateClient().ListUpsAsync(server.Endpoint, cancellationTokenSource.Token);

        await responseBlocked.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public async Task TimesOutWhenTheResponseDoesNotArrive()
    {
        var responseBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await FakeNutServer.StartAsync(async stream =>
        {
            responseBlocked.SetResult();
            var buffer = new byte[1];
            await stream.ReadAtLeastAsync(buffer, 1, throwOnEndOfStream: false);
        });

        var endpoint = new NutEndpoint("127.0.0.1", server.Port, TimeSpan.FromMilliseconds(100));
        var operation = CreateClient().ListUpsAsync(endpoint, CancellationToken.None);

        await responseBlocked.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var exception = await Assert.ThrowsAsync<TimeoutException>(() => operation);
        Assert.Equal("The NUT operation timed out.", exception.Message);
    }

    [Fact]
    public async Task ReturnedCollectionsDoNotExposeMutableClientState()
    {
        await using var server = await FakeNutServer.StartAsync(stream =>
            FakeNutServer.WriteResponseAsync(stream, "BEGIN LIST UPS\nUPS primary \"Primary UPS\"\nEND LIST UPS\n"));

        var devices = await CreateClient().ListUpsAsync(server.Endpoint, CancellationToken.None);
        var collection = Assert.IsAssignableFrom<ICollection<UpsIdentity>>(devices);

        Assert.Throws<NotSupportedException>(() => collection.Add(new UpsIdentity("other")));
    }

    private static NutTcpClient CreateClient(DateTimeOffset? timestamp = null) =>
        new(new FixedTimeProvider(timestamp ?? new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero)));

    private static string CreateValidVariableResponse(string upsName) =>
        $"BEGIN LIST VAR {upsName}\n" +
        $"VAR {upsName} ups.description \"Primary UPS\"\n" +
        $"VAR {upsName} device.mfr \"Example Power\"\n" +
        $"VAR {upsName} device.model \"Tower 1500\"\n" +
        $"VAR {upsName} device.serial \"SERIAL-42\"\n" +
        $"VAR {upsName} ups.status \"OL VENDOR_TOKEN\"\n" +
        $"VAR {upsName} input.voltage \"230.4\"\n" +
        $"VAR {upsName} output.voltage \"231\"\n" +
        $"VAR {upsName} ups.load \"120.5\"\n" +
        $"VAR {upsName} input.frequency \"50\"\n" +
        $"VAR {upsName} ups.temperature \"29.5\"\n" +
        $"VAR {upsName} battery.voltage \"27.2\"\n" +
        $"VAR {upsName} battery.charge \"98\"\n" +
        $"VAR {upsName} battery.runtime \"1800\"\n" +
        $"VAR {upsName} vendor.variable \"unmapped\"\n" +
        $"END LIST VAR {upsName}\n";

    private sealed class FixedTimeProvider(DateTimeOffset timestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => timestamp;
    }

    private sealed class FakeNutServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<NetworkStream, Task> _responseWriter;
        private readonly TaskCompletionSource<string> _commandSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _serverTask;

        private FakeNutServer(Func<NetworkStream, Task> responseWriter)
        {
            _responseWriter = responseWriter;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serverTask = RunAsync();
        }

        public int Port { get; }

        public NutEndpoint Endpoint => new("127.0.0.1", Port, TimeSpan.FromSeconds(1));

        public Task<string> Command => _commandSource.Task;

        public static Task<FakeNutServer> StartAsync(Func<NetworkStream, Task> responseWriter) =>
            Task.FromResult(new FakeNutServer(responseWriter));

        public static async Task WriteResponseAsync(
            NetworkStream stream,
            string response,
            bool writeOneByteAtATime = false)
        {
            var bytes = Encoding.UTF8.GetBytes(response);
            if (writeOneByteAtATime)
            {
                foreach (var value in bytes)
                {
                    await stream.WriteAsync(new[] { value });
                }
            }
            else
            {
                await stream.WriteAsync(bytes);
            }

            await stream.FlushAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await _serverTask;
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task RunAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var command = await reader.ReadLineAsync();
            _commandSource.TrySetResult(command ?? string.Empty);
            await _responseWriter(stream);
        }
    }
}
