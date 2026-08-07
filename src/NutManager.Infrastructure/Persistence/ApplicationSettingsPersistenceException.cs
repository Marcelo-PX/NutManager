namespace NutManager.Infrastructure.Persistence;

public sealed class ApplicationSettingsPersistenceException : Exception
{
    public ApplicationSettingsPersistenceException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
