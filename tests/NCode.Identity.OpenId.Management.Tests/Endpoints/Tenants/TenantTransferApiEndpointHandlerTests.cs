// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NCode.Identity.Cryptography;
using NCode.Identity.Jose;
using NCode.Identity.Jose.Algorithms;
using NCode.Identity.OpenId.Authentication.Clients;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Management.Transfers;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.Secrets.Persistence.DataContracts;
using NCode.Identity.Secrets.Persistence.Protection;

namespace NCode.Identity.OpenId.Management.Endpoints.Tenants;

// See ADR-1514.
public sealed class TenantTransferApiEndpointHandlerTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string TeamId = "team-1";
    private const string Region = "us-east";
    private const string CellName = "cell-a";
    private const string TargetCell = "cell-b";
    private const string MasterKeysPackage = "master-keys-package";
    private const string PackageText = "export-package";

    private MockRepository MockRepository { get; }
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ITenantStore> MockTenantStore { get; }
    private Mock<IAuthorizationService> MockAuthorizationService { get; }
    private Mock<ICryptoService> MockCryptoService { get; }
    private Mock<IClientKeyImporter> MockClientKeyImporter { get; }
    private Mock<ISecretSerializer> MockSecretSerializer { get; }
    private Mock<ITenantTransferContributor> MockCoreContributor { get; }
    private Mock<ITenantTransferContributor> MockAccountsContributor { get; }
    private Mock<IMasterKeyTransfer> MockMasterKeyTransfer { get; }
    private Mock<IJoseSerializer> MockJoseSerializer { get; }
    private Mock<IAlgorithmCollectionProvider> MockAlgorithmCollectionProvider { get; }
    private Mock<IAlgorithmCollection> MockAlgorithmCollection { get; }
    private Mock<KeyManagementAlgorithm> MockKeyManagement { get; }
    private Mock<AuthenticatedEncryptionAlgorithm> MockEncryption { get; }
    private Mock<ITenantDirectory> MockTenantDirectory { get; }
    private Mock<IManagementAuditRecorder> MockManagementAuditRecorder { get; }
    private FakeJoseEncoder Encoder { get; } = new();
    private TenantTransferApiEndpointHandler Handler { get; }

    public TenantTransferApiEndpointHandlerTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockTenantStore = MockRepository.Create<ITenantStore>();
        MockAuthorizationService = MockRepository.Create<IAuthorizationService>();
        MockCryptoService = MockRepository.Create<ICryptoService>();
        MockClientKeyImporter = MockRepository.Create<IClientKeyImporter>();
        MockSecretSerializer = MockRepository.Create<ISecretSerializer>();
        MockCoreContributor = MockRepository.Create<ITenantTransferContributor>();
        MockAccountsContributor = MockRepository.Create<ITenantTransferContributor>();
        MockMasterKeyTransfer = MockRepository.Create<IMasterKeyTransfer>();
        MockJoseSerializer = MockRepository.Create<IJoseSerializer>();
        MockAlgorithmCollectionProvider = MockRepository.Create<IAlgorithmCollectionProvider>();
        MockAlgorithmCollection = MockRepository.Create<IAlgorithmCollection>();
        MockKeyManagement = MockRepository.Create<KeyManagementAlgorithm>();
        MockEncryption = MockRepository.Create<AuthenticatedEncryptionAlgorithm>();
        MockTenantDirectory = MockRepository.Create<ITenantDirectory>();
        MockManagementAuditRecorder = MockRepository.Create<IManagementAuditRecorder>();

        // The handler reads each contributor's order once, to run them in order whatever order they are registered in.
        MockCoreContributor.Setup(x => x.Order).Returns(0).Verifiable();
        MockAccountsContributor.Setup(x => x.Order).Returns(100).Verifiable();

        Handler = new TenantTransferApiEndpointHandler(
            MockStoreManagerFactory.Object,
            MockAuthorizationService.Object,
            MockCryptoService.Object,
            MockClientKeyImporter.Object,
            MockSecretSerializer.Object,
            [MockAccountsContributor.Object, MockCoreContributor.Object],
            MockMasterKeyTransfer.Object,
            MockJoseSerializer.Object,
            MockAlgorithmCollectionProvider.Object,
            MockTenantDirectory.Object,
            MockManagementAuditRecorder.Object,
            Microsoft.Extensions.Options.Options.Create(
                new CellOptions { Name = CellName, Region = Region }
            ),
            TimeProvider.System
        );
    }

    public void Dispose()
    {
        MockRepository.Verify();
    }

    #region Helpers

    private static HttpContext CreateHttpContext(bool authenticated = true)
    {
        var identity = authenticated
            ? new ClaimsIdentity(authenticationType: "test")
            : new ClaimsIdentity();
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        // The shared environment filter publishes the context in the pipeline; emulate it for direct handler calls.
        httpContext.Features.Set<IOpenIdContextFeature>(
            Mock.Of<IOpenIdContextFeature>(feature =>
                feature.OpenIdContext
                == Mock.Of<OpenIdContext>(context =>
                    context.Tenant == Mock.Of<OpenIdTenant>(tenant => tenant.TenantId == "platform")
                )
            )
        );

        return httpContext;
    }

    private static TenantExportRequest CreateRequest() =>
        new()
        {
            Jwk = JsonSerializer.SerializeToElement(
                new
                {
                    kty = "EC",
                    crv = "P-256",
                    use = "enc",
                    alg = "ECDH-ES+A256KW",
                    x = "x-value",
                    y = "y-value",
                }
            ),
        };

    private static PersistedTenant CreateTenant(bool isDisabled) =>
        new PersistedTenantBuilder
        {
            TenantId = TenantId,
            TeamId = TeamId,
            IsDisabled = isDisabled,
            ConcurrencyToken = "tenant-ct",
            DisplayName = "Tenant One",
            SettingsConcurrencyToken = "settings-ct",
            SecretsConcurrencyToken = "secrets-ct",
        }.Build();

    private static PersistedSecret CreateRecipientSecret() =>
        new PersistedSecretBuilder
        {
            SecretId = TenantTransferApiEndpointHandler.RecipientSecretId,
            ConcurrencyToken = "secret-ct",
            Use = SecretKeyUses.Encryption,
            Algorithm = "ECDH-ES+A256KW",
            SecretType = SecretTypes.Ecc,
            SecretEncoding = SecretEncodings.Spki,
            EncodedValue = "spki-value",
        }.Build();

    private void SetupAuthorization(AuthorizationResult result) =>
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.Is<IEnumerable<IAuthorizationRequirement>>(requirements =>
                        requirements.Contains(Operations.Update)
                    )
                )
            )
            .ReturnsAsync(result)
            .Verifiable();

    private void SetupStore(PersistedTenant? tenant)
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();

        MockStoreManager
            .Setup(x => x.GetStore<ITenantStore>())
            .Returns(MockTenantStore.Object)
            .Verifiable();

        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();

        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant)
            .Verifiable();
    }

    // The importer accepts the recipient key and imports it under the client-key policy for encryption.
    private void SetupImportAccepted()
    {
        PersistedSecret? secret = CreateRecipientSecret();
        string? error = null;
        MockClientKeyImporter
            .Setup(x =>
                x.TryImportKey(
                    It.Is<SecretKeyImportRequest>(request =>
                        request.SecretType == SecretTypes.Ecc
                        && request.Format == SecretKeyImportFormats.Jwk
                        && request.Use == SecretKeyUses.Encryption
                        && request.Algorithm == "ECDH-ES+A256KW"
                    ),
                    out secret,
                    out error
                )
            )
            .Returns(true)
            .Verifiable();
    }

    // The serializer loads the imported secret as the recipient key. The key's members are read only until a check
    // refuses the key, so their setups are not verified.
    private AsymmetricSecretKey SetupRecipientKey(
        string keyType = SecretKeyTypes.Ecc,
        string? curveName = "P-256",
        string? use = SecretKeyUses.Encryption
    )
    {
        var key = new Mock<AsymmetricSecretKey>(
            MockBehavior.Strict,
            new KeyMetadata { Use = use },
            256,
            (object?)null,
            (object?)null
        );
        key.Setup(x => x.KeyType).Returns(keyType);
        key.Setup(x => x.CurveName).Returns(curveName);
        key.Setup(x => x.Metadata).Returns(new KeyMetadata { Use = use });

        MockSecretSerializer
            .Setup(x => x.DeserializePublicSecret(It.IsAny<PersistedSecret>()))
            .Returns(key.Object)
            .Verifiable();

        return key.Object;
    }

    // The serializer encrypts to the recipient key with the algorithms the export names and hands over the encoder
    // that captures the payload.
    private void SetupEncoder(SecretKey recipient)
    {
        KeyManagementAlgorithm? keyManagement = MockKeyManagement.Object;
        AuthenticatedEncryptionAlgorithm? encryption = MockEncryption.Object;

        MockAlgorithmCollectionProvider
            .Setup(x => x.Collection)
            .Returns(MockAlgorithmCollection.Object)
            .Verifiable();
        MockAlgorithmCollection
            .Setup(x =>
                x.TryGetKeyManagementAlgorithm(
                    AlgorithmCodes.KeyManagement.EcdhEsAes256,
                    out keyManagement
                )
            )
            .Returns(true)
            .Verifiable();
        MockAlgorithmCollection
            .Setup(x =>
                x.TryGetAuthenticatedEncryptionAlgorithm(
                    AlgorithmCodes.AuthenticatedEncryption.Aes256Gcm,
                    out encryption
                )
            )
            .Returns(true)
            .Verifiable();

        MockJoseSerializer
            .Setup(x =>
                x.CreateEncoder(
                    It.Is<JoseEncryptionOptions>(options =>
                        ReferenceEquals(options.EncryptionAlgorithm, MockEncryption.Object)
                        && options.Format == JoseSerializationFormat.Compact
                        && options.Recipients.Count == 1
                        && ReferenceEquals(
                            options.Recipients.Single().KeyManagementCredentials.SecretKey,
                            recipient
                        )
                        && ReferenceEquals(
                            options
                                .Recipients.Single()
                                .KeyManagementCredentials.KeyManagementAlgorithm,
                            MockKeyManagement.Object
                        )
                    )
                )
            )
            .Returns(Encoder)
            .Verifiable();
    }

    private List<string> SetupContributors()
    {
        var calls = new List<string>();

        MockCoreContributor
            .Setup(x => x.ExportAsync(TenantId, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls.Add("core");
                return new ValueTask<TenantTransferSection>(
                    new TenantTransferSection
                    {
                        Name = "core",
                        Json = JsonSerializer.SerializeToElement(
                            new { displayName = "Tenant One" }
                        ),
                    }
                );
            })
            .Verifiable();

        MockAccountsContributor
            .Setup(x => x.ExportAsync(TenantId, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls.Add("accounts");
                return new ValueTask<TenantTransferSection>(
                    new TenantTransferSection
                    {
                        Name = "accounts",
                        Json = JsonSerializer.SerializeToElement(new { count = 3 }),
                    }
                );
            })
            .Verifiable();

        return calls;
    }

    private void SetupMasterKeys(SecretKey recipient) =>
        MockMasterKeyTransfer
            .Setup(x =>
                x.ExportAsync(
                    It.Is<SecretOwner>(owner => owner == SecretOwner.ForTenant(TenantId)),
                    recipient,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(MasterKeysPackage)
            .Verifiable();

    private void SetupExportedAudit() =>
        MockManagementAuditRecorder
            .Setup(x =>
                x.RecordTenantChangedAsync(
                    It.IsAny<HttpContext>(),
                    TeamId,
                    TenantId,
                    "exported",
                    It.Is<JsonElement?>(values => values == null)
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private void SetupPlacement(string? cell) =>
        MockTenantDirectory
            .Setup(x => x.GetPlacementOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                cell is null
                    ? null
                    : new TenantPlacement
                    {
                        TenantId = TenantId,
                        Issuer = "https://issuer.example.com",
                        Cell = cell,
                        Region = Region,
                    }
            )
            .Verifiable();

    // The release removes the contributors' rows in reverse order, then the tenant, and saves.
    private List<string> SetupReleaseRemovals()
    {
        var calls = new List<string>();

        MockAccountsContributor
            .Setup(x => x.RemoveAsync(TenantId, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls.Add("remove-accounts");
                return ValueTask.CompletedTask;
            })
            .Verifiable();

        MockCoreContributor
            .Setup(x => x.RemoveAsync(TenantId, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls.Add("remove-core");
                return ValueTask.CompletedTask;
            })
            .Verifiable();

        MockTenantStore
            .Setup(x => x.RemoveMovedAsync(TenantId, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls.Add("remove-tenant");
                return new ValueTask<bool>(true);
            })
            .Verifiable();

        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls.Add("save");
                return ValueTask.CompletedTask;
            })
            .Verifiable();

        return calls;
    }

    private void SetupReleasedAudit() =>
        MockManagementAuditRecorder
            .Setup(x =>
                x.RecordTenantChangedAsync(
                    It.IsAny<HttpContext>(),
                    TeamId,
                    TenantId,
                    "released",
                    It.Is<JsonElement?>(values =>
                        values.HasValue
                        && values.Value.GetProperty("targetCell").GetString() == TargetCell
                    )
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    // An encoder that returns the package text and keeps the payload that it was asked to encrypt.
    private sealed class FakeJoseEncoder : JoseEncoder
    {
        public byte[] Payload { get; set; } = [];

        public override ValueTask<string> EncodeAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken
        )
        {
            Payload = payload.ToArray();
            return new ValueTask<string>(PackageText);
        }

        public override ValueTask EncodeAsync(
            IBufferWriter<char> tokenWriter,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public override ValueTask<string> EncodeAsync<T>(
            T payload,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public override ValueTask EncodeAsync<T>(
            IBufferWriter<char> tokenWriter,
            T payload,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public override ValueTask<string> EncodeAsync(
            string payload,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public override ValueTask EncodeAsync(
            IBufferWriter<char> tokenWriter,
            string payload,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public override ValueTask<string> EncodeAsync(
            ReadOnlyMemory<char> payload,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public override ValueTask EncodeAsync(
            IBufferWriter<char> tokenWriter,
            ReadOnlyMemory<char> payload,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    #endregion

    #region ExportTenantAsync Tests

    [Fact]
    public async Task ExportTenantAsync_WhenTheTenantIsDisabled_ReturnsTheSealedPackage()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupImportAccepted();
        var recipient = SetupRecipientKey();
        SetupStore(CreateTenant(isDisabled: true));
        var calls = SetupContributors();
        SetupMasterKeys(recipient);
        SetupEncoder(recipient);
        SetupExportedAudit();

        var result = await Handler.ExportTenantAsync(
            CreateHttpContext(),
            TenantId,
            CreateRequest(),
            CancellationToken.None
        );

        var ok = Assert.IsType<Ok<TenantExportResource>>(result);
        Assert.Equal(PackageText, ok.Value?.Package);
        Assert.Equal(["core", "accounts"], calls);

        var root = JsonElement.Parse(Encoder.Payload);
        Assert.Equal(Region, root.GetProperty("region").GetString());
        Assert.Equal(TenantId, root.GetProperty("tenantId").GetString());
        Assert.Equal(MasterKeysPackage, root.GetProperty("masterKeys").GetString());

        var sections = root.GetProperty("sections");
        Assert.Equal(2, sections.GetArrayLength());
        Assert.Equal("core", sections[0].GetProperty("name").GetString());
        Assert.Equal(
            "Tenant One",
            sections[0].GetProperty("json").GetProperty("displayName").GetString()
        );
        Assert.Equal("accounts", sections[1].GetProperty("name").GetString());
        Assert.Equal(3, sections[1].GetProperty("json").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task ExportTenantAsync_WhenTheTenantIsEnabled_ReturnsConflictAndExportsNothing()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupImportAccepted();
        SetupRecipientKey();
        SetupStore(CreateTenant(isDisabled: false));

        var result = await Handler.ExportTenantAsync(
            CreateHttpContext(),
            TenantId,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task ExportTenantAsync_WhenTheTenantDoesNotExist_ReturnsNotFound()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupImportAccepted();
        SetupRecipientKey();
        SetupStore(tenant: null);

        var result = await Handler.ExportTenantAsync(
            CreateHttpContext(),
            TenantId,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task ExportTenantAsync_WhenTheImporterRefusesTheKey_ReturnsUnprocessableEntity()
    {
        SetupAuthorization(AuthorizationResult.Success());
        PersistedSecret? secret = null;
        string? error = "The key must be a public key.";
        MockClientKeyImporter
            .Setup(x => x.TryImportKey(It.IsAny<SecretKeyImportRequest>(), out secret, out error))
            .Returns(false)
            .Verifiable();

        var result = await Handler.ExportTenantAsync(
            CreateHttpContext(),
            TenantId,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal("The key must be a public key.", problem.ProblemDetails.Detail);
    }

    [Theory]
    [InlineData(SecretKeyTypes.Rsa, null, SecretKeyUses.Encryption)]
    [InlineData(SecretKeyTypes.Ecc, "P-384", SecretKeyUses.Encryption)]
    [InlineData(SecretKeyTypes.Ecc, "P-256", SecretKeyUses.Signature)]
    public async Task ExportTenantAsync_WhenTheKeyIsNotAnEccP256EncryptionKey_ReturnsUnprocessableEntity(
        string keyType,
        string? curveName,
        string use
    )
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupImportAccepted();
        SetupRecipientKey(keyType, curveName, use);

        var result = await Handler.ExportTenantAsync(
            CreateHttpContext(),
            TenantId,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
    }

    [Fact]
    public async Task ExportTenantAsync_WhenTheKeyIsNotAJsonObject_ReturnsUnprocessableEntity()
    {
        SetupAuthorization(AuthorizationResult.Success());

        var result = await Handler.ExportTenantAsync(
            CreateHttpContext(),
            TenantId,
            new TenantExportRequest { Jwk = JsonSerializer.SerializeToElement("not-a-key") },
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
    }

    [Fact]
    public async Task ExportTenantAsync_WhenTheCallerIsNotAuthorized_ReturnsForbiddenBeforeReadingAnything()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var result = await Handler.ExportTenantAsync(
            CreateHttpContext(),
            TenantId,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task ExportTenantAsync_WhenTheCallerIsAnonymous_ReturnsUnauthorized()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var result = await Handler.ExportTenantAsync(
            CreateHttpContext(authenticated: false),
            TenantId,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);
    }

    #endregion

    #region ReleaseMovedTenantAsync Tests

    [Fact]
    public async Task ReleaseMovedTenantAsync_WhenTheDirectoryPlacesTheTenantInAnotherCell_RemovesItInReverseOrderAndAudits()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore(CreateTenant(isDisabled: true));
        SetupPlacement(TargetCell);
        var calls = SetupReleaseRemovals();
        SetupReleasedAudit();

        var result = await Handler.ReleaseMovedTenantAsync(
            CreateHttpContext(),
            TenantId,
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
        Assert.Equal(["remove-accounts", "remove-core", "remove-tenant", "save"], calls);
    }

    [Fact]
    public async Task ReleaseMovedTenantAsync_WhenTheDirectoryPlacesTheTenantInThisCell_ReturnsConflictAndReleasesNothing()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore(CreateTenant(isDisabled: true));
        SetupPlacement(CellName);

        var result = await Handler.ReleaseMovedTenantAsync(
            CreateHttpContext(),
            TenantId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task ReleaseMovedTenantAsync_WhenTheDirectoryDoesNotPlaceTheTenant_ReturnsConflictAndReleasesNothing()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore(CreateTenant(isDisabled: true));
        SetupPlacement(cell: null);

        var result = await Handler.ReleaseMovedTenantAsync(
            CreateHttpContext(),
            TenantId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task ReleaseMovedTenantAsync_WhenTheDirectoryIsUnavailable_ReturnsServiceUnavailableAndReleasesNothing()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore(CreateTenant(isDisabled: true));
        MockTenantDirectory
            .Setup(x => x.GetPlacementOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .Throws(new DirectoryUnavailableException("The directory is unreachable."))
            .Verifiable();
        var httpContext = CreateHttpContext();

        var result = await Handler.ReleaseMovedTenantAsync(
            httpContext,
            TenantId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.True(httpContext.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task ReleaseMovedTenantAsync_WhenTheTenantDoesNotExist_ReturnsNotFound()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore(tenant: null);

        var result = await Handler.ReleaseMovedTenantAsync(
            CreateHttpContext(),
            TenantId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task ReleaseMovedTenantAsync_WhenTheTenantIsReleased_DestroysNoRemoteKeyAndRetiresNothing()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupStore(CreateTenant(isDisabled: true));
        SetupPlacement(TargetCell);
        SetupReleaseRemovals();
        SetupReleasedAudit();

        await Handler.ReleaseMovedTenantAsync(
            CreateHttpContext(),
            TenantId,
            CancellationToken.None
        );

        // The strict mocks refuse any call that no setup names. The directory is only read, the master keys are
        // neither exported nor removed, and the store only removes the moved tenant, so no retirement happens and no
        // master key or remote key is destroyed.
        MockTenantDirectory.Verify(
            x => x.GetPlacementOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()),
            Times.Once
        );
        MockTenantDirectory.VerifyNoOtherCalls();
        MockMasterKeyTransfer.VerifyNoOtherCalls();
        MockTenantStore.Verify(
            x => x.RemoveMovedAsync(TenantId, It.IsAny<CancellationToken>()),
            Times.Once
        );
        MockTenantStore.Verify(
            x => x.GetOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()),
            Times.Once
        );
        MockTenantStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReleaseMovedTenantAsync_WhenTheCallerIsNotAuthorized_ReturnsForbiddenBeforeReadingAnything()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var result = await Handler.ReleaseMovedTenantAsync(
            CreateHttpContext(),
            TenantId,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    #endregion

    #region Map Tests

    [Fact]
    public async Task Map_Always_DeclaresAndDescribesEveryProblemTheRouteAnswers()
    {
        Assert.Equal(
            new[] { 400, 401, 403, 404, 409, 415, 422 },
            await EndpointProblemMetadata.ReadStatusCodesAsync(
                Handler,
                "POST",
                "/tenants/{tenantId}/export",
                CancellationToken.None
            )
        );
        Assert.Empty(
            await EndpointProblemMetadata.ReadUndescribedStatusCodesAsync(
                Handler,
                "POST",
                "/tenants/{tenantId}/export",
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task Map_Always_DeclaresAndDescribesEveryProblemTheReleaseRouteAnswers()
    {
        Assert.Equal(
            new[] { 401, 403, 404, 409, 503 },
            await EndpointProblemMetadata.ReadStatusCodesAsync(
                Handler,
                "POST",
                "/tenants/{tenantId}/release-moved",
                CancellationToken.None
            )
        );
        Assert.Empty(
            await EndpointProblemMetadata.ReadUndescribedStatusCodesAsync(
                Handler,
                "POST",
                "/tenants/{tenantId}/release-moved",
                CancellationToken.None
            )
        );
    }

    #endregion
}
