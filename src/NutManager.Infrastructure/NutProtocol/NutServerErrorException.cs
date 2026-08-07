namespace NutManager.Infrastructure.NutProtocol;

public sealed class NutServerErrorException : Exception
{
    public NutServerErrorException(string rawResponse)
        : base("The NUT server returned an error.")
    {
        RawResponse = rawResponse;
    }

    public string RawResponse { get; }
}
