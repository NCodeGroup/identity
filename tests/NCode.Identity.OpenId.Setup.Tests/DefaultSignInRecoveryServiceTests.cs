// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NCode.Identity.Events.Audit;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Setup;

public sealed class DefaultSignInRecoveryServiceTests
{
    private const string TenantId = "control-plane";
    private const string ConnectionId = "local-connection-id";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; } = new();
    private Mock<IStoreManager> MockStoreManager { get; } = new();
    private Mock<IPlatformStore> MockPlatformStore { get; } = new();
    private Mock<ITenantStore> MockTenantStore { get; } = new();
    private Mock<IConnectionStore> MockConnectionStore { get; } = new();
    private Mock<IClientStore> MockClientStore { get; } = new();
    private Mock<IResourceServerStore> MockResourceServerStore { get; } = new();
    private Mock<ITenantResolver> MockTenantResolver { get; } = new();
    private Mock<IServiceScopeFactory> MockServiceScopeFactory { get; } = new();
    private Mock<IServiceScope> MockServiceScope { get; } = new();
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; } = new();
    private Mock<IPlatformTopology> MockTopology { get; } = new();
    private Mock<IPlatformDirectory> MockPlatformDirectory { get; } = new();
    private Mock<IManagementAuditRecorder> MockAuditRecorder { get; } = new();
    private Mock<ISettingDescriptorCollectionProvider> MockDescriptors { get; } = new();

    private DefaultSignInRecoveryService CreateService(bool isControlPlaneCell)
    {
        MockServiceScopeFactory.Setup(x => x.CreateScope()).Returns(MockServiceScope.Object);
        MockServiceScope.Setup(x => x.ServiceProvider).Returns(new Mock<IServiceProvider>().Object);
        MockTenantResolver
            .Setup(x =>
                x.ResolveControlPlaneTenantAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new PersistedTenantBuilder
                {
                    TenantId = TenantId,
                    Issuer = "https://id.example.test/",
                }.Build()
            );
        MockAmbientTenantAccessor
            .Setup(x => x.BeginUnscoped(It.IsAny<string>()))
            .Returns(new Mock<IDisposable>().Object);
        MockAmbientTenantAccessor
            .Setup(x => x.BeginScope(TenantId))
            .Returns(new Mock<IDisposable>().Object);
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object);
        MockStoreManager.Setup(x => x.GetStore<IPlatformStore>()).Returns(MockPlatformStore.Object);
        MockStoreManager.Setup(x => x.GetStore<ITenantStore>()).Returns(MockTenantStore.Object);
        MockStoreManager
            .Setup(x => x.GetStore<IConnectionStore>())
            .Returns(MockConnectionStore.Object);
        MockStoreManager.Setup(x => x.GetStore<IClientStore>()).Returns(MockClientStore.Object);
        MockStoreManager
            .Setup(x => x.GetStore<IResourceServerStore>())
            .Returns(MockResourceServerStore.Object);
        MockTopology.Setup(x => x.IsControlPlaneCell).Returns(isControlPlaneCell);
        MockDescriptors
            .Setup(x => x.Collection)
            .Returns(
                new DescriptorCollection([
                    new SettingDescriptor<int>
                    {
                        Name = OpenIdSettingNames.PasswordMinLength,
                        IsSignInCritical = true,
                        AppliesTo = [OpenIdSettingScopes.Tenant],
                        Default = 15,
                    },
                    new SettingDescriptor<int>
                    {
                        Name = OpenIdSettingNames.PasswordMaxLength,
                        Default = 128,
                        AppliesTo = [OpenIdSettingScopes.Tenant],
                    },
                ])
            );

        return new DefaultSignInRecoveryService(
            MockStoreManagerFactory.Object,
            MockTenantResolver.Object,
            MockServiceScopeFactory.Object,
            MockAmbientTenantAccessor.Object,
            MockDescriptors.Object,
            MockTopology.Object,
            MockPlatformDirectory.Object,
            Options.Create(new SetupOptions()),
            MockAuditRecorder.Object,
            NullLogger<DefaultSignInRecoveryService>.Instance
        );
    }

    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

    private static PersistedConnection CreateConnection(bool isEnabled) =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = ConnectionId,
            Name = ConnectionDefaults.Name,
            Strategy = ConnectionStrategyNames.Local,
            DisplayName = "Username and password",
            IsSystem = false,
            IsEnabled = isEnabled,
            Options = JsonElements.EmptyObject,
            Settings = new PersistedConnectionSettings
            {
                TenantId = TenantId,
                ConnectionId = ConnectionId,
                Value = JsonElements.EmptyObject,
            },
        };

    private void SetupHealthyResources()
    {
        MockTenantStore
            .Setup(x => x.GetSettingsOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new PersistedTenantBuilder
                {
                    TenantId = TenantId,
                    Settings = Json("""{"password_max_length":200}"""),
                }.BuildSettings()
            );
        MockConnectionStore
            .Setup(x =>
                x.GetByNameOrDefaultAsync(ConnectionDefaults.Name, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(CreateConnection(isEnabled: true));
        MockClientStore
            .Setup(x =>
                x.GetOrDefaultAsync(
                    SetupManagementConsoleOptions.DefaultClientId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new PersistedClientBuilder
                {
                    TenantId = TenantId,
                    ClientId = SetupManagementConsoleOptions.DefaultClientId,
                }.Build()
            );
        MockResourceServerStore
            .Setup(x =>
                x.GetByIdentifierOrDefaultAsync(
                    OpenIdConstants.SystemResourceServerIdentifiers.Management,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new PersistedResourceServerBuilder
                {
                    TenantId = TenantId,
                    Identifier = OpenIdConstants.SystemResourceServerIdentifiers.Management,
                }.Build()
            );
    }

    [Fact]
    public async Task RepairAsync_HealthyTenant_MakesNoChange()
    {
        SetupHealthyResources();
        var service = CreateService(isControlPlaneCell: false);

        var result = await service.RepairAsync(CancellationToken.None);

        Assert.Empty(result.Changes);
        MockStoreManager.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
        MockPlatformStore.Verify(
            x => x.GetSettingsOrDefaultAsync(It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task RepairAsync_BlockedTenant_RemovesTheSettingAndEnablesTheResources()
    {
        SetupHealthyResources();
        var tenantSettings = new PersistedTenantBuilder
        {
            TenantId = TenantId,
            Settings = Json("""{"password_min_length":64,"password_max_length":200}"""),
        }.BuildSettings();
        MockTenantStore
            .Setup(x => x.GetSettingsOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenantSettings);
        var connection = CreateConnection(isEnabled: false);
        MockConnectionStore
            .Setup(x =>
                x.GetByNameOrDefaultAsync(ConnectionDefaults.Name, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(connection);
        var client = new PersistedClientBuilder
        {
            TenantId = TenantId,
            ClientId = SetupManagementConsoleOptions.DefaultClientId,
            IsDisabled = true,
        }.Build();
        MockClientStore
            .Setup(x =>
                x.GetOrDefaultAsync(
                    SetupManagementConsoleOptions.DefaultClientId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(client);
        var resourceServer = new PersistedResourceServerBuilder
        {
            TenantId = TenantId,
            Identifier = OpenIdConstants.SystemResourceServerIdentifiers.Management,
            IsDisabled = true,
        }.Build();
        MockResourceServerStore
            .Setup(x =>
                x.GetByIdentifierOrDefaultAsync(
                    OpenIdConstants.SystemResourceServerIdentifiers.Management,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(resourceServer);
        var service = CreateService(isControlPlaneCell: false);

        var result = await service.RepairAsync(CancellationToken.None);

        Assert.Equal(
            [
                SignInRepairScopes.Tenant,
                SignInRepairScopes.Connection,
                SignInRepairScopes.Client,
                SignInRepairScopes.ResourceServer,
            ],
            result.Changes.Select(change => change.Scope)
        );
        var settingChange = result.Changes[0];
        Assert.Equal(OpenIdSettingNames.PasswordMinLength, settingChange.Name);
        Assert.Equal("64", settingChange.Before);
        Assert.False(tenantSettings.Value.TryGetProperty("password_min_length", out _));
        Assert.True(tenantSettings.Value.TryGetProperty("password_max_length", out _));
        Assert.True(connection.IsEnabled);
        Assert.False(client.IsDisabled);
        Assert.False(resourceServer.IsDisabled);
        MockStoreManager.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        MockAuditRecorder.Verify(
            x =>
                x.RecordTenantChangedAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<string>(),
                    TenantId,
                    ResourceChangeTypes.SettingsUpdated,
                    null
                ),
            Times.Once
        );
        MockAuditRecorder.Verify(
            x =>
                x.RecordClientChangedAsync(
                    It.IsAny<HttpContext>(),
                    TenantId,
                    SetupManagementConsoleOptions.DefaultClientId,
                    ResourceChangeTypes.Updated,
                    null
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task RepairAsync_ControlPlaneCell_ResetsAndPublishesThePlatformSettings()
    {
        SetupHealthyResources();
        var platformSettings = new PersistedPlatformSettings
        {
            Value = Json("""{"password_min_length":64}"""),
        };
        MockPlatformStore
            .Setup(x => x.GetSettingsOrDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(platformSettings);
        MockPlatformDirectory
            .Setup(x => x.PublishAsync(It.IsAny<PlatformDocument>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        var service = CreateService(isControlPlaneCell: true);

        var result = await service.RepairAsync(CancellationToken.None);

        var change = Assert.Single(result.Changes);
        Assert.Equal(SignInRepairScopes.Platform, change.Scope);
        Assert.Equal(OpenIdSettingNames.PasswordMinLength, change.Name);
        Assert.False(platformSettings.Value.TryGetProperty("password_min_length", out _));
        MockPlatformDirectory.Verify(
            x =>
                x.PublishAsync(
                    It.Is<PlatformDocument>(document => HasNoMinLength(document)),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        MockAuditRecorder.Verify(
            x =>
                x.RecordPlatformChangedAsync(
                    It.IsAny<HttpContext>(),
                    ResourceChangeTypes.SettingsUpdated,
                    null
                ),
            Times.Once
        );
    }

    private static bool HasNoMinLength(PlatformDocument document) =>
        !document.Settings.TryGetProperty("password_min_length", out _);

    private sealed class DescriptorCollection(IReadOnlyList<SettingDescriptor> descriptors)
        : ISettingDescriptorCollection
    {
        public int Count => descriptors.Count;

        public IEnumerator<SettingDescriptor> GetEnumerator() => descriptors.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool TryGet(
            string settingName,
            [MaybeNullWhen(false)] out SettingDescriptor descriptor
        )
        {
            descriptor = descriptors.FirstOrDefault(item => item.Name == settingName);
            return descriptor is not null;
        }

        public bool TryGet<TValue>(
            SettingKey<TValue> key,
            [MaybeNullWhen(false)] out SettingDescriptor<TValue> descriptor
        )
            where TValue : notnull
        {
            descriptor = null;
            return false;
        }
    }
}
