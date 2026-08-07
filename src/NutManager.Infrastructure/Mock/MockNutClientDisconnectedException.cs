namespace NutManager.Infrastructure.Mock;

public sealed class MockNutClientDisconnectedException : InvalidOperationException
{
    public MockNutClientDisconnectedException()
        : base("The deterministic mock NUT client is disconnected.")
    {
    }
}
