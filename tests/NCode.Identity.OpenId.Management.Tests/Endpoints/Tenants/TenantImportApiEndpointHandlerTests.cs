// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.Cryptography;
using NCode.Identity.Jose;
using NCode.Identity.Jose.Algorithms;
using NCode.Identity.Jose.Credentials;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Management.Transfers;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.Identity.Secrets.RemoteKeys;

namespace NCode.Identity.OpenId.Management.Endpoints.Tenants;

// See ADR-1514.
public sealed class TenantImportApiEndpointHandlerTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string TeamId = "team-1";
    private const string Region = "us-east";
    private const string CellName = "cell-b";
    private const string SourceCell = "cell-a";
    private const string Kid = "kid-1";
    private const string PackageText = "import-package";
    private const string MasterKeysPackage = "master-keys-package";
    private const string RemoteKeyId = "remote-key-1";

    private MockRepository MockRepository { get; }
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ITenantStore> MockTenantStore { get; }
    private Mock<IAuthorizationService> MockAuthorizationService { get; }
    private Mock<ICryptoService> MockCryptoService { get; }
    private Mock<ITenantTransferKeyService> MockTenantTransferKeyService { get; }
    private Mock<ITenantTransferContributor> MockCoreContributor { get; }
    private Mock<ITenantTransferContributor> MockAccountsContributor { get; }
    private Mock<IMasterKeyTransfer> MockMasterKeyTransfer { get; }
    private Mock<IJoseSerializer> MockJoseSerializer { get; }
    private Mock<JoseToken> MockToken { get; }
    private Mock<SecretKey> MockPrivateKey { get; }
    private Mock<IRemoteKeyProviderCatalog> MockRemoteKeyProviderCatalog { get; }
    private Mock<IRemoteKeyProvider> MockRemoteKeyProvider { get; }
    private Mock<ITenantDirectory> MockTenantDirectory { get; }
    private Mock<IManagementAuditRecorder> MockManagementAuditRecorder { get; }
    private List<string> Calls { get; } = [];
    private TenantImportApiEndpointHandler Handler { get; }

    public TenantImportApiEndpointHandlerTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockTenantStore = MockRepository.Create<ITenantStore>();
        MockAuthorizationService = MockRepository.Create<IAuthorizationService>();
        MockCryptoService = MockRepository.Create<ICryptoService>();
        MockTenantTransferKeyService = MockRepository.Create<ITenantTransferKeyService>();
        MockCoreContributor = MockRepository.Create<ITenantTransferContributor>();
        MockAccountsContributor = MockRepository.Create<ITenantTransferContributor>();
        MockMasterKeyTransfer = MockRepository.Create<IMasterKeyTransfer>();
        MockJoseSerializer = MockRepository.Create<IJoseSerializer>();
        MockToken = MockRepository.Create<JoseToken>();
        MockPrivateKey = MockRepository.Create<SecretKey>();
        MockRemoteKeyProviderCatalog = MockRepository.Create<IRemoteKeyProviderCatalog>();
        MockRemoteKeyProvider = MockRepository.Create<IRemoteKeyProvider>();
        MockTenantDirectory = MockRepository.Create<ITenantDirectory>();
        MockManagementAuditRecorder = MockRepository.Create<IManagementAuditRecorder>();

        // The handler reads each contributor's order once, to run them in order whatever order they are registered in.
        MockCoreContributor.Setup(x => x.Order).Returns(0).Verifiable();
        MockAccountsContributor.Setup(x => x.Order).Returns(100).Verifiable();
        MockCoreContributor.Setup(x => x.Name).Returns("core");
        MockAccountsContributor.Setup(x => x.Name).Returns("accounts");

        Handler = CreateHandler(MockJoseSerializer.Object);
    }

    public void Dispose()
    {
        MockRepository.Verify();
    }

    #region Helpers

    private TenantImportApiEndpointHandler CreateHandler(IJoseSerializer joseSerializer) =>
        new(
            MockStoreManagerFactory.Object,
            MockAuthorizationService.Object,
            MockCryptoService.Object,
            MockTenantTransferKeyService.Object,
            [MockAccountsContributor.Object, MockCoreContributor.Object],
            MockMasterKeyTransfer.Object,
            joseSerializer,
            MockRemoteKeyProviderCatalog.Object,
            MockTenantDirectory.Object,
            MockManagementAuditRecorder.Object,
            Microsoft.Extensions.Options.Options.Create(
                new CellOptions { Name = CellName, Region = Region }
            )
        );

    private static HttpContext CreateHttpContext(bool authenticated)
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
                    context.Tenant == Mock.Of<OpenIdTenant>(tenant => tenant.TenantId == TenantId)
                )
            )
        );

        return httpContext;
    }

    private sealed class CapturedAuthorization
    {
        public object? Resource { get; set; }
        public IAuthorizationRequirement[] Requirements { get; set; } = [];
    }

    private CapturedAuthorization SetupAuthorization(AuthorizationResult result)
    {
        var captured = new CapturedAuthorization();
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<IEnumerable<IAuthorizationRequirement>>()
                )
            )
            .Callback(
                (
                    ClaimsPrincipal _,
                    object? resource,
                    IEnumerable<IAuthorizationRequirement> requirements
                ) =>
                {
                    captured.Resource = resource;
                    captured.Requirements = requirements.ToArray();
                }
            )
            .ReturnsAsync(result)
            .Verifiable();
        return captured;
    }

    private static TenantImportRequest CreateRequest() =>
        new() { Kid = Kid, Package = PackageText };

    private static PersistedTenant CreateTenant() =>
        new PersistedTenantBuilder
        {
            TenantId = TenantId,
            TeamId = TeamId,
            IsDisabled = true,
            ConcurrencyToken = "tenant-ct",
            DisplayName = "Tenant One",
            SettingsConcurrencyToken = "settings-ct",
            SecretsConcurrencyToken = "secrets-ct",
        }.Build();

    // The package that an export writes: { version, region, targetCell, tenantId, sections: [{ name, json }],
    // masterKeys }. A null version or target cell leaves the member out.
    private static byte[] CreatePayload(
        string region = Region,
        string sectionName = "accounts",
        int? version = TenantTransferPackage.FormatVersion,
        string? targetCell = CellName
    )
    {
        var payload = new Dictionary<string, object?>
        {
            [TenantTransferPackage.RegionMember] = region,
            [TenantTransferPackage.TenantIdMember] = TenantId,
            [TenantTransferPackage.SectionsMember] = new object[]
            {
                new { name = "core", json = new { displayName = "Tenant One" } },
                new { name = sectionName, json = new { count = 3 } },
            },
            [TenantTransferPackage.MasterKeysMember] = MasterKeysPackage,
        };

        if (version is { } versionValue)
        {
            payload[TenantTransferPackage.VersionMember] = versionValue;
        }

        if (targetCell is not null)
        {
            payload[TenantTransferPackage.TargetCellMember] = targetCell;
        }

        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    // The real key factory, algorithms and serializer seal and open a package; only the stores and services are mocked.
    private static ServiceProvider CreateJoseServiceProvider()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSecretsLibrary().AddJoseLibrary();
        return serviceCollection.BuildServiceProvider();
    }

    // The transfer key is an ECDH P-256 key under the key identifier the request names.
    private static EccSecretKey CreateTransferKey(IServiceProvider serviceProvider)
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return serviceProvider
            .GetRequiredService<ISecretKeyFactory>()
            .CreateEccPkcs8(
                new KeyMetadata
                {
                    KeyId = Kid,
                    Use = "enc",
                    Algorithm = "ECDH-ES+A256KW",
                },
                ecdh.ExportPkcs8PrivateKey()
            );
    }

    // Seals the payload of an export to the transfer key, as the export does.
    private static async ValueTask<string> SealAsync(
        IServiceProvider serviceProvider,
        SecretKey transferKey,
        CancellationToken cancellationToken
    )
    {
        var collection = serviceProvider
            .GetRequiredService<IAlgorithmCollectionProvider>()
            .Collection;
        Assert.True(
            collection.TryGetKeyManagementAlgorithm(
                AlgorithmCodes.KeyManagement.EcdhEsAes256,
                out var keyManagement
            )
        );
        Assert.True(
            collection.TryGetAuthenticatedEncryptionAlgorithm(
                AlgorithmCodes.AuthenticatedEncryption.Aes256Gcm,
                out var encryption
            )
        );

        var encoder = serviceProvider
            .GetRequiredService<IJoseSerializer>()
            .CreateEncoder(
                new JoseEncryptionOptions(encryption!)
                {
                    Recipients =
                    [
                        new JoseRecipientOptions(
                            new JoseKeyManagementCredentials(transferKey, keyManagement!)
                        ),
                    ],
                }
            );

        ReadOnlyMemory<byte> payload = CreatePayload();
        return await encoder.EncodeAsync(payload, cancellationToken);
    }

    // The cell takes the transfer key, and the serializer decrypts the package to the payload.
    private void SetupPackage(byte[]? payload = null)
    {
        SetupToken(
            JsonSerializer.SerializeToElement(new { alg = "ECDH-ES+A256KW", enc = "A256GCM" })
        );

        MockJoseSerializer
            .Setup(x =>
                x.DecodeAsync(
                    MockToken.Object,
                    MockPrivateKey.Object,
                    It.IsAny<IBufferWriter<byte>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                (JoseToken _, SecretKey _, IBufferWriter<byte> writer, CancellationToken _) =>
                {
                    writer.Write(payload ?? CreatePayload());
                    return ValueTask.CompletedTask;
                }
            )
            .Verifiable();
    }

    // The cell takes the transfer key, and the serializer parses the package to a token with the protected header.
    private void SetupToken(JsonElement header)
    {
        MockTenantTransferKeyService
            .Setup(x => x.TakeAsync(Kid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockPrivateKey.Object)
            .Verifiable();

        MockJoseSerializer.Setup(x => x.Parse(PackageText)).Returns(MockToken.Object).Verifiable();

        MockToken.Setup(x => x.Header).Returns(header).Verifiable();
    }

    // The store holds no tenant of the identifier before the import, and the imported tenant afterwards.
    private void SetupStore(bool tenantExistsBefore = false, bool readImportedTenant = false)
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

        // The first read looks for residue before the import, and a second read finds the imported tenant.
        var reads = 0;
        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
                ++reads == 1 && !tenantExistsBefore ? (PersistedTenant?)null : CreateTenant()
            )
            .Verifiable(Times.Exactly(readImportedTenant ? 2 : 1));
    }

    private static TenantPlacement CreatePlacement(string cell) =>
        new()
        {
            TenantId = TenantId,
            Issuer = "https://issuer.example.com",
            Cell = cell,
            Region = Region,
        };

    private void SetupPlacement(string? cell = SourceCell) =>
        MockTenantDirectory
            .Setup(x => x.GetPlacementOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cell is null ? null : CreatePlacement(cell))
            .Verifiable();

    // The directory places the tenant in the source cell before the import, and answers the read after an unconfirmed
    // move with reread. The tokens of the two reads are returned.
    private List<CancellationToken> SetupPlacementThenReread(
        Func<ValueTask<TenantPlacement?>> reread
    )
    {
        var tokens = new List<CancellationToken>();
        MockTenantDirectory
            .Setup(x => x.GetPlacementOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .Returns(
                (string _, CancellationToken token) =>
                {
                    tokens.Add(token);
                    return tokens.Count == 1
                        ? new ValueTask<TenantPlacement?>(CreatePlacement(SourceCell))
                        : reread();
                }
            )
            .Verifiable(Times.Exactly(2));

        return tokens;
    }

    // The contributors import in order, and the core contributor reports one remote key.
    private void SetupContributorImports()
    {
        MockCoreContributor
            .Setup(x =>
                x.ImportAsync(
                    TenantId,
                    It.Is<TenantTransferSection>(section =>
                        section.Name == "core"
                        && section.Json.GetProperty("displayName").GetString() == "Tenant One"
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(() =>
            {
                Calls.Add("import-core");
                return new ValueTask<TenantTransferImportResult>(
                    new TenantTransferImportResult
                    {
                        RemoteKeys =
                        [
                            new RemoteKeyReference(RemoteKeyProviderNames.AwsKms, RemoteKeyId),
                        ],
                    }
                );
            })
            .Verifiable();

        MockAccountsContributor
            .Setup(x =>
                x.ImportAsync(
                    TenantId,
                    It.Is<TenantTransferSection>(section =>
                        section.Name == "accounts"
                        && section.Json.GetProperty("count").GetInt32() == 3
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(() =>
            {
                Calls.Add("import-accounts");
                return new ValueTask<TenantTransferImportResult>(
                    new TenantTransferImportResult { RemoteKeys = [] }
                );
            })
            .Verifiable();
    }

    private void SetupMasterKeyImport(MasterKeyTransferResult? result = null) =>
        MockMasterKeyTransfer
            .Setup(x =>
                x.ImportAsync(
                    It.Is<SecretOwner>(owner => owner == SecretOwner.ForTenant(TenantId)),
                    MasterKeysPackage,
                    MockPrivateKey.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(() =>
            {
                Calls.Add("import-master-keys");
                return new ValueTask<MasterKeyTransferResult>(
                    result ?? new MasterKeyTransferResult(1)
                );
            })
            .Verifiable();

    private void SetupProvider(bool isInLocation = true)
    {
        IRemoteKeyProvider? provider = MockRemoteKeyProvider.Object;
        MockRemoteKeyProviderCatalog
            .Setup(x => x.TryGetProvider(RemoteKeyProviderNames.AwsKms, out provider))
            .Returns(true)
            .Verifiable();
        MockRemoteKeyProvider
            .Setup(x => x.IsInLocation(RemoteKeyId))
            .Returns(isInLocation)
            .Verifiable();
    }

    private void SetupRetag(string deployment) =>
        MockRemoteKeyProvider
            .Setup(x => x.RetagKeyAsync(RemoteKeyId, deployment, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Calls.Add($"retag-{deployment}");
                return ValueTask.CompletedTask;
            })
            .Verifiable();

    private void SetupMove(MoveResult result) =>
        MockTenantDirectory
            .Setup(x => x.MoveAsync(TenantId, SourceCell, CellName, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Calls.Add("move");
                return new ValueTask<MoveResult>(result);
            })
            .Verifiable();

    // The move fails in a way that the directory does not confirm: failure names a refusal, an unreachable directory, a
    // cancellation or any other failure.
    private void SetupUnconfirmedMove(string failure) =>
        MockTenantDirectory
            .Setup(x => x.MoveAsync(TenantId, SourceCell, CellName, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Calls.Add("move");
                return failure switch
                {
                    "refused" => new ValueTask<MoveResult>(
                        MoveResult.Conflict("The offer is consumed.")
                    ),
                    "unavailable" => ValueTask.FromException<MoveResult>(
                        new DirectoryUnavailableException("The directory is unreachable.")
                    ),
                    "cancelled" => ValueTask.FromException<MoveResult>(
                        new OperationCanceledException()
                    ),
                    _ => ValueTask.FromException<MoveResult>(
                        new InvalidOperationException("The move failed.")
                    ),
                };
            })
            .Verifiable();

    private void SetupImportedAudit() =>
        MockManagementAuditRecorder
            .Setup(x =>
                x.RecordTenantChangedAsync(
                    It.IsAny<HttpContext>(),
                    TeamId,
                    TenantId,
                    "imported",
                    It.IsAny<JsonElement?>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    // The rollback removes the contributors' rows in reverse order, then the tenant with its master keys.
    private void SetupRollbackRemovals(bool accounts = true, bool core = true)
    {
        if (accounts)
        {
            MockAccountsContributor
                .Setup(x => x.RemoveAsync(TenantId, It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Calls.Add("remove-accounts");
                    return ValueTask.CompletedTask;
                })
                .Verifiable();
        }

        if (core)
        {
            MockCoreContributor
                .Setup(x => x.RemoveAsync(TenantId, It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Calls.Add("remove-core");
                    return ValueTask.CompletedTask;
                })
                .Verifiable();
        }

        MockTenantStore
            .Setup(x => x.RemoveFromCellAsync(TenantId, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Calls.Add("remove-tenant");
                return new ValueTask<bool>(true);
            })
            .Verifiable();

        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Calls.Add("save");
                return ValueTask.CompletedTask;
            })
            .Verifiable();
    }

    #endregion

    #region ImportTenantAsync Tests

    [Fact]
    public async Task ImportTenantAsync_WhenTheExportIsValid_ImportsRetagsMovesAndAudits()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage();
        SetupStore(readImportedTenant: true);
        SetupPlacement();
        SetupContributorImports();
        SetupMasterKeyImport();
        SetupProvider();
        SetupRetag(CellName);
        SetupMove(MoveResult.Moved());
        MockManagementAuditRecorder
            .Setup(x =>
                x.RecordTenantChangedAsync(
                    It.IsAny<HttpContext>(),
                    TeamId,
                    TenantId,
                    "imported",
                    It.Is<JsonElement?>(values =>
                        values.HasValue
                        && values.Value.GetProperty("sourceCell").GetString() == SourceCell
                    )
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var ok = Assert.IsType<Ok<TenantImportResource>>(result);
        Assert.Equal(TenantId, ok.Value?.TenantId);
        Assert.Equal(SourceCell, ok.Value?.SourceCell);
        Assert.Equal(
            ["import-core", "import-accounts", "import-master-keys", $"retag-{CellName}", "move"],
            Calls
        );
    }

    [Fact]
    public async Task ImportTenantAsync_Always_RequestsUpdateOnThePlatformNode()
    {
        var captured = SetupAuthorization(AuthorizationResult.Failed());

        await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        Assert.Same(Operations.Update, Assert.Single(captured.Requirements));
        var node = Assert.IsType<ResourceNode>(captured.Resource);
        Assert.Equal(
            (TenantId, ResourceNodeTypes.Platform, OpenIdConstants.PlatformResourceId),
            (node.TenantId, node.ResourceType, node.ResourceId)
        );
    }

    [Fact]
    public async Task ImportTenantAsync_WhenNotAuthorized_ReturnsForbiddenWithoutTakingTheKey()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task ImportTenantAsync_WhenTheCallerIsAnonymous_ReturnsUnauthorized()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: false),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);
    }

    [Fact]
    public async Task ImportTenantAsync_WhenTheKidIsUsedOrUnknown_ReturnsUnprocessableEntity()
    {
        SetupAuthorization(AuthorizationResult.Success());
        MockTenantTransferKeyService
            .Setup(x => x.TakeAsync(Kid, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SecretKey?)null)
            .Verifiable();

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ImportTenantAsync_WhenTheKidIsEmpty_ReturnsUnprocessableEntityWithoutTakingAKey(
        string kid
    )
    {
        SetupAuthorization(AuthorizationResult.Success());

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            new TenantImportRequest { Kid = kid, Package = PackageText },
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal(
            "The transfer key is unknown, expired or already used.",
            problem.ProblemDetails.Detail
        );
    }

    [Theory]
    [InlineData(typeof(CryptographicException))]
    [InlineData(typeof(FormatException))]
    public async Task ImportTenantAsync_WhenThePackageDoesNotDecrypt_ReturnsUnprocessableEntity(
        Type exceptionType
    )
    {
        SetupAuthorization(AuthorizationResult.Success());
        MockTenantTransferKeyService
            .Setup(x => x.TakeAsync(Kid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockPrivateKey.Object)
            .Verifiable();
        MockJoseSerializer
            .Setup(x => x.Parse(PackageText))
            .Throws((Exception)Activator.CreateInstance(exceptionType)!)
            .Verifiable();

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
    }

    [Theory]
    [InlineData("RSA-OAEP", "A256GCM")]
    [InlineData("dir", "A256GCM")]
    [InlineData("ECDH-ES+A256KW", "A128GCM")]
    [InlineData("ES256", null)]
    public async Task ImportTenantAsync_WhenThePackageIsNotSealedToATransferKey_ReturnsUnprocessableEntityWithoutDecrypting(
        string alg,
        string? enc
    )
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupToken(
            enc is null
                ? JsonSerializer.SerializeToElement(new { alg })
                : JsonSerializer.SerializeToElement(new { alg, enc })
        );

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal(
            "The tenant package is not sealed to a transfer key.",
            problem.ProblemDetails.Detail
        );
    }

    [Fact]
    public async Task ImportTenantAsync_WhenARealPackageHasATamperedTag_ReturnsUnprocessableEntity()
    {
        await using var serviceProvider = CreateJoseServiceProvider();
        var transferKey = CreateTransferKey(serviceProvider);
        var package = await SealAsync(serviceProvider, transferKey, CancellationToken.None);

        // The authentication tag is the fifth segment of a compact JWE.
        var segments = package.Split('.');
        segments[4] = (segments[4][0] == 'A' ? 'B' : 'A') + segments[4][1..];

        SetupAuthorization(AuthorizationResult.Success());
        MockTenantTransferKeyService
            .Setup(x => x.TakeAsync(Kid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transferKey)
            .Verifiable();

        var result = await CreateHandler(serviceProvider.GetRequiredService<IJoseSerializer>())
            .ImportTenantAsync(
                CreateHttpContext(authenticated: true),
                new TenantImportRequest { Kid = Kid, Package = string.Join('.', segments) },
                CancellationToken.None
            );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal(
            "The package does not decrypt with the transfer key.",
            problem.ProblemDetails.Detail
        );
    }

    [Fact]
    public async Task ImportTenantAsync_WhenARealPackageNamesRsaOaep_ReturnsUnprocessableEntityWithoutDecrypting()
    {
        await using var serviceProvider = CreateJoseServiceProvider();
        var transferKey = CreateTransferKey(serviceProvider);
        var package = await SealAsync(serviceProvider, transferKey, CancellationToken.None);

        // The protected header is the first segment of a compact JWE.
        var segments = package.Split('.');
        segments[0] = Base64Url.EncodeToString(
            JsonSerializer.SerializeToUtf8Bytes(new { alg = "RSA-OAEP", enc = "A256GCM" })
        );

        SetupAuthorization(AuthorizationResult.Success());
        MockTenantTransferKeyService
            .Setup(x => x.TakeAsync(Kid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transferKey)
            .Verifiable();

        var result = await CreateHandler(serviceProvider.GetRequiredService<IJoseSerializer>())
            .ImportTenantAsync(
                CreateHttpContext(authenticated: true),
                new TenantImportRequest { Kid = Kid, Package = string.Join('.', segments) },
                CancellationToken.None
            );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal(
            "The tenant package is not sealed to a transfer key.",
            problem.ProblemDetails.Detail
        );
    }

    [Fact]
    public async Task ImportTenantAsync_WhenThePayloadIsMalformed_ReturnsUnprocessableEntity()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage(JsonSerializer.SerializeToUtf8Bytes(new { region = Region }));

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
    }

    [Fact]
    public async Task ImportTenantAsync_WhenTheVersionIsAnotherVersion_ReturnsUnprocessableEntityAndImportsNothing()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage(CreatePayload(version: TenantTransferPackage.FormatVersion + 1));

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal(
            "The package's format version is not one this cell imports.",
            problem.ProblemDetails.Detail
        );
    }

    [Fact]
    public async Task ImportTenantAsync_WhenThePackageHasNoVersion_ReturnsUnprocessableEntityAndImportsNothing()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage(CreatePayload(version: null));

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal("The package is malformed.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task ImportTenantAsync_WhenTheRegionIsAnotherRegion_ReturnsUnprocessableEntityAndImportsNothing()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage(CreatePayload(region: "eu-west"));

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
    }

    [Fact]
    public async Task ImportTenantAsync_WhenThePackageIsOfferedToAnotherCell_ReturnsUnprocessableEntityAndImportsNothing()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage(CreatePayload(targetCell: "cell-c"));

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal(
            $"The package is offered to the cell 'cell-c', and this cell is '{CellName}'.",
            problem.ProblemDetails.Detail
        );
    }

    [Fact]
    public async Task ImportTenantAsync_WhenThePackageNamesNoTargetCell_ReturnsUnprocessableEntityAndImportsNothing()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage(CreatePayload(targetCell: null));

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal("The package is malformed.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task ImportTenantAsync_WhenASectionHasNoContributor_ReturnsUnprocessableEntityAndImportsNothing()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage(CreatePayload(sectionName: "unknown"));

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
    }

    [Fact]
    public async Task ImportTenantAsync_WhenTheTenantExistsAndTheDirectoryPlacesItInAnotherCell_RemovesTheResidueThenImports()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage();
        SetupStore(tenantExistsBefore: true, readImportedTenant: true);
        SetupPlacement();
        SetupRollbackRemovals();
        SetupContributorImports();
        SetupMasterKeyImport();
        SetupProvider();
        SetupRetag(CellName);
        SetupMove(MoveResult.Moved());
        MockManagementAuditRecorder
            .Setup(x =>
                x.RecordTenantChangedAsync(
                    It.IsAny<HttpContext>(),
                    TeamId,
                    TenantId,
                    "imported",
                    It.IsAny<JsonElement?>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        Assert.IsType<Ok<TenantImportResource>>(result);
        Assert.Equal(
            [
                "remove-accounts",
                "remove-core",
                "remove-tenant",
                "save",
                "import-core",
                "import-accounts",
                "import-master-keys",
                $"retag-{CellName}",
                "move",
            ],
            Calls
        );
    }

    [Fact]
    public async Task ImportTenantAsync_WhenTheDirectoryHasNoPlacement_ReturnsConflictAndImportsNothing()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage();
        SetupPlacement(cell: null);

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task ImportTenantAsync_WhenTheDirectoryPlacesTheTenantInThisCell_ReturnsConflictAndImportsNothing()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage();
        SetupPlacement(cell: CellName);

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task ImportTenantAsync_WhenTheDirectoryRefusesTheMoveAndPlacesTheTenantInTheSource_RemovesWhatItImportedAndReturnsConflict()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage();
        SetupStore();
        SetupPlacementThenReread(() => new(CreatePlacement(SourceCell)));
        SetupContributorImports();
        SetupMasterKeyImport();
        SetupProvider();
        SetupRetag(CellName);
        SetupRetag(SourceCell);
        SetupMove(MoveResult.Conflict("The tenant is placed in another cell."));
        SetupRollbackRemovals();

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("The tenant is placed in another cell.", problem.ProblemDetails.Detail);
        Assert.Equal(
            [
                "import-core",
                "import-accounts",
                "import-master-keys",
                $"retag-{CellName}",
                "move",
                $"retag-{SourceCell}",
                "remove-accounts",
                "remove-core",
                "remove-tenant",
                "save",
            ],
            Calls
        );
    }

    [Fact]
    public async Task ImportTenantAsync_WhenTheDirectoryIsUnavailableAtTheMoveAndPlacesTheTenantInTheSource_RemovesWhatItImportedAndReturnsServiceUnavailable()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage();
        SetupStore();
        SetupPlacementThenReread(() => new(CreatePlacement(SourceCell)));
        SetupContributorImports();
        SetupMasterKeyImport();
        SetupProvider();
        SetupRetag(CellName);
        SetupRetag(SourceCell);
        SetupUnconfirmedMove("unavailable");
        SetupRollbackRemovals();

        var httpContext = CreateHttpContext(authenticated: true);
        var result = await Handler.ImportTenantAsync(
            httpContext,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.True(httpContext.Response.Headers.ContainsKey("Retry-After"));
        Assert.Equal(
            "The global directory is unavailable. Retry the request.",
            problem.ProblemDetails.Detail
        );
        Assert.Equal(
            [
                "import-core",
                "import-accounts",
                "import-master-keys",
                $"retag-{CellName}",
                "move",
                $"retag-{SourceCell}",
                "remove-accounts",
                "remove-core",
                "remove-tenant",
                "save",
            ],
            Calls
        );
    }

    [Theory]
    [InlineData("refused")]
    [InlineData("unavailable")]
    [InlineData("cancelled")]
    [InlineData("failed")]
    public async Task ImportTenantAsync_WhenTheMoveIsUnconfirmedAndTheDirectoryPlacesTheTenantInThisCell_CompletesTheImport(
        string failure
    )
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage();
        SetupStore(readImportedTenant: true);
        SetupPlacementThenReread(() => new(CreatePlacement(CellName)));
        SetupContributorImports();
        SetupMasterKeyImport();
        SetupProvider();
        SetupRetag(CellName);
        SetupUnconfirmedMove(failure);
        SetupImportedAudit();

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        Assert.IsType<Ok<TenantImportResource>>(result);
        Assert.Equal(
            ["import-core", "import-accounts", "import-master-keys", $"retag-{CellName}", "move"],
            Calls
        );
    }

    [Theory]
    [InlineData("refused")]
    [InlineData("unavailable")]
    [InlineData("cancelled")]
    [InlineData("failed")]
    public async Task ImportTenantAsync_WhenTheMoveIsUnconfirmedAndThePlacementCannotBeRead_KeepsWhatItImportedAndReturnsServiceUnavailable(
        string failure
    )
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage();
        SetupStore();
        var tokens = SetupPlacementThenReread(() =>
            ValueTask.FromException<TenantPlacement?>(
                new DirectoryUnavailableException("The directory is unreachable.")
            )
        );
        SetupContributorImports();
        SetupMasterKeyImport();
        SetupProvider();
        SetupRetag(CellName);
        SetupUnconfirmedMove(failure);

        using var source = new CancellationTokenSource();
        var httpContext = CreateHttpContext(authenticated: true);
        var result = await Handler.ImportTenantAsync(httpContext, CreateRequest(), source.Token);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.True(httpContext.Response.Headers.ContainsKey("Retry-After"));
        Assert.Equal(
            "The outcome of the move is unknown. Read the tenant's placement in the global directory before releasing it at the source; a repeated import clears this cell's copy.",
            problem.ProblemDetails.Detail
        );
        Assert.Equal(
            ["import-core", "import-accounts", "import-master-keys", $"retag-{CellName}", "move"],
            Calls
        );
        Assert.True(tokens[0].CanBeCanceled);
        Assert.False(tokens[1].CanBeCanceled);
    }

    [Theory]
    [InlineData("cell-c")]
    [InlineData(null)]
    public async Task ImportTenantAsync_WhenTheMoveIsRefusedAndTheDirectoryPlacesTheTenantElsewhere_KeepsWhatItImportedAndReturnsServiceUnavailable(
        string? cell
    )
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage();
        SetupStore();
        SetupPlacementThenReread(() => new(cell is null ? null : CreatePlacement(cell)));
        SetupContributorImports();
        SetupMasterKeyImport();
        SetupProvider();
        SetupRetag(CellName);
        SetupUnconfirmedMove("refused");

        var httpContext = CreateHttpContext(authenticated: true);
        var result = await Handler.ImportTenantAsync(
            httpContext,
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.True(httpContext.Response.Headers.ContainsKey("Retry-After"));
        Assert.Equal(
            "The outcome of the move is unknown. Read the tenant's placement in the global directory before releasing it at the source; a repeated import clears this cell's copy.",
            problem.ProblemDetails.Detail
        );
        Assert.Equal(
            ["import-core", "import-accounts", "import-master-keys", $"retag-{CellName}", "move"],
            Calls
        );
    }

    [Fact]
    public async Task ImportTenantAsync_WhenTheMoveIsCancelledAndTheDirectoryPlacesTheTenantInTheSource_RemovesWhatItImportedAndRethrows()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage();
        SetupStore();
        SetupPlacementThenReread(() => new(CreatePlacement(SourceCell)));
        SetupContributorImports();
        SetupMasterKeyImport();
        SetupProvider();
        SetupRetag(CellName);
        SetupRetag(SourceCell);
        SetupUnconfirmedMove("cancelled");
        SetupRollbackRemovals();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Handler.ImportTenantAsync(
                CreateHttpContext(authenticated: true),
                CreateRequest(),
                CancellationToken.None
            )
        );

        Assert.Equal(
            [
                "import-core",
                "import-accounts",
                "import-master-keys",
                $"retag-{CellName}",
                "move",
                $"retag-{SourceCell}",
                "remove-accounts",
                "remove-core",
                "remove-tenant",
                "save",
            ],
            Calls
        );
    }

    [Fact]
    public async Task ImportTenantAsync_WhenTheDirectoryIsUnavailableForThePlacement_ReturnsServiceUnavailableAndImportsNothing()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage();
        MockTenantDirectory
            .Setup(x => x.GetPlacementOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .Throws(new DirectoryUnavailableException("The directory is unreachable."))
            .Verifiable();

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
    }

    [Fact]
    public async Task ImportTenantAsync_WhenARemoteKeyProviderIsNotRegistered_RemovesWhatItImportedAndReturnsUnprocessableEntity()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage();
        SetupStore();
        SetupPlacement();
        SetupContributorImports();
        SetupMasterKeyImport();
        IRemoteKeyProvider? provider = null;
        MockRemoteKeyProviderCatalog
            .Setup(x => x.TryGetProvider(RemoteKeyProviderNames.AwsKms, out provider))
            .Returns(false)
            .Verifiable();
        SetupRollbackRemovals();

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal(
            [
                "import-core",
                "import-accounts",
                "import-master-keys",
                "remove-accounts",
                "remove-core",
                "remove-tenant",
                "save",
            ],
            Calls
        );
    }

    [Fact]
    public async Task ImportTenantAsync_WhenTheTenantsRootKeyIsRefused_RemovesWhatItImportedAndReturnsUnprocessableEntity()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage();
        SetupStore();
        SetupPlacement();
        SetupContributorImports();
        SetupMasterKeyImport(
            new MasterKeyTransferResult(0) { RefusalDetail = "The owner cannot bring a root key." }
        );
        SetupRollbackRemovals();

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal(
            "The tenant's root key is refused. The owner cannot bring a root key.",
            problem.ProblemDetails.Detail
        );
        Assert.Equal(
            [
                "import-core",
                "import-accounts",
                "import-master-keys",
                "remove-accounts",
                "remove-core",
                "remove-tenant",
                "save",
            ],
            Calls
        );
    }

    [Fact]
    public async Task ImportTenantAsync_WhenARemoteKeyIsOutsideTheCellsLocation_RemovesWhatItImportedAndReturnsUnprocessableEntity()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage();
        SetupStore();
        SetupPlacement();
        SetupContributorImports();
        SetupMasterKeyImport();
        SetupProvider(isInLocation: false);
        SetupRollbackRemovals();

        var result = await Handler.ImportTenantAsync(
            CreateHttpContext(authenticated: true),
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
        Assert.Equal(
            $"The remote key '{RemoteKeyId}' of provider '{RemoteKeyProviderNames.AwsKms}' is outside the location this cell uses.",
            problem.ProblemDetails.Detail
        );
        Assert.Equal(
            [
                "import-core",
                "import-accounts",
                "import-master-keys",
                "remove-accounts",
                "remove-core",
                "remove-tenant",
                "save",
            ],
            Calls
        );
    }

    [Fact]
    public async Task ImportTenantAsync_WhenAContributorFails_RemovesWhatItImportedAndRethrows()
    {
        SetupAuthorization(AuthorizationResult.Success());
        SetupPackage();
        SetupStore();
        SetupPlacement();
        MockCoreContributor
            .Setup(x =>
                x.ImportAsync(
                    TenantId,
                    It.IsAny<TenantTransferSection>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Throws(new InvalidOperationException("The section is invalid."))
            .Verifiable();
        SetupRollbackRemovals();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Handler.ImportTenantAsync(
                CreateHttpContext(authenticated: true),
                CreateRequest(),
                CancellationToken.None
            )
        );

        Assert.Equal(["remove-accounts", "remove-core", "remove-tenant", "save"], Calls);
    }

    #endregion

    #region CreateTransferKeyAsync Tests

    [Fact]
    public async Task CreateTransferKeyAsync_WhenAuthorized_ReturnsTheKeyIdAndThePublicKey()
    {
        SetupAuthorization(AuthorizationResult.Success());
        var jwk = JsonSerializer.SerializeToElement(new { kty = "EC", crv = "P-256" });
        MockTenantTransferKeyService
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantTransferKey { KeyId = "kid-1", Jwk = jwk })
            .Verifiable();

        var result = await Handler.CreateTransferKeyAsync(
            CreateHttpContext(authenticated: true),
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<TenantTransferKeyResource>>(result);
        var resource = Assert.IsType<TenantTransferKeyResource>(json.Value);
        Assert.Equal("kid-1", resource.Kid);
        Assert.Equal("EC", resource.Jwk.GetProperty("kty").GetString());
        Assert.Equal("P-256", resource.Jwk.GetProperty("crv").GetString());
    }

    [Fact]
    public async Task CreateTransferKeyAsync_Always_RequestsUpdateOnThePlatformNode()
    {
        var captured = SetupAuthorization(AuthorizationResult.Success());
        MockTenantTransferKeyService
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new TenantTransferKey
                {
                    KeyId = "kid-1",
                    Jwk = JsonSerializer.SerializeToElement(1),
                }
            )
            .Verifiable();

        await Handler.CreateTransferKeyAsync(
            CreateHttpContext(authenticated: true),
            CancellationToken.None
        );

        Assert.Same(Operations.Update, Assert.Single(captured.Requirements));
        var node = Assert.IsType<ResourceNode>(captured.Resource);
        Assert.Equal(
            (TenantId, ResourceNodeTypes.Platform, OpenIdConstants.PlatformResourceId),
            (node.TenantId, node.ResourceType, node.ResourceId)
        );
    }

    [Fact]
    public async Task CreateTransferKeyAsync_WhenNotAuthorized_ReturnsForbiddenWithoutIssuingAKey()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var result = await Handler.CreateTransferKeyAsync(
            CreateHttpContext(authenticated: true),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task CreateTransferKeyAsync_WhenTheCallerIsAnonymous_ReturnsUnauthorizedWithoutIssuingAKey()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var result = await Handler.CreateTransferKeyAsync(
            CreateHttpContext(authenticated: false),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);
    }

    #endregion

    #region Map Tests

    [Theory]
    [InlineData("POST", "/tenant-imports/transfer-keys", new[] { 401, 403 })]
    [InlineData("POST", "/tenant-imports", new[] { 400, 401, 403, 409, 413, 415, 422, 503 })]
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

    [Fact]
    public void Map_Always_MapsTheImportAndTransferKeysRoutesOnAnyCell()
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        Handler.Map(app);

        var routes = ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText);

        Assert.Equal(["/tenant-imports/", "/tenant-imports/transfer-keys"], routes);
    }

    [Fact]
    public async Task Map_Always_LimitsTheImportRequestToTheCeilingOfATenantMove()
    {
        await using var app = WebApplication.CreateSlimBuilder().Build();
        Handler.Map(app);

        var endpoints = ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
        var import = Assert.Single(
            endpoints,
            endpoint => endpoint.RoutePattern.RawText == "/tenant-imports/"
        );
        var transferKeys = Assert.Single(
            endpoints,
            endpoint => endpoint.RoutePattern.RawText == "/tenant-imports/transfer-keys"
        );

        Assert.Equal(
            64L * 1024 * 1024 + 64 * 1024,
            import.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize
        );
        Assert.Null(transferKeys.Metadata.GetMetadata<IRequestSizeLimitMetadata>());
    }

    #endregion
}
