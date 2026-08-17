using System.IO;
using System.Runtime.Versioning;
using NutManager.Infrastructure.Agent;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// The agent's operator group has to resolve against the server's own local security database, and
/// what that database <em>is</em> differs by server role: the SAM on a workstation or member server,
/// the directory a domain controller uses as its local database. One test machine can only ever be
/// one of those, so the difference is exercised through a fake that reproduces what Windows answers
/// in each case.
///
/// The bug these tests exist for: the group was resolved as <c>MachineName\group</c>, which is
/// correct on a member server and resolves to nothing on a domain controller. On GANDALF the group
/// was plainly present as <c>SBRA\NutManager Operators</c> and the agent would have refused every
/// operation.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AgentDomainControllerAuthorizationTests
{
    private const string GroupName = "NutManager Operators";

    // The real values observed on GANDALF, a domain controller for SBRA.LOCAL.
    private const string GandalfGroupSid = "S-1-5-21-2563914070-3062813762-1456838247-1229";
    private const string GandalfOperatorSid = "S-1-5-21-2563914070-3062813762-1456838247-1142";

    private const string LocalGroupSid = "S-1-5-21-1111111111-2222222222-3333333333-1001";
    private const string DomainHomonymSid = "S-1-5-21-9999999999-8888888888-7777777777-2002";

    // ---------------------------------------------------------------- A. member server

    [Fact]
    public void AMemberServerResolvesItsOwnSamGroup()
    {
        var database = new FakeLocalSecurityDatabase();
        database.AddLocalGroup(GroupName, LocalGroupSid, domain: "MEMBERSRV");

        var authorization = new WindowsGroupAuthorization(GroupName, database);

        Assert.True(authorization.IsConfigured);
        Assert.Null(authorization.ConfigurationFailure);
        Assert.Equal(LocalGroupSid, PinnedSid(authorization));
    }

    // ---------------------------------------------------------------- B. domain controller

    [Fact]
    public void ADomainControllerResolvesTheGroupItsLocalDatabaseHolds()
    {
        // On a DC the local group database is the directory, so the group exists and translates to a
        // domain-qualified account. Nothing about that is special-cased in the code.
        var database = new FakeLocalSecurityDatabase();
        database.AddLocalGroup(GroupName, GandalfGroupSid, domain: "SBRA");

        var authorization = new WindowsGroupAuthorization(GroupName, database);

        Assert.True(authorization.IsConfigured);
        Assert.Equal(GandalfGroupSid, PinnedSid(authorization));
    }

    [Fact]
    public void TheGroupIsNeverLookedUpMachineQualified()
    {
        // The regression itself. Every name handed to the translation must be the bare group name;
        // qualifying it with the machine is what broke the domain controller.
        var database = new FakeLocalSecurityDatabase();
        database.AddLocalGroup(GroupName, GandalfGroupSid, domain: "SBRA");

        _ = new WindowsGroupAuthorization(GroupName, database);

        Assert.NotEmpty(database.LookupCalls);
        Assert.All(database.LookupCalls, call =>
        {
            Assert.DoesNotContain('\\', call);
            Assert.DoesNotContain(Environment.MachineName, call, StringComparison.OrdinalIgnoreCase);
        });
        Assert.All(database.LocalGroupCalls, call => Assert.DoesNotContain('\\', call));
    }

    [Fact]
    public void TheDomainNameIsNeverRequiredByTheCode()
    {
        // The same code resolves two servers whose authorities differ, without knowing either name.
        var memberServer = new FakeLocalSecurityDatabase();
        memberServer.AddLocalGroup(GroupName, LocalGroupSid, domain: "MEMBERSRV");

        var domainController = new FakeLocalSecurityDatabase();
        domainController.AddLocalGroup(GroupName, GandalfGroupSid, domain: "SBRA");

        Assert.Equal(LocalGroupSid, PinnedSid(new WindowsGroupAuthorization(GroupName, memberServer)));
        Assert.Equal(GandalfGroupSid, PinnedSid(new WindowsGroupAuthorization(GroupName, domainController)));
    }

    // ---------------------------------------------------------------- C. missing group

    [Fact]
    public void AMissingGroupFailsClosed()
    {
        var database = new FakeLocalSecurityDatabase();

        var authorization = new WindowsGroupAuthorization(GroupName, database);

        Assert.False(authorization.IsConfigured);
        Assert.Null(authorization.GroupSid);
        Assert.NotNull(authorization.ConfigurationFailure);
    }

    [Fact]
    public async Task AnUnconfiguredGroupAuthorizesNobody()
    {
        var database = new FakeLocalSecurityDatabase();
        database.Memberships[@"SBRA\PT90"] = [GroupName];

        var authorization = new WindowsGroupAuthorization(GroupName, database);

        Assert.False(await authorization.IsAuthorizedAsync(@"SBRA\PT90", default));
    }

    // ---------------------------------------------------------------- D. domain homonym

    [Fact]
    public void ADomainGroupOfTheSameNameNeverBecomesTheAuthorityOnAMemberServer()
    {
        // Both exist. The local group database holds only the local one, and the translation starts
        // at the local system, so the local SID is what gets pinned.
        var database = new FakeLocalSecurityDatabase();
        database.AddLocalGroup(GroupName, LocalGroupSid, domain: "MEMBERSRV");
        database.Accounts[$@"SBRA\{GroupName}"] = (DomainHomonymSid, WindowsAccountKind.Alias, "SBRA", null);

        var authorization = new WindowsGroupAuthorization(GroupName, database);

        Assert.Equal(LocalGroupSid, PinnedSid(authorization));
        Assert.NotEqual(DomainHomonymSid, PinnedSid(authorization));
    }

    [Fact]
    public async Task AMemberOfOnlyTheDomainHomonymIsNotAuthorized()
    {
        var database = new FakeLocalSecurityDatabase();
        database.AddLocalGroup(GroupName, LocalGroupSid, domain: "MEMBERSRV");

        // The caller's expanded local groups resolve to the domain homonym's SID, not the pinned one.
        database.Memberships[@"SBRA\Outsider"] = ["Domain Operators"];
        database.Accounts["Domain Operators"] = (DomainHomonymSid, WindowsAccountKind.Alias, "SBRA", null);

        var authorization = new WindowsGroupAuthorization(GroupName, database);

        Assert.False(await authorization.IsAuthorizedAsync(@"SBRA\Outsider", default));
    }

    // ---------------------------------------------------------------- E/F. membership

    [Fact]
    public async Task DirectMembershipIsAuthorized()
    {
        var database = new FakeLocalSecurityDatabase();
        database.AddLocalGroup(GroupName, GandalfGroupSid, domain: "SBRA");
        database.Memberships[@"SBRA\PT90"] = [GroupName];

        var authorization = new WindowsGroupAuthorization(GroupName, database);

        Assert.True(await authorization.IsAuthorizedAsync(@"SBRA\PT90", default));
    }

    [Fact]
    public async Task IndirectMembershipIsAuthorized()
    {
        // What LG_INCLUDE_INDIRECT buys: the caller is in a domain group that was nested into the
        // operators group, and Windows returns the operators group among the expanded names.
        var database = new FakeLocalSecurityDatabase();
        database.AddLocalGroup(GroupName, GandalfGroupSid, domain: "SBRA");
        database.Memberships[@"SBRA\PT90"] = ["Users", "SomeOperators", GroupName];
        database.Accounts["Users"] = ("S-1-5-32-545", WindowsAccountKind.Alias, "BUILTIN", null);
        database.Accounts["SomeOperators"] = (DomainHomonymSid, WindowsAccountKind.Group, "SBRA", null);

        var authorization = new WindowsGroupAuthorization(GroupName, database);

        Assert.True(await authorization.IsAuthorizedAsync(@"SBRA\PT90", default));
    }

    [Fact]
    public async Task AnAccountInNoGroupsIsNotAuthorized()
    {
        var database = new FakeLocalSecurityDatabase();
        database.AddLocalGroup(GroupName, GandalfGroupSid, domain: "SBRA");

        var authorization = new WindowsGroupAuthorization(GroupName, database);

        Assert.False(await authorization.IsAuthorizedAsync(@"SBRA\Nobody", default));
    }

    // ---------------------------------------------------------------- G/H/I. resolution failures

    [Fact]
    public async Task ACandidateGroupThatCannotBeResolvedIsSkippedRatherThanTrusted()
    {
        var database = new FakeLocalSecurityDatabase();
        database.AddLocalGroup(GroupName, GandalfGroupSid, domain: "SBRA");
        database.Memberships[@"SBRA\PT90"] = ["Unresolvable"];

        var authorization = new WindowsGroupAuthorization(GroupName, database);

        Assert.False(await authorization.IsAuthorizedAsync(@"SBRA\PT90", default));
    }

    [Fact]
    public async Task ASidMismatchIsNotAuthorized()
    {
        var database = new FakeLocalSecurityDatabase();
        database.AddLocalGroup(GroupName, GandalfGroupSid, domain: "SBRA");
        database.Memberships[@"SBRA\PT90"] = ["Another Group"];
        database.Accounts["Another Group"] = (LocalGroupSid, WindowsAccountKind.Alias, "SBRA", null);

        var authorization = new WindowsGroupAuthorization(GroupName, database);

        Assert.False(await authorization.IsAuthorizedAsync(@"SBRA\PT90", default));
    }

    [Fact]
    public async Task AGroupRecreatedUnderTheSameNameDoesNotSilentlyAuthorize()
    {
        // The pinned SID is the authority. A group deleted and recreated keeps the name and gets a
        // new SID, and membership of the new one is not membership of what was authorized.
        var database = new FakeLocalSecurityDatabase();
        database.AddLocalGroup(GroupName, GandalfGroupSid, domain: "SBRA");

        var authorization = new WindowsGroupAuthorization(GroupName, database);
        Assert.Equal(GandalfGroupSid, PinnedSid(authorization));

        database.Accounts[GroupName] = (DomainHomonymSid, WindowsAccountKind.Alias, "SBRA", null);
        database.Memberships[@"SBRA\PT90"] = [GroupName];

        Assert.False(await authorization.IsAuthorizedAsync(@"SBRA\PT90", default));
    }

    [Fact]
    public void AGroupThatExistsButCannotBeTranslatedFailsClosed()
    {
        var database = new FakeLocalSecurityDatabase();
        database.LocalGroups[GroupName] = (true, null);

        var authorization = new WindowsGroupAuthorization(GroupName, database);

        Assert.False(authorization.IsConfigured);
        Assert.NotNull(authorization.ConfigurationFailure);
    }

    [Fact]
    public void AnUnusableSidStringFailsClosed()
    {
        var database = new FakeLocalSecurityDatabase();
        database.LocalGroups[GroupName] = (true, null);
        database.Accounts[GroupName] = ("not-a-sid", WindowsAccountKind.Alias, "SBRA", null);

        var authorization = new WindowsGroupAuthorization(GroupName, database);

        Assert.False(authorization.IsConfigured);
        Assert.NotNull(authorization.ConfigurationFailure);
    }

    // ---------------------------------------------------------------- SID_NAME_USE

    [Theory]
    [InlineData(WindowsAccountKind.Alias)]   // member-server SAM group and DC domain-local group
    [InlineData(WindowsAccountKind.Group)]   // global and universal groups
    public void GroupKindsAreAccepted(WindowsAccountKind kind)
    {
        var database = new FakeLocalSecurityDatabase();
        database.LocalGroups[GroupName] = (true, null);
        database.Accounts[GroupName] = (GandalfGroupSid, kind, "SBRA", null);

        Assert.True(new WindowsGroupAuthorization(GroupName, database).IsConfigured);
    }

    [Theory]
    [InlineData(WindowsAccountKind.User)]
    [InlineData(WindowsAccountKind.Computer)]
    [InlineData(WindowsAccountKind.WellKnownGroup)]
    [InlineData(WindowsAccountKind.Domain)]
    [InlineData(WindowsAccountKind.DeletedAccount)]
    [InlineData(WindowsAccountKind.Invalid)]
    [InlineData(WindowsAccountKind.Unknown)]
    public void NonGroupKindsAreRefused(WindowsAccountKind kind)
    {
        // A name that resolves is not a name that may hold members. Pinning a user or a computer as
        // the operators group would be an authority nobody could revoke by editing group membership.
        var database = new FakeLocalSecurityDatabase();
        database.LocalGroups[GroupName] = (true, null);
        database.Accounts[GroupName] = (GandalfGroupSid, kind, "SBRA", null);

        var authorization = new WindowsGroupAuthorization(GroupName, database);

        Assert.False(authorization.IsConfigured);
        Assert.NotNull(authorization.ConfigurationFailure);
    }

    [Fact]
    public async Task ACandidateThatResolvesToANonGroupIsNotAuthorized()
    {
        var database = new FakeLocalSecurityDatabase();
        database.AddLocalGroup(GroupName, GandalfGroupSid, domain: "SBRA");

        // Same SID as the pinned group, but reported as a user. The kind is checked before the match.
        database.Memberships[@"SBRA\PT90"] = ["Impostor"];
        database.Accounts["Impostor"] = (GandalfGroupSid, WindowsAccountKind.User, "SBRA", null);

        var authorization = new WindowsGroupAuthorization(GroupName, database);

        Assert.False(await authorization.IsAuthorizedAsync(@"SBRA\PT90", default));
    }

    // ---------------------------------------------------------------- source boundaries

    [Fact]
    public void TheAuthorityIsNeverBuiltFromTheMachineName()
    {
        // The regression guard for the fixed premise. NTAccount is how the machine-qualified name was
        // constructed; every translation now goes through the local security database instead.
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "NutManager.Infrastructure", "Agent", "WindowsGroupAuthorization.cs"));

        Assert.DoesNotContain("NTAccount", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.MachineName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.UserDomainName", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInteropAsksTheLocalComputerAndExpandsIndirectMembership()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "NutManager.Infrastructure", "Agent", "WindowsAgentGroupInterop.cs"));

        // LG_INCLUDE_INDIRECT must survive: without it a nested domain group stops authorizing.
        Assert.Contains("LgIncludeIndirect = 0x0001", source, StringComparison.Ordinal);
        Assert.Contains("NetLocalGroupGetInfo", source, StringComparison.Ordinal);
        Assert.Contains("LookupAccountName", source, StringComparison.Ordinal);

        // No domain and no machine may be named in production code.
        Assert.DoesNotContain("Environment.MachineName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.UserDomainName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SBRA", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GANDALF", source, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The pinned SID as text. <see cref="System.Security.Principal.SecurityIdentifier"/> is
    /// Windows-typed, which is why the whole fixture carries the platform annotation.
    /// </summary>
    private static string? PinnedSid(WindowsGroupAuthorization authorization) => authorization.GroupSid?.Value;

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NutManager.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    // ---------------------------------------------------------------- fake

    /// <summary>
    /// Reproduces what Windows answers, without needing the Windows that would answer it.
    ///
    /// <see cref="LookupAccount"/> models the documented behaviour that the search starts at the local
    /// system: an unqualified name finds the local entry even when a domain entry of the same name is
    /// also present in the map.
    /// </summary>
    private sealed class FakeLocalSecurityDatabase : IWindowsLocalSecurityDatabase
    {
        public Dictionary<string, (bool Exists, string? Failure)> LocalGroups { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, (string? Sid, WindowsAccountKind Kind, string? Domain, string? Failure)> Accounts { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, IReadOnlyList<string>> Memberships { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> LookupCalls { get; } = [];

        public List<string> LocalGroupCalls { get; } = [];

        public void AddLocalGroup(string name, string sid, string domain)
        {
            LocalGroups[name] = (true, null);
            Accounts[name] = (sid, WindowsAccountKind.Alias, domain, null);
        }

        public (bool Exists, string? Failure) FindLocalGroup(string groupName)
        {
            LocalGroupCalls.Add(groupName);
            return LocalGroups.TryGetValue(groupName, out var found)
                ? found
                : (false, $"The local group '{groupName}' does not exist in this server's local group database.");
        }

        public (string? Sid, WindowsAccountKind Kind, string? Domain, string? Failure) LookupAccount(string accountName)
        {
            LookupCalls.Add(accountName);
            return Accounts.TryGetValue(accountName, out var found)
                ? found
                : (null, WindowsAccountKind.Unknown, null, $"The name '{accountName}' could not be translated to a SID.");
        }

        public IReadOnlyList<string> GetLocalGroupNames(string accountName) =>
            Memberships.TryGetValue(accountName, out var groups) ? groups : [];
    }
}
