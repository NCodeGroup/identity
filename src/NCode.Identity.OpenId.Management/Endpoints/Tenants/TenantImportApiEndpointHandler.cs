// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NCode.Buffers;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.Jose;
using NCode.Identity.Jose.Exceptions;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Management.Transfers;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Registration.AspNetCore;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.Identity.Secrets.RemoteKeys;

namespace NCode.Identity.OpenId.Management.Endpoints.Tenants;

/*

POST api/tenant-imports
POST api/tenant-imports/transfer-keys

*/

/// <summary>
/// Provides the API endpoints that a cell publishes to receive a tenant. One issues a one-time transfer key whose
/// public half seals the tenant's export. The other imports the export: it takes the transfer key, decrypts the
/// package, imports every section and the master keys, retags the remote keys that the imported secrets reference to
/// this cell, and moves the tenant's placement in the global directory to this cell. The tenant stays disabled. Every
/// cell maps the routes, and the caller needs the update operation on the platform node.
/// </summary>
// See ADR-1514.
internal class TenantImportApiEndpointHandler(
    IStoreManagerFactory storeManagerFactory,
    IAuthorizationService authorizationService,
    ICryptoService cryptoService,
    ITenantTransferKeyService tenantTransferKeyService,
    IEnumerable<ITenantTransferContributor> tenantTransferContributors,
    IMasterKeyTransfer masterKeyTransfer,
    IJoseSerializer joseSerializer,
    IRemoteKeyProviderCatalog remoteKeyProviderCatalog,
    ITenantDirectory tenantDirectory,
    IManagementAuditRecorder managementAuditRecorder,
    IOptions<CellOptions> cellOptions
) : BaseApiEndpointHandler, IEndpointProvider
{
    // The members of the package that the export writes. The export's payload is
    // { region, tenantId, sections: [{ name, json }], masterKeys }.
    private const string RegionMember = "region";
    private const string TenantIdMember = "tenantId";
    private const string SectionsMember = "sections";
    private const string SectionNameMember = "name";
    private const string SectionJsonMember = "json";
    private const string MasterKeysMember = "masterKeys";

    private const int DirectoryRetrySeconds = 5;

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private ITenantTransferKeyService TenantTransferKeyService { get; } = tenantTransferKeyService;
    private IReadOnlyList<ITenantTransferContributor> TenantTransferContributors { get; } =
    [.. tenantTransferContributors.OrderBy(contributor => contributor.Order)];
    private IMasterKeyTransfer MasterKeyTransfer { get; } = masterKeyTransfer;
    private IJoseSerializer JoseSerializer { get; } = joseSerializer;
    private IRemoteKeyProviderCatalog RemoteKeyProviderCatalog { get; } = remoteKeyProviderCatalog;
    private ITenantDirectory TenantDirectory { get; } = tenantDirectory;
    private IManagementAuditRecorder ManagementAuditRecorder { get; } = managementAuditRecorder;
    private CellOptions CellOptions { get; } = cellOptions.Value;

    /// <inheritdoc />
    protected override IAuthorizationService AuthorizationService { get; } = authorizationService;

    /// <inheritdoc />
    protected override ICryptoService CryptoService { get; } = cryptoService;

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var imports = endpoints
            .MapGroup("/tenant-imports")
            .WithTags(OpenIdConstants.EndpointTags.Tenants)
            .AddEndpointFilter<RouteGroupBuilder, ControlPlaneScopeEndpointFilter>();

        imports
            .MapPost("", ImportTenantAsync)
            .Produces<TenantImportResource>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        imports
            .MapPost("/transfer-keys", CreateTransferKeyAsync)
            .Produces<TenantTransferKeyResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Issues a one-time transfer key that this cell takes once, when it imports the tenant that the key seals.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The <c>kid</c> and the public <c>JWK</c> of the transfer key, which expires after one hour.</response>
    [EndpointName("api/tenant-imports/transfer-keys/create")]
    internal virtual async ValueTask<IResult> CreateTransferKeyAsync(
        HttpContext httpContext,
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

        var transferKey = await TenantTransferKeyService.CreateAsync(cancellationToken);
        return TypedResults.Json(
            new TenantTransferKeyResource { Kid = transferKey.KeyId, Jwk = transferKey.Jwk }
        );
    }

    /// <summary>
    /// Imports a tenant that another cell exported, and moves the tenant's placement in the global directory to this
    /// cell. The tenant stays disabled.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="request">The <see cref="TenantImportRequest"/> that names the transfer key and carries the package.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The tenant is imported and placed in this cell.</response>
    /// <response code="409">The tenant already exists in this cell, the directory does not place it in another cell, or the directory refuses the move. Nothing the import wrote remains.</response>
    /// <response code="422">The transfer key is unknown, expired or already used, the package does not decrypt with it or is malformed, or its region is not this cell's region.</response>
    /// <response code="503">The global directory is unavailable. Nothing the import wrote remains.</response>
    [EndpointName("api/tenant-imports/create")]
    internal virtual async ValueTask<IResult> ImportTenantAsync(
        HttpContext httpContext,
        [FromBody] TenantImportRequest request,
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

        var privateKey = await TenantTransferKeyService.TakeAsync(request.Kid, cancellationToken);
        if (privateKey is null)
        {
            return Unprocessable("The transfer key is unknown, expired or already used.");
        }

        var decodedPackage = await DecodePackageOrDefaultAsync(
            request.Package,
            privateKey,
            cancellationToken
        );
        if (decodedPackage is not { } root)
        {
            return Unprocessable("The package does not decrypt with the transfer key.");
        }

        if (
            !TryReadPackage(
                root,
                out var region,
                out var tenantId,
                out var masterKeys,
                out var sections
            )
        )
        {
            return Unprocessable("The package is malformed.");
        }

        if (region != CellOptions.Region)
        {
            return Unprocessable(
                $"The package is from region '{region}', and this cell is in region '{CellOptions.Region}'."
            );
        }

        var contributors = TenantTransferContributors
            .Where(contributor => sections.ContainsKey(contributor.Name))
            .ToList();
        if (contributors.Count != sections.Count)
        {
            return Unprocessable("The package has a section that this cell does not import.");
        }

        string teamId;
        await using (var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken))
        {
            var existing = await storeManager
                .GetStore<ITenantStore>()
                .GetOrDefaultAsync(tenantId, cancellationToken);

            if (existing is not null)
            {
                return TypedResults.Problem(
                    detail: $"The tenant '{tenantId}' already exists in this cell.",
                    statusCode: StatusCodes.Status409Conflict
                );
            }
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
            return DirectoryUnavailableProblem(httpContext);
        }

        if (placement is null)
        {
            return TypedResults.Problem(
                detail: $"The directory does not place the tenant '{tenantId}' in any cell.",
                statusCode: StatusCodes.Status409Conflict
            );
        }

        var sourceCell = placement.Cell;
        if (sourceCell == CellOptions.Name)
        {
            return TypedResults.Problem(
                detail: $"The directory already places the tenant '{tenantId}' in this cell.",
                statusCode: StatusCodes.Status409Conflict
            );
        }

        // Each contributor and the master key transfer commit on their own, so a failure at any later point removes
        // what was written and puts the remote keys back under the source cell's tag before the answer.
        var retagged = new List<(IRemoteKeyProvider Provider, string KeyId)>();
        IResult? failure = null;
        try
        {
            failure = await ImportAndMoveAsync(
                tenantId,
                sourceCell,
                masterKeys,
                privateKey,
                contributors,
                sections,
                retagged,
                cancellationToken
            );
        }
        catch (DirectoryUnavailableException)
        {
            failure = DirectoryUnavailableProblem(httpContext);
        }
        catch
        {
            await RollbackAsync(
                tenantId,
                sourceCell,
                contributors,
                retagged,
                CancellationToken.None
            );
            throw;
        }

        if (failure is not null)
        {
            await RollbackAsync(
                tenantId,
                sourceCell,
                contributors,
                retagged,
                CancellationToken.None
            );
            return failure;
        }

        await using (var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken))
        {
            var imported =
                await storeManager
                    .GetStore<ITenantStore>()
                    .GetOrDefaultAsync(tenantId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"The imported tenant '{tenantId}' is not stored."
                );
            teamId = imported.TeamId;
        }

        await ManagementAuditRecorder.RecordTenantChangedAsync(
            httpContext,
            teamId,
            tenantId,
            ResourceChangeTypes.Imported,
            JsonSerializer.SerializeToElement(
                new Dictionary<string, string> { ["sourceCell"] = sourceCell }
            )
        );

        return TypedResults.Ok(
            new TenantImportResource { TenantId = tenantId, SourceCell = sourceCell }
        );
    }

    // Imports each section in order, then the master keys, retags the remote keys that the imported secrets reference
    // to this cell, and moves the placement. A failure that the caller should answer is returned; nothing is returned
    // when the tenant is imported and moved.
    private async ValueTask<IResult?> ImportAndMoveAsync(
        string tenantId,
        string sourceCell,
        string masterKeys,
        SecretKey privateKey,
        List<ITenantTransferContributor> contributors,
        Dictionary<string, JsonElement> sections,
        List<(IRemoteKeyProvider Provider, string KeyId)> retagged,
        CancellationToken cancellationToken
    )
    {
        var remoteKeys = new HashSet<RemoteKeyReference>();
        foreach (var contributor in contributors)
        {
            var section = new TenantTransferSection
            {
                Name = contributor.Name,
                Json = sections[contributor.Name],
            };

            var result = await contributor.ImportAsync(tenantId, section, cancellationToken);
            remoteKeys.UnionWith(result.RemoteKeys);
        }

        await MasterKeyTransfer.ImportAsync(
            SecretOwner.ForTenant(tenantId),
            masterKeys,
            privateKey,
            cancellationToken
        );

        // Every provider is found before any key is retagged, so a key that this cell cannot retag refuses the import
        // while no key has changed.
        var providers = new List<(IRemoteKeyProvider Provider, string KeyId)>(remoteKeys.Count);
        foreach (var remoteKey in remoteKeys)
        {
            if (!RemoteKeyProviderCatalog.TryGetProvider(remoteKey.Provider, out var provider))
            {
                return Unprocessable(
                    $"The tenant's secrets reference a remote key of provider '{remoteKey.Provider}', which this cell does not register."
                );
            }

            providers.Add((provider, remoteKey.KeyId));
        }

        // This cell's sweep keeps a key that it tags, and the source cell's sweep skips it.
        foreach (var (provider, keyId) in providers)
        {
            await provider.RetagKeyAsync(keyId, CellOptions.Name, cancellationToken);
            retagged.Add((provider, keyId));
        }

        var move = await TenantDirectory.MoveAsync(
            tenantId,
            sourceCell,
            CellOptions.Name,
            cancellationToken
        );

        return move.IsMoved
            ? null
            : TypedResults.Problem(
                detail: move.ConflictDetail,
                statusCode: StatusCodes.Status409Conflict
            );
    }

    // Puts the retagged remote keys back under the source cell's tag, so that this cell's sweep does not destroy keys
    // that the source cell still holds, then removes what the import wrote: each contributor's rows in reverse order,
    // then the tenant with its master keys. Callers pass a token that is not cancelled, so the cleanup runs to
    // completion whatever the request's cancellation.
    private async ValueTask RollbackAsync(
        string tenantId,
        string sourceCell,
        List<ITenantTransferContributor> contributors,
        List<(IRemoteKeyProvider Provider, string KeyId)> retagged,
        CancellationToken cancellationToken
    )
    {
        foreach (var (provider, keyId) in retagged)
        {
            await provider.RetagKeyAsync(keyId, sourceCell, cancellationToken);
        }

        for (var index = contributors.Count - 1; index >= 0; index--)
        {
            await contributors[index].RemoveAsync(tenantId, cancellationToken);
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        await storeManager.GetStore<ITenantStore>().RemoveMovedAsync(tenantId, cancellationToken);
        await storeManager.SaveChangesAsync(cancellationToken);
    }

    // Decrypts the package with the transfer key. A package that does not parse, or that the key does not decrypt,
    // yields nothing.
    private async ValueTask<JsonElement?> DecodePackageOrDefaultAsync(
        string package,
        SecretKey privateKey,
        CancellationToken cancellationToken
    )
    {
        byte[]? payload = null;
        try
        {
            using var decoded = BufferFactory.CreatePooledBufferWriter(isSensitive: true);
            var token = JoseSerializer.Parse(package);
            await JoseSerializer.DecodeAsync(token, privateKey, decoded, cancellationToken);
            payload = decoded.AsReadOnlySequence.ToArray();
            return JsonElement.Parse(payload);
        }
        catch (Exception exception)
            when (exception
                    is JoseException
                        or CryptographicException
                        or FormatException
                        or JsonException
            )
        {
            return null;
        }
        finally
        {
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    // Reads the payload that the export writes. The tenant identifier, the region and the master keys are required
    // strings, and each section has a distinct name and a JSON value.
    private static bool TryReadPackage(
        JsonElement root,
        [NotNullWhen(true)] out string? region,
        [NotNullWhen(true)] out string? tenantId,
        [NotNullWhen(true)] out string? masterKeys,
        [NotNullWhen(true)] out Dictionary<string, JsonElement>? sections
    )
    {
        region = null;
        tenantId = null;
        masterKeys = null;
        sections = null;

        if (
            root.ValueKind != JsonValueKind.Object
            || !TryReadString(root, RegionMember, out var regionValue)
            || !TryReadString(root, TenantIdMember, out var tenantIdValue)
            || !TryReadString(root, MasterKeysMember, out var masterKeysValue)
            || !root.TryGetProperty(SectionsMember, out var sectionsElement)
            || sectionsElement.ValueKind != JsonValueKind.Array
        )
        {
            return false;
        }

        var sectionsValue = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var sectionElement in sectionsElement.EnumerateArray())
        {
            if (
                sectionElement.ValueKind != JsonValueKind.Object
                || !TryReadString(sectionElement, SectionNameMember, out var name)
                || !sectionElement.TryGetProperty(SectionJsonMember, out var json)
                || !sectionsValue.TryAdd(name, json)
            )
            {
                return false;
            }
        }

        region = regionValue;
        tenantId = tenantIdValue;
        masterKeys = masterKeysValue;
        sections = sectionsValue;
        return true;
    }

    private static bool TryReadString(
        JsonElement element,
        string propertyName,
        [NotNullWhen(true)] out string? value
    )
    {
        value = null;
        if (
            !element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
        )
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrEmpty(value);
    }

    private static ProblemHttpResult Unprocessable(string detail) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status422UnprocessableEntity);

    private static ProblemHttpResult DirectoryUnavailableProblem(HttpContext httpContext) =>
        RetryAfterProblem.Create(
            httpContext,
            TimeSpan.FromSeconds(DirectoryRetrySeconds),
            new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail = "The global directory is unavailable. Retry the request.",
            }
        );
}
