using NutManager.App.ViewModels;
using NutManager.Core.Agent;
using NutManager.Core.Models;
using NutManager.Core.Validation;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// T36: the agent's transport and authentication in the profile editor.
///
/// The model, the persistence and both clients came from T35; what these tests pin down is the
/// editing surface on top of them — that the draft carries the fields, that the derived state hides
/// what a transport cannot honour, that an invalid endpoint cannot be saved, and above all that no
/// password reaches the draft, the profile or the serialized document.
/// </summary>
public sealed class AgentSettingsPresentationTests
{
    // ---------------------------------------------------------------- draft round trip

    [Fact]
    public void TheDraftCarriesTheAgentSettingsBothWays()
    {
        var profile = RemoteProfile(new NutAgentProfileSettings(
            NutAgentTransportKind.Https, "https://gandalf.sbra.local:5199/",
            NutAgentAuthenticationMode.AlternateWindowsAccount, @"SBRA\operator"));

        var draft = new ManagedNutServerProfileDraftViewModel(profile);

        Assert.Equal(NutAgentTransportKind.Https, draft.AgentTransport);
        Assert.Contains("gandalf.sbra.local", draft.AgentHttpsEndpoint!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(NutAgentAuthenticationMode.AlternateWindowsAccount, draft.AgentAuthentication);
        Assert.Equal(@"SBRA\operator", draft.AgentUsername);
        Assert.True(draft.Matches(profile));
    }

    [Fact]
    public void CopyFromCarriesTheAgentSettings()
    {
        var source = new ManagedNutServerProfileDraftViewModel(RemoteProfile(new NutAgentProfileSettings(
            NutAgentTransportKind.Https, "https://gandalf.sbra.local:5199/")));
        var target = new ManagedNutServerProfileDraftViewModel(RemoteProfile(NutAgentProfileSettings.NamedPipeDefault));

        target.CopyFrom(source);

        Assert.Equal(NutAgentTransportKind.Https, target.AgentTransport);
        Assert.Equal(source.AgentHttpsEndpoint, target.AgentHttpsEndpoint);
    }

    [Fact]
    public void ChangingTheAgentTransportMakesTheDraftDifferFromTheSavedProfile()
    {
        // Dirty tracking: without this, switching transport and pressing Cancel would silently keep
        // the change, or Save would stay disabled on a real edit.
        var profile = RemoteProfile(NutAgentProfileSettings.NamedPipeDefault);
        var draft = new ManagedNutServerProfileDraftViewModel(profile);

        Assert.True(draft.Matches(profile));

        draft.AgentTransport = NutAgentTransportKind.Https;
        draft.AgentHttpsEndpoint = "https://gandalf.sbra.local:5199/";

        Assert.False(draft.Matches(profile));
    }

    [Fact]
    public void ApplyRestoresTheConfirmedProfileAfterAnEdit()
    {
        var profile = RemoteProfile(NutAgentProfileSettings.NamedPipeDefault);
        var draft = new ManagedNutServerProfileDraftViewModel(profile);

        draft.AgentTransport = NutAgentTransportKind.Https;
        draft.AgentUsername = @"SBRA\someone";
        draft.Apply(profile);

        Assert.Equal(NutAgentTransportKind.NamedPipe, draft.AgentTransport);
        Assert.Null(draft.AgentUsername);
        Assert.True(draft.Matches(profile));
    }

    // ---------------------------------------------------------------- derived state

    [Fact]
    public void ALocalProfileShowsNoAgentSection()
    {
        var draft = new ManagedNutServerProfileDraftViewModel(LocalProfile());

        Assert.False(draft.IsAgentSectionVisible);
        Assert.False(draft.IsAgentHttps);
        Assert.False(draft.UsesAgentAlternateAccount);
    }

    [Fact]
    public void TheNamedPipeOffersNoEndpointAndNoAlternateAccount()
    {
        // Over the pipe the caller is whoever Windows already authenticated, so offering an account
        // would be offering something the transport cannot honour.
        var draft = new ManagedNutServerProfileDraftViewModel(RemoteProfile(NutAgentProfileSettings.NamedPipeDefault));

        Assert.True(draft.IsAgentSectionVisible);
        Assert.True(draft.IsAgentNamedPipe);
        Assert.False(draft.IsAgentHttps);
        Assert.False(draft.UsesAgentAlternateAccount);
    }

    [Fact]
    public void HttpsOffersTheEndpointAndTheAlternateAccountOnlyWhenSelected()
    {
        var draft = new ManagedNutServerProfileDraftViewModel(RemoteProfile(NutAgentProfileSettings.NamedPipeDefault));

        draft.AgentTransport = NutAgentTransportKind.Https;
        Assert.True(draft.IsAgentHttps);
        Assert.False(draft.UsesAgentAlternateAccount);

        draft.AgentAuthentication = NutAgentAuthenticationMode.AlternateWindowsAccount;
        Assert.True(draft.UsesAgentAlternateAccount);
    }

    [Fact]
    public void SwitchingBackToTheNamedPipeStopsOfferingTheAlternateAccount()
    {
        var draft = new ManagedNutServerProfileDraftViewModel(RemoteProfile(new NutAgentProfileSettings(
            NutAgentTransportKind.Https, "https://gandalf.sbra.local:5199/",
            NutAgentAuthenticationMode.AlternateWindowsAccount, @"SBRA\operator")));

        draft.AgentTransport = NutAgentTransportKind.NamedPipe;

        Assert.False(draft.UsesAgentAlternateAccount);
        Assert.False(draft.IsAgentHttps);
    }

    [Theory]
    [InlineData("https://gandalf.sbra.local:5199/", false)]
    [InlineData("http://gandalf.sbra.local:5199/", true)]
    [InlineData("", true)]
    [InlineData("gandalf.sbra.local", true)]
    [InlineData("https://user:secret@gandalf", true)]
    public void TheEndpointFieldReportsWhetherItCanBeUsed(string endpoint, bool invalid)
    {
        var draft = new ManagedNutServerProfileDraftViewModel(RemoteProfile(NutAgentProfileSettings.NamedPipeDefault));
        draft.AgentTransport = NutAgentTransportKind.Https;
        draft.AgentHttpsEndpoint = endpoint;

        Assert.Equal(invalid, draft.HasInvalidAgentHttpsEndpoint);
    }

    // ---------------------------------------------------------------- validation

    [Fact]
    public void AnInvalidAgentEndpointStopsTheProfileFromBeingSaved()
    {
        // The document must never receive a draft that names a transport it cannot use.
        var result = Validate(NutAgentTransportKind.Https, "http://gandalf.sbra.local:5199/");

        Assert.Null(result.Profile);
        Assert.Contains(result.Issues, issue => issue.Field == ManagedProfileFields.AgentHttpsEndpoint);
    }

    [Fact]
    public void AValidAgentEndpointProducesAProfile()
    {
        var result = Validate(NutAgentTransportKind.Https, "https://gandalf.sbra.local:5199/");

        Assert.NotNull(result.Profile);
        Assert.Equal(NutAgentTransportKind.Https, result.Profile!.Management.Agent.Transport);
    }

    [Fact]
    public void TheNamedPipeNeedsNoEndpointToValidate()
    {
        var result = Validate(NutAgentTransportKind.NamedPipe, null);

        Assert.NotNull(result.Profile);
        Assert.Equal(NutAgentTransportKind.NamedPipe, result.Profile!.Management.Agent.Transport);
        Assert.Null(result.Profile.Management.Agent.HttpsEndpoint);
    }

    [Fact]
    public void TheAgentTransportSurvivesAlongsideAnSmbConfigurationTransport()
    {
        // The two transports are independent, and editing files over SMB while controlling the
        // service over a named pipe is an ordinary combination.
        var input = new ManagedNutServerProfileInput(
            Guid.NewGuid(), "Gandalf", "gandalf.sbra.local", "3493", null,
            NutManagementMode.Remote, ManagedNutServerAccessMode.Manage,
            null, null, "22", null, SshAuthenticationMode.Password, null, null, null,
            RemoteConfigurationTransportKind.Smb, @"\\gandalf\nut", null,
            SmbAuthenticationMode.CurrentWindowsIdentity, null, null,
            NutAgentTransportKind.Https, "https://gandalf.sbra.local:5199/");

        var result = ManagedNutServerProfileValidator.Validate(input, []);

        Assert.NotNull(result.Profile);
        Assert.Equal(RemoteConfigurationTransportKind.Smb, result.Profile!.Management.ConfigurationTransport);
        Assert.Equal(NutAgentTransportKind.Https, result.Profile.Management.Agent.Transport);
    }

    [Fact]
    public void TheSchemaDidNotChangeForThisWork()
    {
        // T36 edits fields T35 already added. A new schema version here would mean the plan slipped.
        Assert.Equal(6, ManagedNutServerProfiles.CurrentSchemaVersion);
    }

    // ---------------------------------------------------------------- secret boundary

    [Fact]
    public void TheDraftHasNowhereToPutAPassword()
    {
        // The Windows credential dialog collects the secret and the Credential Manager keeps it.
        // A property here would be a copy on the heap for as long as the editor is open.
        var properties = typeof(ManagedNutServerProfileDraftViewModel).GetProperties();

        // Names first. "UsesSmbExplicitCredentials" is a boolean about which mode is selected, so
        // the check is for words that would name a secret rather than for the word "credential".
        foreach (var forbidden in new[] { "Password", "Secret", "Passphrase" })
        {
            Assert.DoesNotContain(
                properties.Select(property => property.Name),
                name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }

        // Then types, which is the check that survives a rename.
        foreach (var property in properties)
        {
            Assert.NotEqual(typeof(System.Security.SecureString), property.PropertyType);
            Assert.NotEqual(typeof(RemoteCredentialSecret), property.PropertyType);
        }
    }

    [Fact]
    public void TheAgentAccountStatusNamesTheAccountAndNothingElse()
    {
        var settings = new SettingsPageViewModel(new ApplicationSettings(), null);
        settings.ProfileDraft.ManagementMode = NutManagementMode.Remote;
        settings.ProfileDraft.AgentTransport = NutAgentTransportKind.Https;
        settings.ProfileDraft.AgentAuthentication = NutAgentAuthenticationMode.AlternateWindowsAccount;
        settings.ProfileDraft.AgentUsername = @"SBRA\operator";

        Assert.Equal(@"SBRA\operator", settings.AgentAccountStatusText);

        settings.ProfileDraft.AgentUsername = null;
        Assert.Equal(settings.Localizer.Get("Agent.Account.NotConfigured"), settings.AgentAccountStatusText);
    }

    [Fact]
    public void TheAgentSettingsViewNeverShowsAPasswordBox()
    {
        var view = Repository.Read(Path.Combine("src", "NutManager.App", "Views", "SettingsPageView.axaml"));
        var agentSection = view[view.IndexOf("IsAgentSectionVisible", StringComparison.Ordinal)..];
        var end = agentSection.IndexOf("StoredCredentialText", StringComparison.Ordinal);
        agentSection = end > 0 ? agentSection[..end] : agentSection;

        foreach (var forbidden in new[] { "PasswordBox", "PasswordChar", "RevealPassword" })
        {
            Assert.DoesNotContain(forbidden, agentSection, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------------------------------------------------------------- presentation

    [Fact]
    public void BothTransportsAndBothAuthenticationModesAreOfferedWithLocalizedTitles()
    {
        var settings = new SettingsPageViewModel(new ApplicationSettings(), null);

        Assert.Equal(
            [NutAgentTransportKind.NamedPipe, NutAgentTransportKind.Https],
            settings.AgentTransportOptions.Select(option => option.Value));
        Assert.Equal(
            [NutAgentAuthenticationMode.CurrentWindowsIdentity, NutAgentAuthenticationMode.AlternateWindowsAccount],
            settings.AgentAuthenticationOptions.Select(option => option.Value));

        Assert.All(settings.AgentTransportOptions, option => Assert.False(string.IsNullOrWhiteSpace(option.Title)));
        Assert.All(settings.AgentAuthenticationOptions, option => Assert.False(string.IsNullOrWhiteSpace(option.Title)));
    }

    [Fact]
    public void SelectingATransportOptionMovesTheDraft()
    {
        var settings = new SettingsPageViewModel(new ApplicationSettings(), null);
        settings.ProfileDraft.ManagementMode = NutManagementMode.Remote;

        settings.SelectedAgentTransportOption =
            settings.AgentTransportOptions.Single(option => option.Value == NutAgentTransportKind.Https);

        Assert.Equal(NutAgentTransportKind.Https, settings.ProfileDraft.AgentTransport);
        Assert.True(settings.ProfileDraft.IsAgentHttps);
    }

    [Fact]
    public void DiscardingADraftMovesTheTransportSelectorBackWithIt()
    {
        // Found by running the app: Discard restored the draft, but the combo box still showed
        // HTTPS beside a named-pipe notice, because nothing told the selector the draft had moved.
        // Every other selector was already in that notification list; these two were not.
        var settings = new SettingsPageViewModel(new ApplicationSettings(), null);
        settings.ProfileDraft.ManagementMode = NutManagementMode.Remote;

        settings.ProfileDraft.AgentTransport = NutAgentTransportKind.Https;
        Assert.Equal(NutAgentTransportKind.Https, settings.SelectedAgentTransportOption.Value);

        var changed = new List<string>();
        settings.PropertyChanged += (_, args) => changed.Add(args.PropertyName ?? string.Empty);

        settings.ProfileDraft.AgentTransport = NutAgentTransportKind.NamedPipe;

        Assert.Equal(NutAgentTransportKind.NamedPipe, settings.SelectedAgentTransportOption.Value);
        Assert.Contains(nameof(SettingsPageViewModel.SelectedAgentTransportOption), changed);
    }

    [Fact]
    public void TheAuthenticationSelectorAndAccountStatusFollowTheDraftToo()
    {
        var settings = new SettingsPageViewModel(new ApplicationSettings(), null);
        settings.ProfileDraft.ManagementMode = NutManagementMode.Remote;
        settings.ProfileDraft.AgentTransport = NutAgentTransportKind.Https;

        var changed = new List<string>();
        settings.PropertyChanged += (_, args) => changed.Add(args.PropertyName ?? string.Empty);

        settings.ProfileDraft.AgentAuthentication = NutAgentAuthenticationMode.AlternateWindowsAccount;

        Assert.Equal(NutAgentAuthenticationMode.AlternateWindowsAccount, settings.SelectedAgentAuthenticationOption.Value);
        Assert.Contains(nameof(SettingsPageViewModel.SelectedAgentAuthenticationOption), changed);
        Assert.Contains(nameof(SettingsPageViewModel.AgentAccountStatusText), changed);
    }

    [Fact]
    public void TheCredentialSurfaceFollowsTheDraftItReads()
    {
        // Found by running the app: selecting the alternate account left "Sign in" disabled beside a
        // perfectly valid endpoint, and the status still described the current identity. Both read
        // the draft, and neither was in the list of properties re-raised when the draft changes.
        var settings = new SettingsPageViewModel(new ApplicationSettings(), null);
        settings.ProfileDraft.ManagementMode = NutManagementMode.Remote;
        settings.ProfileDraft.AgentTransport = NutAgentTransportKind.Https;
        settings.ProfileDraft.AgentHttpsEndpoint = "https://gandalf.sbra.local:5199/";

        var changed = new List<string>();
        settings.PropertyChanged += (_, args) => changed.Add(args.PropertyName ?? string.Empty);

        settings.ProfileDraft.AgentAuthentication = NutAgentAuthenticationMode.AlternateWindowsAccount;

        Assert.Contains(nameof(SettingsPageViewModel.CanAuthenticateAgentCredential), changed);
        Assert.Contains(nameof(SettingsPageViewModel.AgentCredentialStatusText), changed);

        // And the status now describes the alternate account rather than the current identity.
        Assert.NotEqual(settings.Localizer.Get("Agent.Auth.CurrentWindowsIdentity"), settings.AgentCredentialStatusText);
    }

    [Fact]
    public void EveryAgentStringExistsInBothLanguages()
    {
        var keys = new[]
        {
            "Agent.Section", "Agent.Transport", "Agent.Transport.NamedPipe", "Agent.Transport.Https",
            "Agent.HttpsEndpoint", "Agent.HttpsEndpoint.Invalid", "Agent.Authentication",
            "Agent.Auth.CurrentWindowsIdentity", "Agent.Auth.AlternateWindowsAccount",
            "Agent.Account", "Agent.Account.NotConfigured", "Agent.NamedPipe.Notice",
            "Agent.AlternateAccount.Notice", "Validation.Agent.EndpointInvalid", "Validation.Agent.TransportInvalid"
        };

        foreach (var language in new[] { UiLanguagePreference.PtBr, UiLanguagePreference.EnUs })
        {
            var localizer = new App.Localization.NutManagerLocalizer(language);
            foreach (var key in keys)
            {
                var value = localizer.Get(key);
                Assert.False(string.IsNullOrWhiteSpace(value), $"{key} missing for {language}");
                Assert.NotEqual(key, value);
            }
        }
    }

    // ---------------------------------------------------------------- helpers

    private static ManagedNutServerProfileValidationResult Validate(NutAgentTransportKind transport, string? endpoint) =>
        ManagedNutServerProfileValidator.Validate(
            new ManagedNutServerProfileInput(
                Guid.NewGuid(), "Gandalf", "gandalf.sbra.local", "3493", null,
                NutManagementMode.Remote, ManagedNutServerAccessMode.Manage,
                "gandalf.sbra.local", "/etc/nut", "22", "operator",
                SshAuthenticationMode.Password, null, null, null,
                RemoteConfigurationTransportKind.SshSftp, null, null,
                SmbAuthenticationMode.CurrentWindowsIdentity, null, null,
                transport, endpoint),
            []);

    private static ManagedNutServerProfile RemoteProfile(NutAgentProfileSettings agent) => new(
        Guid.NewGuid(),
        "Gandalf",
        new NutMonitoringProfile("gandalf.sbra.local"),
        new NutManagementProfile(
            NutManagementMode.Remote, "gandalf.sbra.local", "/etc/nut", sshUsername: "operator", agent: agent),
        ManagedNutServerAccessMode.Manage);

    private static ManagedNutServerProfile LocalProfile() => new(
        Guid.NewGuid(),
        "Local",
        new NutMonitoringProfile("localhost"),
        new NutManagementProfile(NutManagementMode.Local),
        ManagedNutServerAccessMode.Manage);
}
