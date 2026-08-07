using NutManager.App.ViewModels;
using NutManager.Core.Models;
using NutManager.Core.Services;
using Xunit;

namespace NutManager.Tests;

public sealed class SettingsPageViewModelTests
{
    [Fact]
    public async Task ThemePersistenceUsesOnlyTheConfirmedSettings()
    {
        var saved = new List<ApplicationSettings>();
        var confirmed = new ApplicationSettings(host: "saved", port: 3493, mockMode: true);
        var viewModel = new SettingsPageViewModel(confirmed, new RecordingStore(saved));
        viewModel.Host = "pending-host";
        viewModel.Port = "9999";
        viewModel.MockMode = false;

        await viewModel.PersistThemeAsync(ThemePreference.Dark);

        var persisted = Assert.Single(saved);
        Assert.Equal("saved", persisted.Host);
        Assert.Equal(3493, persisted.Port);
        Assert.True(persisted.MockMode);
        Assert.Equal(ThemePreference.Dark, persisted.Theme);
    }

    [Fact]
    public async Task ExplicitSaveBecomesTheBaseForFutureThemePersistence()
    {
        var saved = new List<ApplicationSettings>();
        var viewModel = new SettingsPageViewModel(new ApplicationSettings(), new RecordingStore(saved));
        viewModel.Host = "confirmed-after-save";
        await viewModel.SaveCommand.ExecuteAsync(null);
        viewModel.Host = "pending-after-save";

        await viewModel.PersistThemeAsync(ThemePreference.Light);

        Assert.Equal("confirmed-after-save", saved.Last().Host);
        Assert.Equal(ThemePreference.Light, saved.Last().Theme);
    }

    [Fact]
    public async Task LoadErrorPreventsAutomaticThemePersistenceButExplicitSaveRemainsAvailable()
    {
        var saved = new List<ApplicationSettings>();
        var viewModel = new SettingsPageViewModel(new ApplicationSettings(), new RecordingStore(saved));
        viewModel.SetLoadError("settings inválido");

        await viewModel.PersistThemeAsync(ThemePreference.Dark);
        Assert.Empty(saved);

        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.Single(saved);
    }

    [Fact]
    public void ThemeSynchronizationDoesNotLoop()
    {
        var page = new SettingsPageViewModel();
        var shell = new MainWindowViewModel();
        page.ThemeChanged += shell.SetTheme;
        shell.ThemeChanged += page.ApplyTheme;

        page.ApplyTheme(ThemePreference.Dark);

        Assert.Equal(ThemePreference.Dark, shell.SelectedTheme);
        Assert.Equal(ThemePreference.Dark, page.SelectedThemeOption?.Preference);
    }

    private sealed class RecordingStore : IApplicationSettingsStore
    {
        private readonly List<ApplicationSettings> _settings;

        public RecordingStore(List<ApplicationSettings> settings) => _settings = settings;

        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new ApplicationSettings());
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken) { _settings.Add(settings); return Task.CompletedTask; }
    }
}
