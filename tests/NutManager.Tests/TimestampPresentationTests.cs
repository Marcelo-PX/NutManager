using NutManager.App.ViewModels;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// Reading timestamps are stored as unambiguous instants and must be presented in the machine's
/// local timezone. These tests pin the conversion semantics without depending on the host's
/// timezone: the representative case supplies its own fixed offset.
/// </summary>
public sealed class TimestampPresentationTests
{
    [Fact]
    public void UtcInstantIsPresentedInTheRequestedTimezone()
    {
        // 18:08 UTC is 15:08 at UTC-03:00 — the offset reported in the field.
        var instant = new DateTimeOffset(2026, 8, 12, 18, 8, 0, TimeSpan.Zero);
        var minusThree = TimeZoneInfo.CreateCustomTimeZone("Test-03", TimeSpan.FromHours(-3), "Test-03", "Test-03");

        Assert.Equal("15:08", NutTimestampPresentation.In(instant, minusThree, "HH:mm"));
    }

    [Fact]
    public void LocalPresentationKeepsTheSameInstant()
    {
        var instant = new DateTimeOffset(2026, 8, 12, 18, 8, 0, TimeSpan.Zero);

        // Formatting must re-express the instant, never shift it.
        Assert.Equal(instant.UtcDateTime, instant.ToLocalTime().UtcDateTime);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(instant), instant.ToLocalTime().Offset);
    }

    [Fact]
    public void ConversionIsIdempotentSoAlreadyLocalValuesAreNotShiftedTwice()
    {
        var local = DateTimeOffset.Now;

        var once = local.ToLocalTime();
        var twice = once.ToLocalTime();

        Assert.Equal(once, twice);
        Assert.Equal(NutTimestampPresentation.Local(once, "g"), NutTimestampPresentation.Local(twice, "g"));
    }

    [Fact]
    public void RawOffsetFormattingWouldHaveShownTheWrongWallClock()
    {
        // Regression guard: formatting the DateTimeOffset directly prints the wall clock of its own
        // offset, which is what displayed a UTC reading as if it were local time.
        var instant = new DateTimeOffset(2026, 8, 12, 18, 8, 0, TimeSpan.Zero);
        var minusThree = TimeZoneInfo.CreateCustomTimeZone("Test-03", TimeSpan.FromHours(-3), "Test-03", "Test-03");

        Assert.Equal("18:08", instant.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        Assert.NotEqual(
            instant.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            NutTimestampPresentation.In(instant, minusThree, "HH:mm"));
    }
}
