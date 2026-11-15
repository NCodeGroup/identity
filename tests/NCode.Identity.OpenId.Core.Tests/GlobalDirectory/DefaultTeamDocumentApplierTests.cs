// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
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

    private static SecretOwner TeamOwner { get; } =
        new SecretOwner(SecretOwnerTypes.Team, TeamId).Normalize();

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ITeamStore> MockTeamStore { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<IOwnerRootKeyManager> MockOwnerRootKeyManager { get; }
    private Mock<IMediator> MockMediator { get; }
    private FakeLogger<DefaultTeamDocumentApplier> Logger { get; } = new();
    private ServiceProvider ServiceProvider { get; }
    private DefaultTeamDocumentApplier Applier { get; }

    public DefaultTeamDocumentApplierTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockTeamStore = CreateStrictMock<ITeamStore>();
        MockAmbientTenantAccessor = CreateStrictMock<IAmbientTenantAccessor>();
        MockOwnerRootKeyManager = CreateStrictMock<IOwnerRootKeyManager>();
        MockMediator = CreateStrictMock<IMediator>();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped(_ => MockMediator.Object);
        ServiceProvider = serviceCollection.BuildServiceProvider();

        Applier = new DefaultTeamDocumentApplier(
            MockStoreManagerFactory.Object,
            MockAmbientTenantAccessor.Object,
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            MockOwnerRootKeyManager.Object,
            Options.Create(new CellOptions { Region = Region }),
            TimeProvider.System,
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
        SetupRootKey(current: null, cancellationToken);

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
        SetupRootKey(current: null);

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
        SetupRootKey(current: null);

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
        SetupRootKey(current: CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA));
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
        SetupRootKey(current: null);
        var document = CreateDocument("""{"flag":true}""", sequence: 8) with { IsDisabled = true };

        await Applier.ApplyAsync(document, CancellationToken.None);

        Assert.NotNull(mirrored);
        Assert.True(mirrored.IsDisabled);
    }

    [Fact]
    public async Task ApplyAsync_WhenTheSequenceIsStale_DoesNotSaveOrSendTheCommand()
    {
        SetupUnscoped();
        SetupStoreManager();
        SetupStoredSettings("""{"flag":false}""");
        SetupMirror(isApplied: false, It.IsAny<CancellationToken>());

        // Strict mocks: neither a save, a command nor a root key call is set up, so any would fail the test.
        await Applier.ApplyAsync(
            CreateDocument("""{"flag":true}""", sequence: 3),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenTheRegionHasANewRootKey_SetsTheTeamsOwnerRootKey()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupApplied(cancellationToken);
        SetupRootKey(current: null, cancellationToken);
        SetupSetRootKey("aws-kms", "kms://key-a", AccessIdentityA, cancellationToken);

        await Applier.ApplyAsync(
            CreateRootKeyDocument("kms://key-a", AccessIdentityA),
            cancellationToken
        );

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ApplyAsync_WhenTheRegionsRootKeyDiffers_SetsTheTeamsOwnerRootKey()
    {
        SetupApplied(It.IsAny<CancellationToken>());
        SetupRootKey(CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA));
        SetupSetRootKey("aws-kms", "kms://key-b", AccessIdentityA, It.IsAny<CancellationToken>());

        await Applier.ApplyAsync(
            CreateRootKeyDocument("kms://key-b", AccessIdentityA),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenTheRegionsAccessIdentityDiffers_SetsTheTeamsOwnerRootKey()
    {
        SetupApplied(It.IsAny<CancellationToken>());
        SetupRootKey(CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA));
        SetupSetRootKey("aws-kms", "kms://key-a", null, It.IsAny<CancellationToken>());

        await Applier.ApplyAsync(
            CreateRootKeyDocument("kms://key-a", null),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenTheRegionsRootKeyIsUnchanged_CallsNothingOnTheManager()
    {
        SetupApplied(It.IsAny<CancellationToken>());
        SetupRootKey(CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA));

        await Applier.ApplyAsync(
            CreateRootKeyDocument("kms://key-a", AccessIdentityA),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task ApplyAsync_WhenTheRegionHasNoRootKeyAndTheCellHoldsOne_RemovesIt()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupApplied(cancellationToken);
        SetupRootKey(
            CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA),
            cancellationToken
        );
        MockOwnerRootKeyManager
            .Setup(x => x.RemoveAsync(TeamOwner, cancellationToken))
            .ReturnsAsync(
                new OwnerRootKeyRemoval(null, true, new MasterKeyRewrapResult(0, 0, 0, 0))
            )
            .Verifiable();

        await Applier.ApplyAsync(
            CreateDocument("""{"flag":true}""", sequence: 8),
            cancellationToken
        );

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ApplyAsync_WhenTheRootKeyFailsItsProbe_LogsAWarningAndStillAppliesTheTeam()
    {
        SetupApplied(It.IsAny<CancellationToken>());
        SetupRootKey(current: null);
        MockOwnerRootKeyManager
            .Setup(x =>
                x.SetAsync(
                    TeamOwner,
                    new OwnerRootKeyRequest("aws-kms", "kms://key-a")
                    {
                        AccessIdentity = AccessIdentityA,
                    },
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(
                new OwnerRootKeyProbeException(TeamOwner, "aws-kms", "kms://key-a", "access denied")
            )
            .Verifiable();

        await Applier.ApplyAsync(
            CreateRootKeyDocument("kms://key-a", AccessIdentityA),
            CancellationToken.None
        );

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsType<OwnerRootKeyProbeException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "Region", Value: Region });
    }

    [Fact]
    public async Task ApplyAsync_WhenTeamChangedCommandFails_LogsWarningAndStillMirrorsRootKey()
    {
        SetupUnscoped();
        SetupStoreManager();
        SetupStoredSettings("""{"flag":true}""");
        SetupMirror(isApplied: true, It.IsAny<CancellationToken>());
        SetupSave(It.IsAny<CancellationToken>());
        SetupFailingCommand(new InvalidOperationException("The mediator is unavailable."));
        SetupRootKey(current: null);
        SetupSetRootKey("aws-kms", "kms://key-a", AccessIdentityA, It.IsAny<CancellationToken>());

        await Applier.ApplyAsync(
            CreateRootKeyDocument("kms://key-a", AccessIdentityA),
            CancellationToken.None
        );

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamChangeNotSent, record.Id.Id);
        Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
    }

    [Fact]
    public async Task ApplyAsync_WhenRootKeyMirrorFails_LogsWarningAndReturns()
    {
        SetupApplied(It.IsAny<CancellationToken>());
        MockOwnerRootKeyManager
            .Setup(x => x.GetAsync(TeamOwner, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The key service is unavailable."))
            .Verifiable();

        await Applier.ApplyAsync(
            CreateRootKeyDocument("kms://key-a", AccessIdentityA),
            CancellationToken.None
        );

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "Region", Value: Region });
    }

    #endregion

    #region ReconcileRootKeysAsync Tests

    [Fact]
    public async Task ReconcileRootKeysAsync_WhenTheRowsRootKeyDiffers_SetsTheTeamsOwnerRootKey()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        SetupTeamPage(
            cursor: null,
            nextCursor: null,
            [CreateRootKeyTeam(TeamId, "kms://key-b")],
            cancellationToken
        );
        SetupGetRootKey(
            TeamOwner,
            CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA),
            cancellationToken
        );
        SetupSetRootKey("aws-kms", "kms://key-b", AccessIdentityA, cancellationToken);

        await Applier.ReconcileRootKeysAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ReconcileRootKeysAsync_WhenTheRowHoldsNoRootKeyAndTheOwnerHasOne_RemovesIt()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        SetupTeamPage(
            cursor: null,
            nextCursor: null,
            [CreateRootKeyTeam(TeamId)],
            cancellationToken
        );
        SetupGetRootKey(
            TeamOwner,
            CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA),
            cancellationToken
        );
        MockOwnerRootKeyManager
            .Setup(x => x.RemoveAsync(TeamOwner, cancellationToken))
            .ReturnsAsync(
                new OwnerRootKeyRemoval(null, true, new MasterKeyRewrapResult(0, 0, 0, 0))
            )
            .Verifiable();

        await Applier.ReconcileRootKeysAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ReconcileRootKeysAsync_WhenTheRootKeysAgree_LeavesThemAcrossPages()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        var otherOwner = new SecretOwner(SecretOwnerTypes.Team, "globex").Normalize();
        SetupUnscoped();
        SetupStoreManager();
        SetupTeamPage(
            cursor: null,
            nextCursor: "next",
            [CreateRootKeyTeam(TeamId, "kms://key-a")],
            cancellationToken
        );
        SetupTeamPage(
            cursor: "next",
            nextCursor: null,
            [CreateRootKeyTeam("globex")],
            cancellationToken
        );
        SetupGetRootKey(
            TeamOwner,
            CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA),
            cancellationToken
        );
        SetupGetRootKey(otherOwner, current: null, cancellationToken);

        await Applier.ReconcileRootKeysAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ReconcileRootKeysAsync_WhenTheTeamIsDisabled_ChangesNothing()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        SetupTeamPage(
            cursor: null,
            nextCursor: null,
            [CreateRootKeyTeam(TeamId, "kms://key-b", isDisabled: true)],
            cancellationToken
        );

        await Applier.ReconcileRootKeysAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ReconcileRootKeysAsync_WhenOneTeamsRootKeyFailsItsProbe_LogsAWarningAndReconcilesTheNextTeam()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        var otherOwner = new SecretOwner(SecretOwnerTypes.Team, "globex").Normalize();
        SetupUnscoped();
        SetupStoreManager();
        SetupTeamPage(
            cursor: null,
            nextCursor: null,
            [CreateRootKeyTeam(TeamId, "kms://key-a"), CreateRootKeyTeam("globex", "kms://key-b")],
            cancellationToken
        );
        SetupGetRootKey(TeamOwner, current: null, cancellationToken);
        MockOwnerRootKeyManager
            .Setup(x =>
                x.SetAsync(
                    TeamOwner,
                    new OwnerRootKeyRequest("aws-kms", "kms://key-a")
                    {
                        AccessIdentity = AccessIdentityA,
                    },
                    cancellationToken
                )
            )
            .ThrowsAsync(
                new OwnerRootKeyProbeException(TeamOwner, "aws-kms", "kms://key-a", "access denied")
            )
            .Verifiable();
        SetupGetRootKey(otherOwner, current: null, cancellationToken);
        MockOwnerRootKeyManager
            .Setup(x =>
                x.SetAsync(
                    otherOwner,
                    new OwnerRootKeyRequest("aws-kms", "kms://key-b")
                    {
                        AccessIdentity = AccessIdentityA,
                    },
                    cancellationToken
                )
            )
            .ReturnsAsync(
                new OwnerRootKeyChange(
                    CreateRootKeyInfo("aws-kms", "kms://key-b", AccessIdentityA),
                    null,
                    null,
                    new MasterKeyRewrapResult(0, 0, 0, 0)
                )
            )
            .Verifiable();

        await Applier.ReconcileRootKeysAsync(cancellationToken);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsType<OwnerRootKeyProbeException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
    }

    [Fact]
    public async Task ReconcileRootKeysAsync_WhenOneTeamThrows_LogsWarningAndReconcilesNextTeam()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        var otherOwner = new SecretOwner(SecretOwnerTypes.Team, "globex").Normalize();
        SetupUnscoped();
        SetupStoreManager();
        SetupTeamPage(
            cursor: null,
            nextCursor: null,
            [CreateRootKeyTeam(TeamId, "kms://key-a"), CreateRootKeyTeam("globex", "kms://key-b")],
            cancellationToken
        );
        MockOwnerRootKeyManager
            .Setup(x => x.GetAsync(TeamOwner, cancellationToken))
            .ThrowsAsync(new InvalidOperationException("The key service is unavailable."))
            .Verifiable();
        SetupGetRootKey(otherOwner, current: null, cancellationToken);
        MockOwnerRootKeyManager
            .Setup(x =>
                x.SetAsync(
                    otherOwner,
                    new OwnerRootKeyRequest("aws-kms", "kms://key-b")
                    {
                        AccessIdentity = AccessIdentityA,
                    },
                    cancellationToken
                )
            )
            .ReturnsAsync(
                new OwnerRootKeyChange(
                    CreateRootKeyInfo("aws-kms", "kms://key-b", AccessIdentityA),
                    null,
                    null,
                    new MasterKeyRewrapResult(0, 0, 0, 0)
                )
            )
            .Verifiable();

        await Applier.ReconcileRootKeysAsync(cancellationToken);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "Region", Value: Region });
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
            .Setup(x =>
                x.UpdateAsync(
                    It.Is<PersistedTeam>(team => team.TeamId == TeamId && team.IsDisabled),
                    cancellationToken
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupSave(cancellationToken);
        SetupCommand(settingsChanged: false);

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

        await Applier.RetireAsync(TeamId, 9, cancellationToken);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamChangeNotSent, record.Id.Id);
        Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
    }

    #endregion

    #region GetAppliedSequenceAsync Tests

    [Fact]
    public async Task GetAppliedSequenceAsync_ReturnsTheHighestMirroredSequence()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetMaxDirectorySequenceAsync(cancellationToken))
            .ReturnsAsync(42L)
            .Verifiable();

        var result = await Applier.GetAppliedSequenceAsync(cancellationToken);

        Assert.Equal(42L, result);
    }

    #endregion

    private static TeamDocument CreateDocument(string settingsJson, long sequence) =>
        new()
        {
            TeamId = TeamId,
            DisplayName = "Acme",
            IsDisabled = false,
            SettingsJson = settingsJson,
            Sequence = sequence,
        };

    private static TeamDocument CreateRootKeyDocument(
        string keyReference,
        string? accessIdentity
    ) =>
        CreateDocument("""{"flag":true}""", sequence: 8) with
        {
            RootKeys = ImmutableDictionary<string, TeamRootKeyReference>.Empty.Add(
                Region,
                new TeamRootKeyReference("aws-kms", keyReference)
                {
                    AccessIdentity = accessIdentity,
                }
            ),
        };

    private static OwnerRootKeyInfo CreateRootKeyInfo(
        string keyWrapperName,
        string keyReference,
        string? accessIdentity
    ) =>
        new(
            TeamOwner,
            keyWrapperName,
            keyReference,
            OwnerRootKeyStates.Active,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            null,
            null
        )
        {
            AccessIdentity = accessIdentity,
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

    private void SetupTeamPage(
        string? cursor,
        string? nextCursor,
        PersistedTeam[] teams,
        CancellationToken cancellationToken
    ) =>
        MockTeamStore
            .Setup(x =>
                x.GetPageAsync(It.Is<PageRequest>(page => page.Cursor == cursor), cancellationToken)
            )
            .ReturnsAsync(new PagedResult<PersistedTeam> { Items = teams, NextCursor = nextCursor })
            .Verifiable();

    private void SetupGetRootKey(
        SecretOwner owner,
        OwnerRootKeyInfo? current,
        CancellationToken cancellationToken
    ) =>
        MockOwnerRootKeyManager
            .Setup(x => x.GetAsync(owner, cancellationToken))
            .ReturnsAsync(current)
            .Verifiable();

    private static PersistedTeam CreateStoredTeam() =>
        new()
        {
            TeamId = TeamId,
            ConcurrencyToken = "token-1",
            IsDisabled = false,
            DisplayName = "Acme",
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

    private void SetupRootKey(OwnerRootKeyInfo? current) =>
        SetupRootKey(current, It.IsAny<CancellationToken>());

    private void SetupRootKey(OwnerRootKeyInfo? current, CancellationToken cancellationToken) =>
        MockOwnerRootKeyManager
            .Setup(x => x.GetAsync(TeamOwner, cancellationToken))
            .ReturnsAsync(current)
            .Verifiable();

    private void SetupSetRootKey(
        string keyWrapperName,
        string keyReference,
        string? accessIdentity,
        CancellationToken cancellationToken
    ) =>
        MockOwnerRootKeyManager
            .Setup(x =>
                x.SetAsync(
                    TeamOwner,
                    new OwnerRootKeyRequest(keyWrapperName, keyReference)
                    {
                        AccessIdentity = accessIdentity,
                    },
                    cancellationToken
                )
            )
            .ReturnsAsync(
                new OwnerRootKeyChange(
                    CreateRootKeyInfo(keyWrapperName, keyReference, accessIdentity),
                    null,
                    null,
                    new MasterKeyRewrapResult(0, 0, 0, 0)
                )
            )
            .Verifiable();

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
