using System.Globalization;
using NutManager.App.ViewModels;
using NutManager.Core.Models;
using NutManager.Infrastructure.Mock;
using Xunit;

namespace NutManager.Tests;

public sealed class OverviewPageViewModelTests
{
    private static readonly DateTimeOffset ReferenceTime = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly NutEndpoint Endpoint = new("mock.nut.local");

    [Fact]
    public async Task LoadsDataThroughTheNutClientAndIdentifiesSimulation()
    {
        var viewModel = await CreateAndInitializeAsync(MockScenario.Online);

        Assert.NotNull(viewModel.Snapshot);
        Assert.Equal("Dados simulados", viewModel.SourceLabel);
        Assert.True(viewModel.IsSimulated);
    }

    [Fact]
    public async Task ExposesTheMockUpsIdentity()
    {
        var viewModel = await CreateAndInitializeAsync(MockScenario.Online);

        Assert.NotNull(viewModel.Identity);
        Assert.Equal("Deterministic simulated UPS", viewModel.Identity.Description);
        Assert.Equal("NutManager", viewModel.Identity.Manufacturer);
        Assert.Equal("Simulated UPS 1500", viewModel.Identity.Model);
        Assert.Equal("mockups", viewModel.Identity.Name);
    }

    [Fact]
    public async Task MapsOnlineStatusToTextualNormalState()
    {
        var viewModel = await CreateAndInitializeAsync(MockScenario.Online);

        var status = Assert.Single(viewModel.StatusItems);
        Assert.Equal("Em rede", status.StateText);
        Assert.Equal("Normal", status.SeverityText);
    }

    [Fact]
    public async Task PreservesEveryStatusTokenInThePresentation()
    {
        var viewModel = await CreateAndInitializeAsync(MockScenario.LowBattery);

        Assert.Equal(
            new[] { "OB", "LB", "DISCHRG" },
            viewModel.StatusItems.Select(item => item.OriginalToken));
    }

    [Fact]
    public async Task PreservesUnknownStatusText()
    {
        var viewModel = await CreateAndInitializeAsync(MockScenario.UnknownStatusToken);

        var unknown = Assert.Single(viewModel.StatusItems, item => item.OriginalToken == "VENDOR_TOKEN");
        Assert.Equal("VENDOR_TOKEN", unknown.StateText);
        Assert.Equal("Desconhecido", unknown.SeverityText);
    }

    [Fact]
    public async Task FormatsOnlineMetricsWithTheCurrentCulture()
    {
        var viewModel = await CreateAndInitializeAsync(MockScenario.Online);

        var inputVoltage = viewModel.MetricCards.Single(card => card.Title == "Tensão de entrada");
        var runtime = viewModel.MetricCards.Single(card => card.Title == "Autonomia");

        Assert.Equal(230.4m.ToString("0.##", CultureInfo.CurrentCulture), inputVoltage.Value);
        Assert.Equal("V", inputVoltage.Unit);
        Assert.Equal("30 min", runtime.DisplayValue);
    }

    [Fact]
    public async Task MissingOptionalValuesUseUnavailableWithoutAUnit()
    {
        var viewModel = await CreateAndInitializeAsync(MockScenario.MissingOptionalValues);

        var frequency = viewModel.MetricCards.Single(card => card.Title == "Frequência");

        Assert.Equal("Indisponível", frequency.Value);
        Assert.Null(frequency.Unit);
        Assert.Equal("Indisponível", frequency.DisplayValue);
    }

    [Fact]
    public async Task StaleDataHasAVisibleTextualIndicationAndPreservesTimestamp()
    {
        var viewModel = await CreateAndInitializeAsync(MockScenario.StaleData);

        Assert.Equal("Dados desatualizados", viewModel.DataFreshnessText);
        Assert.Equal(ReferenceTime.AddMinutes(-15), viewModel.Snapshot?.LastSuccessfulUpdate);
    }

    [Fact]
    public async Task CancellationDoesNotProduceAnError()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var client = new MockNutClient(MockScenario.Online, ReferenceTime);
        var viewModel = CreateViewModel(client);

        await viewModel.InitializeAsync(cancellationTokenSource.Token);

        Assert.Null(viewModel.Snapshot);
        Assert.Null(viewModel.LoadError);
        Assert.False(viewModel.IsLoading);
    }

    private static async Task<OverviewPageViewModel> CreateAndInitializeAsync(MockScenario scenario)
    {
        var client = new MockNutClient(scenario, ReferenceTime);
        var viewModel = CreateViewModel(client);
        await viewModel.InitializeAsync();
        return viewModel;
    }

    private static OverviewPageViewModel CreateViewModel(MockNutClient client) =>
        new(client, Endpoint, "mockups", client.ConnectionState, client.DataFreshness);
}
