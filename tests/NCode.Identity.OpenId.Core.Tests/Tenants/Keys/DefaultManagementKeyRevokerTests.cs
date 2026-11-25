// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCode.Identity.Events;
using NCode.Identity.OpenId.Core.Tests;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.OpenId.Tenants.Keys;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.Secrets.Persistence.DataContracts;
using NCode.Identity.Settings;
using NCode.Mediator;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tenants.Keys;

public sealed class DefaultManagementKeyRevokerTests : BaseTests
{
    private const string TenantId = "platform-tenant";
    private const string TeamId = "platform";
    private static DateTimeOffset Now { get; } = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private OpenIdOptions OpenIdOptions { get; } = new();
    private Mock<ITenantResolver> MockTenantResolver { get; }
    private Mock<ITenantKeyPolicy> MockPolicy { get; }
    private Mock<ITenantKeyRollover> MockRollover { get; }
    private Mock<IEffectiveSettingsResolver> MockEffectiveSettingsResolver { get; }
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ITenantStore> MockTenantStore { get; }
    private Mock<IReplicatedDocumentStore> MockReplicatedDocumentStore { get; }
    private Mock<IEventPublisher> MockEventPublisher { get; }
    private Mock<IRemoteSecretDestroyer> MockRemoteSecretDestroyer { get; }
    private Mock<IMediator> MockMediator { get; }
    private Mock<IReadOnlySettingCollection> MockSettings { get; }
    private List<PersistedRevokedManagementKey> AddedRevokedKeys { get; } = [];
    private List<TenantKeyAuditEvent> PublishedEvents { get; } = [];
    private TenantKeyRolloverPlan? AppliedPlan { get; set; }
    private DefaultManagementKeyRevoker Revoker { get; }

    public DefaultManagementKeyRevokerTests()
    {
        MockTenantResolver = CreateLooseMock<ITenantResolver>();
        MockTenantResolver.Setup(x => x.GetControlPlaneTenantId()).Returns(TenantId);
        MockPolicy = CreateStrictMock<ITenantKeyPolicy>();
        MockRollover = CreateStrictMock<ITenantKeyRollover>();
        MockEffectiveSettingsResolver = CreateStrictMock<IEffectiveSettingsResolver>();
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateLooseMock<IStoreManager>();
        MockTenantStore = CreateLooseMock<ITenantStore>();
        MockReplicatedDocumentStore = CreateLooseMock<IReplicatedDocumentStore>();
        MockEventPublisher = CreateLooseMock<IEventPublisher>();
        MockRemoteSecretDestroyer = CreateLooseMock<IRemoteSecretDestroyer>();
        MockMediator = CreateLooseMock<IMediator>();

        // A value-provider stub; the lifecycle settings it leaves unset read as absent.
        MockSettings = CreateLooseMock<IReadOnlySettingCollection>();
        MockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.TenantKeyLifetime))
            .Returns(TimeSpan.FromDays(90));
        MockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.TenantKeyActivationDelay))
            .Returns(TimeSpan.FromDays(2));
        MockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.TenantKeyRetirementGrace))
            .Returns(TimeSpan.FromDays(7));
        MockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.ClockSkew))
            .Returns(TimeSpan.FromMinutes(5));

        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object);
        MockStoreManager.Setup(x => x.GetStore<ITenantStore>()).Returns(MockTenantStore.Object);
        MockStoreManager
            .Setup(x => x.GetStore<IReplicatedDocumentStore>())
            .Returns(MockReplicatedDocumentStore.Object);
        MockReplicatedDocumentStore
            .Setup(x =>
                x.AddRevokedKeyAsync(
                    It.IsAny<PersistedRevokedManagementKey>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (PersistedRevokedManagementKey key, CancellationToken _) =>
                    AddedRevokedKeys.Add(key)
            )
            .Returns(ValueTask.CompletedTask);
        MockEventPublisher
            .Setup(x =>
                x.PublishAsync(It.IsAny<TenantKeyAuditEvent>(), It.IsAny<CancellationToken>())
            )
            .Callback(
                (TenantKeyAuditEvent auditEvent, CancellationToken _) =>
                    PublishedEvents.Add(auditEvent)
            )
            .Returns(ValueTask.CompletedTask);

        Revoker = new DefaultManagementKeyRevoker(
            Options.Create(OpenIdOptions),
            MockTenantResolver.Object,
            MockPolicy.Object,
            MockRollover.Object,
            MockEffectiveSettingsResolver.Object,
            MockStoreManagerFactory.Object,
            MockEventPublisher.Object,
            MockRemoteSecretDestroyer.Object,
            MockMediator.Object,
            new FakeTimeProvider(Now),
            new FakeLogger<DefaultManagementKeyRevoker>()
        );
    }

    private static PersistedSecret CreateSecret(string secretId, DateTimeOffset activatesWhen) =>
        TenantKeyTestData.CreateSecret(
            secretId,
            Now.AddDays(-100),
            activatesWhen,
            activatesWhen.AddDays(90),
            activatesWhen.AddDays(97)
        );

    private void SetupTenant(params PersistedSecret[] secrets)
    {
        var tenant = new PersistedTenantBuilder
        {
            TenantId = TenantId,
            Issuer = "https://op.example/platform",
            ConcurrencyToken = "token",
            Settings = default,
            SecretsConcurrencyToken = "token",
            Secrets = secrets,
        }.Build();
        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
    }

    private void SetupRollover(params PersistedSecret[] resulting) =>
        MockRollover
            .Setup(x =>
                x.ApplyAsync(
                    MockTenantStore.Object,
                    TenantId,
                    It.IsAny<IReadOnlyCollection<PersistedSecret>>(),
                    It.IsAny<TenantKeyRolloverPlan>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback(
                (
                    ITenantStore _,
                    string _,
                    IReadOnlyCollection<PersistedSecret> _,
                    TenantKeyRolloverPlan plan,
                    CancellationToken _
                ) => AppliedPlan = plan
            )
            .ReturnsAsync(
                new TenantKeyRolloverResult
                {
                    Secrets = resulting,
                    AuditEvents = [],
                    RemovedSecrets = [],
                }
            );

    private void SetupGeneration()
    {
        MockPolicy
            .Setup(x => x.ResolveSpecifications(OpenIdOptions.Tenant.KeyProvisioning))
            .Returns(
                new TenantKeySpecificationResolution
                {
                    Specifications = [TenantKeyTestData.Rs256],
                    Errors = [],
                }
            );
        MockPolicy
            .Setup(x =>
                x.ForTenant(It.IsAny<IReadOnlyList<TenantKeySpecification>>(), MockSettings.Object)
            )
            .Returns([TenantKeyTestData.Rs256]);
        var teamSettings = CreateStrictMock<IReadOnlySettingCollection>().Object;
        MockEffectiveSettingsResolver
            .Setup(x => x.ResolveTeamAsync(TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(teamSettings);
        MockEffectiveSettingsResolver
            .Setup(x =>
                x.Resolve(teamSettings, OpenIdSettingScopes.Tenant, It.IsAny<JsonElement>())
            )
            .Returns(MockSettings.Object);
    }

    [Fact]
    public async Task RevokeAsync_WhenASuccessorIsPending_ActivatesItAtOnce()
    {
        var revoked = CreateSecret("old", Now.AddDays(-30));
        var successor = CreateSecret("next", Now.AddDays(60));
        SetupTenant(revoked, successor);
        SetupRollover(successor);

        var result = await Revoker.RevokeAsync("old", CancellationToken.None);

        Assert.True(result.IsRevoked);
        Assert.Equal("next", result.ReplacementKeyId);
        Assert.NotNull(AppliedPlan);
        Assert.Empty(AppliedPlan.Generations);
        var activation = Assert.Single(AppliedPlan.Activations);
        Assert.Equal(successor, activation.Secret);
        Assert.Equal(Now, activation.ActivatesWhen);
        MockTenantStore.Verify(
            x => x.RemoveSecretAsync(TenantId, "old", It.IsAny<CancellationToken>()),
            Times.Once
        );
        MockStoreManager.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_WhenNoSuccessorIsPending_GeneratesOneActiveAtOnce()
    {
        var revoked = CreateSecret("old", Now.AddDays(-30));
        var generated = CreateSecret("fresh", Now);
        SetupTenant(revoked);
        SetupGeneration();
        SetupRollover(generated);

        var result = await Revoker.RevokeAsync("old", CancellationToken.None);

        Assert.True(result.IsRevoked);
        Assert.Equal("fresh", result.ReplacementKeyId);
        Assert.NotNull(AppliedPlan);
        Assert.Empty(AppliedPlan.Activations);
        var generation = Assert.Single(AppliedPlan.Generations);
        Assert.Equal(Now, generation.ActivatesWhen);
        Assert.Equal(Now.AddDays(90), generation.RetiresWhen);
        Assert.Equal("RS256", generation.Specification.Algorithm);
    }

    private async ValueTask AssertKeepsTheActiveKeyAsync(
        PersistedSecret revoked,
        IReadOnlyList<PersistedSecret> others,
        CancellationToken cancellationToken
    )
    {
        var active = CreateSecret("active", Now.AddDays(-30));
        SetupTenant([revoked, active, .. others]);
        SetupRollover([active, .. others]);

        var result = await Revoker.RevokeAsync(revoked.SecretId, cancellationToken);

        Assert.True(result.IsRevoked);
        Assert.Equal("active", result.ReplacementKeyId);
        Assert.NotNull(AppliedPlan);
        Assert.Empty(AppliedPlan.Activations);
        Assert.Empty(AppliedPlan.Generations);
        MockTenantStore.Verify(
            x => x.RemoveSecretAsync(TenantId, revoked.SecretId, It.IsAny<CancellationToken>()),
            Times.Once
        );
        var revokedKey = Assert.Single(AddedRevokedKeys);
        Assert.Equal(revoked.SecretId, revokedKey.KeyId);
        var auditEvent = Assert.Single(PublishedEvents);
        Assert.Equal(TenantKeyChanges.Revoked, auditEvent.Change);
    }

    [Fact]
    public async Task RevokeAsync_WhenTheKeyIsRetired_KeepsTheActiveKeyAndChangesNoOther()
    {
        var retired = TenantKeyTestData.CreateSecret(
            "retired",
            Now.AddDays(-100),
            Now.AddDays(-100),
            Now.AddDays(-3),
            Now.AddDays(4)
        );

        await AssertKeepsTheActiveKeyAsync(retired, [], CancellationToken.None);
    }

    [Fact]
    public async Task RevokeAsync_WhenTheKeyIsPending_KeepsTheActiveKeyAndChangesNoOther()
    {
        var pending = CreateSecret("pending", Now.AddDays(60));

        await AssertKeepsTheActiveKeyAsync(pending, [], CancellationToken.None);
    }

    [Fact]
    public async Task RevokeAsync_WhenAnotherActiveKeyRemains_KeepsItAndChangesNoOther()
    {
        var revoked = CreateSecret("old", Now.AddDays(-40));
        var pending = CreateSecret("next", Now.AddDays(60));

        await AssertKeepsTheActiveKeyAsync(revoked, [pending], CancellationToken.None);
    }

    [Fact]
    public async Task RevokeAsync_WhenTheKeyIsUnknown_ChangesNothing()
    {
        SetupTenant(CreateSecret("old", Now.AddDays(-30)));

        var result = await Revoker.RevokeAsync("missing", CancellationToken.None);

        Assert.False(result.IsRevoked);
        Assert.Null(result.ReplacementKeyId);
        Assert.Empty(AddedRevokedKeys);
        MockStoreManager.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task RevokeAsync_AddsTheRevokedKeyAndAuditsTheRevocation()
    {
        var revoked = CreateSecret("old", Now.AddDays(-30));
        var successor = CreateSecret("next", Now.AddDays(60));
        SetupTenant(revoked, successor);
        SetupRollover(successor);

        await Revoker.RevokeAsync("old", CancellationToken.None);

        var revokedKey = Assert.Single(AddedRevokedKeys);
        Assert.Equal("old", revokedKey.KeyId);
        Assert.Equal(Now, revokedKey.RevokedWhen);
        Assert.Equal(PersistedRevokedManagementKey.ControlPlaneSource, revokedKey.Source);
        var auditEvent = Assert.Single(PublishedEvents);
        Assert.Equal(TenantKeyChanges.Revoked, auditEvent.Change);
        Assert.Equal("old", auditEvent.KeyId);
        MockMediator.Verify(
            x =>
                x.SendAsync(
                    It.Is<TenantChangedCommand>(command =>
                        command.TenantId == TenantId && !command.SettingsChanged
                    ),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }
}
