using NutManager.App.ViewModels;
using NutManager.Core.Models;
using NutManager.Core.Services;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// The connection card names the server the readings come from. The polling state carries readings
/// but not their origin, so the endpoint has to reach the view model separately; when it did not,
/// the card reported the address as unavailable while the shell header displayed it.
/// </summary>
public sealed class OverviewEndpointPresentationTests
{
    [Fact]
    public void TheConnectionCardShowsTheEndpointTheApplicationIsPolling()
    {
        var viewModel = new OverviewPageViewModel(
            new StaticPolling(),
            UiLanguagePreference.PtBr,
            new NutEndpoint("127.0.0.1", 3493));

        Assert.Equal("127.0.0.1:3493", viewModel.EndpointText);
    }

    [Fact]
    public void AnEndpointThatWasNeverSuppliedIsStillReportedAsUnavailable()
    {
        // No invented placeholder: without an endpoint the field stays honest.
        var viewModel = new OverviewPageViewModel(new StaticPolling(), UiLanguagePreference.PtBr);

        Assert.Equal("Indisponível", viewModel.EndpointText);
    }

    private sealed class StaticPolling : IUpsPollingCoordinator
    {
        public PollingState State => PollingState.Unavailable;

        public event Action<PollingState>? StateChanged;

        public Task MonitorAsync(string? upsName, CancellationToken cancellationToken = default)
        {
            StateChanged?.Invoke(State);
            return Task.CompletedTask;
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() { }
    }
}
