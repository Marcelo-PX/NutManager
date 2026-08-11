using System.Net.Sockets;
using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.Infrastructure.NutProtocol;

public sealed class ManagedNutConnectionTester : IManagedNutConnectionTester
{
    private readonly INutClient _client;

    public ManagedNutConnectionTester(INutClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ManagedNutConnectionTestResult> TestAsync(
        NutEndpoint endpoint,
        string? preferredUpsName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        try
        {
            var devices = await _client.ListUpsAsync(endpoint, cancellationToken);
            var names = devices.Select(device => device.Name).ToArray();
            if (names.Length == 0)
            {
                return new ManagedNutConnectionTestResult(ManagedNutConnectionTestStatus.NoUpsDiscovered, names);
            }

            if (!string.IsNullOrWhiteSpace(preferredUpsName) &&
                !names.Contains(preferredUpsName.Trim(), StringComparer.Ordinal))
            {
                return new ManagedNutConnectionTestResult(ManagedNutConnectionTestStatus.PreferredUpsMissing, names);
            }

            return new ManagedNutConnectionTestResult(ManagedNutConnectionTestStatus.Success, names);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ManagedNutConnectionTestResult(ManagedNutConnectionTestStatus.Cancelled, []);
        }
        catch (TimeoutException)
        {
            return new ManagedNutConnectionTestResult(ManagedNutConnectionTestStatus.Timeout, []);
        }
        catch (NutProtocolException)
        {
            return new ManagedNutConnectionTestResult(ManagedNutConnectionTestStatus.ProtocolError, []);
        }
        catch (NutServerErrorException)
        {
            return new ManagedNutConnectionTestResult(ManagedNutConnectionTestStatus.ProtocolError, []);
        }
        catch (SocketException)
        {
            return new ManagedNutConnectionTestResult(ManagedNutConnectionTestStatus.EndpointUnreachable, []);
        }
        catch (IOException)
        {
            return new ManagedNutConnectionTestResult(ManagedNutConnectionTestStatus.EndpointUnreachable, []);
        }
        catch (Exception)
        {
            return new ManagedNutConnectionTestResult(ManagedNutConnectionTestStatus.Failed, []);
        }
    }
}
