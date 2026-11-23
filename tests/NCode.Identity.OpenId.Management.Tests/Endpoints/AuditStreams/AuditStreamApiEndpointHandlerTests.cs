// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NCode.Identity.AspNetCore;
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
using NCode.Identity.SharedState;
using NCode.Mediator;
using SystemTextJsonPatch;
using SystemTextJsonPatch.Operations;

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
    private Mock<IExpiringValueSet> MockExpiringValueSet { get; }
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
        MockExpiringValueSet = MockRepository.Create<IExpiringValueSet>();
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
            MockExpiringValueSet.Object,
            Time,
            NullLogger<AuditStreamApiEndpointHandler>.Instance
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

    private static string CreateGuardKey { get; } =
        ExpiringKeys.Create("audit-stream-create", TenantId);

    private static bool IsGuardOwner(ReadOnlyMemory<byte> value)
    {
        if (value.Length != 16)
        {
            return false;
        }

        foreach (var b in value.Span)
        {
            if (b != SecretByte)
            {
                return false;
            }
        }

        return true;
    }

    private void SetupCreateGuard(bool taken)
    {
        MockExpiringValueSet
            .Setup(x =>
                x.TryAddAsync(
                    CreateGuardKey,
                    It.Is<ReadOnlyMemory<byte>>(value => IsGuardOwner(value)),
                    Start.AddSeconds(30),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(taken)
            .Verifiable();
    }

    private void SetupCreateGuardRelease()
    {
        MockExpiringValueSet
            .Setup(x =>
                x.TryRemoveAsync(
                    CreateGuardKey,
                    It.Is<ReadOnlyMemory<byte>>(value => IsGuardOwner(value)),
                    It.Is<CancellationToken>(token => token.CanBeCanceled)
                )
            )
            .ReturnsAsync(true)
            .Verifiable();
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

    private void SetupTenantContext(int maxAuditStreams, string routeTenantId = TenantId)
    {
        MockOpenIdContextFactory
            .Setup(x =>
                x.CreateForTenantAsync(
                    It.IsAny<HttpContext>(),
                    routeTenantId,
                    It.IsAny<IMediator>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                Mock.Of<OpenIdContext>(context =>
                    context.Tenant.SettingsProvider.Collection.GetValue(
                        OpenIdSettingKeys.MaxAuditStreams
                    ) == maxAuditStreams
                    && context.Tenant.TenantId == TenantId
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

    private async ValueTask<ValidationProblem> CreateWithRefusalAsync(
        string displayName,
        string endpoint,
        CancellationToken cancellationToken
    )
    {
        // Strict mocks: no store setup, so continuing past the refusal would fail the test.
        SetupAuthorization(AuthorizationResult.Success());
        var request = new CreateAuditStreamRequest
        {
            DisplayName = displayName,
            Endpoint = endpoint,
        };

        var result = await Handler.CreateAsync(
            CreateHttpContext(),
            TenantId,
            request,
            cancellationToken
        );

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        return problem;
    }

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
        var problem = await CreateWithRefusalAsync(
            displayName: "Warehouse",
            endpoint: "http://example.com/audit",
            cancellationToken: CancellationToken.None
        );

        Assert.Equal(["endpoint"], problem.ProblemDetails.Errors.Keys);
        Assert.Equal(
            ["'endpoint' must be an absolute https URL."],
            problem.ProblemDetails.Errors["endpoint"]
        );
    }

    [Fact]
    public async Task CreateAsync_WithAnEmptyDisplayName_ReturnsBadRequest()
    {
        var problem = await CreateWithRefusalAsync(
            displayName: "",
            endpoint: "https://example.com/audit",
            cancellationToken: CancellationToken.None
        );

        Assert.Equal(["displayName"], problem.ProblemDetails.Errors.Keys);
        Assert.Equal(
            ["'displayName' must not be empty."],
            problem.ProblemDetails.Errors["displayName"]
        );
    }

    [Fact]
    public async Task CreateAsync_WithADisplayNameOver300Characters_ReturnsBadRequest()
    {
        var problem = await CreateWithRefusalAsync(
            displayName: new string('a', 301),
            endpoint: "https://example.com/audit",
            cancellationToken: CancellationToken.None
        );

        Assert.Equal(["displayName"], problem.ProblemDetails.Errors.Keys);
        Assert.Equal(
            ["'displayName' must be at most 300 characters."],
            problem.ProblemDetails.Errors["displayName"]
        );
    }

    [Fact]
    public async Task CreateAsync_WithAControlCharacterInTheDisplayName_ReturnsBadRequest()
    {
        var problem = await CreateWithRefusalAsync(
            displayName: "Ware\nhouse",
            endpoint: "https://example.com/audit",
            cancellationToken: CancellationToken.None
        );

        Assert.Equal(["displayName"], problem.ProblemDetails.Errors.Keys);
        Assert.Equal(
            ["'displayName' must not contain control characters."],
            problem.ProblemDetails.Errors["displayName"]
        );
    }

    [Fact]
    public async Task CreateAsync_WithUserInformationInTheEndpoint_ReturnsBadRequest()
    {
        var problem = await CreateWithRefusalAsync(
            displayName: "Warehouse",
            endpoint: "https://user:password@example.com/audit",
            cancellationToken: CancellationToken.None
        );

        Assert.Equal(["endpoint"], problem.ProblemDetails.Errors.Keys);
        Assert.Equal(
            ["'endpoint' must not carry user information."],
            problem.ProblemDetails.Errors["endpoint"]
        );
    }

    [Fact]
    public async Task CreateAsync_WithAFragmentInTheEndpoint_ReturnsBadRequest()
    {
        var problem = await CreateWithRefusalAsync(
            displayName: "Warehouse",
            endpoint: "https://example.com/audit#section",
            cancellationToken: CancellationToken.None
        );

        Assert.Equal(["endpoint"], problem.ProblemDetails.Errors.Keys);
        Assert.Equal(
            ["'endpoint' must not carry a fragment."],
            problem.ProblemDetails.Errors["endpoint"]
        );
    }

    [Fact]
    public async Task CreateAsync_WithAnEndpointOver1000Characters_ReturnsBadRequest()
    {
        var problem = await CreateWithRefusalAsync(
            displayName: "Warehouse",
            endpoint: "https://example.com/" + new string('a', 1000),
            cancellationToken: CancellationToken.None
        );

        Assert.Equal(["endpoint"], problem.ProblemDetails.Errors.Keys);
        Assert.Equal(
            ["'endpoint' must be at most 1000 characters."],
            problem.ProblemDetails.Errors["endpoint"]
        );
    }

    [Fact]
    public async Task CreateAsync_WhenTheTenantHasItsLimit_ReturnsConflict()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupTenantContext(maxAuditStreams: 2);
        SetupCreateGuard(taken: true);
        SetupCreateGuardRelease();
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
    public async Task CreateAsync_WhenAnotherCreateOfTheTenantRuns_ReturnsConflictWithoutCounting()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupTenantContext(maxAuditStreams: 2);
        SetupCreateGuard(taken: false);
        var request = new CreateAuditStreamRequest
        {
            DisplayName = "Warehouse",
            Endpoint = "https://example.com/audit",
        };

        // Strict mocks: no store setup and no guard removal, so counting or releasing a guard that
        // another create holds would fail the test.
        var httpContext = CreateHttpContext();
        var result = await Handler.CreateAsync(
            httpContext,
            TenantId,
            request,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal(
            "Another audit stream of the tenant is being created; try again.",
            problem.ProblemDetails.Detail
        );
        Assert.Equal("1", httpContext.Response.Headers.RetryAfter.ToString());
        Assert.Equal(1L, problem.ProblemDetails.Extensions[RetryAfterProblem.MemberName]);
    }

    [Fact]
    public async Task CreateAsync_WhenTheRouteSpellsTheTenantInAnotherCase_TakesTheCanonicalTenantsGuard()
    {
        const string routeTenantId = "TENANT-1";
        SetupAuthorization(AuthorizationResult.Success());
        SetupTenantContext(maxAuditStreams: 2, routeTenantId);
        SetupCreateGuard(taken: false);
        var request = new CreateAuditStreamRequest
        {
            DisplayName = "Warehouse",
            Endpoint = "https://example.com/audit",
        };

        var result = await Handler.CreateAsync(
            CreateHttpContext(),
            routeTenantId,
            request,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_Always_ReleasesTheCreateGuard()
    {
        CryptoService.ResourceIds.Enqueue(NewStreamId);
        CryptoService.ResourceIds.Enqueue(NewSecretId);
        SetupAuthorization(AuthorizationResult.Success());
        SetupTenantContext(maxAuditStreams: 2);
        SetupCreateGuard(taken: true);
        SetupCreateGuardRelease();
        SetupStore();
        SetupProtector();
        SetupSave();
        var counts = 0;
        MockAuditStreamStore
            .Setup(x => x.CountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++counts == 1 ? 2 : 1)
            .Verifiable(Times.Exactly(2));
        MockAuditStreamStore
            .Setup(x => x.AddAsync(It.IsAny<PersistedAuditStream>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupRecorder(NewStreamId, ResourceChangeTypes.Created);
        var request = new CreateAuditStreamRequest
        {
            DisplayName = "Warehouse",
            Endpoint = "https://example.com/audit",
        };

        var atTheLimit = await Handler.CreateAsync(
            CreateHttpContext(),
            TenantId,
            request,
            CancellationToken.None
        );
        var afterTheLimit = await Handler.CreateAsync(
            CreateHttpContext(),
            TenantId,
            request,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(atTheLimit);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.IsType<Created<CreatedAuditStreamResource>>(afterTheLimit);
        MockExpiringValueSet.Verify(
            x =>
                x.TryRemoveAsync(
                    CreateGuardKey,
                    It.Is<ReadOnlyMemory<byte>>(value => IsGuardOwner(value)),
                    It.Is<CancellationToken>(token => token.CanBeCanceled)
                ),
            Times.Exactly(2)
        );
    }

    [Fact]
    public async Task CreateAsync_WhenTheGuardReleaseFails_StillReturnsCreated()
    {
        CryptoService.ResourceIds.Enqueue(NewStreamId);
        CryptoService.ResourceIds.Enqueue(NewSecretId);
        SetupAuthorization(AuthorizationResult.Success());
        SetupTenantContext(maxAuditStreams: 2);
        SetupCreateGuard(taken: true);
        MockExpiringValueSet
            .Setup(x =>
                x.TryRemoveAsync(
                    CreateGuardKey,
                    It.Is<ReadOnlyMemory<byte>>(value => IsGuardOwner(value)),
                    It.Is<CancellationToken>(token => token.CanBeCanceled)
                )
            )
            .ThrowsAsync(new InvalidOperationException("release failed"))
            .Verifiable();
        SetupStore();
        SetupProtector();
        SetupSave();
        MockAuditStreamStore
            .Setup(x => x.CountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1)
            .Verifiable();
        MockAuditStreamStore
            .Setup(x => x.AddAsync(It.IsAny<PersistedAuditStream>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        SetupRecorder(NewStreamId, ResourceChangeTypes.Created);
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

        Assert.IsType<Created<CreatedAuditStreamResource>>(result);
    }

    [Fact]
    public async Task CreateAsync_WhenValid_StoresAProtectedSecretAndReturnsItOnce()
    {
        CryptoService.ResourceIds.Enqueue(NewStreamId);
        CryptoService.ResourceIds.Enqueue(NewSecretId);
        SetupAuthorization(AuthorizationResult.Success());
        SetupTenantContext(maxAuditStreams: 2);
        SetupCreateGuard(taken: true);
        SetupCreateGuardRelease();
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
        var patch = new JsonPatchDocument<UpdateAuditStreamRequest>();
        patch.Replace(x => x.DisplayName, "Renamed");

        var result = await Handler.UpdateAsync(
            CreateHttpContext(),
            TenantId,
            StreamId,
            patch,
            ManagementEntityTags.Format("another-ct"),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status412PreconditionFailed, problem.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenThePatchedEndpointIsHttp_ReturnsBadRequest()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        MockAuditStreamStore
            .Setup(x => x.GetOrDefaultAsync(StreamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStream())
            .Verifiable();
        // Strict mocks: no update or save setup, so writing the refused stream would fail the test.
        var patch = new JsonPatchDocument<UpdateAuditStreamRequest>();
        patch.Replace(x => x.Endpoint, "http://example.com/changed");

        var result = await Handler.UpdateAsync(
            CreateHttpContext(),
            TenantId,
            StreamId,
            patch,
            ManagementEntityTags.Format(StreamConcurrencyToken),
            CancellationToken.None
        );

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(["endpoint"], problem.ProblemDetails.Errors.Keys);
        Assert.Equal(
            ["'endpoint' must be an absolute https URL."],
            problem.ProblemDetails.Errors["endpoint"]
        );
    }

    [Fact]
    public async Task UpdateAsync_WhenThePatchCannotBeApplied_ReturnsBadRequest()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        MockAuditStreamStore
            .Setup(x => x.GetOrDefaultAsync(StreamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStream())
            .Verifiable();
        // Replace on a path that does not exist throws a JsonPatchException.
        var patch = new JsonPatchDocument<UpdateAuditStreamRequest>();
        patch.Operations.Add(
            new Operation<UpdateAuditStreamRequest>("replace", "/missing", from: null, value: "x")
        );

        var result = await Handler.UpdateAsync(
            CreateHttpContext(),
            TenantId,
            StreamId,
            patch,
            ManagementEntityTags.Format(StreamConcurrencyToken),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
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
        var request = new JsonPatchDocument<UpdateAuditStreamRequest>();
        request.Replace(x => x.DisplayName, "Renamed");
        request.Replace(x => x.Endpoint, "https://example.com/changed");
        request.Replace(x => x.IsEnabled, false);

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

    [Fact]
    public async Task RotateSigningSecretAsync_WhenThreeSecretsSign_ReturnsConflictUntilTheOldestStops()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore();
        var current = CreateSecret("current");
        var older = CreateSecret("older", expiryTime: Start.AddHours(2));
        var newer = CreateSecret("newer", expiryTime: Start.AddHours(5));
        var stream = CreateStream(signingSecrets: [current, older, newer]);
        MockAuditStreamStore
            .Setup(x => x.GetOrDefaultAsync(StreamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream)
            .Verifiable();

        // Strict mocks: no update, protector or save setup, so creating or storing a secret would fail the test.
        var httpContext = CreateHttpContext();
        var result = await Handler.RotateSigningSecretAsync(
            httpContext,
            TenantId,
            StreamId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal(
            "The audit stream has 3 signing secrets that sign, its maximum; a rotated secret stops signing 24 hours after its rotation.",
            problem.ProblemDetails.Detail
        );
        Assert.Equal("7200", httpContext.Response.Headers.RetryAfter.ToString());
        Assert.Equal(7200L, problem.ProblemDetails.Extensions[RetryAfterProblem.MemberName]);
        Assert.Equal(
            ["current", "older", "newer"],
            stream.SigningSecrets.Select(secret => secret.SecretId)
        );
    }

    [Fact]
    public async Task RotateSigningSecretAsync_WhenAnExpiredSecretFreesASlot_Rotates()
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

        Assert.IsType<Created<CreatedAuditStreamSigningSecretResource>>(result);
    }

    #endregion

    #region Map Tests

    [Theory]
    [InlineData("GET", "/audit-streams", new[] { 400, 401, 403 })]
    [InlineData("POST", "/audit-streams", new[] { 400, 401, 403, 409, 412, 415 })]
    [InlineData("GET", "/audit-streams/{streamId}", new[] { 401, 403, 404 })]
    [InlineData("PATCH", "/audit-streams/{streamId}", new[] { 400, 401, 403, 404, 412, 415 })]
    [InlineData("DELETE", "/audit-streams/{streamId}", new[] { 401, 403, 404, 412 })]
    [InlineData(
        "POST",
        "/audit-streams/{streamId}/signing-secrets",
        new[] { 401, 403, 404, 409, 412 }
    )]
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
