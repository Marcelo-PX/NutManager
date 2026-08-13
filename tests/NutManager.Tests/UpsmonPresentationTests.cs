using NutManager.App.Localization;
using NutManager.App.ViewModels;
using NutManager.Core.Configuration;
using NutManager.Core.Configuration.Semantic;
using NutManager.Core.Models;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// The upsmon.conf editor as the user drives it. MONITOR carries its credential inline, so every
/// test that touches a monitor also asserts the sentinel stays out of the view model.
/// </summary>
public sealed class UpsmonPresentationTests
{
    private const string Secret = "UPSMON_SECRET_SENTINEL_A291F1";
    private const string Replacement = "UPSMON_SECRET_SENTINEL_NEW_5C7B";

    private const string Sample =
        "MONITOR ups@localhost 1 monuser " + Secret + " secondary\r\n" +
        "MINSUPPLIES 1\r\n" +
        "POLLFREQ 5\r\n" +
        "NOTIFYFLAG ONBATT SYSLOG+WALL\r\n" +
        "NOTIFYMSG ONLINE \"Power restored\"\r\n";

    private static UpsmonConfigurationEditorViewModel CreateEditor(string text = Sample, bool canEdit = true) => new(
        new NutConfigurationFileSnapshot("/etc/upsmon.conf", NutConfigurationFileKind.UpsmonConf,
            new NutConfigurationParser().Parse(NutConfigurationFileKind.UpsmonConf, text),
            NutConfigurationTextEncoding.Utf8, "fingerprint", text.Length),
        new NutManagerLocalizer(UiLanguagePreference.PtBr),
        canEdit);

    private static UpsmonNotificationEventViewModel Event(UpsmonConfigurationEditorViewModel editor, string name) =>
        editor.Events.Single(item => item.Event == name);

    // ==================== Monitors ====================

    [Fact]
    public void AMonitorIsShownWithoutItsCredential()
    {
        var editor = CreateEditor();

        var monitor = Assert.Single(editor.Monitors);
        Assert.Equal("ups@localhost", monitor.System);
        Assert.Equal("1", monitor.PowerValueText);
        Assert.Equal("monuser", monitor.Username);
        Assert.Equal(NutUpsmonConfigurationCatalog.RoleSecondary, monitor.Role);
        Assert.True(monitor.HasPassword);
        UpsdUsersPresentationTests.AssertNoSecret(editor, Secret);
    }

    [Fact]
    public void AFileWithoutMonitorsOffersTheEmptyState()
    {
        var editor = CreateEditor("MINSUPPLIES 1\r\n");

        Assert.True(editor.HasNoMonitors);
        Assert.Empty(editor.Monitors);
    }

    [Fact]
    public void EditingAMonitorKeepsTheStoredCredentialOutOfSight()
    {
        var editor = CreateEditor();
        var monitor = editor.Monitors.Single();

        monitor.PowerValueText = "2";
        monitor.Username = "operator";
        editor.SaveMonitor(monitor);

        var text = editor.Draft.Materialize().Serialize();
        Assert.Contains("MONITOR ups@localhost 2 operator " + Secret + " secondary", text, StringComparison.Ordinal);
        UpsdUsersPresentationTests.AssertNoSecret(editor, Secret);
    }

    [Theory]
    [InlineData("", "1", "monuser")]
    [InlineData("two hosts", "1", "monuser")]
    [InlineData("ups@localhost", "not-a-number", "monuser")]
    [InlineData("ups@localhost", "-1", "monuser")]
    [InlineData("ups@localhost", "1", "")]
    public void AnInvalidMonitorEditIsRefusedAndTheFileIsUnchanged(string system, string power, string username)
    {
        var editor = CreateEditor();
        var monitor = editor.Monitors.Single();

        monitor.System = system;
        monitor.PowerValueText = power;
        monitor.Username = username;
        editor.SaveMonitor(monitor);

        Assert.False(editor.HasChanges);
        Assert.NotNull(editor.OperationMessage);
    }

    [Fact]
    public void AddingAMonitorRequiresACredentialAndAConfirmationThatMatches()
    {
        var editor = CreateEditor();
        editor.BeginAddMonitorCommand.Execute(null);
        editor.NewMonitorSystem = "backup@10.0.0.9";
        editor.NewMonitorUsername = "monuser";

        Assert.False(editor.ConfirmAddMonitor(string.Empty, string.Empty).Succeeded);
        Assert.False(editor.ConfirmAddMonitor(Replacement, "typo").Succeeded);
        Assert.False(editor.HasChanges);

        Assert.True(editor.ConfirmAddMonitor(Replacement, Replacement).Succeeded);

        Assert.Equal(2, editor.Monitors.Count);
        Assert.False(editor.IsAddingMonitor);
        Assert.Contains("MONITOR backup@10.0.0.9 1 monuser " + Replacement + " primary",
            editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
        UpsdUsersPresentationTests.AssertNoSecret(editor, Replacement);
    }

    [Fact]
    public void ChangingAMonitorCredentialReplacesOnlyThatToken()
    {
        var editor = CreateEditor();
        var monitor = editor.Monitors.Single();

        Assert.True(editor.ChangeMonitorPassword(monitor, Replacement, Replacement).Succeeded);

        var text = editor.Draft.Materialize().Serialize();
        Assert.Contains("MONITOR ups@localhost 1 monuser " + Replacement + " secondary", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
        UpsdUsersPresentationTests.AssertNoSecret(editor, Secret);
        UpsdUsersPresentationTests.AssertNoSecret(editor, Replacement);
    }

    [Fact]
    public void RemovingAMonitorDropsTheWholeLineIncludingItsCredential()
    {
        var editor = CreateEditor();

        editor.RemoveMonitor(editor.Monitors.Single());

        Assert.Empty(editor.Monitors);
        Assert.DoesNotContain(Secret, editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void APrimaryMonitorIsFlaggedSoTheShutdownConsequenceIsVisible()
    {
        var editor = CreateEditor("MONITOR ups@localhost 1 monuser " + Secret + " primary\r\n");

        Assert.True(editor.Monitors.Single().IsPrimary);
    }

    // ==================== Global directives ====================

    [Fact]
    public void GlobalDirectivesAreProjectedIntoTheGenericFieldSurface()
    {
        var editor = CreateEditor();

        var supplies = editor.BasicFields.Single(item => item.Descriptor.SemanticId == "Upsmon.MinSupplies");
        Assert.Equal("1", supplies.DraftValue);
        Assert.Contains(editor.BasicFields.Concat(editor.AdvancedFields),
            item => item.Descriptor.SemanticId == "Upsmon.PollFrequency");
    }

    [Fact]
    public void ATimerIsWrittenAsAPlainNumberWithoutAUnitSuffix()
    {
        var editor = CreateEditor();
        var poll = editor.BasicFields.Concat(editor.AdvancedFields)
            .Single(item => item.Descriptor.SemanticId == "Upsmon.PollFrequency");

        poll.DraftValue = "15";

        Assert.Contains("POLLFREQ 15", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
        Assert.DoesNotContain("POLLFREQ 15s", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void NoShutdownCommandIsExecuted_OnlyRecorded()
    {
        var editor = CreateEditor();
        var shutdown = editor.BasicFields.Concat(editor.AdvancedFields)
            .Single(item => item.Descriptor.SemanticId == "Upsmon.ShutdownCommand");

        shutdown.DraftValue = "shutdown /s /t 0";

        // The value reaches the file and nothing else: there is no execution path in the editor.
        Assert.Contains("SHUTDOWNCMD \"shutdown /s /t 0\"", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    // ==================== Notifications ====================

    [Fact]
    public void EveryKnownEventIsListedAndTheConfiguredOnesCarryTheirState()
    {
        var editor = CreateEditor();

        Assert.Equal(NutUpsmonConfigurationCatalog.NotificationEvents.Count, editor.Events.Count);

        var onBattery = Event(editor, "ONBATT");
        Assert.True(onBattery.Syslog);
        Assert.True(onBattery.Wall);
        Assert.False(onBattery.Exec);
        Assert.True(onBattery.IsConfigured);

        var online = Event(editor, "ONLINE");
        Assert.Equal("Power restored", online.Message);
        Assert.True(online.HasCustomMessage);
    }

    [Fact]
    public void TogglingAFlagRewritesTheDirective()
    {
        var editor = CreateEditor();
        var onBattery = Event(editor, "ONBATT");

        onBattery.Wall = false;

        Assert.Contains("NOTIFYFLAG ONBATT SYSLOG", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
        Assert.DoesNotContain("SYSLOG+WALL", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoreIsExclusiveOfEveryOtherFlagInBothDirections()
    {
        var editor = CreateEditor();
        var onBattery = Event(editor, "ONBATT");

        onBattery.Ignore = true;

        Assert.False(onBattery.Syslog);
        Assert.False(onBattery.Wall);
        Assert.False(onBattery.Exec);
        Assert.Contains("NOTIFYFLAG ONBATT IGNORE", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);

        onBattery.Syslog = true;

        Assert.False(onBattery.Ignore);
        Assert.Contains("NOTIFYFLAG ONBATT SYSLOG", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingEveryFlagRemovesTheDirectiveRatherThanWritingAnEmptyOne()
    {
        var editor = CreateEditor();
        var onBattery = Event(editor, "ONBATT");

        onBattery.Syslog = false;
        onBattery.Wall = false;

        Assert.DoesNotContain("NOTIFYFLAG ONBATT", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExecWithoutACommandIsFlaggedAsAdvisoryAndTheWarningClearsWhenOneIsSet()
    {
        var editor = CreateEditor();
        Assert.False(editor.ShowExecWithoutCommandWarning);

        Event(editor, "ONBATT").Exec = true;
        Assert.True(editor.ShowExecWithoutCommandWarning);

        editor.BasicFields.Concat(editor.AdvancedFields)
            .Single(item => item.Descriptor.SemanticId == "Upsmon.NotifyCommand").DraftValue = "notify.exe";

        Assert.True(editor.HasNotifyCommand);
        Assert.False(editor.ShowExecWithoutCommandWarning);
    }

    [Fact]
    public void EditingAMessageQuotesItAndClearingItRemovesTheDirective()
    {
        var editor = CreateEditor();
        var online = Event(editor, "ONLINE");

        online.Message = "UPS de volta na rede";
        Assert.Contains("NOTIFYMSG ONLINE \"UPS de volta na rede\"", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);

        online.Message = string.Empty;
        Assert.DoesNotContain("NOTIFYMSG ONLINE", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEventThisReleaseDoesNotKnowIsShownSeparatelyAndPreserved()
    {
        var editor = CreateEditor(Sample + "NOTIFYFLAG FUTUREEVENT SYSLOG\r\n");

        var unmanaged = Assert.Single(editor.UnmanagedEvents);
        Assert.Equal("FUTUREEVENT", unmanaged.Event);

        Event(editor, "ONBATT").Wall = false;

        Assert.Contains("NOTIFYFLAG FUTUREEVENT SYSLOG", editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    // ==================== Whole-file guarantees ====================

    [Fact]
    public void CommentsSpacingAndUnknownDirectivesSurviveAnEdit()
    {
        const string text =
            "# managed by hand\r\n" +
            "MONITOR ups@localhost 1 monuser " + Secret + " secondary\r\n" +
            "\r\n" +
            "FUTURE_DIRECTIVE whatever\r\n" +
            "MINSUPPLIES 1\r\n";
        var editor = CreateEditor(text);

        editor.BasicFields.Concat(editor.AdvancedFields)
            .Single(item => item.Descriptor.SemanticId == "Upsmon.MinSupplies").DraftValue = "2";

        var result = editor.Draft.Materialize().Serialize();
        Assert.Equal(text.Replace("MINSUPPLIES 1", "MINSUPPLIES 2", StringComparison.Ordinal), result);
    }

    [Fact]
    public void AReadOnlyProfileRefusesEveryMutation()
    {
        var editor = CreateEditor(canEdit: false);
        var monitor = editor.Monitors.Single();

        monitor.PowerValueText = "9";
        editor.SaveMonitor(monitor);
        editor.RemoveMonitor(monitor);

        Assert.False(editor.HasChanges);
        Assert.False(editor.ChangeMonitorPassword(monitor, Replacement, Replacement).Succeeded);
        Assert.False(editor.ConfirmAddMonitor(Replacement, Replacement).Succeeded);
    }

    [Fact]
    public void TheReviewDescribesTheCredentialChangeWithoutQuotingEitherValue()
    {
        var editor = CreateEditor();

        editor.ChangeMonitorPassword(editor.Monitors.Single(), Replacement, Replacement);
        var review = editor.Draft.Review;

        var change = Assert.Single(review.Changes);
        Assert.True(change.Sensitive);
        Assert.DoesNotContain(Secret, change.OldSafeDisplayValue ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(Replacement, change.NewSafeDisplayValue ?? string.Empty, StringComparison.Ordinal);
    }
}
