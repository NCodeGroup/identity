// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Persistence;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Management.Promotions;

public sealed class ConnectionPromotionContributorTests : IDisposable
{
    private const string TeamId = "team-1";
    private const string SourceTenantId = "source-tenant";
    private const string TargetTenantId = "target-tenant";
    private const string StrategyName = "local";
    private const string MintedId = "minted-id";

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);
    private Mock<IPromotedSettingsComposer> MockPromotedSettingsComposer { get; }
    private Mock<ISettingSerializer> MockSettingSerializer { get; }
    private Mock<IConnectionStrategyCatalog> MockStrategyCatalog { get; }
    private Mock<IConnectionStrategy> MockStrategy { get; }
    private Mock<ICryptoService> MockCryptoService { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IConnectionStore> MockConnectionStore { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<ISupportTenantId> MockTeamNode { get; }
    private Mock<ISupportTenantId> MockTargetTenantNode { get; }
    private TenantConfigurationDocument SourceDocument { get; } = CreateDocument();

    public ConnectionPromotionContributorTests()
    {
        MockPromotedSettingsComposer = MockRepository.Create<IPromotedSettingsComposer>();
        MockSettingSerializer = MockRepository.Create<ISettingSerializer>();
        MockStrategyCatalog = MockRepository.Create<IConnectionStrategyCatalog>();
        MockStrategy = MockRepository.Create<IConnectionStrategy>();
        MockCryptoService = MockRepository.Create<ICryptoService>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockConnectionStore = MockRepository.Create<IConnectionStore>();
        MockAmbientTenantAccessor = MockRepository.Create<IAmbientTenantAccessor>();
        MockTeamNode = MockRepository.Create<ISupportTenantId>();
        MockTargetTenantNode = MockRepository.Create<ISupportTenantId>();
    }

    public void Dispose() => MockRepository.Verify();

    private void SetupStores() =>
        MockStoreManager
            .Setup(x => x.GetStore<IConnectionStore>())
            .Returns(MockConnectionStore.Object)
            .Verifiable();

    private void SetupScopes()
    {
        var scope = MockRepository.Create<IDisposable>();
        scope.Setup(x => x.Dispose()).Verifiable();
        MockAmbientTenantAccessor
            .Setup(x => x.BeginScope(TargetTenantId))
            .Returns(scope.Object)
            .Verifiable();
    }

    private ConnectionPromotionContributor CreateContributor() =>
        new(
            MockPromotedSettingsComposer.Object,
            MockSettingSerializer.Object,
            MockStrategyCatalog.Object,
            MockCryptoService.Object
        );

    private static PersistedTenant CreateTenant(string tenantId) =>
        new PersistedTenantBuilder { TenantId = tenantId, TeamId = TeamId }.Build();

    private TenantPromotionContext CreateContext() =>
        new()
        {
            Source = SourceDocument,
            TargetTenant = CreateTenant(TargetTenantId),
            TeamNode = MockTeamNode.Object,
            TargetTenantNode = MockTargetTenantNode.Object,
            StoreManager = MockStoreManager.Object,
            AmbientTenantAccessor = MockAmbientTenantAccessor.Object,
            JsonSerializerOptions = new JsonSerializerOptions(),
            EnvironmentSettings = [],
            TargetTenantSettings = JsonElements.EmptyObject,
        };

    private static TenantConfigurationDocument CreateDocument() =>
        new()
        {
            SourceTenantId = SourceTenantId,
            Region = "region-1",
            Settings = JsonElements.EmptyObject,
            Clients = [],
            ClientGrants = [],
            ResourceServers = [],
        };

    private static TenantConfigurationConnection CreateSource(
        string name = "Corp",
        string strategy = StrategyName,
        string displayName = "Corporate",
        bool isEnabled = true,
        string optionsJson = """{"store":"default"}""",
        string settingsJson = "{}"
    ) =>
        new()
        {
            Name = name,
            Strategy = strategy,
            DisplayName = displayName,
            IsEnabled = isEnabled,
            Options = JsonElement.Parse(optionsJson),
            Settings = JsonElement.Parse(settingsJson),
        };

    private static PersistedConnection CreateTarget(
        string name = "corp",
        string strategy = StrategyName,
        string displayName = "Corporate",
        bool isEnabled = true,
        string optionsJson = """{"store":"default"}""",
        string settingsJson = "{}",
        bool isSystem = false
    ) =>
        new()
        {
            TenantId = TargetTenantId,
            ConnectionId = "connection-target",
            ConcurrencyToken = "token",
            Name = name,
            Strategy = strategy,
            DisplayName = displayName,
            IsSystem = isSystem,
            IsEnabled = isEnabled,
            Options = JsonElement.Parse(optionsJson),
            Settings = new PersistedConnectionSettings
            {
                TenantId = TargetTenantId,
                ConnectionId = "connection-target",
                Value = JsonElement.Parse(settingsJson),
            },
        };

    private static PromotedSettings CreatePromotedSettings(
        string documentJson,
        List<string>? unsetSettingNames = null,
        List<string>? refusedSettingNames = null
    ) =>
        new()
        {
            Document = JsonElement.Parse(documentJson),
            UnsetSettingNames = unsetSettingNames ?? [],
            RefusedSettingNames = refusedSettingNames ?? [],
        };

    private void SetupTarget(string name, PersistedConnection? target) =>
        MockConnectionStore
            .Setup(x => x.GetByNameOrDefaultAsync(name, CancellationToken.None))
            .ReturnsAsync(target)
            .Verifiable();

    private void SetupStrategy(
        string? optionsError = null,
        string? changeError = null,
        bool isChangeChecked = false
    )
    {
        var strategy = MockStrategy.Object;
        MockStrategyCatalog
            .Setup(x => x.TryGetStrategy(StrategyName, out strategy))
            .Returns(true)
            .Verifiable();
        MockStrategy
            .Setup(x => x.ValidateOptions(It.IsAny<JsonElement>()))
            .Returns(optionsError)
            .Verifiable();
        if (isChangeChecked)
        {
            MockStrategy
                .Setup(x =>
                    x.ValidateOptionsChange(It.IsAny<JsonElement>(), It.IsAny<JsonElement>())
                )
                .Returns(changeError)
                .Verifiable();
        }
    }

    private void SetupCompose(
        TenantConfigurationConnection source,
        PersistedConnection? target,
        PromotedSettings promoted
    ) =>
        MockPromotedSettingsComposer
            .Setup(x =>
                x.Compose(
                    It.Is<JsonElement>(value => JsonElement.DeepEquals(value, source.Settings)),
                    It.Is<JsonElement?>(value =>
                        target == null
                            ? !value.HasValue
                            : value.HasValue
                                && JsonElement.DeepEquals(value.Value, target.Settings.Value)
                    ),
                    It.Is<JsonElement?>(value => !value.HasValue)
                )
            )
            .Returns(promoted)
            .Verifiable();

    private void SetupValidate(
        TenantPromotionContext context,
        PromotedSettings promoted,
        params SettingError[] errors
    ) =>
        MockSettingSerializer
            .Setup(x =>
                x.ValidateSettings(
                    It.Is<JsonElement>(document =>
                        JsonElement.DeepEquals(document, promoted.Document)
                    ),
                    OpenIdSettingScopes.Connection,
                    context.JsonSerializerOptions
                )
            )
            .Returns(errors)
            .Verifiable();

    private void SetupMint() =>
        MockCryptoService
            .Setup(x => x.GenerateKey(It.IsAny<int>(), It.IsAny<string>()))
            .Returns(MintedId)
            .Verifiable();

    #region Order Property Tests

    [Fact]
    public void Order_Always_IsTheConnectionsOrder()
    {
        Assert.Equal(TenantPromotionOrders.Connections, CreateContributor().Order);
        Assert.True(TenantPromotionOrders.Connections > TenantPromotionOrders.TenantSettings);
        Assert.True(TenantPromotionOrders.Connections < TenantPromotionOrders.ResourceServers);
    }

    #endregion

    #region PlanAsync Tests

    [Fact]
    public async Task PlanAsync_WhenTheTargetLacksTheName_RecordsACreatedChange()
    {
        SetupStores();
        SetupScopes();
        var context = CreateContext();
        var source = CreateSource();
        var promoted = CreatePromotedSettings("{}");
        SourceDocument.Connections.Add(source);
        SetupTarget("Corp", null);
        SetupStrategy();
        SetupCompose(source, null, promoted);
        SetupValidate(context, promoted);

        await CreateContributor().PlanAsync(context, CancellationToken.None);

        var change = Assert.Single(context.Changes);
        Assert.Equal(ManagementResourceTypes.Connection, change.ResourceType);
        Assert.Equal("Corp", change.ResourceId);
        Assert.Equal(TenantPromotionActions.Created, change.Action);
        Assert.Empty(context.Errors);
    }

    [Fact]
    public async Task PlanAsync_WhenTheTargetMatches_RecordsAnUnchangedChange()
    {
        SetupStores();
        SetupScopes();
        var context = CreateContext();
        var source = CreateSource();
        var target = CreateTarget();
        var promoted = CreatePromotedSettings("{}");
        SourceDocument.Connections.Add(source);
        SetupTarget("Corp", target);
        SetupStrategy(isChangeChecked: true);
        SetupCompose(source, target, promoted);
        SetupValidate(context, promoted);

        await CreateContributor().PlanAsync(context, CancellationToken.None);

        var change = Assert.Single(context.Changes);
        Assert.Equal(TenantPromotionActions.Unchanged, change.Action);
        Assert.Empty(context.Errors);
        Assert.Empty(context.ChangedLimits);
    }

    [Fact]
    public async Task PlanAsync_WhenTheTargetDiffers_RecordsAnUpdatedChangeAndItsChangedLimits()
    {
        SetupStores();
        SetupScopes();
        var context = CreateContext();
        var source = CreateSource(displayName: "New", settingsJson: """{"a":1}""");
        var target = CreateTarget(displayName: "Old", settingsJson: """{"a":0}""");
        var promoted = CreatePromotedSettings("""{"a":1}""");
        SourceDocument.Connections.Add(source);
        SetupTarget("Corp", target);
        SetupStrategy(isChangeChecked: true);
        SetupCompose(source, target, promoted);
        SetupValidate(context, promoted);
        MockSettingSerializer
            .Setup(x =>
                x.GetChangedLimits(
                    It.Is<JsonElement>(current =>
                        JsonElement.DeepEquals(current, target.Settings.Value)
                    ),
                    It.Is<JsonElement>(replacement =>
                        JsonElement.DeepEquals(replacement, promoted.Document)
                    )
                )
            )
            .Returns(["password_min_length"])
            .Verifiable();

        await CreateContributor().PlanAsync(context, CancellationToken.None);

        var change = Assert.Single(context.Changes);
        Assert.Equal(TenantPromotionActions.Updated, change.Action);
        var limit = Assert.Single(context.ChangedLimits);
        Assert.Equal(["password_min_length"], limit.SettingNames);
    }

    [Fact]
    public async Task PlanAsync_WhenTheStrategyDiffers_AddsAnErrorNamingTheConnection()
    {
        SetupStores();
        SetupScopes();
        var context = CreateContext();
        var source = CreateSource();
        var target = CreateTarget(strategy: "other");
        var promoted = CreatePromotedSettings("{}");
        SourceDocument.Connections.Add(source);
        SetupTarget("Corp", target);
        SetupCompose(source, target, promoted);
        SetupValidate(context, promoted);

        await CreateContributor().PlanAsync(context, CancellationToken.None);

        var error = Assert.Single(context.Errors);
        Assert.Equal(ManagementResourceTypes.Connection, error.ResourceType);
        Assert.Equal("Corp", error.ResourceId);
        Assert.Contains("strategy", error.Detail);
    }

    [Fact]
    public async Task PlanAsync_WhenTheStrategyIsNotRegistered_AddsAnError()
    {
        SetupStores();
        SetupScopes();
        var context = CreateContext();
        var source = CreateSource();
        var promoted = CreatePromotedSettings("{}");
        SourceDocument.Connections.Add(source);
        SetupTarget("Corp", null);
        IConnectionStrategy? missing = null;
        MockStrategyCatalog
            .Setup(x => x.TryGetStrategy(StrategyName, out missing))
            .Returns(false)
            .Verifiable();
        SetupCompose(source, null, promoted);
        SetupValidate(context, promoted);

        await CreateContributor().PlanAsync(context, CancellationToken.None);

        var error = Assert.Single(context.Errors);
        Assert.Equal("Corp", error.ResourceId);
        Assert.Contains("not registered", error.Detail);
    }

    [Fact]
    public async Task PlanAsync_WhenTheStrategyRefusesTheOptions_AddsAnError()
    {
        SetupStores();
        SetupScopes();
        var context = CreateContext();
        var source = CreateSource();
        var promoted = CreatePromotedSettings("{}");
        SourceDocument.Connections.Add(source);
        SetupTarget("Corp", null);
        SetupStrategy(optionsError: "The store is unknown.");
        SetupCompose(source, null, promoted);
        SetupValidate(context, promoted);

        await CreateContributor().PlanAsync(context, CancellationToken.None);

        var error = Assert.Single(context.Errors);
        Assert.Equal("Corp", error.ResourceId);
        Assert.Contains("The store is unknown.", error.Detail);
    }

    [Fact]
    public async Task PlanAsync_WhenTheStrategyRefusesTheOptionsChange_AddsAnError()
    {
        SetupStores();
        SetupScopes();
        var context = CreateContext();
        var source = CreateSource(optionsJson: """{"store":"other"}""");
        var target = CreateTarget();
        var promoted = CreatePromotedSettings("{}");
        SourceDocument.Connections.Add(source);
        SetupTarget("Corp", target);
        SetupStrategy(changeError: "The store cannot change.", isChangeChecked: true);
        SetupCompose(source, target, promoted);
        SetupValidate(context, promoted);

        await CreateContributor().PlanAsync(context, CancellationToken.None);

        var error = Assert.Single(context.Errors);
        Assert.Contains("The store cannot change.", error.Detail);
    }

    [Fact]
    public async Task PlanAsync_WhenTheSettingsHaveUnsetAndRefusedNamesAndErrors_RecordsThem()
    {
        SetupStores();
        SetupScopes();
        var context = CreateContext();
        var source = CreateSource();
        var promoted = CreatePromotedSettings(
            "{}",
            unsetSettingNames: ["environment_setting"],
            refusedSettingNames: ["refused_setting"]
        );
        SourceDocument.Connections.Add(source);
        SetupTarget("Corp", null);
        SetupStrategy();
        SetupCompose(source, null, promoted);
        SetupValidate(context, promoted, new SettingError("a", "The value of 'a' is invalid."));

        await CreateContributor().PlanAsync(context, CancellationToken.None);

        Assert.Equal(2, context.Errors.Count);
        var note = Assert.Single(context.Notes);
        Assert.Equal(TenantPromotionNoteCodes.EnvironmentSettingUnset, note.Code);
        Assert.Equal("environment_setting", note.SettingName);
        Assert.Equal("Corp", note.ResourceId);
    }

    #endregion

    #region ApplyAsync Tests

    [Fact]
    public async Task ApplyAsync_WhenTheTargetLacksTheName_AddsItWithAMintedIdAndStagesACreatedEvent()
    {
        SetupStores();
        SetupScopes();
        SetupMint();
        var context = CreateContext();
        var source = CreateSource(isEnabled: false, settingsJson: """{"a":1}""");
        var promoted = CreatePromotedSettings("""{"a":1}""");
        SourceDocument.Connections.Add(source);
        SetupTarget("Corp", null);
        SetupStrategy();
        SetupCompose(source, null, promoted);
        SetupValidate(context, promoted);
        PersistedConnection? added = null;
        MockConnectionStore
            .Setup(x => x.AddAsync(It.IsAny<PersistedConnection>(), CancellationToken.None))
            .Callback<PersistedConnection, CancellationToken>((item, _) => added = item)
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        await CreateContributor().ApplyAsync(context, CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(TargetTenantId, added.TenantId);
        Assert.Equal(MintedId, added.ConnectionId);
        Assert.Equal("Corp", added.Name);
        Assert.Equal(StrategyName, added.Strategy);
        Assert.Equal("Corporate", added.DisplayName);
        Assert.False(added.IsSystem);
        Assert.False(added.IsEnabled);
        Assert.Equal("""{"store":"default"}""", added.Options.GetRawText());
        Assert.Equal(TargetTenantId, added.Settings.TenantId);
        Assert.Equal(MintedId, added.Settings.ConnectionId);
        Assert.True(JsonElement.DeepEquals(promoted.Document, added.Settings.Value));
        var auditEvent = Assert.IsType<ConnectionChangedAuditEvent>(
            Assert.Single(context.AuditEvents)
        );
        Assert.Equal(TargetTenantId, auditEvent.TenantId);
        Assert.Equal(MintedId, auditEvent.ResourceId);
        Assert.Equal(ResourceChangeTypes.Created, auditEvent.ChangeType);
    }

    [Fact]
    public async Task ApplyAsync_WhenTheTargetDiffers_UpdatesItAndStagesAnUpdatedEvent()
    {
        SetupStores();
        SetupScopes();
        var context = CreateContext();
        var source = CreateSource(
            displayName: "New",
            isEnabled: false,
            settingsJson: """{"a":1}"""
        );
        var target = CreateTarget(displayName: "Old", settingsJson: """{"a":0}""", isSystem: true);
        var promoted = CreatePromotedSettings("""{"a":1}""");
        SourceDocument.Connections.Add(source);
        SetupTarget("Corp", target);
        SetupStrategy(isChangeChecked: true);
        SetupCompose(source, target, promoted);
        SetupValidate(context, promoted);
        MockConnectionStore
            .Setup(x =>
                x.UpdateAsync(
                    It.Is<PersistedConnection>(item =>
                        ReferenceEquals(item, target)
                        && item.IsSystem
                        && item.DisplayName == "New"
                        && !item.IsEnabled
                    ),
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockConnectionStore
            .Setup(x =>
                x.UpdateSettingsAsync(
                    It.Is<PersistedConnectionSettings>(item =>
                        ReferenceEquals(item, target.Settings)
                        && JsonElement.DeepEquals(item.Value, promoted.Document)
                    ),
                    CancellationToken.None
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        await CreateContributor().ApplyAsync(context, CancellationToken.None);

        var auditEvent = Assert.IsType<ConnectionChangedAuditEvent>(
            Assert.Single(context.AuditEvents)
        );
        Assert.Equal("connection-target", auditEvent.ResourceId);
        Assert.Equal(ResourceChangeTypes.Updated, auditEvent.ChangeType);
    }

    [Fact]
    public async Task ApplyAsync_WhenTheTargetMatches_WritesNothing()
    {
        SetupStores();
        SetupScopes();
        var context = CreateContext();
        var source = CreateSource();
        var target = CreateTarget();
        var promoted = CreatePromotedSettings("{}");
        SourceDocument.Connections.Add(source);
        SetupTarget("Corp", target);
        SetupStrategy(isChangeChecked: true);
        SetupCompose(source, target, promoted);
        SetupValidate(context, promoted);

        await CreateContributor().ApplyAsync(context, CancellationToken.None);

        Assert.Empty(context.AuditEvents);
    }

    #endregion
}
