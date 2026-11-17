// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Logging;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Teams;
using NCode.Identity.Persistence;
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
            TimeProvider,
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
        SetupRootKey(current: null);

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
        var document = CreateDocument("""{"flag":true}""", sequence: 8) with { IsDisabled = true };

        await Applier.ApplyAsync(document, CancellationToken.None);

        Assert.NotNull(mirrored);
        Assert.True(mirrored.IsDisabled);
    }

    [Fact]
    public async Task ApplyAsync_WhenTheDocumentIsDisabled_LeavesTheOwnerRootKey()
    {
        SetupUnscoped();
        SetupStoreManager();
        SetupStoredSettings("""{"flag":true}""");
        MockTeamStore
            .Setup(x =>
                x.MirrorAsync(
                    It.Is<PersistedTeam>(team => team.TeamId == TeamId && team.IsDisabled),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true)
            .Verifiable();
        SetupSave(It.IsAny<CancellationToken>());
        SetupCommand(settingsChanged: false);
        var document = CreateRootKeyDocument("kms://key-a", AccessIdentityA) with
        {
            IsDisabled = true,
        };

        // Strict mocks: no owner root key call is set up, so any would fail the test.
        await Applier.ApplyAsync(document, CancellationToken.None);

        Assert.Empty(Logger.Collector.GetSnapshot());
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
        SetupRootKey(current: null);
        SetupSetRootKey("aws-kms", "kms://key-a", AccessIdentityA);

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
        SetupSetRootKey("aws-kms", "kms://key-b", AccessIdentityA);

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
        SetupSetRootKey("aws-kms", "kms://key-a", null);

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
        SetupRootKey(CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA));
        MockOwnerRootKeyManager
            .Setup(x => x.RemoveAsync(TeamOwner, It.IsAny<CancellationToken>()))
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
        SetupSetRootKey("aws-kms", "kms://key-a", AccessIdentityA);

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

    [Fact]
    public async Task ApplyAsync_WhenTheCallersTokenIsCancelledAndTheRootKeyMirrorFails_LogsAndReturns()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupApplied(cancellationToken);
        MockOwnerRootKeyManager
            .Setup(x => x.GetAsync(TeamOwner, It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new InvalidOperationException("The key service is unavailable."))
            .Verifiable();

        await Applier.ApplyAsync(
            CreateRootKeyDocument("kms://key-a", AccessIdentityA),
            cancellationToken
        );

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsType<InvalidOperationException>(record.Exception);
    }

    [Fact]
    public async Task ApplyAsync_WhenTheCallersTokenIsCancelledAfterTheCommit_MirrorsTheRootKeyOnATokenOfItsOwn()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        SetupStoredSettings("""{"flag":true}""");
        SetupMirror(isApplied: true, cancellationToken);
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(cancellationToken))
            .Callback(() => cts.Cancel())
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupCommand(settingsChanged: false);
        MockOwnerRootKeyManager
            .Setup(x =>
                x.GetAsync(
                    TeamOwner,
                    It.Is<CancellationToken>(x => x.CanBeCanceled && !x.IsCancellationRequested)
                )
            )
            .ReturnsAsync((OwnerRootKeyInfo?)null)
            .Verifiable();
        MockOwnerRootKeyManager
            .Setup(x =>
                x.SetAsync(
                    TeamOwner,
                    new OwnerRootKeyRequest("aws-kms", "kms://key-a")
                    {
                        AccessIdentity = AccessIdentityA,
                    },
                    It.Is<CancellationToken>(x => x.CanBeCanceled && !x.IsCancellationRequested)
                )
            )
            .ReturnsAsync(CreateRootKeyChange("aws-kms", "kms://key-a", AccessIdentityA))
            .Verifiable();

        await Applier.ApplyAsync(
            CreateRootKeyDocument("kms://key-a", AccessIdentityA),
            cancellationToken
        );

        Assert.True(cancellationToken.IsCancellationRequested);
        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ApplyAsync_WhenSettingTheRootKeyOutlastsThePostCommitBound_LogsAWarningAndReturns()
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
            .Returns(
                (SecretOwner _, OwnerRootKeyRequest _, CancellationToken x) =>
                {
                    TimeProvider.Advance(PostCommitWork.Timeout);
                    return x.IsCancellationRequested
                        ? ValueTask.FromCanceled<OwnerRootKeyChange>(x)
                        : ValueTask.FromResult(
                            CreateRootKeyChange("aws-kms", "kms://key-a", AccessIdentityA)
                        );
                }
            )
            .Verifiable();

        await Applier.ApplyAsync(
            CreateRootKeyDocument("kms://key-a", AccessIdentityA),
            CancellationToken.None
        );

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsAssignableFrom<OperationCanceledException>(record.Exception);
    }

    [Fact]
    public async Task ApplyAsync_WhenTheBoundCutsTheMirrorAfterTheCallersTokenIsCancelled_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupApplied(cancellationToken);
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
            .Returns(
                (SecretOwner _, OwnerRootKeyRequest _, CancellationToken x) =>
                {
                    cts.Cancel();
                    TimeProvider.Advance(PostCommitWork.Timeout);
                    return x.IsCancellationRequested
                        ? ValueTask.FromCanceled<OwnerRootKeyChange>(x)
                        : ValueTask.FromResult(
                            CreateRootKeyChange("aws-kms", "kms://key-a", AccessIdentityA)
                        );
                }
            )
            .Verifiable();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Applier.ApplyAsync(
                CreateRootKeyDocument("kms://key-a", AccessIdentityA),
                cancellationToken
            )
        );

        Assert.Empty(Logger.Collector.GetSnapshot());
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
        SetupGetManyRootKeys(
            [TeamId],
            [CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA)],
            cancellationToken
        );
        SetupRootKeyPage(cursor: null, nextCursor: null, [], cancellationToken);
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
        SetupGetManyRootKeys(
            [TeamId],
            [CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA)],
            cancellationToken
        );
        SetupRootKeyPage(cursor: null, nextCursor: null, [], cancellationToken);
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
        SetupGetManyRootKeys(
            [TeamId],
            [CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA)],
            cancellationToken
        );
        SetupGetManyRootKeys(["globex"], [], cancellationToken);
        SetupRootKeyPage(cursor: null, nextCursor: null, [], cancellationToken);

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
        SetupRootKeyPage(cursor: null, nextCursor: null, [], cancellationToken);

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
        SetupGetManyRootKeys([TeamId, "globex"], [], cancellationToken);
        SetupRootKeyPage(cursor: null, nextCursor: null, [], cancellationToken);
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
        SetupGetManyRootKeys([TeamId, "globex"], [], cancellationToken);
        SetupRootKeyPage(cursor: null, nextCursor: null, [], cancellationToken);
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
            .ThrowsAsync(new InvalidOperationException("The key service is unavailable."))
            .Verifiable();
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

    [Fact]
    public async Task ReconcileRootKeysAsync_WhenAPageHasThreeTeams_ReadsTheirKeysOnce()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        string[] teamIds = [TeamId, "globex", "initech"];
        SetupUnscoped();
        SetupStoreManager();
        SetupTeamPage(
            cursor: null,
            nextCursor: null,
            [.. teamIds.Select(id => CreateRootKeyTeam(id, "kms://key-a"))],
            cancellationToken
        );
        SetupGetManyRootKeys(
            teamIds,
            [
                .. teamIds.Select(id =>
                    CreateRootKeyInfo(
                        "aws-kms",
                        "kms://key-a",
                        AccessIdentityA,
                        new SecretOwner(SecretOwnerTypes.Team, id).Normalize()
                    )
                ),
            ],
            cancellationToken
        );
        SetupRootKeyPage(cursor: null, nextCursor: null, [], cancellationToken);

        await Applier.ReconcileRootKeysAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
        MockOwnerRootKeyManager.Verify(
            x =>
                x.GetManyAsync(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task ReconcileRootKeysAsync_WhenAKeysTeamIsGone_RemovesTheKey()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        var goneOwner = new SecretOwner(SecretOwnerTypes.Team, "globex").Normalize();
        SetupUnscoped();
        SetupStoreManager();
        SetupTeamPage(cursor: null, nextCursor: null, [], cancellationToken);
        SetupRootKeyPage(
            cursor: null,
            nextCursor: null,
            [CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA, goneOwner)],
            cancellationToken
        );
        SetupExistingTeams(["globex"], [], cancellationToken);
        SetupRemoveRootKey(goneOwner, cancellationToken);

        await Applier.ReconcileRootKeysAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ReconcileRootKeysAsync_WhenAKeysTeamStillHasARow_LeavesTheKey()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        var owner = new SecretOwner(SecretOwnerTypes.Team, "globex").Normalize();
        SetupUnscoped();
        SetupStoreManager();
        SetupTeamPage(cursor: null, nextCursor: null, [], cancellationToken);
        SetupRootKeyPage(
            cursor: null,
            nextCursor: null,
            [CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA, owner)],
            cancellationToken
        );

        // The stored row's identifier differs in case from the normalized owner's.
        SetupExistingTeams(["globex"], ["Globex"], cancellationToken);

        // Strict mocks: no RemoveAsync is set up, so a removal would fail the test.
        await Applier.ReconcileRootKeysAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ReconcileRootKeysAsync_WhenOneKeysTeamIsGoneAndAnothersExists_RemovesOnlyTheGoneTeamsKey()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        var goneOwner = new SecretOwner(SecretOwnerTypes.Team, "globex").Normalize();
        var existingOwner = new SecretOwner(SecretOwnerTypes.Team, "initech").Normalize();
        SetupUnscoped();
        SetupStoreManager();
        SetupTeamPage(cursor: null, nextCursor: null, [], cancellationToken);
        SetupRootKeyPage(
            cursor: null,
            nextCursor: null,
            [
                CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA, goneOwner),
                CreateRootKeyInfo("aws-kms", "kms://key-b", AccessIdentityA, existingOwner),
            ],
            cancellationToken
        );
        SetupExistingTeams(["globex", "initech"], ["initech"], cancellationToken);
        SetupRemoveRootKey(goneOwner, cancellationToken);

        await Applier.ReconcileRootKeysAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ReconcileRootKeysAsync_WhenAGoneTeamsKeyIsBeingRemoved_LeavesIt()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        var goneOwner = new SecretOwner(SecretOwnerTypes.Team, "globex").Normalize();
        SetupUnscoped();
        SetupStoreManager();
        SetupTeamPage(cursor: null, nextCursor: null, [], cancellationToken);
        SetupRootKeyPage(
            cursor: null,
            nextCursor: null,
            [
                CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA, goneOwner) with
                {
                    State = OwnerRootKeyStates.Removing,
                },
            ],
            cancellationToken
        );
        SetupExistingTeams(["globex"], [], cancellationToken);

        // Strict mocks: no RemoveAsync is set up, so a removal would fail the test.
        await Applier.ReconcileRootKeysAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ReconcileRootKeysAsync_WhenRemovingAGoneTeamsKeyFails_LogsAWarningAndRemovesTheNextKey()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        var firstOwner = new SecretOwner(SecretOwnerTypes.Team, "globex").Normalize();
        var secondOwner = new SecretOwner(SecretOwnerTypes.Team, "initech").Normalize();
        SetupUnscoped();
        SetupStoreManager();
        SetupTeamPage(cursor: null, nextCursor: null, [], cancellationToken);
        SetupRootKeyPage(
            cursor: null,
            nextCursor: null,
            [
                CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA, firstOwner),
                CreateRootKeyInfo("aws-kms", "kms://key-b", AccessIdentityA, secondOwner),
            ],
            cancellationToken
        );
        SetupExistingTeams(["globex", "initech"], [], cancellationToken);
        MockOwnerRootKeyManager
            .Setup(x => x.RemoveAsync(firstOwner, cancellationToken))
            .ThrowsAsync(new InvalidOperationException("The key service is unavailable."))
            .Verifiable();
        SetupRemoveRootKey(secondOwner, cancellationToken);

        await Applier.ReconcileRootKeysAsync(cancellationToken);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: "globex" });
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "Region", Value: Region });
    }

    [Fact]
    public async Task ReconcileRootKeysAsync_WhenTheBatchReadFails_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        SetupStoreManager();
        SetupTeamPage(
            cursor: null,
            nextCursor: null,
            [CreateRootKeyTeam(TeamId, "kms://key-a")],
            cancellationToken
        );
        MockOwnerRootKeyManager
            .Setup(x =>
                x.GetManyAsync(
                    SecretOwnerTypes.Team,
                    It.IsAny<IReadOnlyCollection<string>>(),
                    cancellationToken
                )
            )
            .ThrowsAsync(new InvalidOperationException("The key service is unavailable."))
            .Verifiable();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Applier.ReconcileRootKeysAsync(cancellationToken)
        );

        Assert.Empty(Logger.Collector.GetSnapshot());
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
        SetupRootKey(current: null);

        await Applier.RetireAsync(TeamId, 9, cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task RetireAsync_WhenNothingNamesTheTeam_RemovesTheOwnerRootKey()
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
        SetupRootKey(CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA));
        MockOwnerRootKeyManager
            .Setup(x => x.RemoveAsync(TeamOwner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new OwnerRootKeyRemoval(null, true, new MasterKeyRewrapResult(0, 0, 0, 0))
            )
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
        SetupRootKey(current: null);

        await Applier.RetireAsync(TeamId, 9, cancellationToken);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamChangeNotSent, record.Id.Id);
        Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
    }

    [Fact]
    public async Task RetireAsync_WhenRemovingTheRootKeyOutlastsThePostCommitBound_LogsAWarningAndReturns()
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
        SetupRootKey(CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA));
        MockOwnerRootKeyManager
            .Setup(x => x.RemoveAsync(TeamOwner, It.IsAny<CancellationToken>()))
            .Returns(
                (SecretOwner _, CancellationToken x) =>
                {
                    TimeProvider.Advance(PostCommitWork.Timeout);
                    return x.IsCancellationRequested
                        ? ValueTask.FromCanceled<OwnerRootKeyRemoval>(x)
                        : ValueTask.FromResult(
                            new OwnerRootKeyRemoval(
                                null,
                                true,
                                new MasterKeyRewrapResult(0, 0, 0, 0)
                            )
                        );
                }
            )
            .Verifiable();

        await Applier.RetireAsync(TeamId, 9, cancellationToken);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsAssignableFrom<OperationCanceledException>(record.Exception);
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
        string? accessIdentity,
        SecretOwner? owner = null
    ) =>
        new(
            owner ?? TeamOwner,
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

    private static OwnerRootKeyChange CreateRootKeyChange(
        string keyWrapperName,
        string keyReference,
        string? accessIdentity
    ) =>
        new(
            CreateRootKeyInfo(keyWrapperName, keyReference, accessIdentity),
            null,
            null,
            new MasterKeyRewrapResult(0, 0, 0, 0)
        );

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

    private void SetupGetManyRootKeys(
        string[] teamIds,
        OwnerRootKeyInfo[] keys,
        CancellationToken cancellationToken
    ) =>
        MockOwnerRootKeyManager
            .Setup(x =>
                x.GetManyAsync(
                    SecretOwnerTypes.Team,
                    It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(teamIds)),
                    cancellationToken
                )
            )
            .ReturnsAsync((IReadOnlyList<OwnerRootKeyInfo>)keys)
            .Verifiable();

    private void SetupRootKeyPage(
        string? cursor,
        string? nextCursor,
        OwnerRootKeyInfo[] keys,
        CancellationToken cancellationToken
    ) =>
        MockOwnerRootKeyManager
            .Setup(x =>
                x.GetPageAsync(
                    SecretOwnerTypes.Team,
                    It.Is<PageRequest>(page => page.Cursor == cursor),
                    cancellationToken
                )
            )
            .ReturnsAsync(
                new PagedResult<OwnerRootKeyInfo> { Items = keys, NextCursor = nextCursor }
            )
            .Verifiable();

    private void SetupExistingTeams(
        string[] teamIds,
        string[] existingTeamIds,
        CancellationToken cancellationToken
    ) =>
        MockTeamStore
            .Setup(x =>
                x.GetConcurrencyTokensAsync(
                    It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(teamIds)),
                    cancellationToken
                )
            )
            .ReturnsAsync(
                (IReadOnlyDictionary<string, string>)
                    existingTeamIds.ToDictionary(id => id, _ => "token-1")
            )
            .Verifiable();

    private void SetupRemoveRootKey(SecretOwner owner, CancellationToken cancellationToken) =>
        MockOwnerRootKeyManager
            .Setup(x => x.RemoveAsync(owner, cancellationToken))
            .ReturnsAsync(
                new OwnerRootKeyRemoval(null, true, new MasterKeyRewrapResult(0, 0, 0, 0))
            )
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

    // The mirror follows a committed write, so it runs on its own bounded token, never the caller's.
    private void SetupRootKey(OwnerRootKeyInfo? current) =>
        MockOwnerRootKeyManager
            .Setup(x => x.GetAsync(TeamOwner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(current)
            .Verifiable();

    // The reconcile follows no commit, so its calls take the caller's token.
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
            .ReturnsAsync(CreateRootKeyChange(keyWrapperName, keyReference, accessIdentity))
            .Verifiable();

    // The mirror follows a committed write, so it runs on its own bounded token, never the caller's.
    private void SetupSetRootKey(
        string keyWrapperName,
        string keyReference,
        string? accessIdentity
    ) =>
        MockOwnerRootKeyManager
            .Setup(x =>
                x.SetAsync(
                    TeamOwner,
                    new OwnerRootKeyRequest(keyWrapperName, keyReference)
                    {
                        AccessIdentity = accessIdentity,
                    },
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(CreateRootKeyChange(keyWrapperName, keyReference, accessIdentity))
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
