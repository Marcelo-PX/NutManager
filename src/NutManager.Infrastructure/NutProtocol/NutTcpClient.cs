using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Core.Status;

namespace NutManager.Infrastructure.NutProtocol;

public sealed class NutTcpClient : INutClient
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _timeProvider;

    public NutTcpClient(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<UpsIdentity>> ListUpsAsync(
        NutEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return await ExecuteAsync(endpoint, cancellationToken, async (reader, writer, token) =>
        {
            await WriteCommandAsync(writer, "LIST UPS", token);
            await ExpectBeginAsync(reader, ["BEGIN", "LIST", "UPS"], token);

            var devices = new List<UpsIdentity>();
            while (true)
            {
                var tokens = await ReadTokensAsync(reader, token);
                if (TokensEqual(tokens, ["END", "LIST", "UPS"]))
                {
                    return Array.AsReadOnly(devices.ToArray());
                }

                if (tokens.Count != 3 || !string.Equals(tokens[0], "UPS", StringComparison.Ordinal))
                {
                    throw new NutProtocolException("Invalid LIST UPS response line.");
                }

                devices.Add(new UpsIdentity(tokens[1], description: tokens[2]));
            }
        });
    }

    public async Task<UpsSnapshot> GetSnapshotAsync(
        NutEndpoint endpoint,
        string upsName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(upsName);

        return await ExecuteAsync(endpoint, cancellationToken, async (reader, writer, token) =>
        {
            var quotedUpsName = NutProtocolTokenizer.QuoteArgument(upsName);
            await WriteCommandAsync(writer, $"LIST VAR {quotedUpsName}", token);
            await ExpectBeginAsync(reader, ["BEGIN", "LIST", "VAR", upsName], token);

            var variables = new Dictionary<string, UpsVariable>(StringComparer.Ordinal);
            while (true)
            {
                var tokens = await ReadTokensAsync(reader, token);
                if (TokensEqual(tokens, ["END", "LIST", "VAR", upsName]))
                {
                    return CreateSnapshot(upsName, variables, _timeProvider.GetUtcNow());
                }

                if (tokens.Count != 4 ||
                    !string.Equals(tokens[0], "VAR", StringComparison.Ordinal) ||
                    !string.Equals(tokens[1], upsName, StringComparison.Ordinal))
                {
                    throw new NutProtocolException("Invalid LIST VAR response line.");
                }

                if (!variables.TryAdd(tokens[2], new UpsVariable(tokens[2], tokens[3])))
                {
                    throw new NutProtocolException("Duplicate variable name in LIST VAR response.");
                }
            }
        });
    }

    private static async Task<T> ExecuteAsync<T>(
        NutEndpoint endpoint,
        CancellationToken cancellationToken,
        Func<StreamReader, StreamWriter, CancellationToken, Task<T>> operation)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var timeoutSource = new CancellationTokenSource(endpoint.Timeout ?? DefaultTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        var token = linkedSource.Token;

        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(endpoint.Host, endpoint.Port, token);

            await using var stream = tcpClient.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
            {
                NewLine = "\n"
            };

            return await operation(reader, writer, token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException("The NUT operation timed out.");
        }
    }

    private static async Task WriteCommandAsync(StreamWriter writer, string command, CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync(command.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private static async Task ExpectBeginAsync(
        StreamReader reader,
        IReadOnlyList<string> expected,
        CancellationToken cancellationToken)
    {
        var tokens = await ReadTokensAsync(reader, cancellationToken);
        if (!TokensEqual(tokens, expected))
        {
            throw new NutProtocolException("Unexpected NUT response framing.");
        }
    }

    private static async Task<IReadOnlyList<string>> ReadTokensAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var line = await reader.ReadLineAsync(cancellationToken);
        if (line is null)
        {
            throw new NutProtocolException("Unexpected end of NUT response.");
        }

        if (line.Equals("ERR", StringComparison.Ordinal) ||
            line.StartsWith("ERR ", StringComparison.Ordinal))
        {
            throw new NutServerErrorException(line);
        }

        return NutProtocolTokenizer.Tokenize(line);
    }

    private static bool TokensEqual(IReadOnlyList<string> actual, IReadOnlyList<string> expected) =>
        actual.Count == expected.Count && actual.SequenceEqual(expected, StringComparer.Ordinal);

    private static UpsSnapshot CreateSnapshot(
        string upsName,
        IReadOnlyDictionary<string, UpsVariable> variables,
        DateTimeOffset completedAt)
    {
        var description = GetValue(variables, "ups.description");
        var manufacturer = GetValue(variables, "device.mfr") ?? GetValue(variables, "ups.mfr");
        var model = GetValue(variables, "device.model") ?? GetValue(variables, "ups.model");
        var serialNumber = GetValue(variables, "device.serial") ?? GetValue(variables, "ups.serial");
        var status = GetValue(variables, "ups.status");

        return new UpsSnapshot(
            new UpsIdentity(upsName, description, manufacturer, model, serialNumber),
            UpsStatusParser.Parse(status),
            new ReadOnlyDictionary<string, UpsVariable>(new Dictionary<string, UpsVariable>(variables, StringComparer.Ordinal)),
            completedAt,
            DataSource.Live,
            ParseDecimal(GetValue(variables, "input.voltage")),
            ParseDecimal(GetValue(variables, "output.voltage")),
            ParseDecimal(GetValue(variables, "ups.load")),
            ParseDecimal(GetValue(variables, "input.frequency")) ?? ParseDecimal(GetValue(variables, "output.frequency")),
            ParseDecimal(GetValue(variables, "ups.temperature")),
            ParseDecimal(GetValue(variables, "battery.voltage")),
            ParseDecimal(GetValue(variables, "battery.charge")),
            ParseRuntime(GetValue(variables, "battery.runtime")));
    }

    private static string? GetValue(IReadOnlyDictionary<string, UpsVariable> variables, string name) =>
        variables.TryGetValue(name, out var variable) ? variable.Value : null;

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static TimeSpan? ParseRuntime(string? value)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ||
            seconds < 0 ||
            seconds > TimeSpan.MaxValue.TotalSeconds)
        {
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
