using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NutManager.App.Localization;
using NutManager.Core.Configuration;
using NutManager.Core.Configuration.Semantic;

namespace NutManager.App.ViewModels;

/// <summary>
/// Graphical editor for upsd.users. Each section of the file is one NUT user, so the editor works
/// one user at a time rather than flattening every section into a single form.
///
/// No property here ever holds a stored password. The projector drops the value of a sensitive
/// field, so the only thing this view model can know is whether one is configured; replacing it
/// goes straight from the password box into the semantic mutation and the boxes are cleared.
/// </summary>
public sealed partial class UpsdUsersConfigurationEditorViewModel : ServerGeneralConfigurationEditorViewModel
{
    public UpsdUsersConfigurationEditorViewModel(
        NutConfigurationFileSnapshot snapshot,
        NutManagerLocalizer strings,
        bool canEdit)
        : base(snapshot, NutUpsdUsersConfigurationCatalog.CreateSchema(), strings, canEdit)
    {
        Users = [];
        InstantCommands = [];
        RebuildSpecialFields();
    }

    public ObservableCollection<UpsdUserRowViewModel> Users { get; }
    public ObservableCollection<UpsdInstantCommandViewModel> InstantCommands { get; }

    public bool HasUsers => Users.Count > 0;
    public bool HasNoUsers => !HasUsers;
    public bool HasSelectedUser => SelectedUser is not null;

    [ObservableProperty] private UpsdUserRowViewModel? _selectedUser;
    [ObservableProperty] private string _newUserName = string.Empty;
    [ObservableProperty] private string _renameUserName = string.Empty;
    [ObservableProperty] private string _newInstantCommand = string.Empty;
    [ObservableProperty] private bool _isChangingPassword;

    partial void OnSelectedUserChanged(UpsdUserRowViewModel? value)
    {
        // Leaving a user abandons a half-finished password change rather than carrying it across.
        CancelPasswordChange();
        RenameUserName = value?.Name ?? string.Empty;
        RebuildSelectedUser();
        NotifySelection();
    }

    // ==================== Users ====================

    [RelayCommand]
    private void AddUser()
    {
        if (!CanEdit) return;
        var name = NewUserName.Trim();
        if (!ValidateUserName(name, null, out var message)) { OperationMessage = message; return; }

        var result = Complete(Draft.AddSection(name));
        if (!result.Succeeded) return;
        NewUserName = string.Empty;
        SelectedUser = Users.FirstOrDefault(user => string.Equals(user.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private void RenameUser()
    {
        if (!CanEdit || SelectedUser is not { } user) return;
        var name = RenameUserName.Trim();
        if (string.Equals(name, user.Name, StringComparison.Ordinal)) return;
        if (!ValidateUserName(name, user.Name, out var message)) { OperationMessage = message; return; }

        if (Complete(Draft.RenameSection(user.Name, name)).Succeeded)
            SelectedUser = Users.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private void RemoveUser()
    {
        if (!CanEdit || SelectedUser is not { } user) return;
        if (Complete(Draft.RemoveSection(user.Name)).Succeeded) SelectedUser = Users.FirstOrDefault();
    }

    /// <summary>
    /// Validates a section name. <paramref name="currentName"/> is the name being replaced when
    /// renaming, so a user keeping its own name is not reported as a duplicate; adding passes null,
    /// where every existing name conflicts.
    /// </summary>
    private bool ValidateUserName(string name, string? currentName, out string? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            message = Localize("UpsdUsers.Validation.NameRequired", "UpsdUsers.Validation.NameRequired");
            return false;
        }

        // A section header is one bracketed token, so whitespace and brackets would not survive.
        if (name.Any(character => char.IsWhiteSpace(character) || character is '[' or ']' or '#'))
        {
            message = Localize("UpsdUsers.Validation.NameInvalid", "UpsdUsers.Validation.NameInvalid");
            return false;
        }

        if (Users.Any(user => string.Equals(user.Name, name, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(user.Name, currentName, StringComparison.Ordinal)))
        {
            message = Localize("UpsdUsers.Validation.NameDuplicate", "UpsdUsers.Validation.NameDuplicate");
            return false;
        }

        return true;
    }

    // ==================== Password ====================

    public bool HasPassword => SelectedUser?.HasPassword == true;
    public bool HasPendingPassword => SelectedUser?.SensitiveState == NutSensitiveFieldState.ReplacementPending;

    public string PasswordStateText => Strings.Get(
        HasPendingPassword ? "UpsdUsers.Password.Pending" :
        HasPassword ? "UpsdUsers.Password.Configured" : "UpsdUsers.Password.NotConfigured");

    [RelayCommand]
    private void BeginPasswordChange()
    {
        if (CanEdit && HasSelectedUser) IsChangingPassword = true;
    }

    [RelayCommand]
    private void CancelPasswordChange() => IsChangingPassword = false;

    /// <summary>
    /// Takes the typed password straight to the semantic mutation. The value is never stored on
    /// this view model: the view hands over the two boxes' contents and clears them immediately,
    /// so nothing outlives the call.
    /// </summary>
    public NutConfigurationMutationResult ConfirmPasswordChange(ReadOnlySpan<char> password, ReadOnlySpan<char> confirmation)
    {
        if (!CanEdit || SelectedUser is not { } user)
            return new(NutConfigurationMutationStatus.UnsupportedOperation, "Profile.ReadOnly");
        if (password.IsEmpty)
        {
            OperationMessage = Localize("UpsdUsers.Validation.PasswordRequired", "UpsdUsers.Validation.PasswordRequired");
            return new(NutConfigurationMutationStatus.ValidationFailed, "Sensitive.ValueRequired");
        }

        if (!password.SequenceEqual(confirmation))
        {
            OperationMessage = Localize("UpsdUsers.Validation.PasswordMismatch", "UpsdUsers.Validation.PasswordMismatch");
            return new(NutConfigurationMutationStatus.ValidationFailed, "Sensitive.Mismatch");
        }

        using var value = new NutSensitiveValue(password);
        var result = Complete(Draft.ReplaceSensitive("UpsdUsers.Password", value, user.Name));
        if (result.Succeeded)
        {
            IsChangingPassword = false;
            OperationMessage = null;
        }

        return result;
    }

    // ==================== Permissions ====================

    public bool AllowSet
    {
        get => CurrentActions().AllowSet;
        set => ApplyActions(CurrentActions() with { AllowSet = value });
    }

    public bool AllowForcedShutdown
    {
        get => CurrentActions().AllowForcedShutdown;
        set => ApplyActions(CurrentActions() with { AllowForcedShutdown = value });
    }

    public bool ShowForcedShutdownWarning => AllowForcedShutdown;

    /// <summary>Action tokens this release does not manage, kept visible so they are not a surprise.</summary>
    public string UnmanagedActionsText => string.Join(", ", CurrentActions().Unmanaged);
    public bool HasUnmanagedActions => CurrentActions().Unmanaged.Count > 0;

    private NutUpsdUserActions CurrentActions() =>
        SelectedField("UpsdUsers.Actions")?.Value as NutUpsdUserActions ?? new(false, false, []);

    private void ApplyActions(NutUpsdUserActions actions)
    {
        if (!CanEdit || SelectedUser is not { } user) return;
        var result = actions.IsEmpty
            ? Complete(Draft.Remove("UpsdUsers.Actions", user.Name))
            : Complete(Draft.Set("UpsdUsers.Actions", actions, user.Name));
        if (result.Succeeded) NotifyPermissions();
    }

    // ==================== Instant commands ====================

    public enum InstantCommandMode { None, All, Specific }

    public InstantCommandMode CommandMode
    {
        get
        {
            var commands = CurrentInstantCommands();
            return commands.All ? InstantCommandMode.All
                : commands.Commands.Count > 0 ? InstantCommandMode.Specific
                : InstantCommandMode.None;
        }
    }

    public bool IsCommandModeNone => CommandMode == InstantCommandMode.None;
    public bool IsCommandModeAll => CommandMode == InstantCommandMode.All;
    public bool IsCommandModeSpecific => CommandMode == InstantCommandMode.Specific;
    public bool ShowAllCommandsWarning => IsCommandModeAll;

    [RelayCommand]
    private void SelectNoInstantCommands()
    {
        if (!CanEdit || SelectedUser is not { } user) return;
        if (Complete(Draft.Remove("UpsdUsers.InstantCommands", user.Name)).Succeeded) NotifyPermissions();
    }

    [RelayCommand]
    private void SelectAllInstantCommands() => ApplyInstantCommands(new(true, CurrentInstantCommands().Commands));

    [RelayCommand]
    private void SelectSpecificInstantCommands()
    {
        var current = CurrentInstantCommands();
        // Switching away from ALL with nothing listed yet leaves the directive off until a command
        // is added, rather than writing an empty grant.
        if (current.Commands.Count == 0) SelectNoInstantCommands();
        else ApplyInstantCommands(new(false, current.Commands));
    }

    [RelayCommand]
    private void AddInstantCommand()
    {
        if (!CanEdit) return;
        var command = NewInstantCommand.Trim();
        if (command.Length == 0 || command.Any(char.IsWhiteSpace))
        {
            OperationMessage = Localize("UpsdUsers.Validation.CommandInvalid", "UpsdUsers.Validation.CommandInvalid");
            return;
        }

        var current = CurrentInstantCommands();
        if (current.Commands.Contains(command, StringComparer.OrdinalIgnoreCase)) { NewInstantCommand = string.Empty; return; }
        ApplyInstantCommands(new(false, [.. current.Commands, command]));
        NewInstantCommand = string.Empty;
    }

    public void RemoveInstantCommand(UpsdInstantCommandViewModel row)
    {
        var remaining = CurrentInstantCommands().Commands
            .Where(command => !string.Equals(command, row.Name, StringComparison.Ordinal)).ToArray();
        if (remaining.Length == 0) SelectNoInstantCommands();
        else ApplyInstantCommands(new(false, remaining));
    }

    private NutUpsdUserInstantCommands CurrentInstantCommands() =>
        SelectedField("UpsdUsers.InstantCommands")?.Value as NutUpsdUserInstantCommands ?? new(false, []);

    private void ApplyInstantCommands(NutUpsdUserInstantCommands commands)
    {
        if (!CanEdit || SelectedUser is not { } user) return;
        if (Complete(Draft.Set("UpsdUsers.InstantCommands", commands, user.Name)).Succeeded) NotifyPermissions();
    }

    // ==================== upsmon role ====================

    public string? Role => SelectedField("UpsdUsers.UpsmonRole")?.Value as string;
    public bool IsRoleNone => Role is null;
    public bool IsRolePrimary => string.Equals(Role, NutUpsdUsersConfigurationCatalog.UpsmonPrimary, StringComparison.OrdinalIgnoreCase);
    public bool IsRoleSecondary => string.Equals(Role, NutUpsdUsersConfigurationCatalog.UpsmonSecondary, StringComparison.OrdinalIgnoreCase);
    public bool ShowPrimaryWarning => IsRolePrimary;

    [RelayCommand]
    private void SelectRoleNone()
    {
        if (!CanEdit || SelectedUser is not { } user) return;
        if (Complete(Draft.Remove("UpsdUsers.UpsmonRole", user.Name)).Succeeded) NotifyPermissions();
    }

    [RelayCommand]
    private void SelectRolePrimary() => ApplyRole(NutUpsdUsersConfigurationCatalog.UpsmonPrimary);

    [RelayCommand]
    private void SelectRoleSecondary() => ApplyRole(NutUpsdUsersConfigurationCatalog.UpsmonSecondary);

    private void ApplyRole(string role)
    {
        if (!CanEdit || SelectedUser is not { } user) return;
        if (Complete(Draft.Set("UpsdUsers.UpsmonRole", role, user.Name)).Succeeded) NotifyPermissions();
    }

    // ==================== Projection ====================

    private NutConfigurationSemanticField? SelectedField(string semanticId) => SelectedUser is null
        ? null
        : Draft.Projection.Fields.FirstOrDefault(field =>
            field.Descriptor.SemanticId == semanticId &&
            string.Equals(field.Section, SelectedUser.Name, StringComparison.OrdinalIgnoreCase));

    protected override void RebuildSpecialFields()
    {
        if (Users is null) return;

        // Every managed field in this file is section-scoped, so the inherited flat collections
        // would mix one user's permissions with another's. The editor presents one user at a time
        // instead, and those collections stay empty.
        BasicFields.Clear();
        AdvancedFields.Clear();

        var previous = SelectedUser?.Name;
        Users.Clear();
        foreach (var name in Draft.Projection.Fields
                     .Where(field => field.Section is not null)
                     .Select(field => field.Section!)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            var password = Draft.Projection.Fields.FirstOrDefault(field =>
                field.Descriptor.SemanticId == "UpsdUsers.Password" &&
                string.Equals(field.Section, name, StringComparison.OrdinalIgnoreCase));
            Users.Add(new UpsdUserRowViewModel(name, password?.SensitiveState ?? NutSensitiveFieldState.NotConfigured));
        }

        SelectedUser = Users.FirstOrDefault(user => string.Equals(user.Name, previous, StringComparison.OrdinalIgnoreCase))
            ?? Users.FirstOrDefault();

        RebuildSelectedUser();
        OnPropertyChanged(nameof(HasUsers));
        OnPropertyChanged(nameof(HasNoUsers));
        NotifySelection();
    }

    private void RebuildSelectedUser()
    {
        if (InstantCommands is null) return;
        InstantCommands.Clear();
        foreach (var command in CurrentInstantCommands().Commands)
            InstantCommands.Add(new UpsdInstantCommandViewModel(command, this, CanEdit));
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(HasSelectedUser));
        NotifyPermissions();
    }

    private void NotifyPermissions()
    {
        RebuildSelectedUser();
        foreach (var property in PermissionProperties) OnPropertyChanged(property);
    }

    private static readonly string[] PermissionProperties =
    [
        nameof(HasPassword), nameof(HasPendingPassword), nameof(PasswordStateText),
        nameof(AllowSet), nameof(AllowForcedShutdown), nameof(ShowForcedShutdownWarning),
        nameof(UnmanagedActionsText), nameof(HasUnmanagedActions),
        nameof(CommandMode), nameof(IsCommandModeNone), nameof(IsCommandModeAll), nameof(IsCommandModeSpecific),
        nameof(ShowAllCommandsWarning),
        nameof(Role), nameof(IsRoleNone), nameof(IsRolePrimary), nameof(IsRoleSecondary), nameof(ShowPrimaryWarning)
    ];
}

/// <summary>One NUT user. Carries whether a password exists, never the password itself.</summary>
public sealed record UpsdUserRowViewModel(string Name, NutSensitiveFieldState SensitiveState)
{
    public bool HasPassword => SensitiveState is NutSensitiveFieldState.Configured or NutSensitiveFieldState.ReplacementPending;
}

public sealed partial class UpsdInstantCommandViewModel(
    string name,
    UpsdUsersConfigurationEditorViewModel owner,
    bool canEdit) : ObservableObject
{
    public string Name { get; } = name;
    public bool CanEdit { get; } = canEdit;

    [RelayCommand]
    private void Remove() => owner.RemoveInstantCommand(this);
}
