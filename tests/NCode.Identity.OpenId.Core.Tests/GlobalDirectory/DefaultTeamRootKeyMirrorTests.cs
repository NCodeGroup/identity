// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
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
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.Protection;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.GlobalDirectory;

public sealed class DefaultTeamRootKeyMirrorTests : BaseTests
{
    private const string TeamId = "acme";
    private const string Region = "region-a";
    private const string AccessIdentityA = "arn:aws:iam::123456789012:role/team-a";
    private const string UnscopedReason = "directory team root key mirror";

    private static SecretOwner TeamOwner { get; } =
        new SecretOwner(SecretOwnerTypes.Team, TeamId).Normalize();

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ITeamStore> MockTeamStore { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<IOwnerRootKeyManager> MockOwnerRootKeyManager { get; }
    private FakeTimeProvider TimeProvider { get; } =
        new(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
    private FakeLogger<DefaultTeamRootKeyMirror> Logger { get; } = new();
    private DefaultTeamRootKeyMirror Mirror { get; }

    public DefaultTeamRootKeyMirrorTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockTeamStore = CreateStrictMock<ITeamStore>();
        MockAmbientTenantAccessor = CreateStrictMock<IAmbientTenantAccessor>();
        MockOwnerRootKeyManager = CreateStrictMock<IOwnerRootKeyManager>();

        Mirror = new DefaultTeamRootKeyMirror(
            MockStoreManagerFactory.Object,
            MockAmbientTenantAccessor.Object,
            MockOwnerRootKeyManager.Object,
            Options.Create(new CellOptions { Region = Region }),
            TimeProvider,
            Logger
        );
    }

    #region MirrorAsync Tests

    [Fact]
    public async Task MirrorAsync_WhenTheRowsRegionKeyIsNewToTheOwner_SetsTheOwnersRootKey()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupRow(CreateRootKeyTeam(TeamId, "kms://key-a"));
        SetupRootKey(current: null);
        SetupSetRootKey("aws-kms", "kms://key-a", AccessIdentityA);

        await Mirror.MirrorAsync(TeamId, cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task MirrorAsync_WhenTheRowsRootKeyDiffers_SetsTheOwnersRootKey()
    {
        SetupRow(CreateRootKeyTeam(TeamId, "kms://key-b"));
        SetupRootKey(CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA));
        SetupSetRootKey("aws-kms", "kms://key-b", AccessIdentityA);

        await Mirror.MirrorAsync(TeamId, CancellationToken.None);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task MirrorAsync_WhenTheRowsAccessIdentityDiffers_SetsTheOwnersRootKey()
    {
        SetupRow(CreateRootKeyTeam(TeamId, "kms://key-a"));
        SetupRootKey(CreateRootKeyInfo("aws-kms", "kms://key-a", null));
        SetupSetRootKey("aws-kms", "kms://key-a", AccessIdentityA);

        await Mirror.MirrorAsync(TeamId, CancellationToken.None);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task MirrorAsync_WhenTheRowsRootKeyAgrees_CallsNothingOnTheManager()
    {
        SetupRow(CreateRootKeyTeam(TeamId, "kms://key-a"));
        SetupRootKey(CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA));

        // Strict mocks: no SetAsync or RemoveAsync is set up, so a call would fail the test.
        await Mirror.MirrorAsync(TeamId, CancellationToken.None);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task MirrorAsync_WhenTheRowHoldsNoRegionKeyAndTheOwnerHasOne_RemovesIt()
    {
        SetupRow(CreateRootKeyTeam(TeamId));
        SetupRootKey(CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA));
        SetupRemoveRootKey(TeamOwner);

        await Mirror.MirrorAsync(TeamId, CancellationToken.None);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task MirrorAsync_WhenTheRowHoldsNoRegionKeyAndTheOwnerHasNone_CallsNothingOnTheManager()
    {
        SetupRow(CreateRootKeyTeam(TeamId));
        SetupRootKey(current: null);

        // Strict mocks: no SetAsync or RemoveAsync is set up, so a call would fail the test.
        await Mirror.MirrorAsync(TeamId, CancellationToken.None);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task MirrorAsync_WhenTheRowHoldsAKeyOnlyForAnotherRegion_RemovesTheOwnersRootKey()
    {
        SetupRow(CreateRootKeyTeam(TeamId, "kms://key-a", region: "region-b"));
        SetupRootKey(CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA));
        SetupRemoveRootKey(TeamOwner);

        await Mirror.MirrorAsync(TeamId, CancellationToken.None);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task MirrorAsync_WhenTheRowIsDisabled_CallsNothingOnTheManager()
    {
        SetupRow(CreateRootKeyTeam(TeamId, "kms://key-b", isDisabled: true));

        // Strict mocks: no owner root key call is set up, so any would fail the test.
        await Mirror.MirrorAsync(TeamId, CancellationToken.None);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task MirrorAsync_WhenTheTeamHasNoRow_RemovesTheOwnersRootKey()
    {
        SetupRow(row: null);
        SetupRootKey(CreateRootKeyInfo("aws-kms", "kms://key-a", AccessIdentityA));
        SetupRemoveRootKey(TeamOwner);

        await Mirror.MirrorAsync(TeamId, CancellationToken.None);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task MirrorAsync_WhenTheRootKeyFailsItsProbe_LogsAWarningAndReturns()
    {
        SetupRow(CreateRootKeyTeam(TeamId, "kms://key-a"));
        SetupRootKey(current: null);
        SetupFailingSetRootKey(
            new OwnerRootKeyProbeException(TeamOwner, "aws-kms", "kms://key-a", "access denied")
        );

        await Mirror.MirrorAsync(TeamId, CancellationToken.None);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsType<OwnerRootKeyProbeException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "Region", Value: Region });
    }

    [Fact]
    public async Task MirrorAsync_WhenSettingTheRootKeyThrows_LogsAWarningAndReturns()
    {
        SetupRow(CreateRootKeyTeam(TeamId, "kms://key-a"));
        SetupRootKey(current: null);
        SetupFailingSetRootKey(new InvalidOperationException("The key service is unavailable."));

        await Mirror.MirrorAsync(TeamId, CancellationToken.None);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "Region", Value: Region });
    }

    [Fact]
    public async Task MirrorAsync_WhenReadingTheOwnersRootKeyFails_LogsAWarningAndReturns()
    {
        SetupRow(CreateRootKeyTeam(TeamId, "kms://key-a"));
        MockOwnerRootKeyManager
            .Setup(x => x.GetAsync(TeamOwner, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The key service is unavailable."))
            .Verifiable();

        await Mirror.MirrorAsync(TeamId, CancellationToken.None);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "Region", Value: Region });
    }

    [Fact]
    public async Task MirrorAsync_WhenReadingTheRowFails_LogsAWarningAndReturns()
    {
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync(TeamId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The store is unavailable."))
            .Verifiable();

        await Mirror.MirrorAsync(TeamId, CancellationToken.None);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
    }

    [Fact]
    public async Task MirrorAsync_WhenTheCallersTokenIsCancelledBeforeTheCall_MirrorsTheRootKeyOnATokenOfItsOwn()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var cancellationToken = cts.Token;
        SetupUnscoped();
        MockStoreManagerFactory
            .Setup(x =>
                x.CreateAsync(
                    It.Is<CancellationToken>(x => x.CanBeCanceled && !x.IsCancellationRequested)
                )
            )
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<ITeamStore>())
            .Returns(MockTeamStore.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        MockTeamStore
            .Setup(x =>
                x.GetOrDefaultAsync(
                    TeamId,
                    It.Is<CancellationToken>(x => x.CanBeCanceled && !x.IsCancellationRequested)
                )
            )
            .ReturnsAsync(CreateRootKeyTeam(TeamId, "kms://key-a"))
            .Verifiable();
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

        await Mirror.MirrorAsync(TeamId, cancellationToken);

        Assert.True(cancellationToken.IsCancellationRequested);
        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task MirrorAsync_WhenTheCallersTokenIsCancelledAndTheRootKeyFails_LogsAWarningAndReturns()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupRow(CreateRootKeyTeam(TeamId, "kms://key-a"));
        MockOwnerRootKeyManager
            .Setup(x => x.GetAsync(TeamOwner, It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new InvalidOperationException("The key service is unavailable."))
            .Verifiable();

        await Mirror.MirrorAsync(TeamId, cancellationToken);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsType<InvalidOperationException>(record.Exception);
    }

    [Fact]
    public async Task MirrorAsync_WhenSettingTheRootKeyOutlastsThePostCommitBound_LogsAWarningAndReturns()
    {
        SetupRow(CreateRootKeyTeam(TeamId, "kms://key-a"));
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

        await Mirror.MirrorAsync(TeamId, CancellationToken.None);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsAssignableFrom<OperationCanceledException>(record.Exception);
    }

    [Fact]
    public async Task MirrorAsync_WhenTheBoundCutsTheMirrorAfterTheCallersTokenIsCancelled_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;
        SetupRow(CreateRootKeyTeam(TeamId, "kms://key-a"));
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
            await Mirror.MirrorAsync(TeamId, cancellationToken)
        );

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    #endregion

    #region ReconcileAsync Tests

    [Fact]
    public async Task ReconcileAsync_WhenTheRowsRootKeyDiffers_SetsTheTeamsOwnerRootKey()
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

        await Mirror.ReconcileAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ReconcileAsync_WhenTheRowHoldsNoRootKeyAndTheOwnerHasOne_RemovesIt()
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
        SetupRemoveRootKey(TeamOwner, cancellationToken);

        await Mirror.ReconcileAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ReconcileAsync_WhenTheRootKeysAgree_LeavesThemAcrossPages()
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

        await Mirror.ReconcileAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ReconcileAsync_WhenTheTeamIsDisabled_ChangesNothing()
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

        await Mirror.ReconcileAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ReconcileAsync_WhenOneTeamsRootKeyFailsItsProbe_LogsAWarningAndReconcilesTheNextTeam()
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

        await Mirror.ReconcileAsync(cancellationToken);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsType<OwnerRootKeyProbeException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
    }

    [Fact]
    public async Task ReconcileAsync_WhenOneTeamThrows_LogsWarningAndReconcilesNextTeam()
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

        await Mirror.ReconcileAsync(cancellationToken);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: TeamId });
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "Region", Value: Region });
    }

    [Fact]
    public async Task ReconcileAsync_WhenAPageHasThreeTeams_ReadsTheirKeysOnce()
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

        await Mirror.ReconcileAsync(cancellationToken);

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
    public async Task ReconcileAsync_WhenAKeysTeamIsGone_RemovesTheKey()
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

        await Mirror.ReconcileAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ReconcileAsync_WhenAKeysTeamStillHasARow_LeavesTheKey()
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
        await Mirror.ReconcileAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ReconcileAsync_WhenOneKeysTeamIsGoneAndAnothersExists_RemovesOnlyTheGoneTeamsKey()
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

        await Mirror.ReconcileAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ReconcileAsync_WhenAGoneTeamsKeyIsBeingRemoved_LeavesIt()
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
        await Mirror.ReconcileAsync(cancellationToken);

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task ReconcileAsync_WhenRemovingAGoneTeamsKeyFails_LogsAWarningAndRemovesTheNextKey()
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

        await Mirror.ReconcileAsync(cancellationToken);

        var record = Assert.Single(Logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(EventIds.DirectoryTeamRootKeyNotApplied, record.Id.Id);
        Assert.IsType<InvalidOperationException>(record.Exception);
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "TeamId", Value: "globex" });
        Assert.Contains(record.StructuredState ?? [], x => x is { Key: "Region", Value: Region });
    }

    [Fact]
    public async Task ReconcileAsync_WhenTheBatchReadFails_Propagates()
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
            await Mirror.ReconcileAsync(cancellationToken)
        );

        Assert.Empty(Logger.Collector.GetSnapshot());
    }

    #endregion

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
        bool isDisabled = false,
        string region = Region
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
                    [region] = new("aws-kms", keyReference) { AccessIdentity = AccessIdentityA },
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

    // The mirror follows a committed write, so it runs on its own bounded token, never the caller's.
    private void SetupRemoveRootKey(SecretOwner owner) =>
        MockOwnerRootKeyManager
            .Setup(x => x.RemoveAsync(owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new OwnerRootKeyRemoval(null, true, new MasterKeyRewrapResult(0, 0, 0, 0))
            )
            .Verifiable();

    // The mirror follows a committed write, so it reads the row and runs on its own bounded token, never the caller's.
    private void SetupRow(PersistedTeam? row)
    {
        SetupUnscoped();
        SetupStoreManager();
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync(TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(row)
            .Verifiable();
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

    private void SetupFailingSetRootKey(Exception exception) =>
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
            .ThrowsAsync(exception)
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
}
