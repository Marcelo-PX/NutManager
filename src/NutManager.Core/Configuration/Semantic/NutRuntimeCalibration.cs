using System.Globalization;
using NutManager.Core.Validation;

namespace NutManager.Core.Configuration.Semantic;

public sealed record NutRuntimeCalibration(
    TimeSpan HighLoadRuntime,
    decimal HighLoadPercentage,
    TimeSpan LowLoadRuntime,
    decimal LowLoadPercentage)
{
    public static FieldValidationResult<NutRuntimeCalibration> Validate(
        TimeSpan highRuntime,
        decimal highLoad,
        TimeSpan lowRuntime,
        decimal lowLoad,
        string target = "Ups.RuntimeCalibration")
    {
        var issues = new List<FieldValidationIssue>();
        if (highRuntime <= TimeSpan.Zero || lowRuntime <= TimeSpan.Zero)
            issues.Add(new(target, "Runtimecal.Runtime.Positive", ValidationSeverity.Error, "Ups.Validation.Runtimecal.RuntimePositive"));
        if (highLoad <= 0 || highLoad > 100 || lowLoad <= 0 || lowLoad > 100)
            issues.Add(new(target, "Runtimecal.Load.Range", ValidationSeverity.Error, "Ups.Validation.Runtimecal.LoadRange"));
        if (highLoad <= lowLoad)
            issues.Add(new(target, "Runtimecal.Load.Order", ValidationSeverity.Error, "Ups.Validation.Runtimecal.LoadOrder"));
        return new(issues.Count == 0 ? new(highRuntime, highLoad, lowRuntime, lowLoad) : null, issues);
    }

    public string ToNutValue() => string.Join(',',
        ((long)HighLoadRuntime.TotalSeconds).ToString(CultureInfo.InvariantCulture),
        HighLoadPercentage.ToString(CultureInfo.InvariantCulture),
        ((long)LowLoadRuntime.TotalSeconds).ToString(CultureInfo.InvariantCulture),
        LowLoadPercentage.ToString(CultureInfo.InvariantCulture));

    public static FieldValidationResult<NutRuntimeCalibration> Parse(string value, string target = "Ups.RuntimeCalibration")
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4 ||
            !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var highSeconds) ||
            !decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var highLoad) ||
            !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lowSeconds) ||
            !decimal.TryParse(parts[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var lowLoad))
        {
            return new(null, [new(target, "Runtimecal.Format", ValidationSeverity.Error, "Ups.Validation.Runtimecal.Format")]);
        }

        return Validate(TimeSpan.FromSeconds(highSeconds), highLoad, TimeSpan.FromSeconds(lowSeconds), lowLoad, target);
    }
}

public sealed class NutRuntimeCalibrationCodec : INutConfigurationValueCodec
{
    public static NutRuntimeCalibrationCodec Instance { get; } = new();

    public FieldValidationResult<object> Parse(string value, string semanticId)
    {
        var result = NutRuntimeCalibration.Parse(value, semanticId);
        return new(result.Value, result.Issues);
    }

    public FieldValidationResult<string> Serialize(object value, string semanticId) => value is NutRuntimeCalibration calibration
        ? new(calibration.ToNutValue(), [])
        : new(null, [new(semanticId, "Runtimecal.Type", ValidationSeverity.Error, "Ups.Validation.Runtimecal.Format")]);
}

public enum NutRuntimeValueSource { ReportedByUps, EstimatedByDriver, CalculatedByNutManager, Unavailable }
public enum NutRuntimeConfidence { Unknown, Low, Medium, High }
public enum NutRuntimeCalibrationState { NotConfigured, Configured, Invalid, NotApplicable }

public sealed record NutRuntimeEstimate(
    TimeSpan? Runtime,
    NutRuntimeValueSource Source,
    DateTimeOffset? Timestamp,
    decimal? LoadPercentage,
    decimal? BatteryCapacityAmpHours,
    decimal? BatteryVoltage,
    NutRuntimeCalibrationState CalibrationState,
    NutRuntimeConfidence Confidence,
    string? RawValue = null,
    string? ErrorCode = null)
{
    public bool IsAvailable => Runtime is not null;
}
