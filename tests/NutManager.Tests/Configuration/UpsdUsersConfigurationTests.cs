using NutManager.Core.Configuration;
using NutManager.Core.Configuration.Semantic;
using Xunit;

namespace NutManager.Tests.Configuration;

/// <summary>
/// upsd.users holds NUT credentials, so the load-edit-review round trip is where a leak would show
/// up. Every test that touches a password uses a sentinel string and asserts it is absent from
/// anything the application can render.
/// </summary>
public sealed class UpsdUsersConfigurationTests
{
    private const string Secret = "SECRET_UPSD_USERS_SENTINEL_7E4A91C3";
    private const string OtherSecret = "SECRET_UPSD_USERS_SENTINEL_REPLACEMENT_B2";

    private const string Sample =
        "# NUT users\r\n" +
        "[admin]\r\n" +
        "\tpassword = " + Secret + "\r\n" +
        "\tactions = SET FSD\r\n" +
        "\tinstcmds = ALL\r\n" +
        "\r\n" +
        "[monuser]\r\n" +
        "\tpassword = " + Secret + "\r\n" +
        "\tupsmon secondary\r\n" +
        "\tfuture_directive = keep-me\r\n";

    private static NutConfigurationSemanticDraft CreateDraft(string text = Sample) => new(
        new NutConfigurationParser().Parse(NutConfigurationFileKind.UpsdUsers, text),
        NutUpsdUsersConfigurationCatalog.CreateSchema());

    [Fact]
    public void EachSectionBecomesOneUserAndTheirPermissionsProject()
    {
        using var draft = CreateDraft();

        // Compared field by field: the record holds a list, so its generated equality is by reference.
        var actions = (NutUpsdUserActions)Field(draft, "UpsdUsers.Actions", "admin").Value!;
        Assert.True(actions.AllowSet);
        Assert.True(actions.AllowForcedShutdown);
        Assert.Empty(actions.Unmanaged);
        Assert.True(((NutUpsdUserInstantCommands)Field(draft, "UpsdUsers.InstantCommands", "admin").Value!).All);
        Assert.Equal("secondary", Field(draft, "UpsdUsers.UpsmonRole", "monuser").Value);
    }

    [Fact]
    public void AConfiguredPasswordIsReportedWithoutItsValue()
    {
        using var draft = CreateDraft();
        var password = Field(draft, "UpsdUsers.Password", "admin");

        Assert.Equal(NutSensitiveFieldState.Configured, password.SensitiveState);
        Assert.Null(password.Value);
        AssertNoSecret(draft);
    }

    [Fact]
    public void EditingAPermissionLeavesTheStoredPasswordExactlyAsItWas()
    {
        using var draft = CreateDraft();

        var result = draft.Set("UpsdUsers.Actions", new NutUpsdUserActions(true, false, []), "admin");

        Assert.True(result.Succeeded);
        var text = draft.Materialize().Serialize();
        // The line is untouched, byte for byte, including its tab and spacing.
        Assert.Contains("\tpassword = " + Secret + "\r\n", text, StringComparison.Ordinal);
        Assert.Contains("actions = SET\r\n", text, StringComparison.Ordinal);
        AssertNoSecret(draft);
    }

    [Fact]
    public void ReplacingAPasswordWritesTheNewSecretAndDropsTheOldOne()
    {
        using var draft = CreateDraft();
        using var replacement = new NutSensitiveValue(OtherSecret);

        Assert.True(draft.ReplaceSensitive("UpsdUsers.Password", replacement, "admin").Succeeded);

        var text = draft.Materialize().Serialize();
        Assert.Contains(OtherSecret, text, StringComparison.Ordinal);
        // The other user keeps the original; only the edited section changed.
        Assert.Equal(1, CountOccurrences(text, Secret));
        AssertNoSecret(draft);
        Assert.DoesNotContain(OtherSecret, RenderedReview(draft), StringComparison.Ordinal);
    }

    [Fact]
    public void AReplacedPasswordIsReportedAsPendingRatherThanShown()
    {
        using var draft = CreateDraft();
        using var replacement = new NutSensitiveValue(OtherSecret);
        draft.ReplaceSensitive("UpsdUsers.Password", replacement, "admin");

        Assert.Equal(NutSensitiveFieldState.ReplacementPending, Field(draft, "UpsdUsers.Password", "admin").SensitiveState);
        Assert.All(draft.Review.Changes, item => Assert.True(!item.Sensitive || (item.OldSafeDisplayValue is null && item.NewSafeDisplayValue is null)));
    }

    [Fact]
    public void AddingRenamingAndRemovingUsersGoesThroughSectionOperations()
    {
        using var draft = CreateDraft();

        Assert.True(draft.AddSection("backup").Succeeded);
        Assert.True(draft.RenameSection("backup", "backup-operator").Succeeded);
        var text = draft.Materialize().Serialize();
        Assert.Contains("[backup-operator]", text, StringComparison.Ordinal);

        Assert.True(draft.RemoveSection("backup-operator").Succeeded);
        Assert.DoesNotContain("[backup-operator]", draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, "SET")]
    [InlineData(false, true, "FSD")]
    [InlineData(true, true, "SET FSD")]
    public void ManagedActionsSerializeInAStableOrder(bool set, bool fsd, string expected)
    {
        using var draft = CreateDraft();

        draft.Set("UpsdUsers.Actions", new NutUpsdUserActions(set, fsd, []), "admin");

        Assert.Contains($"actions = {expected}\r\n", draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnActionThisReleaseDoesNotKnowSurvivesBeingEdited()
    {
        // A future NUT action must never be dropped just because this editor cannot render it.
        using var draft = CreateDraft(
            "[admin]\r\n\tpassword = " + Secret + "\r\n\tactions = SET UNKNOWN_FUTURE_ACTION\r\n");

        var actions = (NutUpsdUserActions)Field(draft, "UpsdUsers.Actions", "admin").Value!;
        Assert.Equal(["UNKNOWN_FUTURE_ACTION"], actions.Unmanaged);

        draft.Set("UpsdUsers.Actions", actions with { AllowForcedShutdown = true }, "admin");

        Assert.Contains("actions = SET FSD UNKNOWN_FUTURE_ACTION\r\n", draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void InstantCommandsCoverNoneAllAndASpecificList()
    {
        using var draft = CreateDraft();

        draft.Set("UpsdUsers.InstantCommands", new NutUpsdUserInstantCommands(false, ["test.battery.start", "load.off"]), "admin");
        Assert.Contains("instcmds = test.battery.start load.off\r\n", draft.Materialize().Serialize(), StringComparison.Ordinal);

        draft.Set("UpsdUsers.InstantCommands", new NutUpsdUserInstantCommands(true, []), "admin");
        Assert.Contains("instcmds = ALL\r\n", draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void HistoricRoleSpellingsAreUnderstoodAndNormalisedOnlyWhenChanged()
    {
        using var draft = CreateDraft("[monuser]\r\n\tpassword = " + Secret + "\r\n\tupsmon master\r\n");

        Assert.Equal("primary", Field(draft, "UpsdUsers.UpsmonRole", "monuser").Value);
        // Untouched, the file keeps the spelling the administrator wrote.
        Assert.Contains("upsmon master\r\n", draft.Materialize().Serialize(), StringComparison.Ordinal);

        draft.Set("UpsdUsers.UpsmonRole", "secondary", "monuser");
        Assert.Contains("upsmon secondary\r\n", draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void CommentsSpacingLineEndingsAndUnknownDirectivesAllSurvive()
    {
        using var draft = CreateDraft();

        draft.Set("UpsdUsers.Actions", new NutUpsdUserActions(true, false, []), "admin");
        var text = draft.Materialize().Serialize();

        Assert.StartsWith("# NUT users\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\tfuture_directive = keep-me\r\n", text, StringComparison.Ordinal);
        Assert.Contains("[monuser]\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n", text.Replace("\r\n", "\n").Replace("\n\n", "<blank>"), StringComparison.Ordinal);
        // Windows line endings are not silently rewritten.
        Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void EditingOneUserDoesNotRewriteTheOther()
    {
        using var draft = CreateDraft();
        var before = draft.Materialize().Serialize();

        draft.Set("UpsdUsers.Actions", new NutUpsdUserActions(true, false, []), "admin");
        var after = draft.Materialize().Serialize();

        var monuserBefore = before[before.IndexOf("[monuser]", StringComparison.Ordinal)..];
        var monuserAfter = after[after.IndexOf("[monuser]", StringComparison.Ordinal)..];
        Assert.Equal(monuserBefore, monuserAfter);
    }

    private static NutConfigurationSemanticField Field(NutConfigurationSemanticDraft draft, string semanticId, string section) =>
        draft.Projection.Fields.Single(field =>
            field.Descriptor.SemanticId == semanticId &&
            string.Equals(field.Section, section, StringComparison.OrdinalIgnoreCase));

    /// <summary>Everything the application could put on screen or into a message.</summary>
    private static void AssertNoSecret(NutConfigurationSemanticDraft draft)
    {
        foreach (var field in draft.Projection.Fields)
            Assert.DoesNotContain(Secret, field.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
        foreach (var parameter in draft.Projection.CustomParameters)
            Assert.DoesNotContain(Secret, parameter.SafeValue ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, RenderedReview(draft), StringComparison.Ordinal);
        foreach (var issue in draft.Validation.Issues)
            Assert.DoesNotContain(Secret, $"{issue.Code} {issue.ResourceKey} {issue.SemanticTarget}", StringComparison.Ordinal);
    }

    private static string RenderedReview(NutConfigurationSemanticDraft draft) =>
        string.Join('\n', draft.Review.Changes.Select(item =>
            $"{item.SemanticId} {item.LabelResourceKey} {item.OldSafeDisplayValue} {item.NewSafeDisplayValue} {item.Section}"));

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
