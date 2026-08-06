namespace NutManager.Core.Models;

public sealed record NutEndpoint
{
    public const int DefaultPort = 3493;

    public NutEndpoint(string host, int port = DefaultPort, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "The port must be between 1 and 65535.");
        }

        if (timeout is not null && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The timeout must be greater than zero.");
        }

        Host = host;
        Port = port;
        Timeout = timeout;
    }

    public string Host { get; }

    public int Port { get; }

    public TimeSpan? Timeout { get; }
}
