using NutManager.Core.Configuration;
using NutManager.Core.Configuration.Semantic;
using Xunit;

namespace NutManager.Tests.Configuration;

/// <summary>
/// upsmon.conf carries a credential in the middle of each MONITOR line, so its argument list has to
/// stay editable while that one token remains unreadable. These tests pin both halves of that: the
/// other values can be changed freely, and the password never appears anywhere the application can
/// render.
/// </summary>
public sealed class UpsmonConfigurationTests
{
    private const string Secret = "SECRET_UPSMON_SENTINEL_A9F2D4E1";
    private const string OtherSecret = "SECRET_UPSMON_SENTINEL_REPLACEMENT_C7";

    private const string Sample =
        "# upsmon configuration\r\n" +
        "MONITOR nobreak@localhost 1 monuser " + Secret + " primary\r\n" +
        "MINSUPPLIES 1\r\n" +
        "SHUTDOWNCMD \"shutdown -s -t 0\"\r\n" +
        "POLLFREQ 5\r\n" +
        "POLLFREQALERT 5\r\n" +
        "DEADTIME 15\r\n" +
        "NOTIFYCMD C:\\NUT\\bin\\notify.cmd\r\n" +
        "NOTIFYFLAG ONLINE SYSLOG+WALL\r\n" +
        "NOTIFYMSG ONBATT \"UPS %s on battery\"\r\n" +
        "FUTURE_DIRECTIVE keep-me\r\n";

    private static NutConfigurationSemanticDraft CreateDraft(string text = Sample) => new(
        new NutConfigurationParser().Parse(NutConfigurationFileKind.UpsmonConf, text),
        NutUpsmonConfigurationCatalog.CreateSchema());

    [Fact]
    public void AMonitorProjectsEveryValueExceptItsPassword()
    {
        using var draft = CreateDraft();
        var monitor = (NutMonitorEntry)Monitor(draft).Value!;

        Assert.Equal("nobreak@localhost", monitor.System);
        Assert.Equal(1, monitor.PowerValue);
        Assert.Equal("monuser", monitor.Username);
        Assert.Equal("primary", monitor.Role);
        Assert.Equal(NutSensitiveFieldState.Configured, Monitor(draft).SensitiveState);
        AssertNoSecret(draft);
    }

    [Fact]
    public void ChangingThePowerValueKeepsTheStoredPassword()
    {
        using var draft = CreateDraft();
        var row = Monitor(draft);
        var monitor = (NutMonitorEntry)row.Value!;

        var result = draft.EditRepeatedPreservingSecret("Upsmon.Monitor", row.StableRowId!, monitor with { PowerValue = 2 });

        Assert.True(result.Succeeded);
        var text = draft.Materialize().Serialize();
        Assert.Contains("MONITOR nobreak@localhost 2 monuser " + Secret + " primary", text, StringComparison.Ordinal);
        AssertNoSecret(draft);
    }

    [Fact]
    public void ChangingTheRoleAlsoKeepsTheStoredPassword()
    {
        using var draft = CreateDraft();
        var row = Monitor(draft);
        var monitor = (NutMonitorEntry)row.Value!;

        draft.EditRepeatedPreservingSecret("Upsmon.Monitor", row.StableRowId!, monitor with { Role = "secondary" });

        var text = draft.Materialize().Serialize();
        Assert.Contains(Secret, text, StringComparison.Ordinal);
        Assert.Contains("secondary", text, StringComparison.Ordinal);
        AssertNoSecret(draft);
    }

    [Fact]
    public void ReplacingTheMonitorPasswordRemovesTheOldOneAndRedactsTheReview()
    {
        using var draft = CreateDraft();
        using var replacement = new NutSensitiveValue(OtherSecret);

        Assert.True(draft.ReplaceRepeatedSecret("Upsmon.Monitor", Monitor(draft).StableRowId!, replacement).Succeeded);

        var text = draft.Materialize().Serialize();
        Assert.Contains(OtherSecret, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
        AssertNoSecret(draft);
        Assert.DoesNotContain(OtherSecret, RenderedReview(draft), StringComparison.Ordinal);
    }

    [Fact]
    public void ANewMonitorIsWrittenWithItsCredential()
    {
        using var draft = CreateDraft();
        using var secret = new NutSensitiveValue(OtherSecret);

        var result = draft.AddRepeatedWithSecret("Upsmon.Monitor",
            new NutMonitorEntry("second@localhost", 0, "backup", "secondary"), secret);

        Assert.True(result.Succeeded);
        var text = draft.Materialize().Serialize();
        Assert.Contains("MONITOR second@localhost 0 backup " + OtherSecret + " secondary", text, StringComparison.Ordinal);
        // The pre-existing monitor is untouched.
        Assert.Contains("MONITOR nobreak@localhost 1 monuser " + Secret + " primary", text, StringComparison.Ordinal);
        AssertNoSecret(draft);
    }

    [Fact]
    public void AStructurallyIncompleteMonitorIsFlaggedRatherThanSilentlyAccepted()
    {
        // Whitespace-separated arguments have no such thing as an empty position, so a row missing
        // its credential is simply a row with too few tokens. It has to be reported and kept, not
        // read as though some other token were the password.
        using var draft = CreateDraft("MONITOR ups@host 1 user\r\n");

        Assert.Contains(draft.Projection.Issues, issue => issue.SemanticTarget == "Upsmon.Monitor");
        Assert.Contains("MONITOR ups@host 1 user", draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void SystemIdentifiersKeepWhateverFormTheyWereWrittenIn()
    {
        foreach (var system in new[] { "myups", "myups@host", "myups@host:3493" })
        {
            using var draft = CreateDraft($"MONITOR {system} 1 user {Secret} primary\r\n");
            Assert.Equal(system, ((NutMonitorEntry)Monitor(draft).Value!).System);
        }
    }

    [Theory]
    [InlineData("Upsmon.MinSupplies", 2, "MINSUPPLIES 2")]
    [InlineData("Upsmon.PollFrequency", 10, "POLLFREQ 10")]
    [InlineData("Upsmon.DeadTime", 30, "DEADTIME 30")]
    public void TimersAndCountsAreWrittenWithoutTheirUnit(string semanticId, int value, string expected)
    {
        using var draft = CreateDraft();

        Assert.True(draft.Set(semanticId, value).Succeeded);

        Assert.Contains(expected, draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbsentTimerIsNeverMaterialisedJustBecauseItHasADefault()
    {
        using var draft = CreateDraft("MONITOR ups@host 1 user " + Secret + " primary\r\n");

        Assert.DoesNotContain("FINALDELAY", draft.Materialize().Serialize(), StringComparison.Ordinal);
        Assert.DoesNotContain("HOSTSYNC", draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void ACommandContainingSpacesIsQuotedByTheCodecRatherThanByHand()
    {
        using var draft = CreateDraft();

        draft.Set("Upsmon.ShutdownCommand", "shutdown -s -t 60");

        Assert.Contains("SHUTDOWNCMD \"shutdown -s -t 60\"", draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void NotificationFlagsAndMessagesProjectPerEvent()
    {
        using var draft = CreateDraft();

        var flag = (NutNotificationFlagEntry)draft.Projection.Fields
            .Single(field => field.Descriptor.SemanticId == "Upsmon.NotifyFlag").Value!;
        Assert.Equal("ONLINE", flag.Event);
        Assert.True(flag.Has("SYSLOG"));
        Assert.True(flag.Has("WALL"));
        Assert.False(flag.IsIgnored);

        var message = (NutNotificationMessageEntry)draft.Projection.Fields
            .Single(field => field.Descriptor.SemanticId == "Upsmon.NotifyMessage").Value!;
        Assert.Equal("ONBATT", message.Event);
        Assert.Equal("UPS %s on battery", message.Message);
    }

    [Fact]
    public void AnEventThisReleaseDoesNotKnowIsStillPreserved()
    {
        using var draft = CreateDraft(
            "MONITOR ups@host 1 user " + Secret + " primary\r\nNOTIFYFLAG FUTUREEVENT SYSLOG\r\n");

        var flag = (NutNotificationFlagEntry)draft.Projection.Fields
            .Single(field => field.Descriptor.SemanticId == "Upsmon.NotifyFlag").Value!;

        Assert.False(flag.IsManagedEvent);
        Assert.Contains("NOTIFYFLAG FUTUREEVENT SYSLOG", draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void EditingOneDirectiveRewritesNothingElse()
    {
        using var draft = CreateDraft();
        var before = draft.Materialize().Serialize();

        draft.Set("Upsmon.PollFrequency", 10);
        var after = draft.Materialize().Serialize();

        foreach (var line in new[]
        {
            "# upsmon configuration",
            "SHUTDOWNCMD \"shutdown -s -t 0\"",
            "NOTIFYFLAG ONLINE SYSLOG+WALL",
            "NOTIFYMSG ONBATT \"UPS %s on battery\"",
            "FUTURE_DIRECTIVE keep-me"
        })
        {
            Assert.Contains(line, after, StringComparison.Ordinal);
        }

        Assert.Equal(before.Replace("POLLFREQ 5", "POLLFREQ 10"), after);
    }

    [Fact]
    public void WindowsLineEndingsAndUnknownDirectivesSurviveAnEdit()
    {
        using var draft = CreateDraft();

        draft.Set("Upsmon.MinSupplies", 2);
        var text = draft.Materialize().Serialize();

        Assert.Contains("FUTURE_DIRECTIVE keep-me\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty), StringComparison.Ordinal);
    }

    private static NutConfigurationSemanticField Monitor(NutConfigurationSemanticDraft draft) =>
        draft.Projection.Fields.First(field => field.Descriptor.SemanticId == "Upsmon.Monitor");

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
}
