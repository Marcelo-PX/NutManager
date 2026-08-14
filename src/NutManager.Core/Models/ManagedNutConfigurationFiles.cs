using NutManager.Core.Configuration;

namespace NutManager.Core.Models;

/// <summary>
/// Which of the supported NUT configuration files a profile is meant to expose and manage.
///
/// This is a statement of intent, not of fact: a file can be enabled here and be absent from the
/// server, and it can exist on the server without being enabled. Keeping the two apart matters —
/// a profile that manages only <c>upsmon.conf</c> should not start listing the rest just because a
/// probe found them, and a file that is temporarily missing should not silently disappear from the
/// profile's configuration.
///
/// The set is closed to the five files the product has editors for. Enabling anything else would
/// promise a graphical experience that does not exist.
/// </summary>
public sealed class ManagedNutConfigurationFiles : IEquatable<ManagedNutConfigurationFiles>
{
    /// <summary>
    /// The supported files in the order they are presented. Order is fixed here rather than left to
    /// whatever a caller happened to pass, so a profile round-trips to the same document and the
    /// interface does not reshuffle between sessions.
    /// </summary>
    public static readonly IReadOnlyList<NutConfigurationFileKind> SupportedKinds =
    [
        NutConfigurationFileKind.NutConf,
        NutConfigurationFileKind.UpsConf,
        NutConfigurationFileKind.UpsdConf,
        NutConfigurationFileKind.UpsdUsers,
        NutConfigurationFileKind.UpsmonConf
    ];

    private readonly NutConfigurationFileKind[] _kinds;

    private ManagedNutConfigurationFiles(NutConfigurationFileKind[] kinds) => _kinds = kinds;

    /// <summary>Every supported file. This is what a new profile gets and what a profile saved before this setting existed is read as.</summary>
    public static ManagedNutConfigurationFiles All { get; } = new([.. SupportedKinds]);

    public IReadOnlyList<NutConfigurationFileKind> Kinds => _kinds;

    public int Count => _kinds.Length;

    public bool IsEmpty => _kinds.Length == 0;

    public bool IsAll => _kinds.Length == SupportedKinds.Count;

    public bool Contains(NutConfigurationFileKind kind) => Array.IndexOf(_kinds, kind) >= 0;

    /// <summary>
    /// Builds a set from an arbitrary sequence. Duplicates collapse and the result is ordered by
    /// <see cref="SupportedKinds"/>, so two callers listing the same files in different orders
    /// produce the same value. An unsupported value is rejected rather than dropped: silently
    /// ignoring it would hide a real mistake in a caller or a hand-edited document.
    /// </summary>
    public static ManagedNutConfigurationFiles Create(IEnumerable<NutConfigurationFileKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        var requested = kinds as IReadOnlyCollection<NutConfigurationFileKind> ?? [.. kinds];
        foreach (var kind in requested)
        {
            if (!SupportedKinds.Contains(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kinds), kind, "The NUT configuration file kind is not supported.");
            }
        }

        return new([.. SupportedKinds.Where(requested.Contains)]);
    }

    /// <summary>Reads a persisted or user-supplied set, falling back to every file when nothing was recorded.</summary>
    public static ManagedNutConfigurationFiles CreateOrAll(IEnumerable<NutConfigurationFileKind>? kinds) =>
        kinds is null ? All : Create(kinds);

    public ManagedNutConfigurationFiles With(NutConfigurationFileKind kind, bool enabled)
    {
        if (Contains(kind) == enabled)
        {
            return this;
        }

        return Create(enabled ? [.. _kinds, kind] : _kinds.Where(existing => existing != kind));
    }

    public bool Equals(ManagedNutConfigurationFiles? other) =>
        other is not null && _kinds.AsSpan().SequenceEqual(other._kinds);

    public override bool Equals(object? obj) => Equals(obj as ManagedNutConfigurationFiles);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var kind in _kinds)
        {
            hash.Add(kind);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => _kinds.Length == 0
        ? "(none)"
        : string.Join(", ", _kinds.Select(NutConfigurationFileNames.For));
}

/// <summary>The literal file names. They are NUT's own and are never localized.</summary>
public static class NutConfigurationFileNames
{
    public static string For(NutConfigurationFileKind kind) => kind switch
    {
        NutConfigurationFileKind.NutConf => "nut.conf",
        NutConfigurationFileKind.UpsConf => "ups.conf",
        NutConfigurationFileKind.UpsdConf => "upsd.conf",
        NutConfigurationFileKind.UpsdUsers => "upsd.users",
        NutConfigurationFileKind.UpsmonConf => "upsmon.conf",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The NUT configuration file kind is not supported.")
    };

    public static bool TryParse(string? fileName, out NutConfigurationFileKind kind)
    {
        foreach (var candidate in ManagedNutConfigurationFiles.SupportedKinds)
        {
            if (string.Equals(For(candidate), fileName, StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }
}
