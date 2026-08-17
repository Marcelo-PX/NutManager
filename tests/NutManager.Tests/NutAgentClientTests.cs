using System.ComponentModel;
using System.IO.Pipes;
using NutManager.Core.Administration;
using NutManager.Core.Agent;
using NutManager.Infrastructure.Agent;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// The client half of the transport.
///
/// The round trip runs against a loopback pipe with a unique name, created and torn down inside the
/// test: no agent is installed, no service is controlled and nothing outside the process is touched.
/// What it proves is that the two halves of the framing agree, which is exactly the thing that cannot
/// be established by testing either side alone.
/// </summary>
public sealed class NutAgentClientTests
{
    [Fact]
    public async Task TheClientAndTheAgentAgreeOnTheWire()
    {
        if (!OperatingSystem.IsWindows()) return;

        var pipeName = $"NutManagerTests.{Guid.NewGuid():N}";
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var server = RespondOnceAsync(pipeName, new NutAgentResponse(
            NutAgentOptions.ProtocolVersion,
            NutAgentResultCode.Success,
            Status: new NutAgentServiceStatus(
                "GANDALF", "Network UPS Tools", "Network UPS Tools", NutServiceState.Running, 4242, "nut.exe", true,
                DateTimeOffset.UtcNow)),
            stopping.Token);

        var client = new WindowsNamedPipeNutAgentClient(pipeName, TimeSpan.FromSeconds(10));
        var result = await client.GetStatusAsync(".", stopping.Token);
        await server;

        Assert.Equal(NutAgentClientStatus.Success, result.Status);
        Assert.True(result.Succeeded);
        Assert.Equal("Network UPS Tools", result.Value!.ServiceName);
        Assert.Equal(NutServiceState.Running, result.Value.ServiceState);
        Assert.Equal(4242, result.Value.ProcessId);
    }

    [Fact]
    public async Task AnAgentThatIsNotThereIsReportedAsAnAgentThatIsNotThere()
    {
        if (!OperatingSystem.IsWindows()) return;

        // The distinction this whole enum exists for: nothing is listening, and that says nothing
        // whatsoever about whether upsd on that machine is answering.
        var client = new WindowsNamedPipeNutAgentClient($"NutManagerTests.absent.{Guid.NewGuid():N}", TimeSpan.FromSeconds(2));

        var result = await client.GetStatusAsync(".", default);

        Assert.Equal(NutAgentClientStatus.AgentUnavailable, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task AnAgentSpeakingAnotherProtocolIsAProtocolFailureAndNotAnOutage()
    {
        if (!OperatingSystem.IsWindows()) return;

        var pipeName = $"NutManagerTests.{Guid.NewGuid():N}";
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var server = RespondOnceAsync(pipeName, new NutAgentResponse(99, NutAgentResultCode.Success), stopping.Token);

        var client = new WindowsNamedPipeNutAgentClient(pipeName, TimeSpan.FromSeconds(10));
        var result = await client.HandshakeAsync(".", stopping.Token);
        await server;

        Assert.Equal(NutAgentClientStatus.ProtocolFailure, result.Status);
        Assert.Equal(NutAgentResultCode.IncompatibleProtocol, result.Code);
    }

    [Fact]
    public async Task AnAgentThatAcceptsAndSaysNothingIsNotMistakenForAnAnswer()
    {
        if (!OperatingSystem.IsWindows()) return;

        var pipeName = $"NutManagerTests.{Guid.NewGuid():N}";
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var server = Task.Run(async () =>
        {
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(stopping.Token);
            await NutAgentFraming.ReadFrameAsync(pipe, NutAgentFraming.MaxRequestBytes, stopping.Token);
            // Closes without answering.
        }, stopping.Token);

        var client = new WindowsNamedPipeNutAgentClient(pipeName, TimeSpan.FromSeconds(10));
        var result = await client.GetStatusAsync(".", stopping.Token);
        await server;

        Assert.Equal(NutAgentClientStatus.AgentUnavailable, result.Status);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(WindowsNamedPipeNutAgentClient.ErrorFileNotFound, NutAgentClientStatus.AgentUnavailable)]
    [InlineData(WindowsNamedPipeNutAgentClient.ErrorAccessDenied, NutAgentClientStatus.AccessDenied)]
    [InlineData(WindowsNamedPipeNutAgentClient.ErrorLogonFailure, NutAgentClientStatus.AccessDenied)]
    [InlineData(WindowsNamedPipeNutAgentClient.ErrorBadNetworkPath, NutAgentClientStatus.HostUnreachable)]
    public void ConnectionFailuresAreMappedByTheirNumericCode(int win32, NutAgentClientStatus expected)
    {
        // GANDALF refusing a local account is ErrorAccessDenied, and the product must say that
        // rather than implying the server is down. This is the T34 lesson, kept.
        var status = WindowsNamedPipeNutAgentClient.MapFailure(new Win32Exception(win32), out var code);

        Assert.Equal(expected, status);
        Assert.Equal(win32, code);
    }

    [Fact]
    public void NoTransportFailureIsEverReportedAsANutOutage()
    {
        // There is no member of this enum that says anything about the NUT protocol, and there must
        // not be: the agent has no opinion about upsd and neither has its client.
        var names = Enum.GetNames<NutAgentClientStatus>();

        foreach (var forbidden in new[] { "Offline", "Disconnected", "NutUnavailable", "ServerDown" })
        {
            Assert.DoesNotContain(forbidden, names, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheClientCannotNameAServiceOrACommand()
    {
        var parameters = typeof(INutManagerAgentClient)
            .GetMethods()
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.Name ?? string.Empty)
            .Distinct()
            .ToArray();

        // Host, operation id and cancellation. Nothing through which a caller could redirect the
        // agent at a service it did not validate for itself.
        Assert.Equal(["host", "cancellationToken", "operationId"], parameters);
    }

    /// <summary>Accepts one connection, reads the request and answers with the given response.</summary>
    private static Task RespondOnceAsync(string pipeName, NutAgentResponse response, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(cancellationToken);

            var frame = await NutAgentFraming.ReadFrameAsync(pipe, NutAgentFraming.MaxRequestBytes, cancellationToken);
            Assert.Equal(NutAgentFrameStatus.Success, frame.Status);
            Assert.True(NutAgentWireCodec.TryReadRequest(frame.Payload, out _, out _));

            await NutAgentFraming.WriteFrameAsync(pipe, NutAgentWireCodec.Serialize(response), NutAgentFraming.MaxResponseBytes, cancellationToken);
            await pipe.FlushAsync(cancellationToken);
        }, cancellationToken);
}
