namespace NutManager.App.ViewModels;

public sealed record OverviewMetricViewModel(string Title, string Value, string? Unit)
{
    public string DisplayValue => Unit is null ? Value : $"{Value} {Unit}";
}
