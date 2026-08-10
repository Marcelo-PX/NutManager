namespace NutManager.App.Services;

/// <summary>
/// Indicates that a required protected-credential cleanup failed before the
/// managed-profile metadata could be changed or removed.
/// </summary>
public sealed class ManagedProfileCredentialRemovalException : InvalidOperationException
{
    public ManagedProfileCredentialRemovalException()
        : base("The protected credential cleanup did not complete.")
    {
    }
}

/// <summary>
/// Indicates that credential cleanup completed, but the subsequent metadata
/// persistence failed. No secret is retained for rollback.
/// </summary>
public sealed class ManagedProfilePersistenceAfterCredentialRemovalException : InvalidOperationException
{
    public ManagedProfilePersistenceAfterCredentialRemovalException(Exception innerException)
        : base("The managed profile metadata was not persisted after protected credential cleanup.", innerException)
    {
    }
}
