// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NCode.Buffers;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.Jose;
using NCode.Identity.Jose.Algorithms;
using NCode.Identity.Jose.Credentials;
using NCode.Identity.OpenId.Authentication.Clients;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Management.Transfers;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Registration.AspNetCore;
using NCode.Identity.Secrets;
using NCode.Identity.Secrets.KeyMaterial;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.KeyTypes;
using NCode.Identity.Secrets.Persistence;
using NCode.Identity.Secrets.Persistence.Protection;

namespace NCode.Identity.OpenId.Management.Endpoints.Tenants;

/*

POST api/tenants/{tenantId}/export
POST api/tenants/{tenantId}/release-moved

*/

/// <summary>
/// Provides the API endpoints that move a disabled tenant out of the cell that holds it. The export is one compact
/// <c>JWE</c> to a recipient key that the receiving cell published: it carries the package's format version, the cell's
/// region, the tenant's identifier, the section of every <see cref="ITenantTransferContributor"/> in order, and the
/// tenant's master keys sealed to the same key. The tenant must be disabled so that nothing changes while it moves. The
/// release removes the tenant's rows from this cell once the global directory places the tenant in another cell; it
/// destroys no remote key and retires nothing. The caller needs the update operation on the platform node. The
/// export's response is never logged.
/// </summary>
// See ADR-1514.
internal class TenantTransferApiEndpointHandler(
    IStoreManagerFactory storeManagerFactory,
    IAuthorizationService authorizationService,
    ICryptoService cryptoService,
    IClientKeyImporter clientKeyImporter,
    ISecretSerializer secretSerializer,
    IEnumerable<ITenantTransferContributor> tenantTransferContributors,
    IMasterKeyTransfer masterKeyTransfer,
    IJoseSerializer joseSerializer,
    IAlgorithmCollectionProvider algorithmCollectionProvider,
    ITenantDirectory tenantDirectory,
    IManagementAuditRecorder managementAuditRecorder,
    IOptions<CellOptions> cellOptions,
    TimeProvider timeProvider
) : BaseApiEndpointHandler, IEndpointProvider
{
    private const int DirectoryRetrySeconds = 5;

    /// <summary>
    /// The identifier that the recipient key is imported under.
    /// </summary>
    internal const string RecipientSecretId = "tenant-transfer-recipient";

    private const string RecipientUse = SecretKeyUses.Encryption;

    private const string RecipientAlgorithm = AlgorithmCodes.KeyManagement.EcdhEsAes256;

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IClientKeyImporter ClientKeyImporter { get; } = clientKeyImporter;
    private ISecretSerializer SecretSerializer { get; } = secretSerializer;
    private IReadOnlyList<ITenantTransferContributor> TenantTransferContributors { get; } =
    [.. tenantTransferContributors.OrderBy(contributor => contributor.Order)];
    private IMasterKeyTransfer MasterKeyTransfer { get; } = masterKeyTransfer;
    private IJoseSerializer JoseSerializer { get; } = joseSerializer;
    private IAlgorithmCollectionProvider AlgorithmCollectionProvider { get; } =
        algorithmCollectionProvider;
    private ITenantDirectory TenantDirectory { get; } = tenantDirectory;
    private IManagementAuditRecorder ManagementAuditRecorder { get; } = managementAuditRecorder;
    private CellOptions CellOptions { get; } = cellOptions.Value;
    private TimeProvider TimeProvider { get; } = timeProvider;

    /// <inheritdoc />
    protected override IAuthorizationService AuthorizationService { get; } = authorizationService;

    /// <inheritdoc />
    protected override ICryptoService CryptoService { get; } = cryptoService;

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var tenants = endpoints
            .MapGroup("/tenants")
            .WithTags(OpenIdConstants.EndpointTags.Tenants)
            .AddEndpointFilter<RouteGroupBuilder, ControlPlaneScopeEndpointFilter>();

        tenants
            .MapPost("/{tenantId}/export", ExportTenantAsync)
            .Produces<TenantExportResource>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        tenants
            .MapPost("/{tenantId}/release-moved", ReleaseMovedTenantAsync)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// Releases a tenant that the global directory places in another cell: every
    /// <see cref="ITenantTransferContributor"/> removes its rows in reverse order, then the tenant is removed with its
    /// master keys. It destroys no remote key and retires nothing, because the tenant lives on in the other cell.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="204">The tenant is released from this cell.</response>
    /// <response code="404">The tenant does not exist in this cell.</response>
    /// <response code="409">The global directory does not place the tenant in another cell.</response>
    /// <response code="503">The global directory is unavailable; nothing is released.</response>
    [EndpointName("api/tenants/release-moved/create")]
    internal virtual async ValueTask<IResult> ReleaseMovedTenantAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        CancellationToken cancellationToken
    )
    {
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            GetPlatformNode(httpContext),
            Operations.Update,
            cancellationToken
        );

        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var tenantStore = storeManager.GetStore<ITenantStore>();
        var tenant = await tenantStore.GetOrDefaultAsync(tenantId, cancellationToken);

        if (tenant is null)
        {
            return ManagementProblems.ResourceNotFound();
        }

        TenantPlacement? placement;
        try
        {
            placement = await TenantDirectory.GetPlacementOrDefaultAsync(
                tenantId,
                cancellationToken
            );
        }
        catch (DirectoryUnavailableException)
        {
            return RetryAfterProblem.Create(
                httpContext,
                TimeSpan.FromSeconds(DirectoryRetrySeconds),
                new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Detail = "The global directory is unavailable. Retry the request.",
                }
            );
        }

        if (placement is null || placement.Cell == CellOptions.Name)
        {
            return TypedResults.Problem(
                detail: $"The global directory does not place the tenant '{tenantId}' in another cell.",
                statusCode: StatusCodes.Status409Conflict
            );
        }

        for (var index = TenantTransferContributors.Count - 1; index >= 0; index--)
        {
            await TenantTransferContributors[index].RemoveAsync(tenantId, cancellationToken);
        }

        await tenantStore.RemoveFromCellAsync(tenantId, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);

        await ManagementAuditRecorder.RecordTenantChangedAsync(
            httpContext,
            tenant.TeamId,
            tenantId,
            ResourceChangeTypes.Released,
            JsonSerializer.SerializeToElement(
                new Dictionary<string, string> { ["targetCell"] = placement.Cell }
            )
        );

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Exports a disabled tenant as a package that only the holder of the recipient key reads.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="tenantId">The identifier of the OpenID Tenant.</param>
    /// <param name="request">The <see cref="TenantExportRequest"/> that carries the recipient's public key.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The export, a compact <c>JWE</c> to the recipient key.</response>
    /// <response code="404">The tenant does not exist.</response>
    /// <response code="409">The tenant is not disabled.</response>
    /// <response code="422">The recipient key is refused, or it is not an elliptic curve key on <c>P-256</c> for encryption.</response>
    [EndpointName("api/tenants/export/create")]
    internal virtual async ValueTask<IResult> ExportTenantAsync(
        HttpContext httpContext,
        [FromRoute] string tenantId,
        [FromBody] TenantExportRequest request,
        CancellationToken cancellationToken
    )
    {
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            GetPlatformNode(httpContext),
            Operations.Update,
            cancellationToken
        );

        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var recipient = ReadRecipientOrDefault(request, out var recipientRefusal);
        if (recipient is null)
        {
            return TypedResults.Problem(
                detail: recipientRefusal,
                statusCode: StatusCodes.Status422UnprocessableEntity
            );
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var tenant = await storeManager
            .GetStore<ITenantStore>()
            .GetOrDefaultAsync(tenantId, cancellationToken);

        if (tenant is null)
        {
            return ManagementProblems.ResourceNotFound();
        }

        if (!tenant.IsDisabled)
        {
            return TypedResults.Problem(
                detail: "A tenant must be disabled before it is exported, so that nothing changes while it moves.",
                statusCode: StatusCodes.Status409Conflict
            );
        }

        var sections = new List<TenantTransferSection>(TenantTransferContributors.Count);
        foreach (var contributor in TenantTransferContributors)
        {
            sections.Add(await contributor.ExportAsync(tenantId, cancellationToken));
        }

        var masterKeys = await MasterKeyTransfer.ExportAsync(
            SecretOwner.ForTenant(tenantId),
            recipient,
            cancellationToken
        );

        var package = await EncodePackageAsync(
            tenantId,
            sections,
            masterKeys,
            recipient,
            cancellationToken
        );

        await ManagementAuditRecorder.RecordTenantChangedAsync(
            httpContext,
            tenant.TeamId,
            tenantId,
            ResourceChangeTypes.Exported,
            resourceValues: null
        );

        return TypedResults.Ok(new TenantExportResource { Package = package });
    }

    // Imports the recipient's public key under the client-key policy and accepts only an elliptic curve key on P-256
    // for encryption, the key type of the transfer keys that a cell issues.
    private SecretKey? ReadRecipientOrDefault(TenantExportRequest request, out string? refusal)
    {
        refusal = null;

        if (request.Jwk.ValueKind != JsonValueKind.Object)
        {
            refusal = "The recipient key must be a JSON Web Key.";
            return null;
        }

        var importRequest = new SecretKeyImportRequest
        {
            SecretId = RecipientSecretId,
            SecretType = SecretTypes.Ecc,
            Format = SecretKeyImportFormats.Jwk,
            KeyMaterial = request.Jwk.GetRawText(),
            Use = RecipientUse,
            Algorithm = RecipientAlgorithm,
            CreatedWhen = TimeProvider.GetUtcNow(),
            ExpiresWhen = DateTimeOffset.MaxValue,
        };

        if (!ClientKeyImporter.TryImportKey(importRequest, out var secret, out var importError))
        {
            refusal = importError;
            return null;
        }

        var key = SecretSerializer.DeserializePublicSecret(secret);
        if (
            key.KeyType != SecretKeyTypes.Ecc
            || key is not AsymmetricSecretKey { CurveName: var curveName }
            || curveName != EccCurveSpecification.P256.CurveName
            || key.Metadata.Use != RecipientUse
        )
        {
            refusal = "The recipient key must be an elliptic curve key on P-256 for encryption.";
            return null;
        }

        return key;
    }

    // Seals the export to the recipient key as a compact JWE: the package's format version, the cell's region, the
    // tenant's identifier, each section in order, and the master keys that were already sealed to the same key.
    private async ValueTask<string> EncodePackageAsync(
        string tenantId,
        IReadOnlyList<TenantTransferSection> sections,
        string masterKeys,
        SecretKey recipient,
        CancellationToken cancellationToken
    )
    {
        using var payload = BufferFactory.CreatePooledBufferWriter(isSensitive: true);
        using (var writer = new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject();
            writer.WriteNumber(
                TenantTransferPackage.VersionMember,
                TenantTransferPackage.FormatVersion
            );
            writer.WriteString(TenantTransferPackage.RegionMember, CellOptions.Region);
            writer.WriteString(TenantTransferPackage.TenantIdMember, tenantId);

            writer.WriteStartArray(TenantTransferPackage.SectionsMember);
            foreach (var section in sections)
            {
                writer.WriteStartObject();
                writer.WriteString(TenantTransferPackage.SectionNameMember, section.Name);
                writer.WritePropertyName(TenantTransferPackage.SectionJsonMember);
                section.Json.WriteTo(writer);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteString(TenantTransferPackage.MasterKeysMember, masterKeys);
            writer.WriteEndObject();
            writer.Flush();
        }

        var payloadLength = checked((int)payload.Length);
        using var lease = BufferFactory.Rent(
            payloadLength,
            isSensitive: true,
            out Memory<byte> contiguous
        );
        payload.AsReadOnlySequence.CopyTo(contiguous.Span);

        ReadOnlyMemory<byte> content = contiguous[..payloadLength];
        var encoder = JoseSerializer.CreateEncoder(CreateEncryptionOptions(recipient));
        return await encoder.EncodeAsync(content, cancellationToken);
    }

    private JoseEncryptionOptions CreateEncryptionOptions(SecretKey recipient)
    {
        var collection = AlgorithmCollectionProvider.Collection;

        if (
            !collection.TryGetKeyManagementAlgorithm(
                AlgorithmCodes.KeyManagement.EcdhEsAes256,
                out var keyManagement
            )
        )
        {
            throw new InvalidOperationException(
                $"The key management algorithm '{AlgorithmCodes.KeyManagement.EcdhEsAes256}' is not registered."
            );
        }

        if (
            !collection.TryGetAuthenticatedEncryptionAlgorithm(
                AlgorithmCodes.AuthenticatedEncryption.Aes256Gcm,
                out var encryption
            )
        )
        {
            throw new InvalidOperationException(
                $"The authenticated encryption algorithm '{AlgorithmCodes.AuthenticatedEncryption.Aes256Gcm}' is not registered."
            );
        }

        return new JoseEncryptionOptions(encryption)
        {
            Recipients =
            [
                new JoseRecipientOptions(
                    new JoseKeyManagementCredentials(recipient, keyManagement)
                ),
            ],
        };
    }
}
