// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Moq;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Connections;

public class DefaultConnectionResolverTests : BaseTests
{
    private const string TenantId = "tenant-1";

    private sealed class TestStrategy(
        string name,
        string signInMethod = ConnectionSignInMethods.Password
    ) : IConnectionStrategy
    {
        public string Name { get; } = name;

        public string SignInMethod { get; } = signInMethod;

        public string? ValidateOptions(JsonElement options) => null;

        public string? ValidateOptionsChange(JsonElement current, JsonElement proposed) => null;
    }

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IConnectionStore> MockStore { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<IEffectiveSettingsResolver> MockSettingsResolver { get; }
    private Mock<IReadOnlySettingCollection> MockTenantSettings { get; }
    private Mock<IReadOnlySettingCollection> MockConnectionSettings { get; }
    private Mock<OpenIdContext> MockContext { get; }
    private DefaultConnectionResolver Resolver { get; }

    public DefaultConnectionResolverTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockStore = CreateStrictMock<IConnectionStore>();
        MockAmbientTenantAccessor = CreateStrictMock<IAmbientTenantAccessor>();
        MockSettingsResolver = CreateStrictMock<IEffectiveSettingsResolver>();
        MockTenantSettings = CreateStrictMock<IReadOnlySettingCollection>();
        MockConnectionSettings = CreateStrictMock<IReadOnlySettingCollection>();
        MockContext = CreateStrictMock<OpenIdContext>();

        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object);
        MockStoreManager.Setup(x => x.GetStore<IConnectionStore>()).Returns(MockStore.Object);
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        MockAmbientTenantAccessor
            .Setup(x => x.BeginScope(TenantId))
            .Returns(Mock.Of<IDisposable>());

        var mockTenant = CreateStrictMock<OpenIdTenant>();
        var mockProvider = CreateStrictMock<IReadOnlySettingCollectionProvider>();
        mockProvider.SetupGet(x => x.Collection).Returns(MockTenantSettings.Object);
        mockTenant.SetupGet(x => x.TenantId).Returns(TenantId);
        mockTenant.SetupGet(x => x.SettingsProvider).Returns(mockProvider.Object);
        MockContext.SetupGet(x => x.Tenant).Returns(mockTenant.Object);

        MockSettingsResolver
            .Setup(x =>
                x.ResolveStored(
                    MockTenantSettings.Object,
                    OpenIdSettingScopes.Connection,
                    It.IsAny<string>(),
                    It.IsAny<JsonElement>()
                )
            )
            .Returns(MockConnectionSettings.Object);

        Resolver = new DefaultConnectionResolver(
            MockStoreManagerFactory.Object,
            MockAmbientTenantAccessor.Object,
            new DefaultConnectionStrategyCatalog([
                new TestStrategy("local"),
                new TestStrategy("federated", "redirect"),
            ]),
            MockSettingsResolver.Object
        );
    }

    private static PersistedConnection Connection(
        string name,
        bool isEnabled = true,
        string strategy = "local"
    ) =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = $"id-{name}",
            Name = name,
            Strategy = strategy,
            DisplayName = name,
            IsSystem = false,
            IsEnabled = isEnabled,
            Options = JsonElements.EmptyObject,
            Settings = new PersistedConnectionSettings
            {
                TenantId = TenantId,
                ConnectionId = $"id-{name}",
                Value = JsonElements.EmptyObject,
            },
        };

    // A client whose allowed_connections is set to the names, or unset when null, and whose default is defaultName.
    private IReadOnlySettingCollection ClientSettings(
        string[]? allowed,
        string defaultName = ConnectionDefaults.Name
    )
    {
        var mock = CreateStrictMock<IReadOnlySettingCollection>();
        IReadOnlyCollection<string>? allowedValue = allowed;
        mock.Setup(x =>
                x.TryGetValue(It.IsAny<SettingKey<IReadOnlyCollection<string>>>(), out allowedValue)
            )
            .Returns(allowed is not null);
        mock.Setup(x => x.GetValue(It.IsAny<SettingKey<string>>())).Returns(defaultName);
        return mock.Object;
    }

    [Fact]
    public async Task GetByIdOrDefaultAsync_WhenFound_ReturnsConnectionWithSettings()
    {
        MockStore
            .Setup(x => x.GetOrDefaultAsync("id-local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Connection("local", isEnabled: false));

        var result = await Resolver.GetByIdOrDefaultAsync(
            MockContext.Object,
            "id-local",
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal("id-local", result.ConnectionId);
        Assert.Equal("local", result.Name);
        Assert.Same(MockConnectionSettings.Object, result.Settings);
    }

    [Fact]
    public async Task GetByIdOrDefaultAsync_WhenMissing_ReturnsNull()
    {
        MockStore
            .Setup(x => x.GetOrDefaultAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedConnection?)null);

        var result = await Resolver.GetByIdOrDefaultAsync(
            MockContext.Object,
            "missing",
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByNameOrDefaultAsync_WhenStrategyIsNotRegistered_ReturnsNull()
    {
        MockStore
            .Setup(x => x.GetByNameOrDefaultAsync("corp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Connection("corp", strategy: "unregistered"));

        var result = await Resolver.GetByNameOrDefaultAsync(
            MockContext.Object,
            "corp",
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllowedAsync_WhenNoAllowList_ReadsEveryEnabledConnectionOrderedByName()
    {
        MockStore
            .Setup(x => x.GetEnabledAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Connection("alpha"),
                Connection("unregistered", strategy: "other"),
                Connection("zeta"),
            ]);

        var result = await Resolver.GetAllowedAsync(
            MockContext.Object,
            ClientSettings(allowed: null),
            ConnectionSignInMethods.Password,
            CancellationToken.None
        );

        Assert.Equal(["alpha", "zeta"], result.Select(item => item.Name));
    }

    [Fact]
    public async Task GetAllowedAsync_WhenAllowListIsSet_ReadsOnlyTheListedNames()
    {
        MockStore
            .Setup(x =>
                x.GetEnabledAsync(
                    It.Is<IReadOnlyCollection<string>>(names =>
                        names.SequenceEqual(new[] { "ZETA" })
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([Connection("zeta")]);

        var result = await Resolver.GetAllowedAsync(
            MockContext.Object,
            ClientSettings(allowed: ["ZETA"]),
            ConnectionSignInMethods.Password,
            CancellationToken.None
        );

        Assert.Equal(["zeta"], result.Select(item => item.Name));
    }

    [Fact]
    public async Task GetAllowedAsync_WhenStrategyHasAnotherSignInMethod_OmitsIt()
    {
        MockStore
            .Setup(x => x.GetEnabledAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Connection("alpha"), Connection("corp", strategy: "federated")]);

        var result = await Resolver.GetAllowedAsync(
            MockContext.Object,
            ClientSettings(allowed: null),
            ConnectionSignInMethods.Password,
            CancellationToken.None
        );

        Assert.Equal(["alpha"], result.Select(item => item.Name));
    }

    [Fact]
    public async Task SelectAsync_WhenRequestedNameIsAllowed_SelectsIt()
    {
        MockStore
            .Setup(x => x.GetByNameOrDefaultAsync("corp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Connection("corp"));

        var selection = await Resolver.SelectAsync(
            MockContext.Object,
            ClientSettings(allowed: ["corp"]),
            "corp",
            CancellationToken.None
        );

        Assert.False(selection.IsRefused);
        Assert.Equal("corp", selection.Connection?.Name);
    }

    [Fact]
    public async Task SelectAsync_WhenRequestedNameIsUnknown_IsRefused()
    {
        MockStore
            .Setup(x => x.GetByNameOrDefaultAsync("corp", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedConnection?)null);

        var selection = await Resolver.SelectAsync(
            MockContext.Object,
            ClientSettings(allowed: null),
            "corp",
            CancellationToken.None
        );

        Assert.True(selection.IsRefused);
        Assert.Equal(ConnectionSelectionErrors.Unavailable, selection.ErrorCode);
        Assert.Equal(
            "The requested connection is not available to the client.",
            selection.ErrorDescription
        );
        Assert.Null(selection.Connection);
    }

    [Fact]
    public async Task SelectAsync_WhenRequestedNameIsDisabled_IsRefused()
    {
        MockStore
            .Setup(x => x.GetByNameOrDefaultAsync("corp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Connection("corp", isEnabled: false));

        var selection = await Resolver.SelectAsync(
            MockContext.Object,
            ClientSettings(allowed: null),
            "corp",
            CancellationToken.None
        );

        Assert.True(selection.IsRefused);
    }

    [Fact]
    public async Task SelectAsync_WhenRequestedNameIsNotAllowed_IsRefused()
    {
        MockStore
            .Setup(x => x.GetByNameOrDefaultAsync("corp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Connection("corp"));

        var selection = await Resolver.SelectAsync(
            MockContext.Object,
            ClientSettings(allowed: ["local"]),
            "corp",
            CancellationToken.None
        );

        Assert.True(selection.IsRefused);
    }

    [Fact]
    public async Task SelectAsync_WhenRequestedStrategyIsNotRegistered_IsRefused()
    {
        MockStore
            .Setup(x => x.GetByNameOrDefaultAsync("corp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Connection("corp", strategy: "other"));

        var selection = await Resolver.SelectAsync(
            MockContext.Object,
            ClientSettings(allowed: null),
            "corp",
            CancellationToken.None
        );

        Assert.True(selection.IsRefused);
    }

    [Fact]
    public async Task SelectAsync_WhenNothingIsRequested_SelectsTheDefaultConnection()
    {
        MockStore
            .Setup(x => x.GetByNameOrDefaultAsync("local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Connection("local"));

        var selection = await Resolver.SelectAsync(
            MockContext.Object,
            ClientSettings(allowed: null),
            null,
            CancellationToken.None
        );

        Assert.False(selection.IsRefused);
        Assert.Equal("local", selection.Connection?.Name);
    }

    [Fact]
    public async Task SelectAsync_WhenTheDefaultIsNotUsable_SelectsNoneWithoutRefusing()
    {
        MockStore
            .Setup(x => x.GetByNameOrDefaultAsync("local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Connection("local", isEnabled: false));

        var selection = await Resolver.SelectAsync(
            MockContext.Object,
            ClientSettings(allowed: null),
            null,
            CancellationToken.None
        );

        Assert.False(selection.IsRefused);
        Assert.Null(selection.Connection);
    }

    [Fact]
    public async Task SelectAsync_WhenTheDefaultIsNotAllowed_SelectsNoneWithoutRefusing()
    {
        MockStore
            .Setup(x => x.GetByNameOrDefaultAsync("local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Connection("local"));

        var selection = await Resolver.SelectAsync(
            MockContext.Object,
            ClientSettings(allowed: ["corp"]),
            null,
            CancellationToken.None
        );

        Assert.False(selection.IsRefused);
        Assert.Null(selection.Connection);
    }
}
