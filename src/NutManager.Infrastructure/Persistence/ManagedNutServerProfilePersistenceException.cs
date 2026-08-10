namespace NutManager.Infrastructure.Persistence;

public sealed class ManagedNutServerProfilePersistenceException : Exception
{
    public ManagedNutServerProfilePersistenceException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
