namespace NutManager.Infrastructure.NutProtocol;

public sealed class NutProtocolException : Exception
{
    public NutProtocolException(string message)
        : base(message)
    {
    }
}
