using System.Collections;
using System.Reflection;
using NutManager.App.Localization;
using NutManager.App.ViewModels;
using NutManager.Core.Configuration;
using NutManager.Core.Configuration.Semantic;
using NutManager.Core.Models;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// The upsd.users editor as the user drives it. The password assertions walk every public property
/// reachable from the editor, because a leak would most likely arrive through a convenience
/// property added later rather than through the one the test happens to name.
/// </summary>
public sealed class UpsdUsersPresentationTests
{
    private const string Secret = "UPS_USERS_SECRET_SENTINEL_837CA9";
    private const string Replacement = "UPS_USERS_SECRET_SENTINEL_NEW_41DD";

    private const string Sample =
        "[admin]\r\n\tpassword = " + Secret + "\r\n\tactions = SET\r\n\tinstcmds = ALL\r\n" +
        "[monuser]\r\n\tpassword = " + Secret + "\r\n\tupsmon secondary\r\n";

    private static UpsdUsersConfigurationEditorViewModel CreateEditor(string text = Sample, bool canEdit = true) => new(
        new NutConfigurationFileSnapshot("/etc/upsd.users", NutConfigurationFileKind.UpsdUsers,
            new NutConfigurationParser().Parse(NutConfigurationFileKind.UpsdUsers, text),
            NutConfigurationTextEncoding.Utf8, "fingerprint", text.Length),
        new NutManagerLocalizer(UiLanguagePreference.PtBr),
        canEdit);

    [Fact]
    public void ExistingUsersAppearAndTheFirstIsSelected()
    {
        var editor = CreateEditor();

        Assert.Equal(["admin", "monuser"], editor.Users.Select(user => user.Name));
        Assert.True(editor.HasUsers);
        Assert.Equal("admin", editor.SelectedUser?.Name);
    }

    [Fact]
    public void AnEmptyFileOffersTheEmptyStateRatherThanInventingAUser()
    {
        var editor = CreateEditor("# no users yet\r\n");

        Assert.True(editor.HasNoUsers);
        Assert.Empty(editor.Users);
        Assert.False(editor.HasSelectedUser);
    }

    [Fact]
    public void AddingRenamingAndRemovingUsersWorksThroughCommands()
    {
        var editor = CreateEditor();

        editor.NewUserName = "backup";
        editor.AddUserCommand.Execute(null);
        Assert.Contains(editor.Users, user => user.Name == "backup");
        Assert.Equal("backup", editor.SelectedUser?.Name);

        editor.RenameUserName = "backup-operator";
        editor.RenameUserCommand.Execute(null);
        Assert.Contains(editor.Users, user => user.Name == "backup-operator");

        editor.RemoveUserCommand.Execute(null);
        Assert.DoesNotContain(editor.Users, user => user.Name == "backup-operator");
    }

    [Theory]
    [InlineData("", "UpsdUsers.Validation.NameRequired")]
    [InlineData("two words", "UpsdUsers.Validation.NameInvalid")]
    [InlineData("admin", "UpsdUsers.Validation.NameDuplicate")]
    public void InvalidUserNamesAreRejectedWithALocalisedReason(string name, string resourceKey)
    {
        var editor = CreateEditor();
        var expected = new NutManagerLocalizer(UiLanguagePreference.PtBr).Get(resourceKey);

        editor.NewUserName = name;
        editor.AddUserCommand.Execute(null);

        Assert.Equal(expected, editor.OperationMessage);
        Assert.DoesNotContain(editor.Users, user => user.Name == "two words");
    }

    [Fact]
    public void TheEditorReportsThatAPasswordExistsAndNothingMore()
    {
        var editor = CreateEditor();

        Assert.True(editor.HasPassword);
        AssertNoSecret(editor, Secret);
        // There is deliberately no property that could carry the value.
        Assert.DoesNotContain(editor.GetType().GetProperties(),
            property => property.Name is "PasswordValue" or "PasswordText" or "RawPassword" or "Password");
    }

    [Fact]
    public void ChangingAPasswordNeverPublishesEitherTheOldOrTheNewValue()
    {
        var editor = CreateEditor();

        Assert.True(editor.ConfirmPasswordChange(Replacement, Replacement).Succeeded);

        Assert.False(editor.IsChangingPassword);
        Assert.True(editor.HasPendingPassword);
        AssertNoSecret(editor, Secret);
        AssertNoSecret(editor, Replacement);
        // The value did reach the document, which is the whole point of the change.
        Assert.Contains(Replacement, editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void AMismatchedConfirmationIsRefusedAndNothingIsWritten()
    {
        var editor = CreateEditor();

        var result = editor.ConfirmPasswordChange(Replacement, "something-else");

        Assert.False(result.Succeeded);
        Assert.False(editor.HasChanges);
        Assert.Equal(new NutManagerLocalizer(UiLanguagePreference.PtBr).Get("UpsdUsers.Validation.PasswordMismatch"), editor.OperationMessage);
    }

    [Fact]
    public void AnEmptyPasswordIsRefused()
    {
        var editor = CreateEditor();

        Assert.False(editor.ConfirmPasswordChange(string.Empty, string.Empty).Succeeded);
        Assert.False(editor.HasChanges);
    }

    [Fact]
    public void PermissionTogglesDriveTheActionsList()
    {
        var editor = CreateEditor();

        Assert.True(editor.AllowSet);
        Assert.False(editor.AllowForcedShutdown);
        Assert.False(editor.ShowForcedShutdownWarning);

        editor.AllowForcedShutdown = true;

        Assert.True(editor.ShowForcedShutdownWarning);
        Assert.Contains("actions = SET FSD", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);

        editor.AllowSet = false;
        Assert.Contains("actions = FSD", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingEveryPermissionRemovesTheDirectiveRatherThanWritingAnEmptyOne()
    {
        var editor = CreateEditor();

        editor.AllowSet = false;

        Assert.DoesNotContain("actions", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnActionThisReleaseDoesNotManageIsShownAndKept()
    {
        var editor = CreateEditor("[admin]\r\n\tpassword = " + Secret + "\r\n\tactions = SET FUTURE_ACTION\r\n");

        Assert.True(editor.HasUnmanagedActions);
        Assert.Equal("FUTURE_ACTION", editor.UnmanagedActionsText);

        editor.AllowForcedShutdown = true;

        Assert.Contains("FUTURE_ACTION", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void InstantCommandModesCoverNoneAllAndSpecific()
    {
        var editor = CreateEditor();

        Assert.True(editor.IsCommandModeAll);
        Assert.True(editor.ShowAllCommandsWarning);

        editor.SelectNoInstantCommandsCommand.Execute(null);
        Assert.True(editor.IsCommandModeNone);
        Assert.False(editor.ShowAllCommandsWarning);

        editor.NewInstantCommand = "test.battery.start";
        editor.AddInstantCommandCommand.Execute(null);

        Assert.True(editor.IsCommandModeSpecific);
        Assert.Equal(["test.battery.start"], editor.InstantCommands.Select(command => command.Name));
        Assert.Contains("instcmds = test.battery.start", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingTheLastSpecificCommandDropsTheDirective()
    {
        var editor = CreateEditor();
        editor.SelectNoInstantCommandsCommand.Execute(null);
        editor.NewInstantCommand = "load.off";
        editor.AddInstantCommandCommand.Execute(null);

        editor.RemoveInstantCommand(editor.InstantCommands.Single());

        Assert.True(editor.IsCommandModeNone);
        Assert.DoesNotContain("instcmds", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void RolesCoverNonePrimaryAndSecondary()
    {
        var editor = CreateEditor();
        editor.SelectedUser = editor.Users.Single(user => user.Name == "monuser");

        Assert.True(editor.IsRoleSecondary);
        Assert.False(editor.ShowPrimaryWarning);

        editor.SelectRolePrimaryCommand.Execute(null);
        Assert.True(editor.IsRolePrimary);
        Assert.True(editor.ShowPrimaryWarning);
        Assert.Contains("upsmon primary", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);

        editor.SelectRoleNoneCommand.Execute(null);
        Assert.True(editor.IsRoleNone);
        Assert.DoesNotContain("upsmon", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void EditingOneUserLeavesTheOtherUntouched()
    {
        var editor = CreateEditor();
        var before = editor.Draft.Materialize().Serialize();

        editor.AllowForcedShutdown = true;
        var after = editor.Draft.Materialize().Serialize();

        var index = before.IndexOf("[monuser]", StringComparison.Ordinal);
        Assert.Equal(before[index..], after[after.IndexOf("[monuser]", StringComparison.Ordinal)..]);
    }

    [Fact]
    public void AReadOnlyProfileRefusesEveryMutation()
    {
        var editor = CreateEditor(canEdit: false);

        editor.AllowForcedShutdown = true;
        editor.NewUserName = "backup";
        editor.AddUserCommand.Execute(null);

        Assert.False(editor.HasChanges);
        Assert.False(editor.ConfirmPasswordChange(Replacement, Replacement).Succeeded);
    }

    /// <summary>
    /// Walks every public property reachable from the editor, including collection items, and fails
    /// if the sentinel appears anywhere a binding could reach.
    /// </summary>
    internal static void AssertNoSecret(object root, string secret)
    {
        foreach (var value in Reachable(root, depth: 0, new HashSet<object>(ReferenceEqualityComparer.Instance)))
            Assert.DoesNotContain(secret, value, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Reachable(object? node, int depth, HashSet<object> seen)
    {
        if (node is null || depth > 4 || !seen.Add(node)) yield break;

        foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0) continue;
            object? value;
            try { value = property.GetValue(node); }
            catch { continue; }

            switch (value)
            {
                case string text:
                    yield return text;
                    break;
                case IEnumerable items and not string:
                    foreach (var item in items)
                        foreach (var nested in Reachable(item, depth + 1, seen))
                            yield return nested;
                    break;
                case { } other when other.GetType().Namespace?.StartsWith("NutManager", StringComparison.Ordinal) == true:
                    foreach (var nested in Reachable(other, depth + 1, seen)) yield return nested;
                    break;
            }
        }
    }
}
