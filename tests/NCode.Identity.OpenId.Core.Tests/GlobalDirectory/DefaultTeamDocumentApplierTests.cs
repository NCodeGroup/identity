// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Logging;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Teams;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.Mediator;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.GlobalDirectory;

public sealed class DefaultTeamDocumentApplierTests : BaseTests
{
    private const string TeamId = "acme";
    private const string Region = "region-a";
    private const string AccessIdentityA = "arn:aws:iam::123456789012:role/team-a";
    private const string UnscopedReason = "directory team mirror";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ITeamStore> MockTeamStore { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<ITeamRootKeyMirror> MockTeamRootKeyMirror { get; }
    private Mock<IMediator> MockMediator { get; }
    private FakeTimeProvider TimeProvider { get; } =
        new(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
    private FakeLogger<DefaultTeamDocumentApplier> Logger { get; } = new();
    private ServiceProvider ServiceProvider { get; }
    private DefaultTeamDocumentApplier Applier { get; }

    public DefaultTeamDocumentApplierTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockTeamStore = CreateStrictMock<ITeamStore>();
        MockAmbientTenantAccessor = CreateStrictMock<IAmbientTenantAccessor>();
        MockTeamRootKeyMirror = CreateStrictMock<ITeamRootKeyMirror>();
        MockMediator = CreateStrictMock<IMediator>();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped(_ => MockMediator.Object);
        ServiceProvider = serviceCollection.BuildServiceProvider();

        var serviceScopeFactory = ServiceProvider.GetRequiredService<IServiceScopeFactory>();
        var postCommitCommandSender = new DefaultPostCommitCommandSender(
            serviceScopeFactory,
            TimeProvider
        );

        Applier = new DefaultTeamDocumentApplier(
            MockStoreManagerFactory.Object,
            MockAmbientTenantAccessor.Object,
            postCommitCommandSender,
            MockTeamRootKeyMirror.Object,
            Logger
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ServiceProvider.Dispose();
        }

        base.Dispose(disposing);
    }

    #region ApplyAsync Tests

    [Fact]
    public async Task ApplyAsync_WhenTheTeamIsNotStored_MirrorsItAndSendsTheCommandWithSettingsChanged()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetSettingsOrDefaultAsync(TeamId, cancellationToken))
            .ReturnsAsync((PersistedTeamSettings?)null)
            .Verifiable();
        SetupMirror(isApplied: true, cancellationToken);
        SetupSave(cancellationToken);
        SetupCommand(settingsChanged: true);
        SetupMirrorRootKey();

        await Applier.ApplyAsync(
            CreateDocument("""{"flag":true}""", sequence: 7),
            cancellationToken
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenTheSettingsAreUnchanged_SendsTheCommandWithoutSettingsChanged()
    {
        SetupUnscoped();
        SetupStoreManager();
        SetupStoredSettings("""{"flag": true}""");
        SetupMirror(isApplied: true, It.IsAny<CancellationToken>());
        SetupSave(It.IsAny<CancellationToken>());
        SetupCommand(settingsChanged: false);
        SetupMirrorRootKey();

        await Applier.ApplyAsync(
            CreateDocument("""{"flag":true}""", sequence: 8),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenTheSettingsDiffer_SendsTheCommandWithSettingsChanged()
    {
        SetupUnscoped();
        SetupStoreManager();
        SetupStoredSettings("""{"flag":false}""");
        SetupMirror(isApplied: true, It.IsAny<CancellationToken>());
        SetupSave(It.IsAny<CancellationToken>());
        SetupCommand(settingsChanged: true);
        SetupMirrorRootKey();

        await Applier.ApplyAsync(
            CreateDocument("""{"flag":true}""", sequence: 8),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenTheDocumentCarriesRootKeys_MirrorsThemIntoTheTeam()
    {
        SetupUnscoped();
        SetupStoreManager();
        SetupStoredSettings("""{"flag":true}""");
        PersistedTeam? mirrored = null;
        MockTeamStore
            .Setup(x => x.MirrorAsync(It.IsAny<PersistedTeam>(), It.IsAny<CancellationToken>()))
            .Callback<PersistedTeam, CancellationToken>((team, _) => mirrored = team)
            .ReturnsAsync(true)
            .Verifiable();
        SetupSave(It.IsAny<CancellationToken>());
        SetupCommand(settingsChanged: false);
        SetupMirrorRootKey();
        var document = CreateDocument("""{"flag":true}""", sequence: 8) with
        {
            RootKeys = ImmutableDictionary<string, TeamRootKeyReference>
                .Empty.Add(
                    Region,
                    new TeamRootKeyReference("aws-kms", "kms://key-a")
                    {
                        AccessIdentity = AccessIdentityA,
                    }
                )
                .Add("region-b", new TeamRootKeyReference("gcp-kms", "kms://key-b")),
        };

        await Applier.ApplyAsync(document, CancellationToken.None);

        Assert.NotNull(mirrored);
        Assert.Equal(2, mirrored.RootKeys.Count);
        var regionA = mirrored.RootKeys[Region];
        Assert.Equal("aws-kms", regionA.KeyWrapperName);
        Assert.Equal("kms://key-a", regionA.KeyReference);
        Assert.Equal(AccessIdentityA, regionA.AccessIdentity);
        var regionB = mirrored.RootKeys["region-b"];
        Assert.Equal("gcp-kms", regionB.KeyWrapperName);
        Assert.Equal("kms://key-b", regionB.KeyReference);
        Assert.Null(regionB.AccessIdentity);
    }

    [Fact]
    public async Task ApplyAsync_WhenTheDocumentIsDisabled_MirrorsTheTeamAsDisabled()
    {
        SetupUnscoped();
        SetupStoreManager();
        SetupStoredSettings("""{"flag":true}""");
        PersistedTeam? mirrored = null;
        MockTeamStore
            .Setup(x => x.MirrorAsync(It.IsAny<PersistedTeam>(), It.IsAny<CancellationToken>()))
            .Callback<PersistedTeam, CancellationToken>((team, _) => mirrored = team)
            .ReturnsAsync(true)
            .Verifiable();
        SetupSave(It.IsAny<CancellationToken>());
        SetupCommand(settingsChanged: false);
        SetupMirrorRootKey();
        var document = CreateDocument("""{"flag":true}""", sequence: 8) with { IsDisabled = true };

        await Applier.ApplyAsync(document, CancellationToken.None);

        Assert.NotNull(mirrored);
        Assert.True(mirrored.IsDisabled);
    }

    [Fact]
    public async Task ApplyAsync_WhenTheDocumentIsApplied_MirrorsTheTeamsRootKeyAfterTheCommand()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        var calls = new List<string>();
        SetupUnscoped();
        SetupStoreManager();
        SetupStoredSettings("""{"flag":true}""");
        SetupMirror(isApplied: true, cancellationToken);
        SetupSave(cancellationToken);
        MockMediator
            .Setup(x =>
                x.SendAsync(
                    It.Is<TeamChangedCommand>(command => command.TeamId == TeamId),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => calls.Add("command"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);
        MockTeamRootKeyMirror
            .Setup(x => x.MirrorAsync(TeamId, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("mirror"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        await Applier.ApplyAsync(
            CreateDocument("""{"flag":true}""", sequence: 8),
            cancellationToken
        );

        Assert.Equal(["command", "mirror"], calls);
    }

    [Fact]
    public async Task ApplyAsync_WhenTheSequenceIsStale_DoesNotSaveOrSendTheCommand()
    {
        SetupUnscoped();
        SetupStoreManager();
        SetupStoredSettings("""{"flag":false}""");
        SetupMirror(isApplied: false, It.IsAny<CancellationToken>());

        // Strict mocks: neither a save, a command nor a root key mirror call is set up, so any would fail the test.
        await Applier.ApplyAsync(
            CreateDocument("""{"flag":true}""", sequence: 3),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenTheSequenceIsStale_DoesNotMirrorTheRootKey()
    {
        SetupUnscoped();
        SetupStoreManager();
        SetupStoredSettings("""{"flag":false}""");
        SetupMirror(isApplied: false, It.IsAny<CancellationToken>());

        await Applier.ApplyAsync(
            CreateDocument("""{"flag":true}""", sequence: 3),
            CancellationToken.None
        );

        MockTeamRootKeyMirror.Verify(
            x => x.MirrorAsync(TeamId, It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenTheParentIsNotMirrored_LogsAndWritesNothing()
    {
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync("parent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedTeam?)null)
            .Verifiable();
        var document = CreateDocument("""{"flag":true}""", sequence: 8) with
        {
            ParentTeamId = "parent",
        };

        // Strict mocks: no settings read, mirror, save, command or root key mirror call is set up, so any would fail
        // the test.
        await Applier.ApplyAsync(document, CancellationToken.None);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal(EventIds.DirectoryTeamParentNotMirrored, record.Id.Id);
    }

    [Fact]
    public async Task ApplyAsync_WhenTheParentIsMirrored_MirrorsTheTeam()
    {
        SetupApplied(It.IsAny<CancellationToken>());
        SetupMirrorRootKey();
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync("parent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRootKeyTeam("parent"))
            .Verifiable();
        var document = CreateDocument("""{"flag":true}""", sequence: 8) with
        {
            ParentTeamId = "parent",
        };

        await Applier.ApplyAsync(document, CancellationToken.None);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ApplyAsync_WhenTeamChangedCommandFails_LogsWarningAndStillMirrorsTheRootKey()
    {
        SetupUnscoped();
        SetupStoreManager();
        SetupStoredSettings("""{"flag":true}""");
        SetupMirror(isApplied: true, It.IsAny<CancellationToken>());
        SetupSave(It.IsAny<CancellationToken>());
        SetupFailingCommand(new InvalidOperationException("The mediator is unavailable."));
        SetupMirrorRootKey();

        await Applier.ApplyAsync(
            CreateDocument("""{"flag":true}""", sequence: 8),
            CancellationToken.None
        );

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamChangeNotSent, record.Id.Id);
        Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
    }

    #endregion

    #region RetireAsync Tests

    [Fact]
    public async Task RetireAsync_WhenTheTeamIsNotStored_DoesNothing()
    {
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync(TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedTeam?)null)
            .Verifiable();

        await Applier.RetireAsync(TeamId, 9, CancellationToken.None);
    }

    [Fact]
    public async Task RetireAsync_WhenNothingNamesTheTeam_RemovesItAndSendsTheCommand()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync(TeamId, cancellationToken))
            .ReturnsAsync(CreateStoredTeam())
            .Verifiable();
        MockTeamStore
            .Setup(x => x.HasDependentsAsync(TeamId, cancellationToken))
            .ReturnsAsync(false)
            .Verifiable();
        MockTeamStore
            .Setup(x => x.RemoveAsync(TeamId, cancellationToken))
            .ReturnsAsync(true)
            .Verifiable();
        SetupSave(cancellationToken);
        SetupCommand(settingsChanged: false);
        SetupMirrorRootKey();

        await Applier.RetireAsync(TeamId, 9, cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RetireAsync_WhenTheTeamIsRetired_MirrorsTheTeamsRootKey(bool isRemoved)
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        var calls = new List<string>();
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync(TeamId, cancellationToken))
            .ReturnsAsync(CreateStoredTeam())
            .Verifiable();
        MockTeamStore
            .Setup(x => x.HasDependentsAsync(TeamId, cancellationToken))
            .ReturnsAsync(!isRemoved)
            .Verifiable();
        if (isRemoved)
        {
            MockTeamStore
                .Setup(x => x.RemoveAsync(TeamId, cancellationToken))
                .ReturnsAsync(true)
                .Verifiable();
        }
        else
        {
            MockTeamStore
                .Setup(x => x.DisableRetiredAsync(TeamId, 9, cancellationToken))
                .ReturnsAsync(true)
                .Verifiable();
        }

        SetupSave(cancellationToken);
        MockMediator
            .Setup(x =>
                x.SendAsync(
                    It.Is<TeamChangedCommand>(command => command.TeamId == TeamId),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(() => calls.Add("command"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);
        MockTeamRootKeyMirror
            .Setup(x => x.MirrorAsync(TeamId, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("mirror"))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

        await Applier.RetireAsync(TeamId, 9, cancellationToken);

        Assert.Equal(["command", "mirror"], calls);
    }

    [Fact]
    public async Task RetireAsync_WhenTheRowCarriesTheRetirementsSequence_ChangesNothing()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync(TeamId, cancellationToken))
            .ReturnsAsync(CreateStoredTeam(directorySequence: 9))
            .Verifiable();

        await Applier.RetireAsync(TeamId, 9, cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task RetireAsync_WhenTheStoreDoesNotDisableTheTeam_ChangesNothing()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync(TeamId, cancellationToken))
            .ReturnsAsync(CreateStoredTeam())
            .Verifiable();
        MockTeamStore
            .Setup(x => x.HasDependentsAsync(TeamId, cancellationToken))
            .ReturnsAsync(true)
            .Verifiable();
        MockTeamStore
            .Setup(x => x.DisableRetiredAsync(TeamId, 9, cancellationToken))
            .ReturnsAsync(false)
            .Verifiable();

        await Applier.RetireAsync(TeamId, 9, cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task RetireAsync_WhenSomethingNamesTheTeam_DisablesItAndLogsAWarning()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync(TeamId, cancellationToken))
            .ReturnsAsync(CreateStoredTeam())
            .Verifiable();
        MockTeamStore
            .Setup(x => x.HasDependentsAsync(TeamId, cancellationToken))
            .ReturnsAsync(true)
            .Verifiable();
        MockTeamStore
            .Setup(x => x.DisableRetiredAsync(TeamId, 9, cancellationToken))
            .ReturnsAsync(true)
            .Verifiable();
        SetupSave(cancellationToken);
        SetupCommand(settingsChanged: false);
        SetupMirrorRootKey();

        await Applier.RetireAsync(TeamId, 9, cancellationToken);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryRetiredTeamHasDependents, record.Id.Id);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "Sequence", Value: "9" });
    }

    [Fact]
    public async Task RetireAsync_WhenTeamChangedCommandFails_LogsWarningAndReturns()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync(TeamId, cancellationToken))
            .ReturnsAsync(CreateStoredTeam())
            .Verifiable();
        MockTeamStore
            .Setup(x => x.HasDependentsAsync(TeamId, cancellationToken))
            .ReturnsAsync(false)
            .Verifiable();
        MockTeamStore
            .Setup(x => x.RemoveAsync(TeamId, cancellationToken))
            .ReturnsAsync(true)
            .Verifiable();
        SetupSave(cancellationToken);
        SetupFailingCommand(new InvalidOperationException("The mediator is unavailable."));
        SetupMirrorRootKey();

        await Applier.RetireAsync(TeamId, 9, cancellationToken);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamChangeNotSent, record.Id.Id);
        Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
    }

    #endregion

    private static TeamDocument CreateDocument(string settingsJson, long sequence) =>
        new()
        {
            TeamId = TeamId,
            DisplayName = "Acme",
            IsDisabled = false,
            Settings = JsonElement.Parse(settingsJson),
            RootKeys = ImmutableDictionary<string, TeamRootKeyReference>.Empty,
            Sequence = sequence,
        };

    private static PersistedTeam CreateRootKeyTeam(
        string teamId,
        string? keyReference = null,
        bool isDisabled = false
    ) =>
        new()
        {
            TeamId = teamId,
            ConcurrencyToken = "token-1",
            IsDisabled = isDisabled,
            DisplayName = teamId,
            RootKeys = keyReference is null
                ? new Dictionary<string, OwnerRootKeyRequest>()
                : new Dictionary<string, OwnerRootKeyRequest>
                {
                    [Region] = new("aws-kms", keyReference) { AccessIdentity = AccessIdentityA },
                },
            Settings = new PersistedTeamSettings
            {
                TeamId = teamId,
                ConcurrencyToken = "settings-token-1",
                Value = JsonElement.Parse("{}"),
            },
        };

    private static PersistedTeam CreateStoredTeam(long directorySequence = 0) =>
        new()
        {
            TeamId = TeamId,
            ConcurrencyToken = "token-1",
            IsDisabled = false,
            DisplayName = "Acme",
            DirectorySequence = directorySequence,
            Settings = new PersistedTeamSettings
            {
                TeamId = TeamId,
                ConcurrencyToken = "settings-token-1",
                Value = JsonElement.Parse("{}"),
            },
        };

    private void SetupApplied(CancellationToken cancellationToken)
    {
        SetupUnscoped();
        SetupStoreManager();
        SetupStoredSettings("""{"flag":true}""");
        SetupMirror(isApplied: true, cancellationToken);
        SetupSave(cancellationToken);
        SetupCommand(settingsChanged: false);
    }

    // The root key mirror follows a committed write, so the applier calls it once, after the command.
    private void SetupMirrorRootKey() =>
        MockTeamRootKeyMirror
            .Setup(x => x.MirrorAsync(TeamId, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable(Times.Once);

    private void SetupUnscoped() =>
        MockAmbientTenantAccessor
            .Setup(x => x.BeginUnscoped(UnscopedReason))
            .Returns(CreateLooseMock<IDisposable>().Object)
            .Verifiable();

    private void SetupStoreManager()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<ITeamStore>())
            .Returns(MockTeamStore.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
    }

    private void SetupStoredSettings(string settingsJson) =>
        MockTeamStore
            .Setup(x => x.GetSettingsOrDefaultAsync(TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new PersistedTeamSettings
                {
                    TeamId = TeamId,
                    ConcurrencyToken = "settings-token-1",
                    Value = JsonElement.Parse(settingsJson),
                }
            )
            .Verifiable();

    private void SetupMirror(bool isApplied, CancellationToken cancellationToken) =>
        MockTeamStore
            .Setup(x =>
                x.MirrorAsync(
                    It.Is<PersistedTeam>(team =>
                        team.TeamId == TeamId && !team.IsDisabled && team.DisplayName == "Acme"
                    ),
                    cancellationToken
                )
            )
            .ReturnsAsync(isApplied)
            .Verifiable();

    private void SetupSave(CancellationToken cancellationToken) =>
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(cancellationToken))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    // The command follows a committed write, so it runs on its own bounded token, never the caller's.
    private void SetupCommand(bool settingsChanged) =>
        MockMediator
            .Setup(x =>
                x.SendAsync(
                    It.Is<TeamChangedCommand>(command =>
                        command.TeamId == TeamId && command.SettingsChanged == settingsChanged
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupFailingCommand(Exception exception) =>
        MockMediator
            .Setup(x =>
                x.SendAsync(
                    It.Is<TeamChangedCommand>(command => command.TeamId == TeamId),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(exception)
            .Verifiable();
}
