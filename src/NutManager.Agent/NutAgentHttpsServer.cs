using System.Net;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using NutManager.Core.Agent;

namespace NutManager.Agent;

/// <summary>
/// The optional HTTPS transport, on HTTP.sys.
///
/// It exists for the case the named pipe cannot serve: a client that is not recognised by the
/// server's domain has no Windows session to carry a pipe, but Negotiate over HTTPS can be given an
/// explicit credential. That is the whole reason this transport is here, and it is off unless a
/// deployment turned it on.
///
/// <see cref="AuthenticationSchemes.Negotiate"/> is not decoration. HTTP.sys authenticates before
/// the request is handed over, so an anonymous caller never reaches this code at all — there is no
/// branch here that could be made to skip the check, because the check happens before the branch
/// exists. Membership of the operators group is then required before anything is dispatched, using
/// the same authorization object the pipe uses.
///
/// Nothing about controlling a service lives here. This class authenticates, authorizes, bounds the
/// request and calls the dispatcher — the same dispatcher the named pipe calls, so neither transport
/// can develop its own opinion about what an operation means.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class NutAgentHttpsServer
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(15);

    private readonly NutAgentRequestDispatcher _dispatcher;
    private readonly SecurityIdentifier _operatorsGroup;
    private readonly string _prefix;

    internal NutAgentHttpsServer(NutAgentRequestDispatcher dispatcher, SecurityIdentifier operatorsGroup, string prefix)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(operatorsGroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        _dispatcher = dispatcher;
        _operatorsGroup = operatorsGroup;
        _prefix = prefix;
    }

    /// <summary>
    /// Listens until stopped. A failure to bind is reported to the caller rather than swallowed:
    /// HTTPS that was asked for and did not start must be visible, not silently absent.
    /// </summary>
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(_prefix);

        // Anonymous is the default for HttpListener, and leaving it would be the single worst
        // mistake available in this file.
        listener.AuthenticationSchemes = AuthenticationSchemes.Negotiate;
        listener.IgnoreWriteExceptions = true;

        listener.Start();

        using var registration = cancellationToken.Register(listener.Abort);

        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Aborted on shutdown, or one failed accept. Neither is a reason to stop listening.
                if (cancellationToken.IsCancellationRequested) return;
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleAsync(context, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // One client must never take the listener down with it.
                }
                finally
                {
                    try { context.Response.Close(); } catch (Exception) { }
                }
            }, CancellationToken.None);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        if (!NutAgentHttpsProtocol.IsAgentRoute(context.Request.HttpMethod, context.Request.Url?.AbsolutePath))
        {
            // No route information is offered back. An agent that says "wrong path, try another"
            // is an agent that helps someone map it.
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }

        // Reaching here means HTTP.sys authenticated the caller; the group decides the rest.
        var (identity, authorized) = Authorize(context);
        if (!authorized)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            return;
        }

        var payload = await ReadBoundedBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            await RespondAsync(context, HttpStatusCode.RequestEntityTooLarge,
                NutAgentResponse.Refused(NutAgentResultCode.MalformedRequest, "The request exceeded the permitted size."),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!NutAgentWireCodec.TryReadRequest(payload, out var request, out var failure))
        {
            await RespondAsync(context, HttpStatusCode.BadRequest, NutAgentResponse.Refused(failure), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var caller = new NutAgentCallerContext(identity, true, NutAgentHttpsProtocol.TransportName);
        var response = await _dispatcher.DispatchAsync(request!, caller, cancellationToken).ConfigureAwait(false);
        await RespondAsync(context, HttpStatusCode.OK, response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The caller's name and verdict, both taken from the token HTTP.sys authenticated. Nothing in
    /// the request body contributes to either, so a client cannot describe itself into the group.
    /// </summary>
    private (string Identity, bool Authorized) Authorize(HttpListenerContext context)
    {
        try
        {
            if (context.User?.Identity is not WindowsIdentity windows || !windows.IsAuthenticated)
            {
                return ("(unauthenticated)", false);
            }

            // IsInRole with the SID expands nested and domain group membership the way Windows
            // itself resolves it at logon.
            return (windows.Name, new WindowsPrincipal(windows).IsInRole(_operatorsGroup));
        }
        catch (Exception)
        {
            return ("(unknown)", false);
        }
    }

    /// <summary>
    /// Reads at most the permitted number of bytes, and refuses rather than truncating.
    ///
    /// The declared content length is never trusted as an allocation size: a caller that announces
    /// a gigabyte gets a refusal, and one that lies about a small length still cannot write more
    /// than the ceiling because the read itself stops there.
    /// </summary>
    private static async Task<byte[]?> ReadBoundedBodyAsync(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength64 > NutAgentHttpsProtocol.MaxRequestBytes) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);

        var buffer = new byte[NutAgentHttpsProtocol.MaxRequestBytes + 1];
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await request.InputStream.ReadAsync(buffer.AsMemory(total), timeout.Token).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }

        return total > NutAgentHttpsProtocol.MaxRequestBytes ? null : buffer[..total];
    }

    private static async Task RespondAsync(
        HttpListenerContext context,
        HttpStatusCode status,
        NutAgentResponse response,
        CancellationToken cancellationToken)
    {
        var payload = NutAgentWireCodec.Serialize(response);

        context.Response.StatusCode = (int)status;
        context.Response.ContentType = NutAgentHttpsProtocol.ContentType;
        context.Response.ContentEncoding = Encoding.UTF8;
        context.Response.ContentLength64 = payload.Length;

        await context.Response.OutputStream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    }
}
