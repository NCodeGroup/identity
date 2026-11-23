// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Environments;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Contracts.Connections;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;
using SystemTextJsonPatch;
using SystemTextJsonPatch.Operations;

namespace NCode.Identity.OpenId.Management.Endpoints.Connections;

public sealed class ConnectionApiEndpointHandlerTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string ConnectionId = "connection-1";
    private const string NewConnectionId = "connection-new";
    private const string ConcurrencyToken = "connection-ct";

    private MockRepository MockRepository { get; }
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IConnectionStore> MockConnectionStore { get; }
    private Mock<IAuthorizationService> MockAuthorizationService { get; }
    private Mock<IConnectionValidator> MockValidator { get; }
    private Mock<IConnectionDependentSource> MockDependentSource { get; }
    private Mock<ISettingSerializer> MockSettingSerializer { get; }
    private Mock<ISettingScopeCollectionProvider> MockSettingScopeCollectionProvider { get; }
    private Mock<IEffectiveSettingsResolver> MockEffectiveSettingsResolver { get; }
    private Mock<IManagementAuditRecorder> MockManagementAuditRecorder { get; }
    private FakeCryptoService CryptoService { get; }
    private ConnectionApiEndpointHandler Handler { get; }

    public ConnectionApiEndpointHandlerTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockConnectionStore = MockRepository.Create<IConnectionStore>();
        MockAuthorizationService = MockRepository.Create<IAuthorizationService>();
        MockValidator = MockRepository.Create<IConnectionValidator>();
        MockDependentSource = MockRepository.Create<IConnectionDependentSource>();
        MockSettingSerializer = MockRepository.Create<ISettingSerializer>();
        MockSettingScopeCollectionProvider =
            MockRepository.Create<ISettingScopeCollectionProvider>();
        MockEffectiveSettingsResolver = MockRepository.Create<IEffectiveSettingsResolver>();
        MockManagementAuditRecorder = MockRepository.Create<IManagementAuditRecorder>();
        CryptoService = new FakeCryptoService();

        Handler = new ConnectionApiEndpointHandler(
            MockStoreManagerFactory.Object,
            MockAuthorizationService.Object,
            MockValidator.Object,
            [MockDependentSource.Object],
            MockSettingSerializer.Object,
            MockSettingScopeCollectionProvider.Object,
            MockEffectiveSettingsResolver.Object,
            CryptoService,
            MockManagementAuditRecorder.Object,
            NullLogger<ConnectionApiEndpointHandler>.Instance
        );
    }

    public void Dispose()
    {
        MockRepository.Verify();
    }

    #region Helpers

    private sealed class FakeCryptoService : ICryptoService
    {
        public Queue<string> ResourceIds { get; } = new();

        public void GenerateBytes(Span<byte> destination) => throw new NotSupportedException();

        public string GenerateKey(int byteLength, string binaryEncoding) => ResourceIds.Dequeue();

        public string EncodeBinary(ReadOnlySpan<byte> data, string binaryEncoding) =>
            throw new NotSupportedException();

        public string HashValue(
            ReadOnlySpan<byte> data,
            System.Security.Cryptography.HashAlgorithmName hashAlgorithm,
            string binaryEncoding
        ) => throw new NotSupportedException();

        public string HashValue(
            string data,
            System.Security.Cryptography.HashAlgorithmName hashAlgorithm,
            string binaryEncoding,
            System.Text.Encoding? encoding = null
        ) => throw new NotSupportedException();
    }

    private void SetupStore()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();

        MockStoreManager
            .Setup(x => x.GetStore<IConnectionStore>())
            .Returns(MockConnectionStore.Object)
            .Verifiable();

        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
    }

    private void SetupSave()
    {
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
    }

    private void SetupAuthorization(AuthorizationResult result)
    {
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<IEnumerable<IAuthorizationRequirement>>()
                )
            )
            .ReturnsAsync(result)
            .Verifiable();
    }

    private void SetupRecorder(string connectionId, string changeType)
    {
        MockManagementAuditRecorder
            .Setup(x =>
                x.RecordConnectionChangedAsync(
                    It.IsAny<HttpContext>(),
                    TenantId,
                    connectionId,
                    changeType,
                    It.IsAny<JsonElement?>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
    }

    private void SetupGetConnection(PersistedConnection? connection)
    {
        MockConnectionStore
            .Setup(x => x.GetOrDefaultAsync(ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection)
            .Verifiable();
    }

    private void SetupValidSettings()
    {
        MockSettingSerializer
            .Setup(x =>
                x.ValidateSettings(
                    It.IsAny<JsonElement>(),
                    OpenIdSettingScopes.Connection,
                    It.IsAny<JsonSerializerOptions>()
                )
            )
            .Returns([])
            .Verifiable();
    }

    private static HttpContext CreateHttpContext()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test")),
        };
        // The management caller gate publishes the context in the pipeline; emulate it for direct handler calls.
        httpContext.Features.Set<IOpenIdContextFeature>(
            Mock.Of<IOpenIdContextFeature>(feature =>
                feature.OpenIdContext
                == Mock.Of<OpenIdContext>(context =>
                    context.Environment
                    == Mock.Of<OpenIdEnvironment>(environment =>
                        environment.JsonSerializerOptions == JsonSerializerOptions.Default
                    )
                )
            )
        );
        return httpContext;
    }

    private static PersistedConnection CreateConnection(
        string connectionId = ConnectionId,
        string displayName = "Corporate",
        bool isEnabled = true,
        string concurrencyToken = ConcurrencyToken
    ) =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = connectionId,
            ConcurrencyToken = concurrencyToken,
            Name = "corp",
            Strategy = ConnectionStrategyNames.Local,
            DisplayName = displayName,
            IsSystem = false,
            IsEnabled = isEnabled,
            Options = JsonSerializer.SerializeToElement(new { store = "default" }),
            Settings = new PersistedConnectionSettings
            {
                TenantId = TenantId,
                ConnectionId = connectionId,
                Value = JsonElements.EmptyObject,
            },
        };

    private static CreateConnectionRequest CreateRequest() =>
        new()
        {
            Name = "corp",
            DisplayName = "Corporate",
            Strategy = ConnectionStrategyNames.Local,
        };

    private static ManagementError Error(int statusCode) =>
        new() { StatusCode = statusCode, Detail = "Refused." };

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_WhenUnauthorized_ReturnsForbidden()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var result = await Handler.ListAsync(
            CreateHttpContext(),
            TenantId,
            cursor: null,
            limit: null,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task ListAsync_WhenAuthorized_ReturnsThePage()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        MockConnectionStore
            .Setup(x => x.GetPageAsync(It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new PagedResult<PersistedConnection>
                {
                    Items = [CreateConnection()],
                    NextCursor = "next",
                }
            )
            .Verifiable();

        var result = await Handler.ListAsync(
            CreateHttpContext(),
            TenantId,
            cursor: null,
            limit: null,
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<CollectionResource<ConnectionResource>>>(result);
        Assert.NotNull(json.Value);
        Assert.Equal(ConnectionId, Assert.Single(json.Value.Items).ConnectionId);
        Assert.Equal("next", json.Value.ContinuationToken);
    }

    #endregion

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_WhenUnauthorized_ReturnsForbidden()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var result = await Handler.CreateAsync(
            CreateHttpContext(),
            TenantId,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_WhenTheValidatorRefuses_ReturnsBadRequest()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        CryptoService.ResourceIds.Enqueue(NewConnectionId);
        MockValidator
            .Setup(x =>
                x.ValidateCreateAsync(
                    It.IsAny<PersistedConnection>(),
                    MockStoreManager.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Error(StatusCodes.Status400BadRequest))
            .Verifiable();

        var result = await Handler.CreateAsync(
            CreateHttpContext(),
            TenantId,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_WhenTheNameExists_ReturnsConflict()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        CryptoService.ResourceIds.Enqueue(NewConnectionId);
        MockValidator
            .Setup(x =>
                x.ValidateCreateAsync(
                    It.IsAny<PersistedConnection>(),
                    MockStoreManager.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Error(StatusCodes.Status409Conflict))
            .Verifiable();

        var result = await Handler.CreateAsync(
            CreateHttpContext(),
            TenantId,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_WhenTheSettingsAreInvalid_ReturnsBadRequest()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        CryptoService.ResourceIds.Enqueue(NewConnectionId);
        MockValidator
            .Setup(x =>
                x.ValidateCreateAsync(
                    It.IsAny<PersistedConnection>(),
                    MockStoreManager.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((ManagementError?)null)
            .Verifiable();
        MockSettingSerializer
            .Setup(x =>
                x.ValidateSettings(
                    It.IsAny<JsonElement>(),
                    OpenIdSettingScopes.Connection,
                    It.IsAny<JsonSerializerOptions>()
                )
            )
            .Returns([new SettingError("unregistered_setting", "Not registered.")])
            .Verifiable();

        var result = await Handler.CreateAsync(
            CreateHttpContext(),
            TenantId,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_WhenValid_StoresAuditsAndReturnsCreated()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupSave();
        SetupValidSettings();
        CryptoService.ResourceIds.Enqueue(NewConnectionId);
        MockValidator
            .Setup(x =>
                x.ValidateCreateAsync(
                    It.IsAny<PersistedConnection>(),
                    MockStoreManager.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((ManagementError?)null)
            .Verifiable();
        MockConnectionStore
            .Setup(x =>
                x.AddAsync(
                    It.Is<PersistedConnection>(connection =>
                        connection.ConnectionId == NewConnectionId
                        && connection.TenantId == TenantId
                        && connection.Name == "corp"
                        && connection.Settings.ConnectionId == NewConnectionId
                        && connection.Settings.Value.ValueKind == JsonValueKind.Object
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupRecorder(NewConnectionId, ResourceChangeTypes.Created);

        var result = await Handler.CreateAsync(
            CreateHttpContext(),
            TenantId,
            CreateRequest(),
            CancellationToken.None
        );

        var created = Assert.IsType<Created<ConnectionResource>>(result);
        Assert.Equal($"/tenants/{TenantId}/connections/{NewConnectionId}", created.Location);
        Assert.NotNull(created.Value);
        Assert.Equal(NewConnectionId, created.Value.ConnectionId);
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_WhenMissing_ReturnsNotFound()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupGetConnection(null);

        var result = await Handler.GetAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task GetAsync_WhenPresent_ReturnsTheConnectionWithItsEntityTag()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupGetConnection(CreateConnection());
        var httpContext = CreateHttpContext();

        var result = await Handler.GetAsync(
            httpContext,
            TenantId,
            ConnectionId,
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<ConnectionResource>>(result);
        Assert.NotNull(json.Value);
        Assert.Equal("corp", json.Value.Name);
        Assert.Equal(
            ManagementEntityTags.Format(ConcurrencyToken),
            httpContext.Response.Headers.ETag.ToString()
        );
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_WhenTheETagDiffers_ReturnsPreconditionFailed()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupGetConnection(CreateConnection());
        var patch = new JsonPatchDocument<UpdateConnectionRequest>();
        patch.Replace(x => x.IsEnabled, false);

        var result = await Handler.UpdateAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            patch,
            ManagementEntityTags.Format("another-ct"),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, problem.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenTheOptionsAreRefused_ReturnsBadRequestWithoutWriting()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupGetConnection(CreateConnection());
        MockValidator
            .Setup(x =>
                x.ValidateUpdateAsync(
                    It.Is<PersistedConnection>(connection =>
                        connection.Options.ValueKind == JsonValueKind.Object
                        && connection.Options.EnumerateObject().Count() == 0
                    ),
                    It.IsAny<JsonElement>(),
                    MockStoreManager.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Error(StatusCodes.Status400BadRequest))
            .Verifiable();
        var patch = new JsonPatchDocument<UpdateConnectionRequest>();
        patch.Replace(x => x.Options, JsonElements.EmptyObject);

        var result = await Handler.UpdateAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            patch,
            ManagementEntityTags.Format(ConcurrencyToken),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenThePatchChangesTheStore_ReturnsBadRequestWithTheStoredOptionsAsPrevious()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupGetConnection(CreateConnection());
        MockValidator
            .Setup(x =>
                x.ValidateUpdateAsync(
                    It.Is<PersistedConnection>(connection =>
                        connection.Options.GetProperty("store").GetString() == "other"
                    ),
                    It.Is<JsonElement>(previous =>
                        previous.GetProperty("store").GetString() == "default"
                    ),
                    MockStoreManager.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Error(StatusCodes.Status400BadRequest))
            .Verifiable();
        var patch = new JsonPatchDocument<UpdateConnectionRequest>();
        patch.Replace(x => x.Options, JsonSerializer.SerializeToElement(new { store = "other" }));

        var result = await Handler.UpdateAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            patch,
            ManagementEntityTags.Format(ConcurrencyToken),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenDisabling_UpdatesAndAudits()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupSave();
        var saved = CreateConnection(isEnabled: false, concurrencyToken: "updated-ct");
        MockConnectionStore
            .SetupSequence(x => x.GetOrDefaultAsync(ConnectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateConnection())
            .ReturnsAsync(saved);
        MockValidator
            .Setup(x =>
                x.ValidateUpdateAsync(
                    It.Is<PersistedConnection>(connection => !connection.IsEnabled),
                    It.IsAny<JsonElement>(),
                    MockStoreManager.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((ManagementError?)null)
            .Verifiable();
        MockConnectionStore
            .Setup(x =>
                x.UpdateAsync(
                    It.Is<PersistedConnection>(connection => !connection.IsEnabled),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupRecorder(ConnectionId, ResourceChangeTypes.Updated);
        var httpContext = CreateHttpContext();
        var patch = new JsonPatchDocument<UpdateConnectionRequest>();
        patch.Replace(x => x.IsEnabled, false);

        var result = await Handler.UpdateAsync(
            httpContext,
            TenantId,
            ConnectionId,
            patch,
            ManagementEntityTags.Format(ConcurrencyToken),
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<ConnectionResource>>(result);
        Assert.NotNull(json.Value);
        Assert.False(json.Value.IsEnabled);
        Assert.Equal(
            ManagementEntityTags.Format("updated-ct"),
            httpContext.Response.Headers.ETag.ToString()
        );
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WhenMissing_ReturnsNotFound()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupGetConnection(null);

        var result = await Handler.DeleteAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_WhenTheConnectionIsASystemConnection_ReturnsConflict()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        var connection = CreateConnection();
        var systemConnection = new PersistedConnection
        {
            TenantId = connection.TenantId,
            ConnectionId = connection.ConnectionId,
            ConcurrencyToken = connection.ConcurrencyToken,
            Name = connection.Name,
            Strategy = connection.Strategy,
            DisplayName = connection.DisplayName,
            IsSystem = true,
            IsEnabled = connection.IsEnabled,
            Options = connection.Options,
            Settings = connection.Settings,
        };
        SetupGetConnection(systemConnection);

        var result = await Handler.DeleteAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal(
            "A reserved system connection cannot be deleted.",
            problem.ProblemDetails.Detail
        );
    }

    [Fact]
    public async Task DeleteAsync_WhenADependentSourceReportsDependents_ReturnsConflict()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        var connection = CreateConnection();
        SetupGetConnection(connection);
        MockDependentSource
            .Setup(x =>
                x.HasDependentsAsync(
                    MockStoreManager.Object,
                    connection,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true)
            .Verifiable();

        var result = await Handler.DeleteAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal(
            "The connection still holds identities or local accounts.",
            problem.ProblemDetails.Detail
        );
    }

    [Fact]
    public async Task DeleteAsync_WhenNothingDependsOnIt_RemovesAndAudits()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupSave();
        var connection = CreateConnection();
        SetupGetConnection(connection);
        MockDependentSource
            .Setup(x =>
                x.HasDependentsAsync(
                    MockStoreManager.Object,
                    connection,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(false)
            .Verifiable();
        MockConnectionStore
            .Setup(x => x.RemoveAsync(ConnectionId, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupRecorder(ConnectionId, ResourceChangeTypes.Deleted);

        var result = await Handler.DeleteAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
    }

    #endregion

    #region Settings Tests

    private void SetupConnectionLimits(params string[] limits) =>
        MockSettingSerializer
            .Setup(x => x.GetLimits(OpenIdSettingScopes.Connection))
            .Returns(limits)
            .Verifiable();

    private void SetupNoChangedLimits() =>
        MockSettingSerializer
            .Setup(x => x.GetChangedLimits(It.IsAny<JsonElement>(), It.IsAny<JsonElement>()))
            .Returns([])
            .Verifiable();

    [Fact]
    public async Task GetSettingsAsync_WhenAuthorized_ReturnsTheSettings()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupGetConnection(CreateConnection());

        var result = await Handler.GetSettingsAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<ConnectionSettingsResource>>(result);
        Assert.Equal(ConnectionId, json.Value?.ConnectionId);
    }

    [Fact]
    public async Task GetSettingsAsync_WhenTheConnectionIsMissing_ReturnsNotFound()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupGetConnection(null);

        var result = await Handler.GetSettingsAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task GetSettingLimitsAsync_WhenTheCallerMayUpdateTheTenant_ReportsCanChange()
    {
        SetupConnectionLimits(OpenIdSettingNames.PasswordMinLength);
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupGetConnection(CreateConnection());

        var result = await Handler.GetSettingLimitsAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<SettingLimitsResource>>(result);
        Assert.Equal([OpenIdSettingNames.PasswordMinLength], json.Value?.Names);
        Assert.True(json.Value?.CanChange);
    }

    [Fact]
    public async Task GetSettingLimitsAsync_WhenTheConnectionIsMissing_ReturnsNotFound()
    {
        SetupConnectionLimits(OpenIdSettingNames.PasswordMinLength);
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupGetConnection(null);

        var result = await Handler.GetSettingLimitsAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task UpdateSettingsAsync_WhenTheSettingsAreValid_UpdatesAndAudits()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupSave();
        SetupValidSettings();
        SetupNoChangedLimits();
        SetupGetConnection(CreateConnection());
        MockConnectionStore
            .Setup(x =>
                x.UpdateSettingsAsync(
                    It.Is<PersistedConnectionSettings>(settings =>
                        settings.Value.GetProperty("password_min_length").GetInt32() == 12
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupRecorder(ConnectionId, ResourceChangeTypes.SettingsUpdated);
        var patch = new JsonPatchDocument<JsonObject>();
        patch.Operations.Add(
            new Operation<JsonObject>("add", "/password_min_length", from: null, value: 12)
        );

        var result = await Handler.UpdateSettingsAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            patch,
            ifMatch: null,
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task UpdateSettingsAsync_WhenASettingDoesNotApplyToAConnection_ReturnsValidationProblem()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupGetConnection(CreateConnection());
        MockSettingSerializer
            .Setup(x =>
                x.ValidateSettings(
                    It.IsAny<JsonElement>(),
                    OpenIdSettingScopes.Connection,
                    It.IsAny<JsonSerializerOptions>()
                )
            )
            .Returns([
                new SettingError("require_pkce", "'require_pkce' does not apply to a connection."),
            ])
            .Verifiable();
        var patch = new JsonPatchDocument<JsonObject>();
        patch.Operations.Add(
            new Operation<JsonObject>("add", "/require_pkce", from: null, value: true)
        );

        // Strict mocks: no UpdateSettingsAsync setup, so a write would fail the test.
        var result = await Handler.UpdateSettingsAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            patch,
            ifMatch: null,
            CancellationToken.None
        );

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(["require_pkce"], problem.ProblemDetails.Errors.Keys);
    }

    [Fact]
    public async Task UpdateSettingsAsync_WhenTheEntityTagIsStale_ReturnsPreconditionFailed()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupGetConnection(CreateConnection());

        var result = await Handler.UpdateSettingsAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            new JsonPatchDocument<JsonObject>(),
            ManagementEntityTags.Format("stale-settings-ct"),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, problem.StatusCode);
    }

    [Fact]
    public async Task UpdateSettingsAsync_WhenTheConnectionIsMissing_ReturnsNotFound()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupGetConnection(null);

        var result = await Handler.UpdateSettingsAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            new JsonPatchDocument<JsonObject>(),
            ifMatch: null,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task GetEffectiveSettingsAsync_NarrowsTheTenantsSettingsWithTheConnectionsOwn()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupGetConnection(CreateConnection());
        var mockTenantStore = MockRepository.Create<ITenantStore>();
        MockStoreManager
            .Setup(x => x.GetStore<ITenantStore>())
            .Returns(mockTenantStore.Object)
            .Verifiable();
        var tenant = new PersistedTenantBuilder
        {
            TenantId = TenantId,
            Settings = JsonSerializer.SerializeToElement(
                new Dictionary<string, object> { ["password_min_length"] = 8 }
            ),
        }.Build();
        mockTenantStore
            .Setup(x => x.GetOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant)
            .Verifiable();

        var passwordMinLength = new SettingDescriptor<int>
        {
            Name = "password_min_length",
            AppliesTo = ["tenant", "connection"],
        };
        var requirePkce = new SettingDescriptor<bool>
        {
            Name = "require_pkce",
            AppliesTo = ["client"],
        };
        IReadOnlyCollection<Setting> effectiveScopeSettings =
        [
            passwordMinLength.Create(12),
            requirePkce.Create(true),
        ];
        var effectiveSettings = new Mock<ISettingCollection>(MockBehavior.Loose);
        effectiveSettings
            .Setup(x => x.GetEnumerator())
            .Returns(() => effectiveScopeSettings.GetEnumerator());
        var teamSettings = MockRepository.Create<IReadOnlySettingCollection>().Object;
        var tenantSettings = MockRepository.Create<IReadOnlySettingCollection>().Object;
        MockEffectiveSettingsResolver
            .Setup(x => x.ResolveTeamAsync(tenant.TeamId, It.IsAny<CancellationToken>()))
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
        MockEffectiveSettingsResolver
            .Setup(x =>
                x.ResolveStored(
                    tenantSettings,
                    OpenIdSettingScopes.Connection,
                    ConnectionId,
                    It.IsAny<JsonElement>()
                )
            )
            .Returns(effectiveSettings.Object)
            .Verifiable();
        var mockScopes = MockRepository.Create<ISettingScopeCollection>();
        mockScopes
            .Setup(x => x.AppliesWithin(passwordMinLength, OpenIdSettingScopes.Connection))
            .Returns(true)
            .Verifiable();
        mockScopes
            .Setup(x => x.AppliesWithin(requirePkce, OpenIdSettingScopes.Connection))
            .Returns(false)
            .Verifiable();
        MockSettingScopeCollectionProvider
            .Setup(x => x.Collection)
            .Returns(mockScopes.Object)
            .Verifiable();

        var result = await Handler.GetEffectiveSettingsAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<ConnectionEffectiveSettingsResource>>(result);
        Assert.Equal(12, json.Value!.Settings.GetProperty("password_min_length").GetInt32());
        Assert.False(json.Value.Settings.TryGetProperty("require_pkce", out _));
    }

    [Fact]
    public async Task GetEffectiveSettingsAsync_WhenForbidden_ReturnsForbidWithoutReadingTheConnection()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var result = await Handler.GetEffectiveSettingsAsync(
            CreateHttpContext(),
            TenantId,
            ConnectionId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    #endregion

    #region Map Tests

    [Theory]
    [InlineData("GET", "/connections", new[] { 400, 401, 403 })]
    [InlineData("POST", "/connections", new[] { 400, 401, 403, 409, 412, 415 })]
    [InlineData("GET", "/connections/{connectionId}", new[] { 401, 403, 404 })]
    [InlineData("PATCH", "/connections/{connectionId}", new[] { 400, 401, 403, 404, 412, 415 })]
    [InlineData("DELETE", "/connections/{connectionId}", new[] { 401, 403, 404, 409, 412 })]
    [InlineData("GET", "/connections/{connectionId}/settings", new[] { 401, 403, 404 })]
    [InlineData("GET", "/connections/{connectionId}/settings/limits", new[] { 401, 403, 404 })]
    [InlineData(
        "PATCH",
        "/connections/{connectionId}/settings",
        new[] { 400, 401, 403, 404, 412, 415 }
    )]
    [InlineData("GET", "/connections/{connectionId}/effective-settings", new[] { 401, 403, 404 })]
    public async Task Map_Always_DeclaresAndDescribesEveryProblemARouteAnswers(
        string httpMethod,
        string routePattern,
        int[] statusCodes
    )
    {
        Assert.Equal(
            statusCodes,
            await EndpointProblemMetadata.ReadStatusCodesAsync(
                Handler,
                httpMethod,
                routePattern,
                CancellationToken.None
            )
        );
        Assert.Empty(
            await EndpointProblemMetadata.ReadUndescribedStatusCodesAsync(
                Handler,
                httpMethod,
                routePattern,
                CancellationToken.None
            )
        );
    }

    #endregion
}
