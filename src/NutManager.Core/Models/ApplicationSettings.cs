namespace NutManager.Core.Models;

public sealed record ApplicationSettings
{
    public const int CurrentSchemaVersion = 2;

    public ApplicationSettings(
        int schemaVersion = CurrentSchemaVersion,
        string host = "localhost",
        int port = NutEndpoint.DefaultPort,
        string? preferredUpsName = null,
        TimeSpan? pollingInterval = null,
        TimeSpan? connectionTimeout = null,
        ThemePreference theme = ThemePreference.System,
        bool mockMode = true,
        UiLanguagePreference language = UiLanguagePreference.PtBr,
        SidebarPreference sidebarPreference = SidebarPreference.Expanded)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Unsupported settings schema version.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "The port must be between 1 and 65535.");
        }

        PollingInterval = pollingInterval ?? TimeSpan.FromSeconds(5);
        ConnectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(5);
        if (PollingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollingInterval), "The polling interval must be greater than zero.");
        }

        if (ConnectionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(connectionTimeout), "The connection timeout must be greater than zero.");
        }

        if (!Enum.IsDefined(theme))
        {
            throw new ArgumentOutOfRangeException(nameof(theme), "The theme preference is invalid.");
        }

        if (!Enum.IsDefined(language))
        {
            throw new ArgumentOutOfRangeException(nameof(language), "The language preference is invalid.");
        }

        if (!Enum.IsDefined(sidebarPreference))
        {
            throw new ArgumentOutOfRangeException(nameof(sidebarPreference), "The sidebar preference is invalid.");
        }

        SchemaVersion = schemaVersion;
        Host = host;
        Port = port;
        PreferredUpsName = string.IsNullOrWhiteSpace(preferredUpsName) ? null : preferredUpsName;
        Theme = theme;
        MockMode = mockMode;
        Language = language;
        SidebarPreference = sidebarPreference;
    }

    public int SchemaVersion { get; }
    public string Host { get; }
    public int Port { get; }
    public string? PreferredUpsName { get; }
    public TimeSpan PollingInterval { get; }
    public TimeSpan ConnectionTimeout { get; }
    public ThemePreference Theme { get; }
    public bool MockMode { get; }
    public UiLanguagePreference Language { get; }
    public SidebarPreference SidebarPreference { get; }
}

public enum UiLanguagePreference
{
    PtBr,
    EnUs
}

public enum SidebarPreference
{
    Expanded,
    Collapsed
}
