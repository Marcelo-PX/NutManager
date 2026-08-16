using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using NutManager.Core.Agent;
using NutManager.Infrastructure.Agent;

namespace NutManager.Agent;

/// <summary>
/// The default transport: a local named pipe, authenticated by Windows.
///
/// Two independent things have to be true before a request is acted on. The pipe's own ACL decides
/// who may connect at all, and it is built from the operators group SID, so a caller outside the
/// group is refused by the operating system before a byte is read. Then the caller's token is asked
/// again — <see cref="WindowsPrincipal.IsInRole(SecurityIdentifier)"/>, which Windows answers with the
/// group nesting already expanded — and the answer travels to the application service, which asks the
/// group a third time by identity. Defence in depth here is cheap and the alternative is a single
/// point of failure in front of a privileged service.
///
/// The transport itself makes no decision about what is allowed. It authenticates, frames, and hands
/// the request to the dispatcher.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class NutAgentNamedPipeServer
{
    /// <summary>
    /// Enough for a client polling status while a restart runs, and bounded so a peer that opens
    /// connections without finishing them cannot grow the process without limit.
    /// </summary>
    private const int MaxConcurrentConnections = 4;

    /// <summary>
    /// A connected client has to say what it wants promptly. The dispatch that follows is not on this
    /// budget: a restart legitimately takes far longer than any client should take to send a request.
    /// </summary>
    private static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(15);

    private readonly NutAgentRequestDispatcher _dispatcher;
    private readonly SecurityIdentifier _operatorsGroup;
    private readonly SemaphoreSlim _connections = new(MaxConcurrentConnections, MaxConcurrentConnections);

    internal NutAgentNamedPipeServer(NutAgentRequestDispatcher dispatcher, SecurityIdentifier operatorsGroup)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(operatorsGroup);

        _dispatcher = dispatcher;
        _operatorsGroup = operatorsGroup;
    }

    /// <summary>
    /// Accepts connections until stopped. Each one is handled on its own task, because a status poll
    /// must keep being answered while a restart holds the mutation gate — serialising connections
    /// here would undo that.
    /// </summary>
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _connections.WaitAsync(cancellationToken).ConfigureAwait(false);

            NamedPipeServerStream? server = null;
            try
            {
                server = CreateServer();
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                _connections.Release();
                return;
            }
            catch (Exception)
            {
                // One failed accept is not a reason to stop listening.
                server?.Dispose();
                _connections.Release();
                continue;
            }

            var accepted = server;
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleConnectionAsync(accepted, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // A single client must never take the listener down with it.
                }
                finally
                {
                    accepted.Dispose();
                    _connections.Release();
                }
            }, CancellationToken.None);
        }
    }

    private NamedPipeServerStream CreateServer() => NamedPipeServerStreamAcl.Create(
        NutAgentNamedPipe.PipeName,
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.WriteThrough,
        inBufferSize: 0,
        outBufferSize: 0,
        WindowsNutAgentPipe.CreateSecurity(_operatorsGroup));

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        using var exchange = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        exchange.CancelAfter(ExchangeTimeout);

        var frame = await NutAgentFraming.ReadFrameAsync(server, NutAgentFraming.MaxRequestBytes, exchange.Token)
            .ConfigureAwait(false);

        if (frame.Status == NutAgentFrameStatus.Closed) return;

        if (frame.Status != NutAgentFrameStatus.Success)
        {
            // A frame that could not be read is answered, not silently dropped: a client that sent
            // something oversized or truncated deserves to be told which.
            await RespondAsync(server, NutAgentResponse.Refused(NutAgentResultCode.MalformedRequest, frame.Status.ToString()), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!NutAgentWireCodec.TryReadRequest(frame.Payload, out var request, out var failure))
        {
            await RespondAsync(server, NutAgentResponse.Refused(failure), cancellationToken).ConfigureAwait(false);
            return;
        }

        var caller = ResolveCaller(server);
        var response = await _dispatcher.DispatchAsync(request!, caller, cancellationToken).ConfigureAwait(false);
        await RespondAsync(server, response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Who is on the other end, according to Windows rather than according to the request.
    ///
    /// The name and the verdict are both taken from the connected token. Nothing in the payload
    /// contributes to either, so a client cannot describe itself into the operators group.
    /// </summary>
    private NutAgentCallerContext ResolveCaller(NamedPipeServerStream server)
    {
        var identity = "(unknown)";
        var authorized = false;

        try
        {
            server.RunAsClient(() =>
            {
                using var client = WindowsIdentity.GetCurrent();
                identity = client.Name;
                authorized = new WindowsPrincipal(client).IsInRole(_operatorsGroup);
            });
        }
        catch (Exception)
        {
            // An identity that could not be established is not authorized.
            return NutAgentCallerContext.Denied(identity, NutAgentNamedPipe.TransportName);
        }

        return new NutAgentCallerContext(identity, authorized, NutAgentNamedPipe.TransportName);
    }

    private static async Task RespondAsync(NamedPipeServerStream server, NutAgentResponse response, CancellationToken cancellationToken)
    {
        using var write = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        write.CancelAfter(ExchangeTimeout);

        var payload = NutAgentWireCodec.Serialize(response);
        await NutAgentFraming.WriteFrameAsync(server, payload, NutAgentFraming.MaxResponseBytes, write.Token).ConfigureAwait(false);
    }
}
