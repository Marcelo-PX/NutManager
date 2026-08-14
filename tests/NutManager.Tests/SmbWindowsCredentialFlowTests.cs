using NutManager.App.Services;
using NutManager.App.ViewModels;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.Remote.Smb;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// How an SMB profile authenticates after T30. The rules being pinned are that the current Windows
/// identity never asks for anything, and that an explicit credential is proven against the share
/// before it is allowed to replace whatever was stored.
/// </summary>
public sealed class SmbWindowsCredentialFlowTests
{
    private const string Secret = "SMB_FLOW_SENTINEL_C41E9A";
    private const string Account = @"SBRA\pt90";

    // ==================== Fakes ====================

    private sealed class FakePrompt : IWindowsCredentialPrompt
    {
        private readonly Func<WindowsCredentialPromptResult> _factory;

        public FakePrompt(Func<WindowsCredentialPromptResult> factory) => _factory = factory;

        public int Calls { get; private set; }

        public WindowsCredentialPromptRequest? LastRequest { get; private set; }

        public Task<WindowsCredentialPromptResult> RequestAsync(
            WindowsCredentialPromptRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(_factory());
        }
    }

    private sealed class FakeTransport : IRemoteNutConfigurationTransport
    {
        private readonly bool _succeeds;

        public FakeTransport(bool succeeds) => _succeeds = succeeds;

        public int ConnectCalls { get; private set; }

        public SmbRemoteNutConnectionRequest? LastSmbRequest { get; private set; }

        /// <summary>
        /// The password as it was at the moment of the call. It has to be copied here rather than
        /// read from the request afterwards: the buffer is deliberately zeroed once the sign-in
        /// finishes, so a later read would see nothing — which is the point.
        /// </summary>
        public string? ObservedPassword { get; private set; }

        public Task<RemoteNutConnectionResult> ConnectAsync(
            RemoteNutConfigurationConnectionRequest request, CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            LastSmbRequest = request as SmbRemoteNutConnectionRequest;
            ObservedPassword = LastSmbRequest is null ? null : new string(LastSmbRequest.Password.Span);
            return Task.FromResult(_succeeds
                ? new RemoteNutConnectionResult(RemoteNutConnectionState.Connected, new FakeSession())
                : new RemoteNutConnectionResult(RemoteNutConnectionState.AccessDenied, message: "denied"));
        }
    }

    /// <summary>
    /// A session that exists only so a successful connection has something to hand back. Every
    /// member beyond identity and directory validation throws: these tests never write.
    /// </summary>
    private sealed class FakeSession : IRemoteNutConfigurationSession
    {
        public RemoteNutPlatform Platform => RemoteNutPlatform.Windows;

        public IRemoteNutConfigurationPathPolicy PathPolicy { get; } =
            new SmbRemoteNutConfigurationPathPolicy(@"\\server\share");

        public string HomeDirectory => @"\\server\share";

        public bool IsSafeWriteCapabilityValidFor(string configurationDirectory) => true;

        public Task<RemoteNutDirectoryListing> BrowseDirectoryAsync(string directory, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteNutDirectoryListing(directory, null, []));

        public Task<RemoteNutDirectoryValidationResult> ValidateConfigurationDirectoryAsync(string directory, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteNutDirectoryValidationResult(RemoteNutTransportStatus.Success, directory));

        public Task<RemoteNutFileReadResult> ReadFileAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteNutWriteCapabilityResult> ProbeSafeWriteCapabilityAsync(string directory, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteNutWriteCapabilityResult(true, RemoteNutPlatform.Windows));

        public void InvalidateSafeWriteCapability()
        {
        }

        public Task<RemoteNutFileReadResult> UploadCandidateAsync(RemoteNutCandidateUploadRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteNutCommitResult> CommitConfigurationAsync(RemoteNutConfigurationCommitRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteNutCommitResult> RollbackConfigurationAsync(RemoteNutConfigurationRollbackRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteNutTemporaryCleanupResult> DeleteGeneratedTemporaryFileAsync(string configurationDirectory, string temporaryFileName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeCredentialStore : IRemoteCredentialStore
    {
        private readonly Dictionary<RemoteCredentialKind, string> _stored = [];

        public int WriteCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public void Seed(RemoteCredentialKind kind, string secret) => _stored[kind] = secret;

        public string? Peek(RemoteCredentialKind kind) => _stored.GetValueOrDefault(kind);

        public Task<RemoteCredentialStoreResult> ContainsAsync(Guid profileId, RemoteCredentialKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteCredentialStoreResult(_stored.ContainsKey(kind)
                ? RemoteCredentialStoreStatus.Success
                : RemoteCredentialStoreStatus.NotFound));

        public Task<RemoteCredentialReadResult> ReadAsync(Guid profileId, RemoteCredentialKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(_stored.TryGetValue(kind, out var value)
                ? new RemoteCredentialReadResult(RemoteCredentialStoreStatus.Success, new RemoteCredentialSecret(value))
                : new RemoteCredentialReadResult(RemoteCredentialStoreStatus.NotFound));

        public Task<RemoteCredentialStoreResult> WriteAsync(Guid profileId, RemoteCredentialKind kind, ReadOnlyMemory<char> secret, CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            _stored[kind] = new string(secret.Span);
            return Task.FromResult(new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.Success));
        }

        public Task<RemoteCredentialStoreResult> DeleteAsync(Guid profileId, RemoteCredentialKind kind, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            _stored.Remove(kind);
            return Task.FromResult(new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.Success));
        }

        public Task<RemoteCredentialStoreResult> DeleteAllForProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
        {
            _stored.Clear();
            return Task.FromResult(new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.Success));
        }
    }

    private sealed class InMemoryProfileStore : IManagedNutServerProfileStore
    {
        private ManagedNutServerProfiles _document;

        public InMemoryProfileStore(ManagedNutServerProfile profile) =>
            _document = new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, profile.Id, [profile]);

        public Task<ManagedNutServerProfiles?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ManagedNutServerProfiles?>(_document);

        public Task SaveAsync(ManagedNutServerProfiles profiles, CancellationToken cancellationToken)
        {
            _document = profiles;
            return Task.CompletedTask;
        }
    }

    // ==================== Setup ====================

    private static ManagedNutServerProfile Profile(
        SmbAuthenticationMode mode,
        string? username = null,
        ManagedNutServerAccessMode access = ManagedNutServerAccessMode.Manage) => new(
        Guid.NewGuid(),
        "SMB",
        new NutMonitoringProfile("monitor.example"),
        new NutManagementProfile(
            NutManagementMode.Remote,
            configurationTransport: RemoteConfigurationTransportKind.Smb,
            smbSharePath: @"\\server\share",
            smbAuthenticationMode: mode,
            smbUsername: username),
        access);

    /// <summary>
    /// Persisting a remembered credential goes through the profile update service, so the session
    /// is given a real one over in-memory stores rather than a null.
    /// </summary>
    private static RemoteManagementSessionViewModel Session(
        ManagedNutServerProfile profile,
        IRemoteNutConfigurationTransport transport,
        FakeCredentialStore credentials,
        IWindowsCredentialPrompt? prompt = null) =>
        new(profile,
            transport,
            new ManagedNutServerProfileUpdateService(new InMemoryProfileStore(profile), credentials),
            credentials,
            UiLanguagePreference.PtBr,
            prompt);

    private static WindowsCredentialPromptResult Prompted(bool remember = false, string account = Account) =>
        WindowsCredentialPromptResult.Success(account, Secret, remember);

    // ==================== Current Windows identity ====================

    [Fact]
    public async Task TheCurrentWindowsIdentityNeverOpensACredentialDialog()
    {
        var prompt = new FakePrompt(() => Prompted());
        var transport = new FakeTransport(succeeds: true);
        var viewModel = Session(Profile(SmbAuthenticationMode.CurrentWindowsIdentity), transport, new FakeCredentialStore(), prompt);

        await viewModel.ConnectSmbAsync();

        Assert.Equal(0, prompt.Calls);
        Assert.Equal(1, transport.ConnectCalls);
        Assert.False(viewModel.CanUseWindowsCredentialPrompt);
    }

    [Fact]
    public async Task TheCurrentWindowsIdentityIgnoresAnyStoredSmbPasswordAndSendsNoCredential()
    {
        var credentials = new FakeCredentialStore();
        credentials.Seed(RemoteCredentialKind.SmbPassword, "left-over-from-an-older-profile");
        var transport = new FakeTransport(succeeds: true);
        var prompt = new FakePrompt(() => Prompted());
        var viewModel = Session(Profile(SmbAuthenticationMode.CurrentWindowsIdentity), transport, credentials, prompt);

        await viewModel.ConnectSmbAsync();

        Assert.Equal(0, prompt.Calls);
        // The session's own Windows token is the credential; nothing is read or sent.
        Assert.True(transport.LastSmbRequest!.Password.IsEmpty);
        Assert.Equal(SmbAuthenticationMode.CurrentWindowsIdentity, transport.LastSmbRequest.AuthenticationMode);
    }

    [Fact]
    public void TheCurrentWindowsIdentityNeedsNoUsernameAndNoProtectedCredential()
    {
        var viewModel = Session(Profile(SmbAuthenticationMode.CurrentWindowsIdentity), new FakeTransport(true), new FakeCredentialStore());

        Assert.True(viewModel.UsesSmbCurrentWindowsIdentity);
        Assert.Null(viewModel.SmbCredentialIdentity);
        Assert.False(viewModel.CanForgetStoredCredential);
    }

    // ==================== Explicit account ====================

    [Fact]
    public async Task AnExplicitProfileWithoutAStoredCredentialOpensTheWindowsDialog()
    {
        var prompt = new FakePrompt(() => Prompted());
        var transport = new FakeTransport(succeeds: true);
        var viewModel = Session(Profile(SmbAuthenticationMode.ExplicitCredentials), transport, new FakeCredentialStore(), prompt);

        await viewModel.ConnectSmbAsync();

        Assert.Equal(1, prompt.Calls);
        Assert.Equal(Account, transport.LastSmbRequest!.Username);
        Assert.Equal(Secret, transport.ObservedPassword);

        // Once the sign-in has finished the buffer the prompt produced is wiped, so a transport
        // that held on to the memory cannot read the password back out of it later.
        Assert.DoesNotContain(Secret, new string(transport.LastSmbRequest.Password.Span), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStoredCredentialIsReusedWithoutShowingTheDialogAgain()
    {
        var credentials = new FakeCredentialStore();
        credentials.Seed(RemoteCredentialKind.SmbPassword, Secret);
        var prompt = new FakePrompt(() => Prompted());
        var transport = new FakeTransport(succeeds: true);
        var viewModel = Session(Profile(SmbAuthenticationMode.ExplicitCredentials, Account), transport, credentials, prompt);

        await viewModel.ConnectSmbAsync();

        Assert.Equal(0, prompt.Calls);
        Assert.Equal(1, transport.ConnectCalls);
    }

    [Fact]
    public async Task CancellingTheDialogChangesNothingAtAll()
    {
        var credentials = new FakeCredentialStore();
        credentials.Seed(RemoteCredentialKind.SmbPassword, "known-good");
        var prompt = new FakePrompt(WindowsCredentialPromptResult.Cancelled);
        var transport = new FakeTransport(succeeds: true);
        var viewModel = Session(Profile(SmbAuthenticationMode.ExplicitCredentials, Account), transport, credentials, prompt);

        var signedIn = await viewModel.SignInWithWindowsCredentialAsync();

        Assert.False(signedIn);
        Assert.Equal(0, transport.ConnectCalls);
        Assert.Equal(0, credentials.WriteCalls);
        Assert.Equal(0, credentials.DeleteCalls);
        Assert.Equal("known-good", credentials.Peek(RemoteCredentialKind.SmbPassword));
    }

    [Fact]
    public async Task ACredentialTheShareRefusesIsNeverStored()
    {
        var credentials = new FakeCredentialStore();
        var prompt = new FakePrompt(() => Prompted(remember: true));
        var transport = new FakeTransport(succeeds: false);
        var viewModel = Session(Profile(SmbAuthenticationMode.ExplicitCredentials), transport, credentials, prompt);

        var signedIn = await viewModel.SignInWithWindowsCredentialAsync();

        Assert.False(signedIn);
        Assert.Equal(1, transport.ConnectCalls);
        // Remember was ticked, but the credential never proved itself.
        Assert.Equal(0, credentials.WriteCalls);
        Assert.Null(credentials.Peek(RemoteCredentialKind.SmbPassword));
    }

    [Fact]
    public async Task AFailedReplacementKeepsTheCredentialThatStillWorks()
    {
        var credentials = new FakeCredentialStore();
        credentials.Seed(RemoteCredentialKind.SmbPassword, "known-good");
        var prompt = new FakePrompt(() => Prompted(remember: true));
        var transport = new FakeTransport(succeeds: false);
        var viewModel = Session(Profile(SmbAuthenticationMode.ExplicitCredentials, Account), transport, credentials, prompt);

        var changed = await viewModel.ChangeWindowsCredentialAsync();

        Assert.False(changed);
        // The good credential is never destroyed first and then re-created; it is simply left alone.
        Assert.Equal("known-good", credentials.Peek(RemoteCredentialKind.SmbPassword));
        Assert.Equal(0, credentials.DeleteCalls);
    }

    [Fact]
    public async Task RememberIsHonouredOnlyWhenTheDialogAskedForIt()
    {
        var remembered = new FakeCredentialStore();
        await Session(Profile(SmbAuthenticationMode.ExplicitCredentials), new FakeTransport(true), remembered,
            new FakePrompt(() => Prompted(remember: true))).SignInWithWindowsCredentialAsync();

        var sessionOnly = new FakeCredentialStore();
        await Session(Profile(SmbAuthenticationMode.ExplicitCredentials), new FakeTransport(true), sessionOnly,
            new FakePrompt(() => Prompted(remember: false))).SignInWithWindowsCredentialAsync();

        Assert.Equal(Secret, remembered.Peek(RemoteCredentialKind.SmbPassword));
        Assert.Null(sessionOnly.Peek(RemoteCredentialKind.SmbPassword));
    }

    [Fact]
    public async Task TheDialogIsAskedForTheAccountAlreadyOnTheProfile()
    {
        var prompt = new FakePrompt(() => Prompted());
        var viewModel = Session(Profile(SmbAuthenticationMode.ExplicitCredentials, Account), new FakeTransport(true), new FakeCredentialStore(), prompt);
        viewModel.OwnerWindowHandle = 99;

        await viewModel.SignInWithWindowsCredentialAsync();

        Assert.Equal(Account, prompt.LastRequest!.PreferredUsername);
        Assert.Equal(99, prompt.LastRequest.OwnerWindowHandle);
        Assert.Contains(@"\\server\share", prompt.LastRequest.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoSecretReachesTheViewModelsOwnState()
    {
        var prompt = new FakePrompt(() => Prompted(remember: true));
        var viewModel = Session(Profile(SmbAuthenticationMode.ExplicitCredentials), new FakeTransport(true), new FakeCredentialStore(), prompt);

        await viewModel.SignInWithWindowsCredentialAsync();

        var published = string.Join('\n', viewModel.GetType().GetProperties()
            .Where(property => property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
            .Select(property => property.GetValue(viewModel) as string));
        Assert.DoesNotContain(Secret, published, StringComparison.Ordinal);
    }

    // ==================== Manage versus ReadOnly wording ====================

    [Fact]
    public void AManagementProfileIsNeverDescribedAsReadOnlyBeforeItsCapabilityIsProbed()
    {
        var viewModel = Session(
            Profile(SmbAuthenticationMode.CurrentWindowsIdentity, access: ManagedNutServerAccessMode.Manage),
            new FakeTransport(true), new FakeCredentialStore());

        Assert.True(viewModel.IsManageProfile);
        Assert.False(viewModel.CanEditConfiguration);
        // Not yet verified is a different statement from read-only, and saying the latter beside
        // "Acesso: Gerenciar" is what made the screen contradict itself.
        Assert.DoesNotContain("somente leitura", viewModel.WriteCapabilityText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AReadOnlyProfileIsStillDescribedAsReadOnly()
    {
        var viewModel = Session(
            Profile(SmbAuthenticationMode.CurrentWindowsIdentity, access: ManagedNutServerAccessMode.ReadOnly),
            new FakeTransport(true), new FakeCredentialStore());

        Assert.False(viewModel.IsManageProfile);
        Assert.Contains("somente leitura", viewModel.WriteCapabilityText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoConnectionFailureMessageClaimsAnAccessModeOfItsOwn()
    {
        var source = Repository.Read(Path.Combine(
            "src", "NutManager.Infrastructure", "Remote", "Smb", "WindowsSmbRemoteNutConfigurationTransport.cs"));

        // The only place allowed to mention read-only is the probe that genuinely reflects the
        // profile's own ReadOnly policy.
        var mentions = source.Split('\n')
            .Where(line => line.Contains("somente leitura", StringComparison.OrdinalIgnoreCase) && line.Contains("message:", StringComparison.Ordinal))
            .ToArray();
        var only = Assert.Single(mentions);
        Assert.Contains("O perfil SMB está configurado como somente leitura.", only, StringComparison.Ordinal);
    }

    // ==================== Simplified SMB surface ====================

    [Fact]
    public void NeitherSmbScreenOffersAUsernameFieldADirectoryFieldOrAPasswordBox()
    {
        var settings = Repository.Read(Path.Combine("src", "NutManager.App", "Views", "SettingsPageView.axaml"));
        var remote = Repository.Read(Path.Combine("src", "NutManager.App", "Views", "RemoteAccessAdministrationView.axaml"));

        // The share is the exact configuration location, so there is no second directory to type.
        Assert.DoesNotContain("ProfileDraft.SmbConfigurationDirectory", settings, StringComparison.Ordinal);
        // The account comes back from the Windows dialog; it is never typed into the form.
        Assert.DoesNotContain("ProfileDraft.SmbUsername", settings, StringComparison.Ordinal);
        // And no NutManager control ever collects an SMB password.
        Assert.DoesNotContain("SmbPasswordBox", remote, StringComparison.Ordinal);
        Assert.Contains("Smb.Credential.SignIn", remote, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCredentialActionsAreOfferedOnlyForAnExplicitAccount()
    {
        var remote = Repository.Read(Path.Combine("src", "NutManager.App", "Views", "RemoteAccessAdministrationView.axaml"));

        // Current Windows identity shows none of this: the credential actions live after the
        // explicit-mode gate, so they are not rendered for the current-identity profile.
        var gate = remote.IndexOf("RemoteManagement.UsesSmbExplicitCredentials", StringComparison.Ordinal);
        Assert.True(gate >= 0, "the credential section must be gated on the explicit mode");
        Assert.True(remote.IndexOf("Smb.Credential.SignIn", StringComparison.Ordinal) > gate);
        Assert.True(remote.IndexOf("Smb.Credential.Change", StringComparison.Ordinal) > gate);

        // The sign-in action is a Button, so it is keyboard reachable, and it is announced by name.
        Assert.Contains("AutomationProperties.Name=\"{Binding Strings[Smb.Credential.SignIn]}\"", remote, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding Strings[Smb.Credential.Change]}\"", remote, StringComparison.Ordinal);
    }

    [Fact]
    public void ALegacyConfigurationDirectoryIsSurfacedRatherThanDroppedOrRetargeted()
    {
        var profile = Profile(SmbAuthenticationMode.CurrentWindowsIdentity);
        var draft = new ManagedNutServerProfileDraftViewModel(profile)
        {
            SmbSharePath = @"\server\share",
            SmbConfigurationDirectory = @"\server\share\etc"
        };

        // The value is still there — nothing was silently discarded — and the form says so.
        Assert.True(draft.HasLegacySmbConfigurationDirectory);
        Assert.Equal(@"\server\share\etc", draft.SmbConfigurationDirectory);

        draft.SmbSharePath = @"\server\share\etc";
        Assert.False(draft.HasLegacySmbConfigurationDirectory);
    }

    [Fact]
    public void NothingInTheSmbPathShellsOutOrTouchesGlobalWindowsSessions()
    {
        foreach (var file in new[]
        {
            Path.Combine("src", "NutManager.Infrastructure", "Remote", "Smb", "WindowsSmbRemoteNutConfigurationTransport.cs"),
            Path.Combine("src", "NutManager.Infrastructure", "Remote", "Smb", "IWindowsSmbSessionIdentity.cs"),
            Path.Combine("src", "NutManager.App", "ViewModels", "RemoteManagementSessionViewModel.cs")
        })
        {
            var source = Repository.Read(file);
            foreach (var forbidden in new[] { "Process.Start", "net use", "cmdkey", "WNetAddConnection", "WNetCancelConnection" })
            {
                Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
