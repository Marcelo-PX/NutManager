namespace NutManager.Core.Models;

public sealed record ApplicationSettings
{
    public const int CurrentSchemaVersion = 5;

    public ApplicationSettings(
        int schemaVersion = CurrentSchemaVersion,
        TimeSpan? pollingInterval = null,
        TimeSpan? connectionTimeout = null,
        ThemePreference theme = ThemePreference.System,
        bool mockMode = false,
        UiLanguagePreference language = UiLanguagePreference.PtBr,
        SidebarPreference sidebarPreference = SidebarPreference.Expanded,
        LegacyMonitoringEndpoint? legacyMonitoringEndpoint = null,
        bool backgroundTransparency = true)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Unsupported settings schema version.");
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
        Theme = theme;
        MockMode = mockMode;
        Language = language;
        SidebarPreference = sidebarPreference;
        BackgroundTransparency = backgroundTransparency;
        LegacyMonitoringEndpoint = legacyMonitoringEndpoint;
    }

    public int SchemaVersion { get; }
    public TimeSpan PollingInterval { get; }
    public TimeSpan ConnectionTimeout { get; }
    public ThemePreference Theme { get; }
    public bool MockMode { get; }
    public UiLanguagePreference Language { get; }
    public SidebarPreference SidebarPreference { get; }

    /// <summary>
    /// Whether the window's backdrop is see-through. It exists because the effect is not free: a
    /// translucent background inherits whatever wallpaper the machine has, and on a busy or light
    /// one the page gets harder to read. Turning it off replaces the acrylic with a solid surface
    /// rather than dimming it, so the answer is unambiguous on any desktop.
    /// </summary>
    public bool BackgroundTransparency { get; }
    public LegacyMonitoringEndpoint? LegacyMonitoringEndpoint { get; }
}

/// <summary>
/// Compatibility payload populated only while reading legacy settings. Managed
/// server profiles are the runtime and persistence source of monitoring endpoints.
/// </summary>
public sealed record LegacyMonitoringEndpoint
{
    public LegacyMonitoringEndpoint(string host, int port = NutEndpoint.DefaultPort, string? preferredUpsName = null)
    {
        Host = NutMonitoringProfile.ValidateRequiredText(host, nameof(host), 255);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "The port must be between 1 and 65535.");
        }

        Port = port;
        PreferredUpsName = NutMonitoringProfile.NormalizeOptionalText(preferredUpsName, nameof(preferredUpsName), 255);
    }

    public string Host { get; }

    public int Port { get; }

    public string? PreferredUpsName { get; }
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
