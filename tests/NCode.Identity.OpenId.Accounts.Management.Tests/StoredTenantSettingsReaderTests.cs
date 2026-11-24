// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Accounts.Management;

public sealed class StoredTenantSettingsReaderTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string TeamId = "team-1";

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ITenantStore> MockTenantStore { get; }
    private Mock<IEffectiveSettingsResolver> MockEffectiveSettingsResolver { get; }
    private StoredTenantSettingsReader Reader { get; }

    public StoredTenantSettingsReaderTests()
    {
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockTenantStore = MockRepository.Create<ITenantStore>();
        MockEffectiveSettingsResolver = MockRepository.Create<IEffectiveSettingsResolver>();
        Reader = new StoredTenantSettingsReader(MockEffectiveSettingsResolver.Object);

        MockStoreManager
            .Setup(x => x.GetStore<ITenantStore>())
            .Returns(MockTenantStore.Object)
            .Verifiable();
    }

    public void Dispose() => MockRepository.Verify();

    [Fact]
    public async Task ReadAsync_MissingTenant_ReturnsNull()
    {
        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedTenant?)null)
            .Verifiable();

        var settings = await Reader.ReadAsync(
            MockStoreManager.Object,
            TenantId,
            CancellationToken.None
        );

        Assert.Null(settings);
    }

    [Fact]
    public async Task ReadAsync_ExistingTenant_ResolvesTeamSettingsUnderTenantDocument()
    {
        var tenant = new PersistedTenantBuilder { TenantId = TenantId, TeamId = TeamId }.Build();
        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant)
            .Verifiable();
        var teamSettings = MockRepository.Create<IReadOnlySettingCollection>().Object;
        var tenantSettings = MockRepository.Create<IReadOnlySettingCollection>().Object;
        MockEffectiveSettingsResolver
            .Setup(x => x.ResolveTeamAsync(TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(teamSettings)
            .Verifiable();
        MockEffectiveSettingsResolver
            .Setup(x =>
                x.ResolveStored(
                    teamSettings,
                    OpenIdSettingScopes.Tenant,
                    TenantId,
                    It.IsAny<JsonElement>()
                )
            )
            .Returns(tenantSettings)
            .Verifiable();

        var settings = await Reader.ReadAsync(
            MockStoreManager.Object,
            TenantId,
            CancellationToken.None
        );

        Assert.Same(tenantSettings, settings);
    }
}
