using System.Net;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.HttpSys;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NutManager.Core.Agent;

// System.Net carries a type of the same name; the HTTP.sys one is the one that configures this
// server, and the alias makes which is meant unambiguous at the point of use.
using AuthenticationSchemes = Microsoft.AspNetCore.Server.HttpSys.AuthenticationSchemes;

namespace NutManager.Agent;

/// <summary>
/// The optional HTTPS transport, on HTTP.sys through ASP.NET Core.
///
/// It exists for the case the named pipe cannot serve: a client that is not recognised by the
/// server's domain has no Windows session to carry a pipe, but Negotiate over HTTPS can be given an
/// explicit credential. That is the whole reason this transport is here, and it is off unless a
/// deployment turned it on.
///
/// Authentication is settled before this code runs. <see cref="AuthenticationSchemes.Negotiate"/>
/// with <c>AllowAnonymous</c> false makes HTTP.sys reject an unauthenticated caller in kernel mode,
/// so there is no branch here that could be made to skip the check — the check happens before the
/// branch exists. Membership of the operators group is then required before anything is dispatched,
/// using the same authorization the pipe uses, because being an authenticated Windows account is not
/// the same as being allowed to control a UPS service.
///
/// TLS belongs to HTTP.sys and to the certificate an administrator bound to the port. Nothing here
/// loads a certificate, and there is no Kestrel HTTPS configuration to get wrong.
///
/// Nothing about controlling a service lives here either. This class authenticates, authorizes,
/// bounds the request and calls the same dispatcher the named pipe calls.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class NutAgentHttpsServer : IAsyncDisposable
{
    private readonly NutAgentRequestDispatcher _dispatcher;
    private readonly SecurityIdentifier _operatorsGroup;
    private readonly string _prefix;

    private WebApplication? _app;

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
    /// Builds and starts the host, and lets a failure to bind reach the caller.
    ///
    /// That is the point of starting it here rather than on a detached task: an absent SSL binding
    /// or a missing URL reservation fails at <c>StartAsync</c>, and a listener that was asked for and
    /// did not start must be recorded rather than lost in a task nobody observes.
    /// </summary>
    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // The agent's record of privileged control is the Windows Event Log, written by the audit
        // sink. Framework logging would add a second, noisier account of the same events and a
        // status poll every ten seconds would be most of it.
        builder.Logging.ClearProviders();

        builder.WebHost.UseHttpSys(options =>
        {
            options.UrlPrefixes.Add(_prefix);

            // Both explicit. The defaults are None and true, which would be an agent that accepts
            // anonymous requests, and no default is allowed to decide this.
            options.Authentication.Schemes = AuthenticationSchemes.Negotiate;
            options.Authentication.AllowAnonymous = false;

            // A server-side ceiling in addition to the bounded read below, so an oversized body is
            // refused by HTTP.sys before it reaches managed code at all.
            options.MaxRequestBodySize = NutAgentHttpsProtocol.MaxRequestBytes;
        });

        builder.Services.AddAuthentication(HttpSysDefaults.AuthenticationScheme);
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();

        // Terminal middleware rather than routing. The agent answers exactly one method on one path
        // and everything else is a plain 404: an agent that distinguishes "wrong method" from
        // "wrong path" is an agent that helps someone map it.
        app.Run(context => HandleAsync(context));

        _app = app;
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is null) return;

        try
        {
            await _app.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Stopping is not allowed to fail: the process is going down either way.
        }
    }

    private async Task HandleAsync(HttpContext context)
    {
        if (!NutAgentHttpsProtocol.IsAgentRoute(context.Request.Method, context.Request.Path.Value))
        {
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

        var payload = await ReadBoundedBodyAsync(context).ConfigureAwait(false);
        if (payload is null)
        {
            await RespondAsync(context, HttpStatusCode.RequestEntityTooLarge,
                NutAgentResponse.Refused(NutAgentResultCode.MalformedRequest, "The request exceeded the permitted size."))
                .ConfigureAwait(false);
            return;
        }

        if (!NutAgentWireCodec.TryReadRequest(payload, out var request, out var failure))
        {
            await RespondAsync(context, HttpStatusCode.BadRequest, NutAgentResponse.Refused(failure)).ConfigureAwait(false);
            return;
        }

        var caller = new NutAgentCallerContext(identity, true, NutAgentHttpsProtocol.TransportName);

        // Deliberately not the request's own cancellation token: a client that hangs up mid-restart
        // must not abort a privileged mutation half-way. The application service owns that lifetime.
        var response = await _dispatcher.DispatchAsync(request!, caller, CancellationToken.None).ConfigureAwait(false);
        await RespondAsync(context, HttpStatusCode.OK, response).ConfigureAwait(false);
    }

    /// <summary>
    /// Who is on the other end, according to the token HTTP.sys authenticated. Nothing in the
    /// request body or headers contributes, so a client cannot describe itself into the group.
    /// </summary>
    private (string Identity, bool Authorized) Authorize(HttpContext context)
    {
        try
        {
            if (context.User.Identity is not WindowsIdentity windows || !windows.IsAuthenticated)
            {
                // Should be unreachable with AllowAnonymous false, which is exactly why it is
                // checked: the transport's guarantee is verified rather than assumed.
                return ("(unauthenticated)", false);
            }

            // IsInRole with the SID expands nested and domain membership the way Windows resolves
            // it at logon, and the SID keeps a local group distinct from a domain group of the
            // same name.
            return (windows.Name, new WindowsPrincipal(windows).IsInRole(_operatorsGroup));
        }
        catch (Exception)
        {
            return ("(unknown)", false);
        }
    }

    /// <summary>
    /// Reads at most the permitted number of bytes and refuses rather than truncating.
    ///
    /// Kept even though HTTP.sys enforces its own ceiling: a declared length is never used as an
    /// allocation size, and a chunked body that lies about its size still cannot write past the
    /// buffer because the read itself stops there.
    /// </summary>
    private static async Task<byte[]?> ReadBoundedBodyAsync(HttpContext context)
    {
        if (context.Request.ContentLength > NutAgentHttpsProtocol.MaxRequestBytes) return null;

        var buffer = new byte[NutAgentHttpsProtocol.MaxRequestBytes + 1];
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await context.Request.Body
                .ReadAsync(buffer.AsMemory(total), context.RequestAborted)
                .ConfigureAwait(false);

            if (read == 0) break;
            total += read;
        }

        return total > NutAgentHttpsProtocol.MaxRequestBytes ? null : buffer[..total];
    }

    private static async Task RespondAsync(HttpContext context, HttpStatusCode status, NutAgentResponse response)
    {
        // The same codec both transports use. A framework serializer here would give the two
        // transports different wire shapes for the same contract.
        var payload = NutAgentWireCodec.Serialize(response);

        context.Response.StatusCode = (int)status;
        context.Response.ContentType = NutAgentHttpsProtocol.ContentType;
        context.Response.ContentLength = payload.Length;

        await context.Response.Body.WriteAsync(payload).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is null) return;
        await _app.DisposeAsync().ConfigureAwait(false);
        _app = null;
    }
}
