// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.Secrets.Persistence.DataContracts;
using NCode.Identity.Secrets.Persistence.Protection;

namespace NCode.Identity.OpenId.Management.Endpoints.Tenants;

// See ADR-1536.
public sealed class TenantTransferKeyCustodyTests
{
    private const string TenantId = "tenant-1";
    private const string ControlPlaneTenantId = "platform";
    private const string TeamId = "team-1";
    private const string CellName = "cell-a";
    private const string TargetCell = "cell-b";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; } = new();
    private Mock<IStoreManager> MockStoreManager { get; } = new();
    private Mock<ITenantStore> MockTenantStore { get; } = new();
    private Mock<IAuthorizationService> MockAuthorizationService { get; } = new();
    private Mock<IClientKeyImporter> MockClientKeyImporter { get; } = new();
    private Mock<ISecretSerializer> MockSecretSerializer { get; } = new();
    private Mock<ITenantTransferContributor> MockContributor { get; } = new();
    private Mock<ITenantDirectory> MockTenantDirectory { get; } = new();
    private List<string> Calls { get; } = [];
    private bool? HeldWriteTokenCancelled { get; set; }
    private TenantTransferApiEndpointHandler Handler { get; }

    public TenantTransferKeyCustodyTests()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object);
        MockStoreManager.Setup(x => x.GetStore<ITenantStore>()).Returns(MockTenantStore.Object);
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTenant());
        MockTenantStore
            .Setup(x =>
                x.SetKeyCustodyAsync(
                    TenantId,
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                (string _, string custody, string? cell, CancellationToken token) =>
                {
                    Calls.Add($"{custody}:{cell}");
                    if (custody == TenantKeyCustody.Held)
                    {
                        HeldWriteTokenCancelled = token.IsCancellationRequested;
                    }

                    return new ValueTask<bool>(true);
                }
            );

        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<IEnumerable<IAuthorizationRequirement>>()
                )
            )
            .ReturnsAsync(AuthorizationResult.Success());

        PersistedSecret? secret = new PersistedSecretBuilder
        {
            SecretId = TenantTransferApiEndpointHandler.RecipientSecretId,
            ConcurrencyToken = "secret-ct",
            Use = SecretKeyUses.Encryption,
            Algorithm = "ECDH-ES+A256KW",
            SecretType = SecretTypes.Ecc,
            SecretEncoding = SecretEncodings.Spki,
            EncodedValue = "spki-value",
        }.Build();
        string? error = null;
        MockClientKeyImporter
            .Setup(x => x.TryImportKey(It.IsAny<SecretKeyImportRequest>(), out secret, out error))
            .Returns(true);

        var key = new Mock<AsymmetricSecretKey>(
            MockBehavior.Loose,
            new KeyMetadata { Use = SecretKeyUses.Encryption },
            256,
            (object?)null,
            (object?)null
        );
        key.Setup(x => x.KeyType).Returns(SecretKeyTypes.Ecc);
        key.Setup(x => x.CurveName).Returns("P-256");
        key.Setup(x => x.Metadata).Returns(new KeyMetadata { Use = SecretKeyUses.Encryption });
        MockSecretSerializer
            .Setup(x => x.DeserializePublicSecret(It.IsAny<PersistedSecret>()))
            .Returns(key.Object);

        MockContributor.Setup(x => x.Order).Returns(0);

        Handler = new TenantTransferApiEndpointHandler(
            MockStoreManagerFactory.Object,
            MockAuthorizationService.Object,
            new Mock<ICryptoService>().Object,
            MockClientKeyImporter.Object,
            MockSecretSerializer.Object,
            [MockContributor.Object],
            new Mock<IMasterKeyTransfer>().Object,
            new Mock<IJoseSerializer>().Object,
            new Mock<IAlgorithmCollectionProvider>().Object,
            MockTenantDirectory.Object,
            new Mock<ITenantResolver>().Object,
            new Mock<IManagementAuditRecorder>().Object,
            Microsoft.Extensions.Options.Options.Create(
                new CellOptions { Name = CellName, Region = "us-east" }
            ),
            TimeProvider.System
        );
    }

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

    private static DefaultHttpContext CreateHttpContext()
    {
        var identity = new ClaimsIdentity(authenticationType: "test");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        httpContext.Features.Set<IOpenIdContextFeature>(
            Mock.Of<IOpenIdContextFeature>(feature =>
                feature.OpenIdContext
                == Mock.Of<OpenIdContext>(context =>
                    context.Tenant
                    == Mock.Of<OpenIdTenant>(tenant => tenant.TenantId == ControlPlaneTenantId)
                )
            )
        );
        return httpContext;
    }

    private static TenantExportRequest CreateRequest() =>
        new()
        {
            TargetCell = TargetCell,
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

    private void SetupOffer(Func<MoveOfferResult> result) =>
        MockTenantDirectory
            .Setup(x =>
                x.OfferMoveAsync(TenantId, CellName, TargetCell, It.IsAny<CancellationToken>())
            )
            .Returns(() =>
            {
                Calls.Add("offer");
                return new ValueTask<MoveOfferResult>(result());
            });

    [Fact]
    public async Task ExportTenantAsync_WhenTheOfferIsRefused_FencesBeforeTheOfferAndLiftsTheFence()
    {
        SetupOffer(() => MoveOfferResult.Conflict("The cells are in different regions."));

        await Handler.ExportTenantAsync(
            CreateHttpContext(),
            TenantId,
            CreateRequest(),
            CancellationToken.None
        );

        Assert.Equal([$"outgoing:{TargetCell}", "offer", "held:"], Calls);
    }

    [Fact]
    public async Task ExportTenantAsync_WhenTheDirectoryIsUnavailable_FencesBeforeTheOfferAndLiftsTheFence()
    {
        SetupOffer(() => throw new DirectoryUnavailableException("The directory is unreachable."));

        await Handler.ExportTenantAsync(
            CreateHttpContext(),
            TenantId,
            CreateRequest(),
            CancellationToken.None
        );

        Assert.Equal([$"outgoing:{TargetCell}", "offer", "held:"], Calls);
    }

    [Fact]
    public async Task ExportTenantAsync_WhenTheRequestIsCancelledAndTheDirectoryIsUnavailable_LiftsTheFenceOnItsOwnToken()
    {
        using var requestSource = new CancellationTokenSource();
        SetupOffer(() =>
        {
            requestSource.Cancel();
            throw new DirectoryUnavailableException("The directory is unreachable.");
        });

        await Handler.ExportTenantAsync(
            CreateHttpContext(),
            TenantId,
            CreateRequest(),
            requestSource.Token
        );

        Assert.Equal([$"outgoing:{TargetCell}", "offer", "held:"], Calls);
        Assert.False(HeldWriteTokenCancelled);
    }

    [Fact]
    public async Task ExportTenantAsync_WhenTheOfferIsRecorded_KeepsTheFence()
    {
        SetupOffer(MoveOfferResult.Offered);
        MockContributor
            .Setup(x => x.ExportAsync(TenantId, It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("The export stops here."));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Handler.ExportTenantAsync(
                CreateHttpContext(),
                TenantId,
                CreateRequest(),
                CancellationToken.None
            )
        );

        Assert.Equal([$"outgoing:{TargetCell}", "offer"], Calls);
    }

    [Fact]
    public async Task ImportAsync_Always_WritesTheTenantIncoming()
    {
        PersistedTenant? added = null;
        var tenantStore = new Mock<ITenantStore>();
        tenantStore
            .Setup(x => x.AddAsync(It.IsAny<PersistedTenant>(), It.IsAny<CancellationToken>()))
            .Callback((PersistedTenant tenant, CancellationToken _) => added = tenant)
            .Returns(ValueTask.CompletedTask);
        var storeManager = new Mock<IStoreManager>();
        storeManager.Setup(x => x.GetStore<ITenantStore>()).Returns(tenantStore.Object);
        storeManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        var factory = new Mock<IStoreManagerFactory>();
        factory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(storeManager.Object);
        var accessor = new Mock<IAmbientTenantAccessor>();
        accessor.Setup(x => x.BeginScope(TenantId)).Returns(new Mock<IDisposable>().Object);
        var contributor = new CoreTenantTransferContributor(factory.Object, accessor.Object);

        var exported = CreateTenant();
        exported.KeyCustody = TenantKeyCustody.Outgoing;
        exported.KeyCustodyCell = CellName;
        var json = JsonSerializer.SerializeToElement(
            new
            {
                tenant = exported,
                connections = Array.Empty<PersistedConnection>(),
                resourceServers = Array.Empty<PersistedResourceServer>(),
                clients = Array.Empty<PersistedClient>(),
                clientGrants = Array.Empty<PersistedClientGrant>(),
                roleAssignments = Array.Empty<PersistedRoleAssignment>(),
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );

        await contributor.ImportAsync(
            TenantId,
            new TenantTransferSection
            {
                Name = CoreTenantTransferContributor.SectionName,
                Json = json,
            },
            CancellationToken.None
        );

        Assert.NotNull(added);
        Assert.Equal(TenantKeyCustody.Incoming, added.KeyCustody);
        Assert.Null(added.KeyCustodyCell);
    }
}
