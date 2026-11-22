// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Contracts.AuditStreams;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.Endpoints.AuditStreams;

public sealed class AuditStreamApiEndpointHandlerTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string StreamId = "stream-1";
    private const string NewStreamId = "stream-new";
    private const string NewSecretId = "secret-new";
    private const string StreamConcurrencyToken = "stream-ct";
    private const string ProtectedSecretValue = "protected-secret-value";
    private const byte SecretByte = 0x2A;

    private static DateTimeOffset Start { get; } = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private MockRepository MockRepository { get; }
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IAuditStreamStore> MockAuditStreamStore { get; }
    private Mock<IAuthorizationService> MockAuthorizationService { get; }
    private Mock<ISecretProtectorProvider> MockSecretProtectorProvider { get; }
    private Mock<IOpenIdContextFactory> MockOpenIdContextFactory { get; }
    private Mock<IManagementAuditRecorder> MockManagementAuditRecorder { get; }
    private FakeCryptoService CryptoService { get; }
    private FakeSecretProtector Protector { get; }
    private FakeTimeProvider Time { get; }
    private AuditStreamApiEndpointHandler Handler { get; }

    public AuditStreamApiEndpointHandlerTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockAuditStreamStore = MockRepository.Create<IAuditStreamStore>();
        MockAuthorizationService = MockRepository.Create<IAuthorizationService>();
        MockSecretProtectorProvider = MockRepository.Create<ISecretProtectorProvider>();
        MockOpenIdContextFactory = MockRepository.Create<IOpenIdContextFactory>();
        MockManagementAuditRecorder = MockRepository.Create<IManagementAuditRecorder>();
        CryptoService = new FakeCryptoService();
        Protector = new FakeSecretProtector();
        Time = new FakeTimeProvider(Start);

        Handler = new AuditStreamApiEndpointHandler(
            MockStoreManagerFactory.Object,
            MockAuthorizationService.Object,
            CryptoService,
            MockSecretProtectorProvider.Object,
            MockOpenIdContextFactory.Object,
            MockManagementAuditRecorder.Object,
            Time
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

        public void GenerateBytes(Span<byte> destination) => destination.Fill(SecretByte);

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

    // Protects a value as its hexadecimal text, so a test reads the protected value back from the plaintext.
    private sealed class FakeSecretProtector : ISecretProtector
    {
        public SecretOwner Owner { get; } = SecretOwner.ForTenant(TenantId);

        public List<SecretProtectionContext> Contexts { get; } = [];

        public int GetProtectedLength(int plaintextLength) => plaintextLength * 2;

        public int GetMaxPlaintextLength(int protectedLength) => protectedLength / 2;

        public void Protect<TWriter>(
            ReadOnlySpan<byte> plaintext,
            SecretProtectionContext context,
            ref TWriter destination
        )
            where TWriter : IBufferWriter<char>, allows ref struct
        {
            Contexts.Add(context);
            var hex = Convert.ToHexString(plaintext);
            hex.CopyTo(destination.GetSpan(hex.Length));
            destination.Advance(hex.Length);
        }

        public void Unprotect<TWriter>(
            ReadOnlySpan<char> protectedValue,
            SecretProtectionContext context,
            ref TWriter destination
        )
            where TWriter : IBufferWriter<byte>, allows ref struct =>
            throw new NotSupportedException();

        public void Rewrap<TWriter>(
            ReadOnlySpan<char> protectedValue,
            SecretProtectionContext context,
            ref TWriter destination
        )
            where TWriter : IBufferWriter<char>, allows ref struct =>
            throw new NotSupportedException();
    }

    private void SetupStore()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();

        MockStoreManager
            .Setup(x => x.GetStore<IAuditStreamStore>())
            .Returns(MockAuditStreamStore.Object)
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

    private void SetupTenantContext(int maxAuditStreams)
    {
        MockOpenIdContextFactory
            .Setup(x =>
                x.CreateForTenantAsync(
                    It.IsAny<HttpContext>(),
                    TenantId,
                    It.IsAny<IMediator>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                Mock.Of<OpenIdContext>(context =>
                    context.Tenant.SettingsProvider.Collection.GetValue(
                        OpenIdSettingKeys.MaxAuditStreams
                    ) == maxAuditStreams
                )
            )
            .Verifiable();
    }

    private void SetupProtector()
    {
        MockSecretProtectorProvider
            .Setup(x =>
                x.GetProtectorAsync(
                    SecretOwner.ForTenant(TenantId),
                    It.Is<IReadOnlyCollection<string>>(values => values.Count == 0),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(new ValueTask<ISecretProtector>(Protector))
            .Verifiable();
    }

    private void SetupRecorder(
        string streamId,
        string changeType,
        Action<JsonElement?>? capture = null
    )
    {
        MockManagementAuditRecorder
            .Setup(x =>
                x.RecordAuditStreamChangedAsync(
                    It.IsAny<HttpContext>(),
                    TenantId,
                    streamId,
                    changeType,
                    It.IsAny<JsonElement?>()
                )
            )
            .Callback<HttpContext, string, string, string, JsonElement?>(
                (_, _, _, _, values) => capture?.Invoke(values)
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
    }

    private static HttpContext CreateHttpContext(bool authenticated = true)
    {
        var identity = authenticated
            ? new ClaimsIdentity(authenticationType: "test")
            : new ClaimsIdentity();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(Mock.Of<IMediator>());
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = serviceCollection.BuildServiceProvider(),
        };
    }

    private static PersistedAuditStreamSigningSecret CreateSecret(
        string secretId,
        DateTimeOffset? expiryTime = null
    ) =>
        new()
        {
            SecretId = secretId,
            ProtectedValue = ProtectedSecretValue,
            CreatedTime = Start.AddDays(-1),
            ExpiryTime = expiryTime,
        };

    private static PersistedAuditStream CreateStream(
        string concurrencyToken = StreamConcurrencyToken,
        string displayName = "Warehouse",
        IReadOnlyList<PersistedAuditStreamSigningSecret>? signingSecrets = null
    ) =>
        new()
        {
            TenantId = TenantId,
            StreamId = StreamId,
            DisplayName = displayName,
            Endpoint = "https://example.com/audit",
            IsEnabled = true,
            CreatedTime = Start.AddDays(-1),
            ConcurrencyToken = concurrencyToken,
            SigningSecrets = signingSecrets ?? [CreateSecret("secret-1")],
        };

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_WhenUnauthorized_ReturnsForbiddenWithoutReading()
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
    public async Task ListAsync_WhenAuthorized_ReturnsTheStreams()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        MockAuditStreamStore
            .Setup(x => x.GetPageAsync(It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new PagedResult<PersistedAuditStream>
                {
                    Items = [CreateStream()],
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

        var json = Assert.IsType<JsonHttpResult<CollectionResource<AuditStreamResource>>>(result);
        Assert.NotNull(json.Value);
        var item = Assert.Single(json.Value.Items);
        Assert.Equal(StreamId, item.StreamId);
        Assert.Equal("next", json.Value.ContinuationToken);
    }

    #endregion

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_WithAnHttpEndpoint_ReturnsBadRequest()
    {
        SetupAuthorization(AuthorizationResult.Success());
        var request = new CreateAuditStreamRequest
        {
            DisplayName = "Warehouse",
            Endpoint = "http://example.com/audit",
        };

        var result = await Handler.CreateAsync(
            CreateHttpContext(),
            TenantId,
            request,
            CancellationToken.None
        );

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(["endpoint"], problem.ProblemDetails.Errors.Keys);
    }

    [Fact]
    public async Task CreateAsync_WhenTheTenantHasItsLimit_ReturnsConflict()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupTenantContext(maxAuditStreams: 2);
        SetupStore();
        MockAuditStreamStore
            .Setup(x => x.CountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(2)
            .Verifiable();
        var request = new CreateAuditStreamRequest
        {
            DisplayName = "Warehouse",
            Endpoint = "https://example.com/audit",
        };

        var result = await Handler.CreateAsync(
            CreateHttpContext(),
            TenantId,
            request,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_WhenValid_StoresAProtectedSecretAndReturnsItOnce()
    {
        CryptoService.ResourceIds.Enqueue(NewStreamId);
        CryptoService.ResourceIds.Enqueue(NewSecretId);
        SetupAuthorization(AuthorizationResult.Success());
        SetupTenantContext(maxAuditStreams: 2);
        SetupStore();
        SetupProtector();
        SetupSave();
        MockAuditStreamStore
            .Setup(x => x.CountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1)
            .Verifiable();
        PersistedAuditStream? stored = null;
        MockAuditStreamStore
            .Setup(x => x.AddAsync(It.IsAny<PersistedAuditStream>(), It.IsAny<CancellationToken>()))
            .Callback<PersistedAuditStream, CancellationToken>(
                (stream, _) =>
                {
                    stored = stream;
                    stream.ConcurrencyToken = "created-ct";
                }
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        JsonElement? auditValues = null;
        SetupRecorder(NewStreamId, ResourceChangeTypes.Created, values => auditValues = values);
        var httpContext = CreateHttpContext();
        var request = new CreateAuditStreamRequest
        {
            DisplayName = "Warehouse",
            Endpoint = "https://example.com/audit",
        };

        var result = await Handler.CreateAsync(
            httpContext,
            TenantId,
            request,
            CancellationToken.None
        );

        var secretBytes = Enumerable.Repeat(SecretByte, 32).ToArray();
        var expectedSecret = "whsec_" + Convert.ToBase64String(secretBytes);
        var created = Assert.IsType<Created<CreatedAuditStreamResource>>(result);
        Assert.NotNull(created.Value);
        Assert.Equal(NewStreamId, created.Value.AuditStream.StreamId);
        Assert.Equal(NewSecretId, created.Value.SigningSecret.SecretId);
        Assert.Equal(expectedSecret, created.Value.SigningSecret.SigningSecret);
        Assert.Equal(
            ManagementEntityTags.Format("created-ct"),
            httpContext.Response.Headers.ETag.ToString()
        );

        Assert.NotNull(stored);
        Assert.Equal(TenantId, stored.TenantId);
        Assert.True(stored.IsEnabled);
        var storedSecret = Assert.Single(stored.SigningSecrets);
        Assert.Equal(NewSecretId, storedSecret.SecretId);
        Assert.Equal(Convert.ToHexString(secretBytes), storedSecret.ProtectedValue);
        Assert.Null(storedSecret.ExpiryTime);
        Assert.Equal(
            [SecretProtectionContext.ForAuditStreamSigningSecret(NewSecretId)],
            Protector.Contexts
        );

        Assert.NotNull(auditValues);
        var auditText = auditValues.Value.GetRawText();
        Assert.DoesNotContain(expectedSecret, auditText);
        Assert.DoesNotContain(storedSecret.ProtectedValue, auditText);
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_WhenMissing_ReturnsNotFound()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        MockAuditStreamStore
            .Setup(x => x.GetOrDefaultAsync(StreamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedAuditStream?)null)
            .Verifiable();

        var result = await Handler.GetAsync(
            CreateHttpContext(),
            TenantId,
            StreamId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task GetAsync_Always_ReturnsNoSecretValue()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        MockAuditStreamStore
            .Setup(x => x.GetOrDefaultAsync(StreamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStream())
            .Verifiable();
        var httpContext = CreateHttpContext();

        var result = await Handler.GetAsync(
            httpContext,
            TenantId,
            StreamId,
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<AuditStreamResource>>(result);
        Assert.NotNull(json.Value);
        var text = JsonSerializer.Serialize(json.Value, JsonSerializerOptions.Web);
        Assert.DoesNotContain(ProtectedSecretValue, text);
        Assert.Equal("secret-1", Assert.Single(json.Value.SigningSecrets).SecretId);
        Assert.Equal(
            ManagementEntityTags.Format(StreamConcurrencyToken),
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
        MockAuditStreamStore
            .Setup(x => x.GetOrDefaultAsync(StreamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStream())
            .Verifiable();
        var request = new UpdateAuditStreamRequest
        {
            DisplayName = "Renamed",
            Endpoint = "https://example.com/audit",
            IsEnabled = false,
        };

        var result = await Handler.UpdateAsync(
            CreateHttpContext(),
            TenantId,
            StreamId,
            request,
            ManagementEntityTags.Format("another-ct"),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, problem.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenValid_UpdatesAndAudits()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupSave();
        var stream = CreateStream();
        var saved = CreateStream(concurrencyToken: "updated-ct", displayName: "Renamed");
        MockAuditStreamStore
            .SetupSequence(x => x.GetOrDefaultAsync(StreamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream)
            .ReturnsAsync(saved);
        MockAuditStreamStore
            .Setup(x =>
                x.UpdateAsync(
                    It.Is<PersistedAuditStream>(updated =>
                        updated.DisplayName == "Renamed"
                        && updated.Endpoint == "https://example.com/changed"
                        && !updated.IsEnabled
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupRecorder(StreamId, ResourceChangeTypes.Updated);
        var httpContext = CreateHttpContext();
        var request = new UpdateAuditStreamRequest
        {
            DisplayName = "Renamed",
            Endpoint = "https://example.com/changed",
            IsEnabled = false,
        };

        var result = await Handler.UpdateAsync(
            httpContext,
            TenantId,
            StreamId,
            request,
            ManagementEntityTags.Format(StreamConcurrencyToken),
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<AuditStreamResource>>(result);
        Assert.NotNull(json.Value);
        Assert.Equal("Renamed", json.Value.DisplayName);
        Assert.Equal(
            ManagementEntityTags.Format("updated-ct"),
            httpContext.Response.Headers.ETag.ToString()
        );
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WhenPresent_RemovesAndAudits()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupSave();
        MockAuditStreamStore
            .Setup(x => x.GetOrDefaultAsync(StreamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStream())
            .Verifiable();
        MockAuditStreamStore
            .Setup(x => x.RemoveAsync(StreamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Verifiable();
        SetupRecorder(StreamId, ResourceChangeTypes.Deleted);

        var result = await Handler.DeleteAsync(
            CreateHttpContext(),
            TenantId,
            StreamId,
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
    }

    #endregion

    #region RotateSigningSecretAsync Tests

    [Fact]
    public async Task RotateSigningSecretAsync_Always_KeepsThePreviousSecretFor24Hours()
    {
        CryptoService.ResourceIds.Enqueue(NewSecretId);
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        SetupProtector();
        SetupSave();
        var current = CreateSecret("current");
        var expired = CreateSecret("expired", expiryTime: Start.AddMinutes(-1));
        var overlapping = CreateSecret("overlapping", expiryTime: Start.AddHours(3));
        var stream = CreateStream(signingSecrets: [current, expired, overlapping]);
        MockAuditStreamStore
            .Setup(x => x.GetOrDefaultAsync(StreamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream)
            .Verifiable();
        MockAuditStreamStore
            .Setup(x => x.UpdateAsync(stream, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupRecorder(StreamId, ResourceChangeTypes.Updated);

        var result = await Handler.RotateSigningSecretAsync(
            CreateHttpContext(),
            TenantId,
            StreamId,
            CancellationToken.None
        );

        var created = Assert.IsType<Created<CreatedAuditStreamSigningSecretResource>>(result);
        Assert.NotNull(created.Value);
        Assert.Equal(NewSecretId, created.Value.SecretId);
        Assert.StartsWith("whsec_", created.Value.SigningSecret, StringComparison.Ordinal);
        Assert.Equal(
            ["current", "overlapping", NewSecretId],
            stream.SigningSecrets.Select(secret => secret.SecretId)
        );
        Assert.Equal(Start.AddHours(24), current.ExpiryTime);
        Assert.Equal(Start.AddHours(3), overlapping.ExpiryTime);
        Assert.Null(stream.SigningSecrets[2].ExpiryTime);
    }

    #endregion

    #region Map Tests

    [Theory]
    [InlineData("GET", "/audit-streams", new[] { 400, 401, 403 })]
    [InlineData("POST", "/audit-streams", new[] { 400, 401, 403, 409, 412, 415 })]
    [InlineData("GET", "/audit-streams/{streamId}", new[] { 401, 403, 404 })]
    [InlineData("PATCH", "/audit-streams/{streamId}", new[] { 400, 401, 403, 404, 412, 415 })]
    [InlineData("DELETE", "/audit-streams/{streamId}", new[] { 401, 403, 404, 412 })]
    [InlineData("POST", "/audit-streams/{streamId}/signing-secrets", new[] { 401, 403, 404, 412 })]
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
