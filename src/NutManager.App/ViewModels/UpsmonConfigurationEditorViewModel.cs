using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NutManager.App.Localization;
using NutManager.Core.Configuration;
using NutManager.Core.Configuration.Semantic;

namespace NutManager.App.ViewModels;

/// <summary>
/// Graphical editor for upsmon.conf. The global directives - supplies, shutdown, timers and the
/// notification command - come from the shared field machinery; this class adds the two shapes
/// that file has and the others do not: MONITOR rows, which carry a credential inside an ordinary
/// argument list, and the per-event notification matrix built from NOTIFYFLAG and NOTIFYMSG.
///
/// A monitor's password is never projected. Editing a monitor's other values goes through the
/// preserving mutation so the stored credential is carried across without this view model ever
/// holding it.
/// </summary>
public sealed partial class UpsmonConfigurationEditorViewModel : ServerGeneralConfigurationEditorViewModel
{
    public UpsmonConfigurationEditorViewModel(
        NutConfigurationFileSnapshot snapshot,
        NutManagerLocalizer strings,
        bool canEdit)
        : base(snapshot, NutUpsmonConfigurationCatalog.CreateSchema(), strings, canEdit)
    {
        Monitors = [];
        Events = [];
        UnmanagedEvents = [];
        RebuildSpecialFields();
    }

    public ObservableCollection<UpsmonMonitorRowViewModel> Monitors { get; }
    public ObservableCollection<UpsmonNotificationEventViewModel> Events { get; }
    public ObservableCollection<UpsmonUnmanagedEventViewModel> UnmanagedEvents { get; }

    public bool HasMonitors => Monitors.Count > 0;
    public bool HasNoMonitors => !HasMonitors;
    public bool HasUnmanagedEvents => UnmanagedEvents.Count > 0;

    // ==================== New monitor ====================

    [ObservableProperty] private bool _isAddingMonitor;
    [ObservableProperty] private string _newMonitorSystem = string.Empty;
    [ObservableProperty] private string _newMonitorPowerValue = "1";

    /// <summary>
    /// The numeric face of <see cref="NewMonitorPowerValue"/>, which stays the stored form. MONITOR's
    /// power value counts supplies, so it is whole and never negative; the check that already guards
    /// the add still runs, because a spinner narrows what can be typed but does not replace
    /// validation.
    /// </summary>
    public decimal? NewMonitorPowerValueNumber
    {
        get => int.TryParse(NewMonitorPowerValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
        set
        {
            var text = value is null
                ? string.Empty
                : ((int)decimal.Truncate(value.Value)).ToString(CultureInfo.InvariantCulture);
            if (!string.Equals(text, NewMonitorPowerValue, StringComparison.Ordinal)) NewMonitorPowerValue = text;
        }
    }

    partial void OnNewMonitorPowerValueChanged(string value) => OnPropertyChanged(nameof(NewMonitorPowerValueNumber));
    [ObservableProperty] private string _newMonitorUsername = string.Empty;
    [ObservableProperty] private string _newMonitorRole = NutUpsmonConfigurationCatalog.RolePrimary;

    [RelayCommand]
    private void BeginAddMonitor()
    {
        if (CanEdit) IsAddingMonitor = true;
    }

    [RelayCommand]
    private void CancelAddMonitor()
    {
        IsAddingMonitor = false;
        NewMonitorSystem = string.Empty;
        NewMonitorPowerValue = "1";
        NewMonitorUsername = string.Empty;
        NewMonitorRole = NutUpsmonConfigurationCatalog.RolePrimary;
    }

    /// <summary>
    /// Appends a monitor. A new row has no stored credential to carry over, so one has to be
    /// supplied here; the caller passes the password boxes' contents and clears them straight away.
    /// </summary>
    public NutConfigurationMutationResult ConfirmAddMonitor(ReadOnlySpan<char> password, ReadOnlySpan<char> confirmation)
    {
        if (!CanEdit) return new(NutConfigurationMutationStatus.UnsupportedOperation, "Profile.ReadOnly");
        if (!TryBuildNewMonitor(out var entry, out var message))
        {
            OperationMessage = message;
            return new(NutConfigurationMutationStatus.ValidationFailed, "Upsmon.Monitor.Invalid");
        }

        if (password.IsEmpty)
        {
            OperationMessage = Localize("Upsmon.Validation.PasswordRequired", "Upsmon.Validation.PasswordRequired");
            return new(NutConfigurationMutationStatus.ValidationFailed, "Sensitive.ValueRequired");
        }

        if (!password.SequenceEqual(confirmation))
        {
            OperationMessage = Localize("Upsmon.Validation.PasswordMismatch", "Upsmon.Validation.PasswordMismatch");
            return new(NutConfigurationMutationStatus.ValidationFailed, "Sensitive.Mismatch");
        }

        using var secret = new NutSensitiveValue(password);
        var result = Complete(Draft.AddRepeatedWithSecret("Upsmon.Monitor", entry!, secret));
        if (result.Succeeded)
        {
            CancelAddMonitor();
            OperationMessage = null;
        }

        return result;
    }

    private bool TryBuildNewMonitor(out NutMonitorEntry? entry, out string? message)
    {
        entry = null;
        message = null;
        var system = NewMonitorSystem.Trim();
        var username = NewMonitorUsername.Trim();
        if (system.Length == 0 || system.Any(char.IsWhiteSpace))
        {
            message = Localize("Upsmon.Validation.MonitorSystem", "Upsmon.Validation.MonitorSystem");
            return false;
        }

        if (!int.TryParse(NewMonitorPowerValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var power) || power < 0)
        {
            message = Localize("Upsmon.Validation.MonitorPowerValue", "Upsmon.Validation.MonitorPowerValue");
            return false;
        }

        if (username.Length == 0 || username.Any(char.IsWhiteSpace))
        {
            message = Localize("Upsmon.Validation.MonitorUsername", "Upsmon.Validation.MonitorUsername");
            return false;
        }

        entry = new NutMonitorEntry(system, power, username, NewMonitorRole);
        return true;
    }

    // ==================== Existing monitors ====================

    /// <summary>Rewrites a monitor's visible values; the stored credential is carried across in Core.</summary>
    public void SaveMonitor(UpsmonMonitorRowViewModel row)
    {
        if (!CanEdit) return;
        if (row.System.Trim().Length == 0 || row.System.Any(char.IsWhiteSpace))
        {
            OperationMessage = Localize("Upsmon.Validation.MonitorSystem", "Upsmon.Validation.MonitorSystem");
            return;
        }

        if (!int.TryParse(row.PowerValueText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var power) || power < 0)
        {
            OperationMessage = Localize("Upsmon.Validation.MonitorPowerValue", "Upsmon.Validation.MonitorPowerValue");
            return;
        }

        if (row.Username.Trim().Length == 0 || row.Username.Any(char.IsWhiteSpace))
        {
            OperationMessage = Localize("Upsmon.Validation.MonitorUsername", "Upsmon.Validation.MonitorUsername");
            return;
        }

        Complete(Draft.EditRepeatedPreservingSecret("Upsmon.Monitor", row.RowId,
            new NutMonitorEntry(row.System.Trim(), power, row.Username.Trim(), row.Role)));
    }

    public void RemoveMonitor(UpsmonMonitorRowViewModel row)
    {
        if (CanEdit) Complete(Draft.RemoveRepeated("Upsmon.Monitor", row.RowId));
    }

    /// <summary>Replaces only the credential of an existing monitor.</summary>
    public NutConfigurationMutationResult ChangeMonitorPassword(
        UpsmonMonitorRowViewModel row,
        ReadOnlySpan<char> password,
        ReadOnlySpan<char> confirmation)
    {
        if (!CanEdit) return new(NutConfigurationMutationStatus.UnsupportedOperation, "Profile.ReadOnly");
        if (password.IsEmpty)
        {
            OperationMessage = Localize("Upsmon.Validation.PasswordRequired", "Upsmon.Validation.PasswordRequired");
            return new(NutConfigurationMutationStatus.ValidationFailed, "Sensitive.ValueRequired");
        }

        if (!password.SequenceEqual(confirmation))
        {
            OperationMessage = Localize("Upsmon.Validation.PasswordMismatch", "Upsmon.Validation.PasswordMismatch");
            return new(NutConfigurationMutationStatus.ValidationFailed, "Sensitive.Mismatch");
        }

        using var secret = new NutSensitiveValue(password);
        var result = Complete(Draft.ReplaceRepeatedSecret("Upsmon.Monitor", row.RowId, secret));
        if (result.Succeeded) OperationMessage = null;
        return result;
    }

    // ==================== Notifications ====================

    // The lambda parameter is not called "field": inside a property accessor that is a contextual
    // keyword in C# 14 and would bind to the backing field instead.
    public bool HasNotifyCommand => Draft.Projection.Fields
        .Any(entry => entry.Descriptor.SemanticId == "Upsmon.NotifyCommand" && entry.Value is string { Length: > 0 });

    /// <summary>
    /// EXEC only does something when a command is configured. This is advisory: upsmon accepts the
    /// combination, it simply never runs anything.
    /// </summary>
    public bool ShowExecWithoutCommandWarning => !HasNotifyCommand && Events.Any(item => item.Exec);

    internal void ApplyNotificationFlags(UpsmonNotificationEventViewModel item, IReadOnlyList<string> flags)
    {
        if (!CanEdit) return;
        var result = flags.Count == 0
            ? RemoveRepeatedRow("Upsmon.NotifyFlag", item.FlagRowId)
            : item.FlagRowId is null
                ? Complete(Draft.AddRepeated("Upsmon.NotifyFlag", new NutNotificationFlagEntry(item.Event, flags)))
                : Complete(Draft.EditRepeated("Upsmon.NotifyFlag", item.FlagRowId, new NutNotificationFlagEntry(item.Event, flags)));
        if (result.Succeeded) OnPropertyChanged(nameof(ShowExecWithoutCommandWarning));
    }

    internal void ApplyNotificationMessage(UpsmonNotificationEventViewModel item, string? message)
    {
        if (!CanEdit) return;
        // An absent NOTIFYMSG means "use the NUT default", so clearing the box removes the line
        // rather than writing an empty message.
        if (string.IsNullOrWhiteSpace(message))
        {
            RemoveRepeatedRow("Upsmon.NotifyMessage", item.MessageRowId);
            return;
        }

        var entry = new NutNotificationMessageEntry(item.Event, message.Trim());
        Complete(item.MessageRowId is null
            ? Draft.AddRepeated("Upsmon.NotifyMessage", entry)
            : Draft.EditRepeated("Upsmon.NotifyMessage", item.MessageRowId, entry));
    }

    private NutConfigurationMutationResult RemoveRepeatedRow(string semanticId, string? rowId) => rowId is null
        ? NutConfigurationMutationResult.Success()
        : Complete(Draft.RemoveRepeated(semanticId, rowId));

    // ==================== Projection ====================

    protected override void RebuildSpecialFields()
    {
        if (Monitors is null) return;

        Monitors.Clear();
        foreach (var field in Draft.Projection.Fields.Where(field =>
                     field.Descriptor.SemanticId == "Upsmon.Monitor" && field.Value is NutMonitorEntry))
        {
            Monitors.Add(new UpsmonMonitorRowViewModel(
                field.StableRowId!, (NutMonitorEntry)field.Value!, field.SensitiveState, this, CanEdit, Strings));
        }

        var flags = Rows<NutNotificationFlagEntry>("Upsmon.NotifyFlag");
        var messages = Rows<NutNotificationMessageEntry>("Upsmon.NotifyMessage");

        Events.Clear();
        foreach (var name in NutUpsmonConfigurationCatalog.NotificationEvents)
        {
            var flag = flags.FirstOrDefault(item => string.Equals(item.Value.Event, name, StringComparison.OrdinalIgnoreCase));
            var message = messages.FirstOrDefault(item => string.Equals(item.Value.Event, name, StringComparison.OrdinalIgnoreCase));
            Events.Add(new UpsmonNotificationEventViewModel(
                name, flag.RowId, flag.Value, message.RowId, message.Value?.Message, this, CanEdit));
        }

        // Anything the catalog does not know about is listed rather than dropped.
        UnmanagedEvents.Clear();
        foreach (var row in flags.Where(item => !item.Value.IsManagedEvent))
            UnmanagedEvents.Add(new UpsmonUnmanagedEventViewModel(row.Value.Event, string.Join('+', row.Value.Flags)));
        foreach (var row in messages.Where(item => !item.Value.IsManagedEvent))
            UnmanagedEvents.Add(new UpsmonUnmanagedEventViewModel(row.Value.Event, row.Value.Message));

        foreach (var property in new[]
        {
            nameof(HasMonitors), nameof(HasNoMonitors), nameof(HasUnmanagedEvents),
            nameof(HasNotifyCommand), nameof(ShowExecWithoutCommandWarning)
        })
        {
            OnPropertyChanged(property);
        }
    }

    private (string? RowId, T Value)[] Rows<T>(string semanticId) where T : class =>
        Draft.Projection.Fields
            .Where(field => field.Descriptor.SemanticId == semanticId && field.Value is T)
            .Select(field => (field.StableRowId, (T)field.Value!))
            .ToArray();
}

/// <summary>
/// One MONITOR line. Holds the editable values and whether a credential is configured, never the
/// credential.
/// </summary>
public sealed partial class UpsmonMonitorRowViewModel : ObservableObject
{
    private readonly UpsmonConfigurationEditorViewModel _owner;
    private readonly NutManagerLocalizer _strings;

    public UpsmonMonitorRowViewModel(
        string rowId,
        NutMonitorEntry entry,
        NutSensitiveFieldState? sensitiveState,
        UpsmonConfigurationEditorViewModel owner,
        bool canEdit,
        NutManagerLocalizer strings)
    {
        RowId = rowId;
        _owner = owner;
        _strings = strings;
        CanEdit = canEdit;
        SensitiveState = sensitiveState ?? NutSensitiveFieldState.NotConfigured;
        _system = entry.System;
        _powerValueText = entry.PowerValue.ToString(CultureInfo.InvariantCulture);
        _username = entry.Username;
        _role = entry.Role;
        HasManagedRole = entry.HasManagedRole;
    }

    public string RowId { get; }
    public bool CanEdit { get; }
    public NutSensitiveFieldState SensitiveState { get; }
    public bool HasManagedRole { get; }

    [ObservableProperty] private string _system;
    [ObservableProperty] private string _powerValueText;

    /// <summary>
    /// The numeric face of <see cref="PowerValueText"/>, which stays the stored form. Whole and never
    /// negative, like the value MONITOR carries; the save path still validates it.
    /// </summary>
    public decimal? PowerValue
    {
        get => int.TryParse(PowerValueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
        set
        {
            var text = value is null
                ? string.Empty
                : ((int)decimal.Truncate(value.Value)).ToString(CultureInfo.InvariantCulture);
            if (!string.Equals(text, PowerValueText, StringComparison.Ordinal)) PowerValueText = text;
        }
    }

    partial void OnPowerValueTextChanged(string value) => OnPropertyChanged(nameof(PowerValue));
    [ObservableProperty] private string _username;
    [ObservableProperty] private string _role;
    [ObservableProperty] private bool _isChangingPassword;

    public bool HasPassword => SensitiveState is NutSensitiveFieldState.Configured or NutSensitiveFieldState.ReplacementPending;

    public string PasswordStateText => _strings.Get(
        SensitiveState == NutSensitiveFieldState.ReplacementPending ? "Upsmon.Monitor.PasswordPending" :
        HasPassword ? "Upsmon.Monitor.PasswordConfigured" : "Upsmon.Monitor.PasswordMissing");

    public bool IsPrimary => string.Equals(Role, NutUpsmonConfigurationCatalog.RolePrimary, StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void Save() => _owner.SaveMonitor(this);

    [RelayCommand]
    private void Remove() => _owner.RemoveMonitor(this);

    [RelayCommand]
    private void BeginPasswordChange() => IsChangingPassword = CanEdit;

    [RelayCommand]
    private void CancelPasswordChange() => IsChangingPassword = false;

    public NutConfigurationMutationResult ConfirmPasswordChange(ReadOnlySpan<char> password, ReadOnlySpan<char> confirmation)
    {
        var result = _owner.ChangeMonitorPassword(this, password, confirmation);
        if (result.Succeeded) IsChangingPassword = false;
        return result;
    }
}

/// <summary>One notification event: its flag set and its optional custom message.</summary>
public sealed partial class UpsmonNotificationEventViewModel : ObservableObject
{
    private readonly UpsmonConfigurationEditorViewModel _owner;
    private bool _suppress;

    public UpsmonNotificationEventViewModel(
        string name,
        string? flagRowId,
        NutNotificationFlagEntry? flags,
        string? messageRowId,
        string? message,
        UpsmonConfigurationEditorViewModel owner,
        bool canEdit)
    {
        Event = name;
        FlagRowId = flagRowId;
        MessageRowId = messageRowId;
        _owner = owner;
        CanEdit = canEdit;
        _suppress = true;
        _syslog = flags?.Has("SYSLOG") == true;
        _wall = flags?.Has("WALL") == true;
        _exec = flags?.Has("EXEC") == true;
        _ignore = flags?.IsIgnored == true;
        _message = message ?? string.Empty;
        _suppress = false;
    }

    public string Event { get; }
    public string? FlagRowId { get; }
    public string? MessageRowId { get; }
    public bool CanEdit { get; }
    public bool HasCustomMessage => MessageRowId is not null;
    public bool IsConfigured => FlagRowId is not null || MessageRowId is not null;

    [ObservableProperty] private bool _syslog;
    [ObservableProperty] private bool _wall;
    [ObservableProperty] private bool _exec;
    [ObservableProperty] private bool _ignore;
    [ObservableProperty] private string _message;

    partial void OnSyslogChanged(bool value) => OnFlagToggled(value);
    partial void OnWallChanged(bool value) => OnFlagToggled(value);
    partial void OnExecChanged(bool value) => OnFlagToggled(value);

    /// <summary>
    /// IGNORE tells upsmon the event produces nothing, so it cannot sit alongside the others.
    /// Selecting it clears them, and selecting any of them clears it - enforced here rather than in
    /// the view, so the configuration written can never contradict itself.
    /// </summary>
    partial void OnIgnoreChanged(bool value)
    {
        if (_suppress) return;
        if (value)
        {
            _suppress = true;
            Syslog = false;
            Wall = false;
            Exec = false;
            _suppress = false;
        }

        Publish();
    }

    private void OnFlagToggled(bool value)
    {
        if (_suppress) return;
        if (value && Ignore)
        {
            _suppress = true;
            Ignore = false;
            _suppress = false;
        }

        Publish();
    }

    partial void OnMessageChanged(string value)
    {
        if (!_suppress) _owner.ApplyNotificationMessage(this, value);
    }

    private void Publish()
    {
        var flags = new List<string>();
        if (Ignore) flags.Add("IGNORE");
        else
        {
            if (Syslog) flags.Add("SYSLOG");
            if (Wall) flags.Add("WALL");
            if (Exec) flags.Add("EXEC");
        }

        _owner.ApplyNotificationFlags(this, flags);
    }
}

/// <summary>A notification entry for an event this release does not manage. Shown, never edited.</summary>
public sealed record UpsmonUnmanagedEventViewModel(string Event, string Value);
